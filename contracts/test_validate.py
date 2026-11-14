# /// script
# requires-python = ">=3.12"
# dependencies = [
#   "jsonschema[format]>=4.23,<5",
#   "openapi-spec-validator>=0.7.2,<0.8",
#   "pyyaml>=6.0.2,<7",
#   "referencing>=0.35,<0.38",
# ]
# ///
"""Exercise contract validation using temporary fixtures, never product goldens."""

from __future__ import annotations

import hashlib
import json
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from typing import Any

import yaml
from openapi_spec_validator.validation.exceptions import OpenAPIValidationError

from validate import DRAFT_URI, ContractValidator


class ContractValidatorTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory(prefix="martian-map-contracts-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.fixtures = self.root / "fixtures"
        source = Path(__file__).parent / "schemas"
        (self.root / "schemas").mkdir()
        for name in ("map.json", "map.schema.json"):
            shutil.copyfile(source / name, self.root / "schemas" / name)

    def _json(self, relative: str, value: Any) -> Path:
        path = self.root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes((json.dumps(value, indent=2) + "\n").encode("utf-8"))
        return path

    def _schema(self, name: str, **fields: Any) -> None:
        self._json(
            f"schemas/{name}.schema.json", {"$schema": DRAFT_URI, "type": "object", **fields}
        )

    def _digest(self, path: Path) -> str:
        return hashlib.sha256(path.read_bytes()).hexdigest()

    def _dataset(self) -> None:
        for name in ("tile", "zone", "conditions", "bundle", "zone_response"):
            self._schema(name)
        tile_dir = self.fixtures / "tile_synth_0_0_0"
        tile_dir.mkdir(parents=True)
        hashes = {}
        for name in ("height.r16", "slope.r8", "roughness.r8", "hazard.r8"):
            path = tile_dir / name
            path.write_bytes(b"\x00\x01")
            hashes[name] = self._digest(path)
        tile = self._json(
            "fixtures/tile_synth_0_0_0/tile.json",
            {
                "schema_version": 1,
                "zone_id": "synth",
                "z": 0,
                "x": 0,
                "y": 0,
                "version": 1,
                "height": {"file": "height.r16"},
                "layers": [],
                "sha256": hashes,
            },
        )
        pin = self.fixtures / "zone" / "pov" / "test.jpg"
        pin.parent.mkdir(parents=True)
        pin.write_bytes(b"synthetic image bytes")
        self._json(
            "fixtures/zone/zone.json",
            {
                "schema_version": 1,
                "zone_id": "synth",
                "tiles": [{"z": 0, "x": 0, "y": 0, "version": 1, "sha256": self._digest(tile)}],
                "pov": [{"file": "test.jpg", "sha256": self._digest(pin)}],
            },
        )
        self._json("fixtures/conditions/conditions.json", {"schema_version": 1, "value": 6.0})

    def _bundle(self) -> Path:
        base = self.fixtures / "bundle_synth"
        data = base / "data"
        data.mkdir(parents=True)
        shutil.copyfile(self.fixtures / "zone" / "zone.json", data / "zone.json")
        shutil.copyfile(self.fixtures / "conditions" / "conditions.json", data / "conditions.json")
        shutil.copytree(self.fixtures / "tile_synth_0_0_0", data / "tiles" / "0" / "0" / "0")
        shutil.copytree(self.fixtures / "zone" / "pov", data / "pov")
        return self._refresh_bundle()

    def _refresh_bundle(self) -> Path:
        base = self.fixtures / "bundle_synth"
        files = sorted(path for path in (base / "data").rglob("*") if path.is_file())
        entries = [
            {
                "path": path.relative_to(base).as_posix(),
                "size": path.stat().st_size,
                "sha256": self._digest(path),
            }
            for path in files
        ]
        return self._json(
            "fixtures/bundle_synth/bundle.json",
            {
                "schema_version": 1,
                "files": entries,
                "payload_oxum": f"{sum(entry['size'] for entry in entries)}.{len(entries)}",
            },
        )

    def _api(self) -> Path:
        zone = (self.fixtures / "zone" / "zone.json").read_text(encoding="utf-8").strip()
        conditions = (
            (self.fixtures / "conditions" / "conditions.json").read_text(encoding="utf-8").strip()
        )
        path = self.fixtures / "api" / "zone.json"
        path.parent.mkdir()
        path.write_bytes(
            f'{{"zone_version": 1, "zone": {zone}, "conditions": {conditions}}}\n'.encode()
        )
        return path

    def test_empty_repository_passes(self) -> None:
        ContractValidator(self.root).run()
        self.fixtures.mkdir()
        ContractValidator(self.root).run()

    def test_only_empty_gitkeep_placeholders_are_ignored(self) -> None:
        placeholder = self.fixtures / "bundle_synth" / "data" / ".gitkeep"
        placeholder.parent.mkdir(parents=True)
        placeholder.write_bytes(b"")
        ContractValidator(self.root).run()
        placeholder.write_bytes(b"unexpected payload")
        with self.assertRaisesRegex(ValueError, "No original fixture mapping"):
            ContractValidator(self.root).run()

    def test_cli_exit_codes_and_english_errors(self) -> None:
        script = self.root / "validate.py"
        shutil.copyfile(Path(__file__).parent / "validate.py", script)
        result = subprocess.run([sys.executable, str(script)], capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("Contract validation passed", result.stderr)
        self._json("fixtures/mission/mission.json", {})
        result = subprocess.run([sys.executable, str(script)], capture_output=True, text=True)
        self.assertEqual(result.returncode, 1, result.stderr)
        self.assertIn("Missing schema", result.stderr)

    def test_valid_manifests_and_copies_pass(self) -> None:
        self._dataset()
        self._bundle()
        self._api()
        ContractValidator(self.root).run()

    def test_map_rejects_unknown_missing_and_incorrect_assignments(self) -> None:
        path = self.root / "schemas" / "map.json"
        original = json.loads(path.read_text(encoding="utf-8"))
        for change in ("unknown", "missing", "wrong_schema"):
            with self.subTest(change=change):
                mapping = original.copy()
                if change == "unknown":
                    mapping["fixtures/unexpected.json"] = "tile.schema.json"
                elif change == "missing":
                    del mapping["fixtures/zone/zone.json"]
                else:
                    mapping["fixtures/zone/zone.json"] = "tile.schema.json"
                self._json("schemas/map.json", mapping)
                with self.assertRaisesRegex(ValueError, "Schema validation failed"):
                    ContractValidator(self.root).run()

    def test_unmapped_json_and_binary_fail(self) -> None:
        for name in ("unexpected.json", "unexpected.bin"):
            with self.subTest(name=name):
                path = self._json(f"fixtures/{name}", {})
                with self.assertRaisesRegex(ValueError, "pattern|Unmapped"):
                    ContractValidator(self.root).run()
                path.unlink()

    def test_existing_fixture_requires_its_schema(self) -> None:
        self._json("fixtures/mission/mission.json", {})
        with self.assertRaisesRegex(ValueError, "Missing schema"):
            ContractValidator(self.root).run()

    def test_relative_schema_references_and_formats(self) -> None:
        self._schema(
            "mission", properties={"date": {"$ref": "date.schema.json"}}, required=["date"]
        )
        self._json(
            "schemas/date.schema.json",
            {"$schema": DRAFT_URI, "type": "string", "format": "date-time"},
        )
        self._json("fixtures/mission/mission.json", {"date": "2026-11-14T12:00:00Z"})
        ContractValidator(self.root).run()
        self._json("fixtures/mission/mission.json", {"date": "invalid"})
        with self.assertRaisesRegex(ValueError, "Schema validation failed"):
            ContractValidator(self.root).run()

    def test_duplicate_keys_and_non_finite_json_fail(self) -> None:
        self._schema("mission")
        path = self._json("fixtures/mission/mission.json", {})
        for payload in (b'{"x": 1, "x": 2}', b'{"x": NaN}', b'{"x": Infinity}'):
            with self.subTest(payload=payload):
                path.write_bytes(payload)
                with self.assertRaisesRegex(ValueError, "Invalid JSON"):
                    ContractValidator(self.root).run()

    def test_lfs_pointer_fails(self) -> None:
        path = self.fixtures / "routing" / "costgrid_synth.bin"
        path.parent.mkdir(parents=True)
        path.write_bytes(b"version https://git-lfs.github.com/spec/v1\noid sha256:abc\n")
        with self.assertRaisesRegex(ValueError, "Git LFS pointer"):
            ContractValidator(self.root).run()

    def test_all_payload_hashes_are_checked(self) -> None:
        self._dataset()
        self._bundle()
        for relative in (
            "tile_synth_0_0_0/height.r16",
            "zone/pov/test.jpg",
            "bundle_synth/data/conditions.json",
        ):
            with self.subTest(relative=relative):
                path = self.fixtures / relative
                original = path.read_bytes()
                path.write_bytes(original + b" ")
                with self.assertRaisesRegex(ValueError, "SHA-256 mismatch"):
                    ContractValidator(self.root).run()
                path.write_bytes(original)

    def test_zone_tile_manifest_hash_is_checked(self) -> None:
        self._dataset()
        path = self.fixtures / "tile_synth_0_0_0" / "tile.json"
        path.write_bytes(path.read_bytes() + b"\n")
        with self.assertRaisesRegex(ValueError, "SHA-256 mismatch"):
            ContractValidator(self.root).run()

    def test_hash_format_and_path_traversal_fail(self) -> None:
        self._dataset()
        path = self.fixtures / "tile_synth_0_0_0" / "tile.json"
        original = json.loads(path.read_text(encoding="utf-8"))
        for change in ("uppercase", "traversal", "missing_payload"):
            with self.subTest(change=change):
                tile = json.loads(json.dumps(original))
                if change == "uppercase":
                    tile["sha256"]["height.r16"] = "A" * 64
                elif change == "traversal":
                    tile["sha256"]["../outside.bin"] = "a" * 64
                else:
                    tile["sha256"]["missing.r8"] = "a" * 64
                self._json("fixtures/tile_synth_0_0_0/tile.json", tile)
                with self.assertRaisesRegex(ValueError, "lowercase|safe relative|does not exist"):
                    ContractValidator(self.root).run()

    def test_bundle_copy_difference_fails_even_with_updated_hash(self) -> None:
        self._dataset()
        self._bundle()
        copy = self.fixtures / "bundle_synth" / "data" / "conditions.json"
        copy.write_bytes(copy.read_bytes() + b"\n")
        self._refresh_bundle()
        with self.assertRaisesRegex(ValueError, "byte for byte"):
            ContractValidator(self.root).run()

    def test_api_embedded_copy_preserves_internal_bytes(self) -> None:
        self._dataset()
        path = self._api()
        path.write_bytes(path.read_bytes().replace(b'"value": 6.0', b'"value": 6.00'))
        with self.assertRaisesRegex(ValueError, "embedded conditions bytes differ"):
            ContractValidator(self.root).run()

    def test_bundle_inventory_size_and_oxum_are_checked(self) -> None:
        self._dataset()
        path = self._bundle()
        original = json.loads(path.read_text(encoding="utf-8"))
        for change in ("size", "oxum", "missing", "duplicate"):
            with self.subTest(change=change):
                bundle = json.loads(json.dumps(original))
                if change == "size":
                    bundle["files"][0]["size"] += 1
                elif change == "oxum":
                    bundle["payload_oxum"] = "0.0"
                elif change == "missing":
                    bundle["files"].pop()
                else:
                    bundle["files"].append(bundle["files"][0])
                self._json("fixtures/bundle_synth/bundle.json", bundle)
                with self.assertRaisesRegex(
                    ValueError, "size mismatch|oxum mismatch|inventory mismatch|Duplicate"
                ):
                    ContractValidator(self.root).run()

    def test_routing_and_edit_goldens_are_recognized(self) -> None:
        self._schema("edit_log")
        self._json("fixtures/edits/edits_01.json", {"schema_version": 1, "edits": []})
        golden = self.fixtures / "edits" / "expected_01.sha256"
        golden.write_bytes(("a" * 64 + "\n").encode("ascii"))
        grid = self.fixtures / "routing" / "costgrid_synth.bin"
        grid.parent.mkdir()
        grid.write_bytes(b"grid golden")
        ContractValidator(self.root).run()
        golden.write_bytes(b"invalid\n")
        with self.assertRaisesRegex(ValueError, "SHA-256 golden"):
            ContractValidator(self.root).run()

    def _openapi(self, example: dict[str, Any], schema: dict[str, Any]) -> None:
        specification = {
            "openapi": "3.1.0",
            "info": {"title": "Test API", "version": "1.0.0"},
            "paths": {
                "/test": {
                    "get": {
                        "responses": {
                            "200": {
                                "description": "Test response",
                                "content": {
                                    "application/json": {
                                        "schema": schema,
                                        "examples": {"golden": example},
                                    }
                                },
                            }
                        }
                    }
                }
            },
        }
        (self.root / "openapi.yaml").write_bytes(yaml.safe_dump(specification).encode("utf-8"))

    def test_openapi_examples_are_validated_against_response_schema(self) -> None:
        self._openapi({"value": "wrong"}, {"type": "integer"})
        with self.assertRaisesRegex((ValueError, OpenAPIValidationError), "example|integer"):
            ContractValidator(self.root).run()
        self._openapi({"value": 1}, {"type": "integer"})
        ContractValidator(self.root).run()

    def test_openapi_external_fixture_example_is_validated(self) -> None:
        self._schema("mission")
        self._json("fixtures/mission/mission.json", {"count": 1})
        schema = {"type": "object", "properties": {"count": {"type": "integer"}}}
        self._openapi({"$ref": "fixtures/mission/mission.json"}, schema)
        ContractValidator(self.root).run()
        self._json("fixtures/mission/mission.json", {"count": "wrong"})
        with self.assertRaisesRegex((ValueError, OpenAPIValidationError), "example|integer"):
            ContractValidator(self.root).run()

    def test_invalid_openapi_is_rejected(self) -> None:
        (self.root / "openapi.yaml").write_bytes(b"openapi: 3.1.0\n")
        with self.assertRaises(OpenAPIValidationError):
            ContractValidator(self.root).run()


if __name__ == "__main__":
    unittest.main()
