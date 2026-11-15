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

import copy
import hashlib
import json
import shutil
import subprocess
import sys
import tempfile
import unittest
from copy import deepcopy
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

    def _mission_fixture(self) -> dict[str, Any]:
        source = Path(__file__).parent
        shutil.copyfile(
            source / "schemas/mission.schema.json", self.root / "schemas/mission.schema.json"
        )
        return json.loads((source / "fixtures/mission/mission.json").read_text(encoding="utf-8"))

    def _validate_mission(self, mission: dict[str, Any]) -> None:
        self._json("fixtures/mission/mission.json", mission)
        ContractValidator(self.root).run()

    def test_mission_fixture_passes_real_contract_validation(self) -> None:
        self._validate_mission(self._mission_fixture())

    def test_mission_accepts_both_modes_and_an_omitted_base(self) -> None:
        original = self._mission_fixture()
        for mode in ("TSP", "OP"):
            for explicit_base in (False, True):
                with self.subTest(mode=mode, explicit_base=explicit_base):
                    mission = copy.deepcopy(original)
                    mission["mode"] = mode
                    if not explicit_base:
                        del mission["base_enu_m"]
                    self._validate_mission(mission)

    def test_mission_requires_all_documented_fields_and_rejects_unknown_fields(self) -> None:
        original = self._mission_fixture()
        for scope, fields in (
            ("root", ("schema_version", "zone_id", "name", "stops", "budget", "mode")),
            ("stop", ("id", "name", "enu_m", "objective", "science_value", "dwell_s")),
            ("budget", ("t_s", "dose_uGy")),
        ):
            for field in (*fields, "unexpected"):
                with self.subTest(scope=scope, field=field):
                    mission = copy.deepcopy(original)
                    target = (
                        mission["stops"][0]
                        if scope == "stop"
                        else mission["budget"]
                        if scope == "budget"
                        else mission
                    )
                    if field == "unexpected":
                        target[field] = "unsupported"
                    else:
                        del target[field]
                    with self.assertRaisesRegex(ValueError, "Schema validation failed"):
                        self._validate_mission(mission)

    def test_mission_checks_science_values_times_and_budgets(self) -> None:
        original = self._mission_fixture()
        for field, invalid in (
            ("science_value", (-1, 11, 0.5, True, "5")),
            ("dwell_s", (-1, True, "600")),
            ("t_s", (-1, True, "14400")),
            ("dose_uGy", (-1, True, "50")),
        ):
            for value in invalid:
                with self.subTest(field=field, value=value):
                    mission = copy.deepcopy(original)
                    target = (
                        mission["stops"][0]
                        if field in ("science_value", "dwell_s")
                        else mission["budget"]
                    )
                    target[field] = value
                    with self.assertRaisesRegex(ValueError, "Schema validation failed"):
                        self._validate_mission(mission)
        for science_value in (0, 10):
            with self.subTest(science_value=science_value):
                mission = copy.deepcopy(original)
                mission["stops"][0].update(science_value=science_value, dwell_s=0)
                mission["budget"] = {"t_s": 0, "dose_uGy": 0}
                self._validate_mission(mission)
        mission = copy.deepcopy(original)
        mission["stops"][0]["dwell_s"] = 0.5
        self._validate_mission(mission)

    def test_mission_coordinates_are_two_numbers_in_metres(self) -> None:
        original = self._mission_fixture()
        for scope in ("base", "stop"):
            for value in (None, [], [0], [0, 0, 0], ["0", 0], [True, 0], {"e": 0, "n": 0}):
                with self.subTest(scope=scope, value=value):
                    mission = copy.deepcopy(original)
                    target = mission if scope == "base" else mission["stops"][0]
                    target["base_enu_m" if scope == "base" else "enu_m"] = value
                    with self.assertRaisesRegex(ValueError, "Schema validation failed"):
                        self._validate_mission(mission)
        mission = copy.deepcopy(original)
        mission["base_enu_m"] = [-100.5, -20.25]
        mission["stops"][0]["enu_m"] = [0.5, -20.25]
        self._validate_mission(mission)

    def test_mission_rejects_invalid_labels_version_zone_and_mode(self) -> None:
        original = self._mission_fixture()
        for field, values in (
            ("schema_version", (2, True, "1")),
            ("zone_id", ("SYNTH", "s", "synth\n", "s" * 33)),
            ("name", ("", None)),
            ("mode", ("tsp", "unknown", None)),
            ("stops", ({}, None)),
        ):
            for value in values:
                with self.subTest(field=field, value=value):
                    mission = copy.deepcopy(original)
                    mission[field] = value
                    with self.assertRaisesRegex(ValueError, "Schema validation failed"):
                        self._validate_mission(mission)
        for field in ("id", "name", "objective"):
            for value in ("", None):
                with self.subTest(stop_field=field, value=value):
                    mission = copy.deepcopy(original)
                    mission["stops"][0][field] = value
                    with self.assertRaisesRegex(ValueError, "Schema validation failed"):
                        self._validate_mission(mission)

    def test_mission_fixture_matches_routing_plan_endpoints_and_dwell_times(self) -> None:
        mission = self._mission_fixture()
        source = Path(__file__).parent / "fixtures/routing/expected_plan.json"
        plans = json.loads(source.read_text(encoding="utf-8"))["plans"]
        self.assertEqual(mission["zone_id"], "synth")
        self.assertEqual(mission["base_enu_m"], [-100, -20])
        self.assertEqual([stop["enu_m"] for stop in mission["stops"]], [[0, -20], [100, -20]])
        self.assertEqual([stop["science_value"] for stop in mission["stops"]], [5, 8])
        self.assertEqual([stop["dwell_s"] for stop in mission["stops"]], [600, 600])
        self.assertEqual(mission["budget"], {"t_s": 14400, "dose_uGy": 50})
        self.assertEqual(mission["mode"], "TSP")
        points = [
            mission["base_enu_m"],
            *[stop["enu_m"] for stop in mission["stops"]],
            mission["base_enu_m"],
        ]
        for plan in plans:
            self.assertEqual(plan["order"], [stop["id"] for stop in mission["stops"]])
            self.assertEqual(len(plan["legs"]), len(points) - 1)
            for index, leg in enumerate(plan["legs"]):
                self.assertEqual(leg["polyline_enu_m"][0][:2], points[index])
                self.assertEqual(leg["polyline_enu_m"][-1][:2], points[index + 1])
            for percentile in ("p10", "p50", "p90"):
                expected = sum(leg["eta_s"][percentile] for leg in plan["legs"])
                expected += sum(stop["dwell_s"] for stop in mission["stops"])
                self.assertAlmostEqual(plan["total_s"][percentile], expected, delta=0.01)

    def test_mission_fixture_base_and_stops_are_walkable_tile_samples(self) -> None:
        mission = self._mission_fixture()
        directory = Path(__file__).parent / "fixtures/tile_synth_0_0_0"
        tile = json.loads((directory / "tile.json").read_text(encoding="utf-8"))
        hazard = (directory / "hazard.r8").read_bytes()
        height = (directory / tile["height"]["file"]).read_bytes()
        size = tile["size_px"]
        origin_e, origin_n = tile["origin_enu_m"]
        points = [mission["base_enu_m"], *[stop["enu_m"] for stop in mission["stops"]]]
        for east, north in points:
            with self.subTest(east=east, north=north):
                col = (east - origin_e) / tile["m_per_px"]
                row = size - 1 - (north - origin_n) / tile["m_per_px"]
                self.assertEqual(col, int(col))
                self.assertEqual(row, int(row))
                self.assertTrue(0 <= col < size and 0 <= row < size)
                index = int(row) * size + int(col)
                self.assertEqual(hazard[index], 0)
                sample = int.from_bytes(height[index * 2 : index * 2 + 2], "little")
                self.assertNotEqual(sample, tile["height"]["nodata"])

    def _edit_identifier_fixture(self) -> dict[str, Any]:
        source = Path(__file__).parent
        for name in ("edit.schema.json", "edit_log.schema.json"):
            shutil.copyfile(source / "schemas" / name, self.root / "schemas" / name)
        log = json.loads((source / "fixtures/merge/log_in.json").read_text(encoding="utf-8"))
        return log["edits"][0]

    def test_edit_identifiers_reject_surrounding_whitespace_and_controls(self) -> None:
        original = self._edit_identifier_fixture()
        for field in ("uuid", "entity_id", "author", "creator", "zone_id"):
            for extra in ("\n", "\r", "\r\n", "\t", " ", "\u2028", "\u2029", "\x00"):
                for position in ("prefix", "suffix"):
                    with self.subTest(field=field, extra=repr(extra), position=position):
                        edit = original.copy()
                        value = original[field]
                        edit[field] = extra + value if position == "prefix" else value + extra
                        self._json(
                            "fixtures/merge/log_in.json", {"schema_version": 1, "edits": [edit]}
                        )
                        with self.assertRaisesRegex(ValueError, "Schema validation failed"):
                            ContractValidator(self.root).run()

    def test_edit_identifiers_accept_valid_values_and_boundaries(self) -> None:
        original = self._edit_identifier_fixture()
        uuids = (
            "00000000-0000-4000-8000-000000000000",
            "aaaaaaaa-aaaa-4aaa-9aaa-aaaaaaaaaaaa",
            "ffffffff-ffff-4fff-bfff-ffffffffffff",
        )
        values = {
            "uuid": uuids,
            "entity_id": uuids,
            "author": ("a-", "ev1", "a" * 16),
            "creator": ("a-", "ev1", "a" * 16),
            "zone_id": ("a0", "gale-south", "a" + "0" * 31),
        }
        for field, candidates in values.items():
            for value in candidates:
                with self.subTest(field=field, value=value):
                    self._json(
                        "fixtures/merge/log_in.json",
                        {"schema_version": 1, "edits": [{**original, field: value}]},
                    )
                    ContractValidator(self.root).run()

    def test_edit_identifiers_reject_invalid_formats_and_lengths(self) -> None:
        original = self._edit_identifier_fixture()
        invalid_uuids = (
            "AAAAAAAA-AAAA-4AAA-8AAA-AAAAAAAAAAAA",
            "aaaaaaaa-aaaa-3aaa-8aaa-aaaaaaaaaaaa",
            "aaaaaaaaaaaa4aaa8aaaaaaaaaaaaaaa",
        )
        values = {
            "uuid": invalid_uuids,
            "entity_id": invalid_uuids,
            "author": ("a", "a" * 17, "EV1"),
            "creator": ("a", "a" * 17, "EV1"),
            "zone_id": ("a", "a" * 33, "1synth", "SYNTH"),
        }
        for field, candidates in values.items():
            for value in candidates:
                with self.subTest(field=field, value=value):
                    self._json(
                        "fixtures/merge/log_in.json",
                        {"schema_version": 1, "edits": [{**original, field: value}]},
                    )
                    with self.assertRaisesRegex(ValueError, "Schema validation failed"):
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


