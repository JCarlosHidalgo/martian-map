"""Metadata-only merge of one entity's accepted C3 edits."""

from __future__ import annotations


def order_key(edit: dict) -> tuple[int, str, str]:
    """Return the documented Lamport, author, UUID ordering."""
    return edit["lamport"], edit["author"], edit["uuid"]


def merge_entity(edits: list[dict]) -> tuple[dict | None, set[str]]:
    """Replay an entity and return its winner plus edits ignored by confirmation."""
    winner = None
    ignored: set[str] = set()
    for edit in sorted(edits, key=order_key):
        if winner is not None and edit["op"] in ("hazard.set", "obstacle.set"):
            old_level = winner["payload"].get("level", 255)
            new_level = edit["payload"].get("level", 255)
            lowering = edit["tombstone"] or new_level < old_level
            if (
                lowering
                and edit["author"] != edit["creator"]
                and not edit["payload"].get("confirmed", False)
            ):
                ignored.add(edit["uuid"])
                continue
        winner = edit
    return winner, ignored
