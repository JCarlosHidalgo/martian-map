"""Persistent habitat zone catalogue; bundle importers publish completed zones here."""

from __future__ import annotations

import json
import re
import sqlite3
from contextlib import contextmanager
from pathlib import Path
from typing import Iterator

from merge import merge_entity


class InvalidEditBatch(ValueError):
    """A cross-record edit inconsistency that rejects the whole batch."""


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
            db.execute(
                "CREATE TABLE IF NOT EXISTS edits ("
                "uuid TEXT PRIMARY KEY, zone_id TEXT NOT NULL, entity_id TEXT NOT NULL, "
                "creator TEXT NOT NULL, op TEXT NOT NULL, lamport INTEGER NOT NULL, "
                "author TEXT NOT NULL, document TEXT NOT NULL, accepted_seq INTEGER NOT NULL)"
            )
            db.execute("CREATE INDEX IF NOT EXISTS edits_entity ON edits(zone_id, entity_id)")
            db.execute(
                "CREATE TABLE IF NOT EXISTS winners ("
                "zone_id TEXT NOT NULL, entity_id TEXT NOT NULL, uuid TEXT NOT NULL, "
                "changed_seq INTEGER NOT NULL, PRIMARY KEY(zone_id, entity_id))"
            )
            db.execute(
                "CREATE INDEX IF NOT EXISTS winners_changes ON winners(zone_id, changed_seq)"
            )

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

    def post_edits(self, edits: list[dict]) -> list[dict]:
        """Save accepted edits and return one acknowledgement per input UUID."""
        acks = []
        with self.connect() as db:
            db.execute("BEGIN IMMEDIATE")
            for edit in edits:
                uuid = edit["uuid"]
                if db.execute("SELECT 1 FROM edits WHERE uuid = ?", (uuid,)).fetchone():
                    acks.append({"uuid": uuid, "status": "duplicate", "code": None})
                    continue
                zone_id = edit["zone_id"]
                if not db.execute("SELECT 1 FROM zones WHERE zone_id = ?", (zone_id,)).fetchone():
                    acks.append({"uuid": uuid, "status": "rejected", "code": "unknown_zone"})
                    continue
                rows = db.execute(
                    "SELECT document, creator, op FROM edits WHERE zone_id = ? AND entity_id = ?",
                    (zone_id, edit["entity_id"]),
                ).fetchall()
                if rows and (rows[0]["creator"], rows[0]["op"]) != (
                    edit["creator"],
                    edit["op"],
                ):
                    raise InvalidEditBatch("creator and op must remain constant per entity")
                candidate, ignored = merge_entity(
                    [json.loads(row["document"]) for row in rows] + [edit]
                )
                if uuid in ignored:
                    acks.append(
                        {"uuid": uuid, "status": "rejected", "code": "confirmation_required"}
                    )
                    continue
                sequence = db.execute("SELECT value FROM cursor WHERE id = 1").fetchone()[0] + 1
                db.execute("UPDATE cursor SET value = ? WHERE id = 1", (sequence,))
                db.execute(
                    "INSERT INTO edits (uuid, zone_id, entity_id, creator, op, lamport, author, "
                    "document, accepted_seq) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)",
                    (
                        uuid,
                        zone_id,
                        edit["entity_id"],
                        edit["creator"],
                        edit["op"],
                        edit["lamport"],
                        edit["author"],
                        json.dumps(edit, separators=(",", ":")),
                        sequence,
                    ),
                )
                previous = db.execute(
                    "SELECT uuid FROM winners WHERE zone_id = ? AND entity_id = ?",
                    (zone_id, edit["entity_id"]),
                ).fetchone()
                if previous is None or previous["uuid"] != candidate["uuid"]:
                    db.execute(
                        "INSERT INTO winners(zone_id, entity_id, uuid, changed_seq) VALUES (?, ?, ?, ?) "
                        "ON CONFLICT(zone_id, entity_id) DO UPDATE SET "
                        "uuid=excluded.uuid, changed_seq=excluded.changed_seq",
                        (zone_id, edit["entity_id"], candidate["uuid"], sequence),
                    )
                acks.append({"uuid": uuid, "status": "accepted", "code": None})
        return acks

    def changes(self, zone_id: str, since: int, limit: int) -> dict:
        """Page winners without crossing a zone publication before its snapshot is delivered."""
        with self.connect() as db:
            latest = db.execute("SELECT value FROM cursor WHERE id = 1").fetchone()[0]
            zone = db.execute(
                "SELECT catalog_seq FROM zones WHERE zone_id = ?", (zone_id,)
            ).fetchone()
            boundary = zone["catalog_seq"] if zone["catalog_seq"] > since else None
            cutoff = boundary if boundary is not None else max(latest, since) + 1
            rows = db.execute(
                "SELECT e.document, w.changed_seq FROM winners AS w "
                "JOIN edits AS e ON e.uuid = w.uuid "
                "WHERE w.zone_id = ? AND w.changed_seq > ? AND w.changed_seq < ? "
                "ORDER BY w.changed_seq, w.entity_id LIMIT ?",
                (zone_id, since, cutoff, limit + 1),
            ).fetchall()
        has_more = len(rows) > limit
        page = rows[:limit]
        result = {
            "next_since": page[-1]["changed_seq"] if has_more else boundary or max(latest, since),
            "has_more": has_more,
            "edits": [json.loads(row["document"]) for row in page],
            "tiles": [],
        }
        if boundary is not None and not has_more:
            root = self.zones_root / zone_id
            zone_document = json.loads((root / "zone.json").read_text(encoding="utf-8"))
            conditions = json.loads((root / "conditions.json").read_text(encoding="utf-8"))
            # Conservative snapshot until C1/C6 fixtures allow exact changed-file tracking.
            result["tiles"] = zone_document["tiles"]
            result["conditions"] = conditions
        return result

    def current_winners(self, zone_id: str) -> list[dict]:
        """Return all winning edits for golden verification, including tombstones."""
        with self.connect() as db:
            rows = db.execute(
                "SELECT e.document FROM winners AS w JOIN edits AS e ON e.uuid = w.uuid "
                "WHERE w.zone_id = ?",
                (zone_id,),
            ).fetchall()
        return sorted(
            (json.loads(row["document"]) for row in rows),
            key=lambda edit: (edit["lamport"], edit["author"], edit["uuid"]),
        )
