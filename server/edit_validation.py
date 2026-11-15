"""Validate incoming edits against P2's shared JSON Schema contract."""

from __future__ import annotations

import json
from pathlib import Path

from jsonschema import Draft202012Validator

SCHEMA = Path(__file__).resolve().parents[1] / "contracts" / "schemas" / "edit.schema.json"
VALIDATOR = Draft202012Validator(json.loads(SCHEMA.read_text(encoding="utf-8")))


def valid_edit(edit: object) -> bool:
    """Return whether a document matches the published Edit wire format."""
    return VALIDATOR.is_valid(edit)
