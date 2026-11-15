"""End-to-end spool tests using clearly synthetic, locally packaged C1b data."""

from __future__ import annotations

import hashlib
import io
import json
import tarfile
import time
from datetime import datetime, timedelta, timezone
from pathlib import Path

from fastapi.testclient import TestClient

from habitat import create_app
from importer import BundleImporter
from store import ZoneStore
from test_habitat import stage_synthetic_zone

ULID_A = "01ARZ3NDEKTSV4RRFFQ69G5FAV"
ULID_B = "01ARZ3NDEKTSV4RRFFQ69G5FAW"
ULID_C = "01ARZ3NDEKTSV4RRFFQ69G5FAX"
ULID_D = "01ARZ3NDEKTSV4RRFFQ69G5FAY"


def bundle(
    spool: Path,
    source: Path,
    *,
    bundle_id: str,
    version: int,
    kind: str,
    deliver: datetime,
    corrupt: str | None = None,
    expires: datetime | None = None,
) -> Path:
    """Package synthetic source files in the documented uncompressed tar layout."""
    if kind == "conditions":
        files = [source / "conditions.json"]
    else:
        files = sorted(p for p in source.rglob("*") if p.is_file())
    entries = []
    contents = {}
    for file in files:
        name = "data/" + file.relative_to(source).as_posix()
        content = file.read_bytes()
        contents[name] = content
        entries.append(
            {
                "path": name,
                "size": len(content),
                "sha256": hashlib.sha256(content).hexdigest(),
                "media_type": (
                    "application/json"
                    if file.suffix == ".json"
                    else "image/jpeg"
                    if file.suffix == ".jpg"
                    else "application/octet-stream"
                ),
            }
        )
    manifest = {
        "schema_version": 1,
        "bundle_id": bundle_id,
        "source": "earth",
        "zone_id": "synth",
        "zone_version": version,
        "kind": kind,
        "created_utc": (deliver - timedelta(seconds=10)).isoformat().replace("+00:00", "Z"),
        "delay_s": 10,
        "deliver_at_utc": deliver.isoformat().replace("+00:00", "Z"),
        "files": entries,
        "payload_oxum": f"{sum(e['size'] for e in entries)}.{len(entries)}",
    }
    if expires is not None:
        manifest["expires_utc"] = expires.isoformat().replace("+00:00", "Z")
    contents["bundle.json"] = json.dumps(manifest).encode()
    if corrupt is not None:
        contents[corrupt] += b"changed"
    incoming = spool / "incoming"
    incoming.mkdir(parents=True, exist_ok=True)
    path = incoming / f"{deliver:%Y%m%dT%H%M%SZ}_synth_{bundle_id}.tar"
    with tarfile.open(path, "w:") as archive:
        for name, content in contents.items():
            info = tarfile.TarInfo(name)
            info.size = len(content)
            archive.addfile(info, io.BytesIO(content))
    return path


def test_delay_duplicate_hash_and_catalogue(tmp_path: Path) -> None:
    source_root = tmp_path / "source"
    stage_synthetic_zone(source_root)
    source = source_root / "zones" / "synth"
    store = ZoneStore(tmp_path / "habitat")
    store.initialize()
    importer = BundleImporter(store, tmp_path / "habitat" / "spool")
    importer.initialize()
    deliver = datetime(2026, 11, 14, 12, 0, 10, tzinfo=timezone.utc)
    first = bundle(
        importer.spool, source, bundle_id=ULID_A, version=1, kind="zone_full", deliver=deliver
    )
    importer.poll_once(deliver - timedelta(seconds=1))
    assert first.is_file()
    assert store.list_zones(0) == {"next_since": 0, "zones": []}
    importer.poll_once(deliver)
    assert (importer.done / first.name).is_file()
    assert store.has_bundle(ULID_A)
    assert store.list_zones(0)["zones"][0]["zone_version"] == 1
    with TestClient(create_app(store.data_root, importer.spool)) as client:
        assert client.get("/tiles/synth/0/0/0/height.r16?v=1").status_code == 200
    bundle(importer.spool, source, bundle_id=ULID_A, version=1, kind="zone_full", deliver=deliver)
    importer.poll_once(deliver)
    assert store.list_zones(0)["next_since"] == 1
    damaged = bundle(
        importer.spool,
        source,
        bundle_id=ULID_B,
        version=2,
        kind="zone_full",
        deliver=deliver,
        corrupt="data/zone.json",
    )
    importer.poll_once(deliver)
    reason = json.loads((importer.rejected / f"{damaged.name}.reason.json").read_text())
    assert reason == {"reason": "hash"}
    assert store.list_zones(0)["next_since"] == 1


