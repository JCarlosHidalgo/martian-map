"""Probe the C6 upstream sources and cache the evidence for E14.

Run from the repository root: python3 ingest/probe_c6.py
The cache is evidence, never the synthetic contracts/ fixture or a production ingest output.
"""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
import tempfile
from datetime import datetime, timedelta, timezone
from pathlib import Path
from urllib.parse import urlencode, urljoin


DONKI = "https://ccmc.gsfc.nasa.gov/DONKI-API/get/"
RAD = "https://pds-ppi.igpp.ucla.edu/data/MSL-M-RAD-3-RDR-V1.0/DATA/"


def get(url: str, byte_range: str | None = None) -> tuple[int, bytes, str | None]:
    # CCMC rejects Python's urllib TLS client; curl is also the guide's H0 probe tool.
    with tempfile.TemporaryDirectory(prefix="martian-c6-") as directory:
        body = Path(directory) / "body"
        headers = Path(directory) / "headers"
        cmd = [
            "curl",
            "--fail",
            "--location",
            "--silent",
            "--show-error",
            "--max-time",
            "30",
            "--dump-header",
            str(headers),
            "--output",
            str(body),
            url,
        ]
        if byte_range is not None:
            cmd[1:1] = ["--range", byte_range, "--max-filesize", "131072"]
        result = subprocess.run(cmd, capture_output=True, text=True, check=False)
        if result.returncode != 0:
            raise OSError(
                f"curl exit {result.returncode} for {url}: {result.stderr.strip()}"
            )
        lines = headers.read_text(encoding="ascii", errors="replace").splitlines()
        status_lines = [line for line in lines if line.startswith("HTTP/")]
        status = int(status_lines[-1].split()[1])
        content_range = next(
            (
                line.split(":", 1)[1].strip()
                for line in reversed(lines)
                if line.lower().startswith("content-range:")
            ),
            None,
        )
        return status, body.read_bytes(), content_range


def json_file(path: Path, value: object) -> None:
    path.write_text(
        json.dumps(value, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )


def probe_donki(out: Path, start: str, end: str) -> dict[str, object]:
    results: dict[str, object] = {}
    for kind in ("WSAEnlilSimulations", "SEP", "FLR"):
        url = DONKI + kind + "?" + urlencode({"startDate": start, "endDate": end})
        status, raw, _ = get(url)
        value = json.loads(raw)
        if not isinstance(value, list):
            raise ValueError(
                f"DONKI {kind} returned {type(value).__name__}, expected an array"
            )
        json_file(out / f"donki_{kind}.json", value)
        result = {"url": url, "status": status, "count": len(value)}
        if kind == "WSAEnlilSimulations":
            result["mars_impacts"] = sum(
                any(
                    impact.get("location") == "Mars"
                    for impact in item.get("impactList") or []
                )
                for item in value
            )
        results[kind] = result
    return results


def probe_rad(out: Path) -> dict[str, object]:
    status, raw, _ = get(RAD)
    html = raw.decode("utf-8", errors="replace")
    folders = re.findall(r'href="(SOL_(\d+)_(\d+)/)"', html, re.IGNORECASE)
    if not folders:
        raise ValueError("RAD index has no SOL directory")
    folder = max(folders, key=lambda item: (int(item[2]), int(item[1])))[0]
    folder_url = urljoin(RAD, folder)
    _, raw, _ = get(folder_url)
    html = raw.decode("utf-8", errors="replace")
    labels = re.findall(r'href="(RAD_RDR_[^"/]+_V\d+\.LBL)"', html, re.IGNORECASE)
    if not labels:
        raise ValueError(f"RAD directory {folder} has no RDR label")

    def label_order(name: str) -> tuple[int, int]:
        match = re.search(r"_(\d+)_V(\d+)\.LBL$", name, re.IGNORECASE)
        return (int(match[1]), int(match[2])) if match else (-1, -1)

    label = max(labels, key=label_order)
    label_url = urljoin(folder_url, label)
    label_status, label_bytes, _ = get(label_url)
    (out / "rad_latest.lbl").write_bytes(label_bytes)
    text_url = label_url[:-4] + ".TXT"
    sample_status, sample_bytes, content_range = get(text_url, "0-131071")
    if sample_status != 206 or len(sample_bytes) != 131072:
        raise ValueError(
            f"RAD range request returned status={sample_status}, bytes={len(sample_bytes)}"
        )
    (out / "rad_latest_sample.txt").write_bytes(sample_bytes)
    sample = sample_bytes.decode("ascii", errors="replace")
    doses = {}
    for detector in ("B", "E"):
        match = re.search(
            rf"\[DOSIMETRY_TOTAL_DOSE_{detector}:\s*\d+\]\s*([0-9]+(?:\.[0-9]+)?)",
            sample,
        )
        if match:
            doses[detector] = float(match[1])
    return {
        "directory": folder_url,
        "label_url": label_url,
        "label_status": label_status,
        "data_url": text_url,
        "sample_status": sample_status,
        "content_range": content_range,
        "sample_bytes": len(sample_bytes),
        "dose_b_blocks_in_sample": len(re.findall(r"DOSIMETRY_TOTAL_DOSE_B", sample)),
        "dose_e_blocks_in_sample": len(re.findall(r"DOSIMETRY_TOTAL_DOSE_E", sample)),
        "first_observation_dose_uGy_h": doses,
        "first_observation_within_4_10_uGy_h": bool(doses)
        and all(4 <= rate <= 10 for rate in doses.values()),
        "note": "These are the first observation's detector rates, not the daily mean or a human dose estimate.",
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", type=Path, default=Path("ingest/cache/h0"))
    parser.add_argument("--end", default=datetime.now(timezone.utc).date().isoformat())
    args = parser.parse_args()
    end = datetime.fromisoformat(args.end).date()
    start = (end - timedelta(days=30)).isoformat()
    args.out.mkdir(parents=True, exist_ok=True)
    summary: dict[str, object] = {
        "checked_at_utc": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "window": {"start": start, "end": args.end},
        "rad_sis": "https://pds-ppi.igpp.ucla.edu/data/MSL-M-RAD-3-RDR-V1.0/DOCUMENT/RAD_RDR_SIS.HTM",
        "detector_selection": "B is silicon and E is plastic; the SIS does not establish a human-representative detector. Use the larger valid rate until E14 resolves this.",
    }
    failures = []
    for name, probe in (
        ("donki", lambda: probe_donki(args.out, start, args.end)),
        ("rad", lambda: probe_rad(args.out)),
    ):
        try:
            summary[name] = probe()
        except Exception as error:  # Keep evidence from successful probes.
            summary[name] = {"error": f"{type(error).__name__}: {error}"}
            failures.append(name)
    json_file(args.out / "c6_probe_summary.json", summary)
    print(json.dumps(summary, indent=2, ensure_ascii=False))
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
