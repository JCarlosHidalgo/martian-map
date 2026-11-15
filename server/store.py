"""Persistent habitat zone catalogue; bundle importers publish completed zones here."""

from __future__ import annotations

import json
import re
import sqlite3
from contextlib import contextmanager
from pathlib import Path
from typing import Iterator


class ZoneStore:
    """Keep zone versions and the global polling cursor in SQLite."""

    def __init__(self, data_root: Path) -> None:
        self.data_root = data_root
        self.zones_root = data_root / "zones"
        self.database = data_root / "catalog.sqlite3"

    def initialize(self) -> None:
        """Create the catalogue once; existing rows survive server restarts."""
        self.zones_root.mkdir(parents=True, exist_ok=True)
        with self.connect() as db:
            db.execute(
                "CREATE TABLE IF NOT EXISTS cursor (id INTEGER PRIMARY KEY CHECK (id = 1), value INTEGER NOT NULL)"
            )
            db.execute("INSERT OR IGNORE INTO cursor (id, value) VALUES (1, 0)")
            db.execute(
                "CREATE TABLE IF NOT EXISTS zones ("
                "zone_id TEXT PRIMARY KEY, name TEXT NOT NULL, zone_version INTEGER NOT NULL, "
                "catalog_seq INTEGER NOT NULL)"
            )
            db.execute("CREATE TABLE IF NOT EXISTS bundles (bundle_id TEXT PRIMARY KEY)")

    @contextmanager
    def connect(self) -> Iterator[sqlite3.Connection]:
        """Open a short lived connection suitable for one request or transaction."""
        db = sqlite3.connect(self.database, timeout=5)
        db.row_factory = sqlite3.Row
        try:
            with db:
                yield db
        finally:
            db.close()

    def publish_zone(self, zone_id: str, version: int, bundle_id: str | None = None) -> int:
        """Publish a complete on-disk zone; called only after the importer installs its files."""
        if re.fullmatch(r"[a-z][a-z0-9-]{1,31}", zone_id) is None:
            raise ValueError("invalid zone_id")
        if version < 1:
            raise ValueError("zone_version must be positive")
        root = self.zones_root / zone_id
        zone = json.loads((root / "zone.json").read_text(encoding="utf-8"))
        json.loads((root / "conditions.json").read_text(encoding="utf-8"))
        if zone["zone_id"] != zone_id or not isinstance(zone["name"], str):
            raise ValueError("zone.json identity does not match the catalogue entry")
        with self.connect() as db:
            db.execute("BEGIN IMMEDIATE")
            current = db.execute(
                "SELECT zone_version FROM zones WHERE zone_id = ?", (zone_id,)
            ).fetchone()
            if current is not None and version <= current["zone_version"]:
                raise ValueError("zone_version must increase")
            sequence = db.execute("SELECT value FROM cursor WHERE id = 1").fetchone()[0] + 1
            db.execute("UPDATE cursor SET value = ? WHERE id = 1", (sequence,))
            db.execute(
                "INSERT INTO zones (zone_id, name, zone_version, catalog_seq) VALUES (?, ?, ?, ?) "
                "ON CONFLICT(zone_id) DO UPDATE SET name=excluded.name, "
                "zone_version=excluded.zone_version, catalog_seq=excluded.catalog_seq",
                (zone_id, zone["name"], version, sequence),
            )
            if bundle_id is not None:
                db.execute("INSERT INTO bundles (bundle_id) VALUES (?)", (bundle_id,))
            return sequence

    def has_bundle(self, bundle_id: str) -> bool:
        """Report whether the bundle has already been committed with a zone version."""
        with self.connect() as db:
            return (
                db.execute("SELECT 1 FROM bundles WHERE bundle_id = ?", (bundle_id,)).fetchone()
                is not None
            )

    def list_zones(self, since: int) -> dict:
        """Return changed zone metadata and the latest global cursor."""
        with self.connect() as db:
            sequence = db.execute("SELECT value FROM cursor WHERE id = 1").fetchone()[0]
            rows = db.execute(
                "SELECT zone_id, name, zone_version FROM zones WHERE catalog_seq > ? "
                "ORDER BY catalog_seq, zone_id",
                (since,),
            ).fetchall()
        return {"next_since": sequence, "zones": [dict(row) for row in rows]}

    def zone_version(self, zone_id: str) -> int | None:
        """Read the imported version of one zone, or None if unknown."""
        with self.connect() as db:
            row = db.execute(
                "SELECT zone_version FROM zones WHERE zone_id = ?", (zone_id,)
            ).fetchone()
        return None if row is None else row["zone_version"]