class ConditionsContractTests(unittest.TestCase):
    def setUp(self) -> None:
        temporary = tempfile.TemporaryDirectory(prefix="martian-map-conditions-")
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        directory = self.root / "schemas"
        directory.mkdir()
        source = Path(__file__).parent
        for name in ("map.json", "map.schema.json", "conditions.schema.json"):
            shutil.copyfile(source / "schemas" / name, directory / name)
        self.original = json.loads(
            (source / "fixtures/conditions/conditions.json").read_text(encoding="utf-8")
        )

    def _write(
        self, value: dict[str, Any], relative: str = "fixtures/conditions/conditions.json"
    ) -> Path:
        path = self.root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes((json.dumps(value, indent=2) + "\n").encode("utf-8"))
        return path

    def test_conditions_golden_is_explicitly_synthetic_and_matches_c4_inputs(self) -> None:
        self._write(self.original)
        ContractValidator(self.root).run()
        # Shared synthetic source and C4 FixtureGen inputs from the contract guide.
        self.assertEqual(self.original["value"], 6.0)
        self.assertEqual(self.original["uncertainty"], 0.2)
        self.assertEqual(self.original["basis"], "modelled")
        self.assertIn("not a mission measurement", self.original["derived_from"])
        modifier = self.original["modifiers"][0]
        self.assertEqual(modifier["type"], "WSAEnlilSimulations")
        self.assertEqual(modifier["factor"], 0.95)
        self.assertEqual(modifier["valid_from"], "2026-11-14T00:00:00Z")
        self.assertEqual(modifier["valid_to"], "2026-11-15T00:00:00Z")

    def test_conditions_required_and_unknown_fields_are_checked(self) -> None:
        for scope, fields in (
            (
                "root",
                (
                    "schema_version",
                    "source_id",
                    "t_utc",
                    "t_valid_utc",
                    "quantity",
                    "value",
                    "unit",
                    "uncertainty",
                    "basis",
                    "modifiers",
                ),
            ),
            ("modifier", ("type", "factor", "valid_from", "valid_to")),
        ):
            for field in (*fields, "unexpected"):
                with self.subTest(scope=scope, field=field):
                    value = deepcopy(self.original)
                    target = value if scope == "root" else value["modifiers"][0]
                    if field == "unexpected":
                        target[field] = True
                    else:
                        del target[field]
                    self._write(value)
                    with self.assertRaisesRegex(ValueError, "Schema validation failed"):
                        ContractValidator(self.root).run()

    def test_conditions_optional_fields_and_free_activity_types_pass(self) -> None:
        for basis, activity in (("measured", "CME"), ("modelled", "SEP")):
            with self.subTest(basis=basis, activity=activity):
                value = deepcopy(self.original)
                value["basis"] = basis
                value.pop("derived_from")
                value["sol"] = 4710
                value["modifiers"][0].update(
                    type=activity, factor=2, donki_activity_id="synthetic-activity"
                )
                self._write(value)
                ContractValidator(self.root).run()
        value = deepcopy(self.original)
        value["modifiers"] = []
        self._write(value)
        ContractValidator(self.root).run()

    def test_conditions_reject_invalid_values_and_identifiers(self) -> None:
        for field, candidates in (
            ("schema_version", (2, True, "1")),
            ("source_id", ("", "synth-dose", "SYNTH-generator-dose", "synth-generator-dose\n")),
            ("quantity", ("temperature", None)),
            ("unit", ("mSv/h", "uGy", None)),
            ("basis", ("unknown", "MEASURED", None)),
            ("value", (0, -1, True, "6", 10**400)),
            ("uncertainty", (-0.1, True, "0.2", 10**400)),
            ("sol", (-1, True, 0.5, 2147483648)),
            ("modifiers", (None, {})),
            ("derived_from", (None, "")),
        ):
            for candidate in candidates:
                with self.subTest(field=field, candidate=candidate):
                    value = deepcopy(self.original)
                    value[field] = candidate
                    self._write(value)
                    with self.assertRaisesRegex(ValueError, "Schema validation failed"):
                        ContractValidator(self.root).run()

    def test_conditions_reject_invalid_modifier_values(self) -> None:
        for field, candidates in (
            ("factor", (0, -1, True, "0.95", 10**400)),
            ("type", ("", " ", None)),
            ("donki_activity_id", ("", None)),
        ):
            for candidate in candidates:
                with self.subTest(field=field, candidate=candidate):
                    value = deepcopy(self.original)
                    value["modifiers"][0][field] = candidate
                    self._write(value)
                    with self.assertRaisesRegex(ValueError, "Schema validation failed"):
                        ContractValidator(self.root).run()

    def test_conditions_utc_dates_support_c4_precision_and_reject_invalid_dates(self) -> None:
        for timestamp in ("2026-11-14T12:00:00.1234567Z", "2026-11-14T12:00:00+00:00"):
            with self.subTest(valid=timestamp):
                value = deepcopy(self.original)
                value["t_utc"] = timestamp
                self._write(value)
                ContractValidator(self.root).run()
        for timestamp in (
            "2026-02-30T12:00:00Z",
            "2026-11-14T12:00:00",
            "2026-11-14T12:00:00+01:00",
            "2026-11-14T12:00:00.12345678Z",
            "2026-11-14T12:00:00Z\n",
        ):
            with self.subTest(invalid=timestamp):
                value = deepcopy(self.original)
                value["t_utc"] = timestamp
                self._write(value)
                with self.assertRaisesRegex(ValueError, "Schema validation failed"):
                    ContractValidator(self.root).run()

    def test_conditions_modifier_windows_are_checked_in_canonical_and_bundle_copies(self) -> None:
        self._write(self.original)
        for relative in (
            "fixtures/conditions/conditions.json",
            "fixtures/bundle_synth/data/conditions.json",
        ):
            for end in ("2026-11-13T00:00:00Z", "2026-11-14T00:00:00.0000000+00:00"):
                with self.subTest(relative=relative, end=end):
                    value = deepcopy(self.original)
                    value["modifiers"][0]["valid_to"] = end
                    path = self._write(value, relative)
                    with self.assertRaisesRegex(ValueError, "valid_from < valid_to"):
                        ContractValidator(self.root).run()
                    if relative.startswith("fixtures/bundle_synth/"):
                        path.unlink()
                    else:
                        self._write(self.original)

    def test_conditions_window_comparison_preserves_100_nanosecond_precision(self) -> None:
        value = deepcopy(self.original)
        value["modifiers"][0].update(
            valid_from="2026-11-14T12:00:00.0000001Z",
            valid_to="2026-11-14T12:00:00.0000002+00:00",
        )
        self._write(value)
        ContractValidator(self.root).run()

    def test_conditions_reject_overflowed_json_numbers(self) -> None:
        path = self._write(self.original)
        path.write_bytes(path.read_bytes().replace(b'"value": 6.0', b'"value": 1e999'))
        with self.assertRaisesRegex(ValueError, "Schema validation failed"):
            ContractValidator(self.root).run()


