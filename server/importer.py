"""Import delayed Earth bundles from a local spool into the habitat catalogue."""

from __future__ import annotations

import hashlib
import json
import logging
import os
import re
import shutil
import tarfile
import tempfile
from datetime import datetime, timezone
from pathlib import Path, PurePosixPath

from store import ZoneStore

LOG = logging.getLogger("habitat.importer")
NAME = re.compile(r"(\d{8}T\d{6}Z)_([a-z][a-z0-9-]{1,31})_([0-9A-HJKMNP-TV-Z]{26})\.tar\Z")
SHA256 = re.compile(r"[0-9a-f]{64}\Z")
KINDS = {"zone_full", "zone_update", "conditions"}


class BundleRejected(Exception):
    """A non-retryable bundle failure with a machine-readable reason."""

    def __init__(self, reason: str) -> None:
        super().__init__(reason)
        self.reason = reason


def utc(value: str) -> datetime:
    """Parse a C1b UTC timestamp ending in Z."""
    if not isinstance(value, str) or not value.endswith("Z"):
        raise BundleRejected("invalid_bundle")
    try:
        return datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError as error:
        raise BundleRejected("invalid_bundle") from error


def safe_path(name: str) -> bool:
    """Allow a relative POSIX path with no traversal or empty components."""
    if not isinstance(name, str) or not name or "\\" in name:
        return False
    path = PurePosixPath(name)
    return not path.is_absolute() and all(part not in ("", ".", "..") for part in name.split("/"))


def digest(path: Path) -> str:
    """Hash a payload without loading the whole file into memory."""
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def read_object(path: Path) -> dict:
    """Read one JSON object or reject the bundle."""
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise BundleRejected("invalid_bundle") from error
    if not isinstance(value, dict):
        raise BundleRejected("invalid_bundle")
    return value