def test_conditions_bundle_updates_zone_without_reversioning_tile(tmp_path: Path) -> None:
    source_root = tmp_path / "source"
    stage_synthetic_zone(source_root)
    source = source_root / "zones" / "synth"
    app = create_app(tmp_path / "habitat")
    deliver = datetime.now(timezone.utc).replace(microsecond=0) - timedelta(seconds=1)
    with TestClient(app) as client:
        bundle(
            app.state.importer.spool,
            source,
            bundle_id=ULID_A,
            version=1,
            kind="zone_full",
            deliver=deliver,
        )
        deadline = time.monotonic() + 5
        while (
            client.get("/zones?since=0").json()["next_since"] == 0 and time.monotonic() < deadline
        ):
            time.sleep(0.1)
        assert client.get("/zones?since=0").json()["next_since"] == 1
        (source / "conditions.json").write_text(
            json.dumps({"schema_version": 1, "value": 8.0}), encoding="utf-8"
        )
        update = bundle(
            app.state.importer.spool,
            source,
            bundle_id=ULID_B,
            version=2,
            kind="conditions",
            deliver=deliver,
        )
        app.state.importer.poll_once()
        assert (app.state.importer.done / update.name).is_file()
        assert client.get("/zones?since=1").json()["zones"][0]["zone_version"] == 2
        assert client.get("/zones/synth").json()["conditions"]["value"] == 8.0
        assert client.get("/tiles/synth/0/0/0/height.r16?v=1").status_code == 200


def test_expired_unknown_and_stale_bundles_are_rejected(tmp_path: Path) -> None:
    source_root = tmp_path / "source"
    stage_synthetic_zone(source_root)
    source = source_root / "zones" / "synth"
    store = ZoneStore(tmp_path / "habitat")
    store.initialize()
    importer = BundleImporter(store, tmp_path / "habitat" / "spool")
    importer.initialize()
    deliver = datetime(2026, 11, 14, 12, 0, 10, tzinfo=timezone.utc)
    cases = [
        (ULID_A, "zone_full", 1, deliver - timedelta(seconds=1), "expired"),
        (ULID_B, "conditions", 1, None, "unknown_zone"),
    ]
    for bundle_id, kind, version, expires, reason in cases:
        path = bundle(
            importer.spool,
            source,
            bundle_id=bundle_id,
            version=version,
            kind=kind,
            deliver=deliver,
            expires=expires,
        )
        importer.poll_once(deliver)
        assert json.loads((importer.rejected / f"{path.name}.reason.json").read_text()) == {
            "reason": reason
        }
    full = bundle(
        importer.spool, source, bundle_id=ULID_C, version=1, kind="zone_full", deliver=deliver
    )
    importer.poll_once(deliver)
    assert (importer.done / full.name).is_file()
    stale = bundle(
        importer.spool, source, bundle_id=ULID_D, version=1, kind="conditions", deliver=deliver
    )
    importer.poll_once(deliver)
    assert json.loads((importer.rejected / f"{stale.name}.reason.json").read_text()) == {
        "reason": "stale"
    }
    assert store.list_zones(0)["next_since"] == 1
    invalid_date = importer.incoming / f"20261131T120000Z_synth_{ULID_A}.tar"
    invalid_date.write_bytes(b"")
    importer.poll_once(deliver)
    assert json.loads((importer.rejected / f"{invalid_date.name}.reason.json").read_text()) == {
        "reason": "invalid_bundle"
    }