class LayerSourceContractTests(unittest.TestCase):
    """Check the C2 golden and producer-facing errors through the real validator."""

    def setUp(self) -> None:
        temporary = tempfile.TemporaryDirectory(prefix="martian-map-layer-source-")
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.source = Path(__file__).parent
        schemas = self.root / "schemas"
        schemas.mkdir()
        for name in ("map.json", "map.schema.json", "layer_source.schema.json"):
            shutil.copyfile(self.source / "schemas" / name, schemas / name)
        self.fixture = json.loads(
            (self.source / "fixtures/layer_source/layer_source.json").read_text(encoding="utf-8")
        )
        self.path = self.root / "fixtures/layer_source/layer_source.json"
        self._write(self.path, self.fixture)

    def _write(self, path: Path, value: Any) -> None:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes((json.dumps(value, indent=2) + "\n").encode("utf-8"))

    def _assert_invalid(self, value: dict[str, Any]) -> None:
        self._write(self.path, value)
        with self.assertRaisesRegex(ValueError, "Schema validation failed"):
            ContractValidator(self.root).run()

    def test_layer_source_golden_passes(self) -> None:
        ContractValidator(self.root).run()

    def test_layer_source_requires_document_version_and_source_fields(self) -> None:
        fields = (
            "schema_version",
            "id",
            "kind",
            "adapter",
            "mission",
            "instrument",
            "product_id",
            "time_range",
            "license",
            "url",
            "qualitative",
        )
        for field in fields:
            with self.subTest(field=field):
                value = deepcopy(self.fixture)
                del value[field]
                self._assert_invalid(value)

    def test_layer_source_rejects_invalid_values(self) -> None:
        cases = (
            ("schema_version", 2),
            ("schema_version", True),
            ("id", "MRO-HiRISE-DTM"),
            ("id", "source with spaces"),
            ("id", "mro-hirise"),
            ("kind", "vector"),
            ("adapter", "unknown"),
            ("mission", ""),
            ("instrument", "   "),
            ("product_id", ""),
            ("url", "not a URI"),
            ("license", "unlisted license"),
            ("qualitative", "false"),
            ("resolution_m", 0),
            ("resolution_m", -1),
            ("resolution_m", True),
            ("vertical_datum", "sphere"),
        )
        for field, invalid in cases:
            with self.subTest(field=field, value=invalid):
                value = deepcopy(self.fixture)
                value[field] = invalid
                self._assert_invalid(value)

    def test_layer_source_raster_and_image_rules(self) -> None:
        missing_resolution = deepcopy(self.fixture)
        del missing_resolution["resolution_m"]
        self._assert_invalid(missing_resolution)

        for kind in ("timeseries", "point", "image"):
            with self.subTest(kind=kind):
                value = deepcopy(self.fixture)
                value["kind"] = kind
                value.pop("resolution_m")
                value.pop("vertical_datum")
                if kind == "image":
                    value.pop("units")
                self._write(self.path, value)
                ContractValidator(self.root).run()
                value["resolution_m"] = 1
                self._assert_invalid(value)

        image = deepcopy(self.fixture)
        image["kind"] = "image"
        image.pop("resolution_m")
        image.pop("vertical_datum")
        self._assert_invalid(image)

    def test_layer_source_vertical_datum_is_conditional(self) -> None:
        value = deepcopy(self.fixture)
        value.pop("vertical_datum")
        self._assert_invalid(value)

        value.pop("units")
        value.pop("coverage_geojson")
        self._write(self.path, value)
        ContractValidator(self.root).run()

        value["units"] = "J m-2 K-1 s-1/2"
        self._write(self.path, value)
        ContractValidator(self.root).run()
        value["vertical_datum"] = "areoid"
        self._assert_invalid(value)

        value = deepcopy(self.fixture)
        value["kind"] = "point"
        value.pop("resolution_m")
        self._assert_invalid(value)

    def test_layer_source_utc_dates_and_interval_shape(self) -> None:
        invalid_times = (
            "2026-11-14T00:00:00+00:00",
            "2026-11-14T00:00:00.000Z",
            "2026-11-14T00:00:00z",
            "2026-02-30T00:00:00Z",
            "2026-11-14",
        )
        for invalid in invalid_times:
            with self.subTest(timestamp=invalid):
                value = deepcopy(self.fixture)
                value["time_range"] = [invalid, "2026-11-14T00:00:00Z"]
                self._assert_invalid(value)
        for interval in ([], ["2026-11-14T00:00:00Z"], self.fixture["time_range"] * 2):
            with self.subTest(interval=interval):
                value = deepcopy(self.fixture)
                value["time_range"] = interval
                self._assert_invalid(value)

    def test_layer_source_coverage_uses_c0_coordinates(self) -> None:
        value = deepcopy(self.fixture)
        for position in ([-180, -90], [180, 90], [0, 0]):
            with self.subTest(position=position):
                value["coverage_geojson"] = {"type": "Point", "coordinates": position}
                self._write(self.path, value)
                ContractValidator(self.root).run()
        for position in ([180.1, 0], [0, 90.1], [0], [0, 0, 0]):
            with self.subTest(position=position):
                value["coverage_geojson"] = {"type": "Point", "coordinates": position}
                self._assert_invalid(value)
        for coverage in (
            {"type": "LineString", "coordinates": [[0, 0], [1, 1]]},
            {"type": "Polygon", "coordinates": []},
            {"type": "Polygon", "coordinates": [[[0, 0], [1, 0], [0, 0]]]},
            {"type": "Point", "coordinates": [0, 0], "crs": "unexpected"},
        ):
            with self.subTest(coverage=coverage):
                value["coverage_geojson"] = coverage
                self._assert_invalid(value)

    def test_layer_source_omits_absent_optional_values_and_rejects_unknown_fields(self) -> None:
        for field in ("coverage_geojson", "units", "resolution_m", "vertical_datum"):
            with self.subTest(field=field):
                value = deepcopy(self.fixture)
                value[field] = None
                self._assert_invalid(value)
        value = deepcopy(self.fixture)
        value["unexpected_field"] = "unexpected"
        self._assert_invalid(value)

    def test_layer_source_embeds_in_zone_without_document_version(self) -> None:
        embedded = deepcopy(self.fixture)
        embedded.pop("schema_version")
        self._write(
            self.root / "schemas/zone.schema.json",
            {
                "$schema": DRAFT_URI,
                "type": "object",
                "additionalProperties": False,
                "required": ["zone_id", "tiles", "pov", "layer_sources"],
                "properties": {
                    "zone_id": {"type": "string"},
                    "tiles": {"type": "array"},
                    "pov": {"type": "array"},
                    "layer_sources": {
                        "type": "array",
                        "items": {"$ref": "layer_source.schema.json#/$defs/layer_source"},
                    },
                },
            },
        )
        zone_path = self.root / "fixtures/zone/zone.json"
        zone = {"zone_id": "synth", "tiles": [], "pov": [], "layer_sources": [embedded]}
        self._write(zone_path, zone)
        ContractValidator(self.root).run()
        embedded.pop("license")
        self._write(zone_path, zone)
        with self.assertRaisesRegex(ValueError, "Schema validation failed"):
            ContractValidator(self.root).run()


if __name__ == "__main__":
    unittest.main()