class BundleImporter:
    """Scan complete .tar files every second; one bad bundle never stops the scan."""

    def __init__(self, store: ZoneStore, spool: Path) -> None:
        self.store = store
        self.spool = spool
        self.incoming = spool / "incoming"
        self.done = spool / "done"
        self.rejected = spool / "rejected"

    def initialize(self) -> None:
        """Create the spool directories without touching Earth's private state directory."""
        for path in (self.incoming, self.done, self.rejected):
            path.mkdir(parents=True, exist_ok=True)

    def poll_once(self, now: datetime | None = None) -> None:
        """Process all due bundles in filename order; leave future and partial files alone."""
        now = now or datetime.now(timezone.utc)
        for path in sorted(self.incoming.glob("*.tar")):
            match = NAME.fullmatch(path.name)
            try:
                if match is None:
                    raise BundleRejected("invalid_bundle")
                try:
                    deliver = datetime.strptime(match[1], "%Y%m%dT%H%M%SZ").replace(
                        tzinfo=timezone.utc
                    )
                except ValueError as error:
                    raise BundleRejected("invalid_bundle") from error
                if deliver > now:
                    continue
                self.import_one(path, match, now)
            except BundleRejected as error:
                self.reject(path, error.reason)
            except (OSError, ValueError, tarfile.TarError, KeyError, TypeError) as error:
                LOG.exception("bundle import failed path=%s error=%s", path, error)

    def import_one(self, path: Path, match: re.Match[str], now: datetime) -> None:
        """Verify a due tar, then install files and commit its catalogue version."""
        with tempfile.TemporaryDirectory(dir=self.store.data_root) as temporary:
            work = Path(temporary)
            extracted = work / "extracted"
            extracted.mkdir()
            self.extract(path, extracted)
            manifest = read_object(extracted / "bundle.json")
            self.verify_inventory(manifest, extracted, match)
            bundle_id = manifest["bundle_id"]
            if self.store.has_bundle(bundle_id):
                os.replace(path, self.done / path.name)
                return
            if "expires_utc" in manifest and utc(manifest["expires_utc"]) <= now:
                raise BundleRejected("expired")
            zone_id = manifest["zone_id"]
            current = self.store.zone_version(zone_id)
            if current is None and manifest["kind"] != "zone_full":
                raise BundleRejected("unknown_zone")
            if current is not None and manifest["zone_version"] <= current:
                raise BundleRejected("stale")
            stage = work / "zone"
            target = self.store.zones_root / zone_id
            if manifest["kind"] != "zone_full":
                shutil.copytree(target, stage)
            else:
                stage.mkdir()
            for item in (extracted / "data").rglob("*"):
                if item.is_file():
                    destination = stage / item.relative_to(extracted / "data")
                    destination.parent.mkdir(parents=True, exist_ok=True)
                    shutil.copyfile(item, destination)
            self.verify_zone(stage, zone_id)
            backup = work / "previous"
            if target.exists():
                os.replace(target, backup)
            try:
                os.replace(stage, target)
                self.store.publish_zone(zone_id, manifest["zone_version"], bundle_id)
            except Exception:
                if target.exists():
                    os.replace(target, stage)
                if backup.exists():
                    os.replace(backup, target)
                raise
            os.replace(path, self.done / path.name)
            LOG.info("bundle imported bundle_id=%s zone_id=%s", bundle_id, zone_id)

    def extract(self, path: Path, extracted: Path) -> None:
        """Extract only ordinary, uniquely named files from an uncompressed tar."""
        seen: set[str] = set()
        try:
            with tarfile.open(path, mode="r:") as archive:
                for member in archive:
                    if member.isdir():
                        continue
                    if not member.isfile() or not safe_path(member.name) or member.name in seen:
                        raise BundleRejected("invalid_bundle")
                    if member.name != "bundle.json" and not member.name.startswith("data/"):
                        raise BundleRejected("invalid_bundle")
                    seen.add(member.name)
                    destination = extracted / member.name
                    destination.parent.mkdir(parents=True, exist_ok=True)
                    source = archive.extractfile(member)
                    if source is None:
                        raise BundleRejected("invalid_bundle")
                    with source, destination.open("wb") as sink:
                        shutil.copyfileobj(source, sink)
        except (tarfile.TarError, OSError) as error:
            raise BundleRejected("invalid_bundle") from error

    def verify_inventory(self, manifest: dict, extracted: Path, match: re.Match[str]) -> None:
        """Check C1b fields and every listed file before consulting bundle identity."""
        # C1b integration point: validate against bundle.schema.json once P1 publishes it.
        try:
            bundle_id = manifest["bundle_id"]
            zone_id = manifest["zone_id"]
            version = manifest["zone_version"]
            kind = manifest["kind"]
            files = manifest["files"]
            oxum = manifest["payload_oxum"]
            delay = manifest["delay_s"]
            deliver = utc(manifest["deliver_at_utc"])
            utc(manifest["created_utc"])
            if (
                manifest["schema_version"] != 1
                or manifest["source"] != "earth"
                or bundle_id != match[3]
                or zone_id != match[2]
                or not isinstance(version, int)
                or isinstance(version, bool)
                or version < 1
                or kind not in KINDS
                or deliver.strftime("%Y%m%dT%H%M%SZ") != match[1]
                or not isinstance(files, list)
                or not isinstance(delay, int)
                or isinstance(delay, bool)
                or delay < 0
            ):
                raise BundleRejected("invalid_bundle")
            listed: set[str] = set()
            total = 0
            for entry in files:
                name, size, sha = entry["path"], entry["size"], entry["sha256"]
                media_type = entry["media_type"]
                if (
                    not safe_path(name)
                    or not name.startswith("data/")
                    or name in listed
                    or not isinstance(size, int)
                    or isinstance(size, bool)
                    or size < 0
                    or not isinstance(sha, str)
                    or SHA256.fullmatch(sha) is None
                    or media_type
                    not in ("application/json", "application/octet-stream", "image/jpeg")
                ):
                    raise BundleRejected("invalid_bundle")
                listed.add(name)
                payload = extracted / name
                if (
                    not payload.is_file()
                    or payload.stat().st_size != size
                    or digest(payload) != sha
                ):
                    raise BundleRejected("hash")
                total += size
            actual = {
                p.relative_to(extracted).as_posix() for p in extracted.rglob("*") if p.is_file()
            }
            if actual != listed | {"bundle.json"} or oxum != f"{total}.{len(listed)}":
                raise BundleRejected("invalid_bundle")
            required = {
                "zone_full": {"data/zone.json", "data/conditions.json"},
                "zone_update": {"data/zone.json"},
                "conditions": {"data/conditions.json"},
            }[kind]
            if not required.issubset(listed):
                raise BundleRejected("invalid_bundle")
        except (KeyError, TypeError, ValueError) as error:
            raise BundleRejected("invalid_bundle") from error

    def verify_zone(self, root: Path, zone_id: str) -> None:
        """Check that the staged zone references complete tile and POV files."""
        # C1/C6 integration point: validate the zone and conditions JSON against their schemas.
        zone = read_object(root / "zone.json")
        read_object(root / "conditions.json")
        if zone.get("zone_id") != zone_id or not isinstance(zone.get("name"), str):
            raise BundleRejected("invalid_bundle")
        tiles = zone.get("tiles")
        pins = zone.get("pov")
        if not isinstance(tiles, list) or not isinstance(pins, list):
            raise BundleRejected("invalid_bundle")
        for tile in tiles:
            try:
                z, x, y, version = (tile[key] for key in ("z", "x", "y", "version"))
                if any(not isinstance(n, int) or n < 0 for n in (z, x, y, version)):
                    raise BundleRejected("invalid_bundle")
                tile_root = root / "tiles" / str(z) / str(x) / str(y)
                manifest_path = tile_root / "tile.json"
                if digest(manifest_path) != tile["sha256"]:
                    raise BundleRejected("hash")
                tile_manifest = read_object(manifest_path)
                if tile_manifest.get("version") != version:
                    raise BundleRejected("invalid_bundle")
                for name, sha in tile_manifest["sha256"].items():
                    if not safe_path(name) or "/" in name or digest(tile_root / name) != sha:
                        raise BundleRejected("hash")
            except (OSError, KeyError, TypeError) as error:
                raise BundleRejected("invalid_bundle") from error
        for pin in pins:
            try:
                name = pin["file"]
                if (
                    not safe_path(name)
                    or "/" in name
                    or digest(root / "pov" / name) != pin["sha256"]
                ):
                    raise BundleRejected("hash")
            except (OSError, KeyError, TypeError) as error:
                raise BundleRejected("invalid_bundle") from error

    def reject(self, path: Path, reason: str) -> None:
        """Move one bad tar and write its documented reason file."""
        os.replace(path, self.rejected / path.name)
        (self.rejected / f"{path.name}.reason.json").write_text(
            json.dumps({"reason": reason}) + "\n", encoding="utf-8"
        )
        LOG.warning("bundle rejected path=%s reason=%s", path.name, reason)
