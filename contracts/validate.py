# /// script
# requires-python = ">=3.12"
# dependencies = [
#   "jsonschema[format]>=4.23,<5",
#   "openapi-spec-validator>=0.7.2,<0.8",
#   "pyyaml>=6.0.2,<7",
#   "referencing>=0.35,<0.38",
# ]
# ///
"""Validate contract fixtures, payload integrity, copies, and OpenAPI examples."""

from __future__ import annotations

import fnmatch
import hashlib
import json
import logging
import re
import sys
import time
from pathlib import Path, PurePosixPath
from typing import Any
from urllib.parse import unquote, urldefrag, urljoin, urlparse

import yaml
from jsonschema import Draft202012Validator, FormatChecker
from openapi_spec_validator import OpenAPIV31SpecValidator
from referencing import Registry, Resource
from referencing.jsonschema import DRAFT202012

LOGGER = logging.getLogger("contracts")
DRAFT_URI = "https://json-schema.org/draft/2020-12/schema"
SHA256 = re.compile(r"[0-9a-f]{64}\Z")
SPACE = re.compile(r"[ \t\r\n]*")


def _require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def _unique_object(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        _require(key not in result, f"Duplicate JSON key: {key!r}")
        result[key] = value
    return result


def _reject_constant(value: str) -> None:
    raise ValueError(f"Non-finite JSON number: {value}")


def _fixture_files(directory: Path) -> list[Path]:
    return sorted(
        path.resolve()
        for path in directory.rglob("*")
        if path.is_file() and not (path.name == ".gitkeep" and path.stat().st_size == 0)
    )


def _read_json(path: Path) -> Any:
    raw = path.read_bytes()
    _require(not raw.startswith(b"\xef\xbb\xbf"), f"UTF-8 BOM is forbidden: {path}")
    _require(b"\r" not in raw, f"Use LF line endings: {path}")
    try:
        return json.loads(
            raw.decode("utf-8"),
            object_pairs_hook=_unique_object,
            parse_constant=_reject_constant,
        )
    except (ValueError, UnicodeError) as error:
        raise ValueError(f"Invalid JSON in {path}: {error}") from error


def _local_file(base: Path, relative: str, boundary: Path) -> Path:
    _require(isinstance(relative, str), "File paths must be strings")
    parts = PurePosixPath(relative)
    _require(
        bool(relative)
        and not parts.is_absolute()
        and ".." not in parts.parts
        and "\\" not in relative
        and ":" not in relative,
        f"Expected a safe relative POSIX path: {relative!r}",
    )
    target = (base / relative).resolve()
    _require(target.is_relative_to(boundary.resolve()), f"Path escapes fixtures: {target}")
    _require(target.is_file(), f"Referenced file does not exist: {target}")
    return target


def _json_value_bytes(path: Path, member: str | None = None) -> bytes:
    """Extract a JSON value without changing its internal whitespace or key order."""
    text = path.read_text(encoding="utf-8")
    decoder = json.JSONDecoder()
    start = SPACE.match(text, 0).end()
    if member is None:
        _, end = decoder.raw_decode(text, start)
        return text[start:end].encode("utf-8")
    _require(text[start] == "{", f"Expected a JSON object: {path}")
    position = start + 1
    while True:
        position = SPACE.match(text, position).end()
        _require(text[position] != "}", f"Missing member {member!r} in {path}")
        key, position = decoder.raw_decode(text, position)
        position = SPACE.match(text, position).end()
        _require(text[position] == ":", f"Expected ':' in {path}")
        start = SPACE.match(text, position + 1).end()
        _, end = decoder.raw_decode(text, start)
        if key == member:
            return text[start:end].encode("utf-8")
        position = SPACE.match(text, end).end()
        _require(text[position] == ",", f"Missing member {member!r} in {path}")
        position += 1


class ContractValidator:
    """Validate one contracts directory without requiring other project components."""

    def __init__(self, root: Path) -> None:
        self.root = root.resolve()
        self.fixtures = self.root / "fixtures"
        self.schemas: dict[str, dict[str, Any]] = {}
        self.documents: dict[Path, Any] = {}
        self.covered_assets: set[Path] = set()
        self.registry: Registry = Registry()

    def _validate(self, document: Any, reference: str, label: str) -> None:
        validator = Draft202012Validator(
            {"$ref": reference}, registry=self.registry, format_checker=FormatChecker()
        )
        errors = list(validator.iter_errors(document))
        if errors:
            details = " | ".join(f"{error.json_path}: {error.message}" for error in errors)
            raise ValueError(f"Schema validation failed for {label}: {details}")

    def _load_schemas(self) -> dict[str, str]:
        directory = self.root / "schemas"
        resources: dict[str, Resource] = {}
        for path in sorted(directory.rglob("*.json")):
            if path == directory / "map.json":
                continue
            schema = _read_json(path)
            _require(
                isinstance(schema, dict) and schema.get("$schema") == DRAFT_URI,
                f"Schema must declare JSON Schema Draft 2020-12: {path}",
            )
            Draft202012Validator.check_schema(schema)
            resource = Resource.from_contents(schema)
            uri = path.as_uri()
            aliases = {uri}
            if "$id" in schema:
                aliases.add(urljoin(uri, schema["$id"]))
            for alias in aliases:
                _require(alias not in resources, f"Duplicate schema identifier: {alias}")
                resources[alias] = resource
            self.schemas[path.relative_to(directory).as_posix()] = schema
        self.registry = Registry().with_resources(resources.items())
        _require("map.schema.json" in self.schemas, "Missing schemas/map.schema.json")
        mapping = _read_json(directory / "map.json")
        self._validate(mapping, (directory / "map.schema.json").as_uri(), "map.json")
        return mapping

    def _load_fixtures(self, mapping: dict[str, str]) -> list[Path]:
        files = _fixture_files(self.fixtures)
        for path in files:
            _require(path.is_relative_to(self.fixtures), f"Fixture escapes directory: {path}")
            with path.open("rb") as stream:
                _require(
                    not stream.read(128).startswith(b"version https://git-lfs.github.com/spec/v1"),
                    f"Git LFS pointer is forbidden in contracts fixtures: {path}",
                )
            if path.suffix.lower() != ".json":
                continue
            relative = path.relative_to(self.root).as_posix()
            parts = PurePosixPath(relative).parts
            matches = [
                name
                for pattern, name in mapping.items()
                if len(parts) == len(PurePosixPath(pattern).parts)
                and all(
                    fnmatch.fnmatchcase(part, glob)
                    for part, glob in zip(parts, PurePosixPath(pattern).parts, strict=True)
                )
            ]
            _require(
                len(matches) == 1,
                f"Expected exactly one map.json pattern for {relative}; found {len(matches)}",
            )
            name = matches[0]
            _require(name in self.schemas, f"Missing schema {name!r} required by {relative}")
            document = _read_json(path)
            self._validate(document, (self.root / "schemas" / name).as_uri(), relative)
            self.documents[path] = document
        return files

    def _hash(self, path: Path, expected: str, source: Path) -> None:
        _require(
            isinstance(expected, str) and SHA256.fullmatch(expected) is not None,
            f"Expected 64 lowercase hexadecimal SHA-256 characters in {source}: {expected!r}",
        )
        with path.open("rb") as stream:
            actual = hashlib.file_digest(stream, "sha256").hexdigest()
        _require(
            actual == expected,
            f"SHA-256 mismatch: {path} (declared in {source}); expected {expected}, got {actual}",
        )
        self.covered_assets.add(path)

    def _canonical_tile(self, entry: dict[str, Any], zone_id: str) -> Path:
        matches = []
        for path, tile in self.documents.items():
            if path.parent.parent != self.fixtures or path.name != "tile.json":
                continue
            if tile.get("zone_id") == zone_id and all(
                tile.get(key) == entry[key] for key in ("z", "x", "y")
            ):
                matches.append(path)
        _require(
            len(matches) == 1,
            f"Expected one original tile for zone {zone_id!r}, coordinates {entry}; found {len(matches)}",
        )
        return matches[0]

    def _check_tile(self, path: Path, tile: dict[str, Any]) -> None:
        hashes = tile["sha256"]
        _require(isinstance(hashes, dict), f"Expected a SHA-256 object in {path}")
        required = {tile["height"]["file"], "slope.r8", "roughness.r8", "hazard.r8"}
        required.update(layer["file"] for layer in tile["layers"])
        _require(
            required.issubset(hashes),
            f"Missing tile payload hashes in {path}: {sorted(required - hashes.keys())}",
        )
        for name, expected in hashes.items():
            _require(name != "tile.json", f"A tile cannot hash its own manifest: {path}")
            target = _local_file(path.parent, name, self.fixtures)
            self._hash(target, expected, path)

    def _check_zone(self, path: Path, zone: dict[str, Any]) -> None:
        for entry in zone["tiles"]:
            if path.parent == self.fixtures / "bundle_synth" / "data":
                name = f"tiles/{entry['z']}/{entry['x']}/{entry['y']}/tile.json"
                target = _local_file(path.parent, name, self.fixtures)
            else:
                target = self._canonical_tile(entry, zone["zone_id"])
            tile = self.documents[target]
            _require(
                tile["version"] == entry["version"], f"Tile version mismatch in {path}: {target}"
            )
            self._hash(target, entry["sha256"], path)
        for pin in zone["pov"]:
            target = _local_file(path.parent / "pov", pin["file"], self.fixtures)
            self._hash(target, pin["sha256"], path)

    def _check_bundle(self, path: Path, bundle: dict[str, Any]) -> None:
        listed: set[Path] = set()
        total = 0
        for entry in bundle["files"]:
            _require(
                entry["path"].startswith("data/"),
                f"Bundle payload must be under data/: {entry['path']}",
            )
            target = _local_file(path.parent, entry["path"], self.fixtures)
            _require(target not in listed, f"Duplicate bundle payload: {target}")
            listed.add(target)
            self._hash(target, entry["sha256"], path)
            size = target.stat().st_size
            _require(size == entry["size"], f"Bundle file size mismatch: {target}")
            total += size
        actual = set(_fixture_files(path.parent / "data"))
        _require(
            actual == listed, f"Bundle file inventory mismatch in {path}: {sorted(actual ^ listed)}"
        )
        _require(
            bundle["payload_oxum"] == f"{total}.{len(listed)}",
            f"Bundle payload_oxum mismatch in {path}",
        )

    def _check_copies(self) -> None:
        api = self.fixtures / "api" / "zone.json"
        if api in self.documents:
            for member in ("zone", "conditions"):
                original = _local_file(self.fixtures, f"{member}/{member}.json", self.fixtures)
                _require(
                    _json_value_bytes(api, member) == _json_value_bytes(original),
                    f"API embedded {member} bytes differ from original: {api} vs {original}",
                )
        data = self.fixtures / "bundle_synth" / "data"
        for copy in _fixture_files(data):
            parts = copy.relative_to(data).parts
            if len(parts) == 1 and parts[0] in {"zone.json", "conditions.json"}:
                original = self.fixtures / copy.stem / copy.name
            elif len(parts) == 2 and parts[0] == "pov":
                original = self.fixtures / "zone" / "pov" / parts[1]
            elif len(parts) == 5 and parts[0] == "tiles":
                tile_path = copy.parent / "tile.json"
                _require(tile_path in self.documents, f"Missing copied tile manifest: {tile_path}")
                tile = self.documents[tile_path]
                _require(
                    tuple(str(tile[key]) for key in ("z", "x", "y")) == parts[1:4],
                    f"Tile coordinates do not match bundle path: {copy}",
                )
                original = self._canonical_tile(tile, tile["zone_id"]).parent / copy.name
            else:
                raise ValueError(f"No original fixture mapping for bundle copy: {copy}")
            _require(original.is_file(), f"Missing original fixture for {copy}: {original}")
            _require(
                copy.read_bytes() == original.read_bytes(),
                f"Fixture copy differs byte for byte: {copy} vs {original}",
            )

    def _check_assets(self, files: list[Path]) -> None:
        for path in files:
            if path in self.documents or path in self.covered_assets:
                continue
            relative = path.relative_to(self.fixtures).as_posix()
            if relative == "routing/costgrid_synth.bin":
                _require(path.stat().st_size > 0, f"Empty routing golden: {path}")
                continue
            match = re.fullmatch(r"edits/expected_([0-9]{2})\.sha256", relative)
            if match:
                _require(
                    SHA256.fullmatch(path.read_text(encoding="ascii").removesuffix("\n"))
                    is not None,
                    f"Expected a lowercase SHA-256 golden in {path}",
                )
                _require(
                    self.fixtures / "edits" / f"edits_{match[1]}.json" in self.documents,
                    f"Missing edit log for hash golden: {path}",
                )
                continue
            raise ValueError(f"Unmapped fixture or unreferenced payload: {relative}")

    def _example_reference(self, reference: str, base: Path) -> tuple[Any, Path]:
        location, fragment = urldefrag(reference)
        _require(
            not urlparse(location).scheme,
            f"Only local example references are supported: {reference}",
        )
        path = _local_file(base.parent, unquote(location), self.root) if location else base
        value = (
            _read_json(path)
            if path.suffix == ".json"
            else yaml.safe_load(path.read_text(encoding="utf-8"))
        )
        if fragment:
            _require(fragment.startswith("/"), f"Expected a JSON Pointer in {reference}")
            for part in unquote(fragment)[1:].split("/"):
                key = part.replace("~1", "/").replace("~0", "~")
                value = value[int(key)] if isinstance(value, list) else value[key]
        return value, path

    def _openapi_examples(self, value: Any, pointer: str, owner: Path) -> None:
        if isinstance(value, dict):
            if "schema" in value and ("example" in value or "examples" in value):
                examples = []
                if "example" in value:
                    examples.append(value["example"])
                for example in value.get("examples", {}).values():
                    seen: set[tuple[Path, str]] = set()
                    source = owner
                    while isinstance(example, dict) and "$ref" in example:
                        reference = example["$ref"]
                        marker = (source, reference)
                        _require(marker not in seen, f"Circular example reference: {reference}")
                        seen.add(marker)
                        example, source = self._example_reference(reference, source)
                    if isinstance(example, dict) and "externalValue" in example:
                        example, _ = self._example_reference(example["externalValue"], source)
                    elif isinstance(example, dict) and "value" in example:
                        example = example["value"]
                    examples.append(example)
                for example in examples:
                    self._validate(
                        example,
                        f"{owner.as_uri()}#{pointer}/schema",
                        f"OpenAPI example at {pointer}",
                    )
            for key, child in value.items():
                escaped = str(key).replace("~", "~0").replace("/", "~1")
                self._openapi_examples(child, f"{pointer}/{escaped}", owner)
        elif isinstance(value, list):
            for index, child in enumerate(value):
                self._openapi_examples(child, f"{pointer}/{index}", owner)

    def _check_openapi(self) -> None:
        path = self.root / "openapi.yaml"
        if not path.exists():
            LOGGER.info("Skipping OpenAPI validation: openapi.yaml does not exist")
            return
        specification = yaml.safe_load(path.read_text(encoding="utf-8"))
        _require(
            isinstance(specification, dict)
            and str(specification.get("openapi", "")).startswith("3.1."),
            "openapi.yaml must declare OpenAPI 3.1",
        )
        OpenAPIV31SpecValidator(specification, base_uri=path.as_uri()).validate()
        resource = Resource.from_contents(specification, default_specification=DRAFT202012)
        self.registry = self.registry.with_resource(path.as_uri(), resource)
        self._openapi_examples(specification, "", path)

    def run(self) -> None:
        """Run all checks, raising an exception on the first invalid contract."""
        self.schemas.clear()
        self.documents.clear()
        self.covered_assets.clear()
        mapping = self._load_schemas()
        files = self._load_fixtures(mapping)
        for path, document in self.documents.items():
            if path.name == "tile.json":
                self._check_tile(path, document)
            elif path.name == "zone.json" and path.parent.name != "api":
                self._check_zone(path, document)
            elif path.name == "bundle.json":
                self._check_bundle(path, document)
        self._check_copies()
        self._check_assets(files)
        self._check_openapi()
        LOGGER.info(
            "Contract validation passed schemas=%d fixtures=%d payloads=%d",
            len(self.schemas),
            len(self.documents),
            len(files) - len(self.documents),
        )


def main() -> int:
    """Return zero for valid contracts and one for any validation failure."""
    handler = logging.StreamHandler()
    formatter = logging.Formatter(
        "%(asctime)sZ %(levelname)s contracts %(message)s", datefmt="%Y-%m-%dT%H:%M:%S"
    )
    formatter.converter = time.gmtime
    handler.setFormatter(formatter)
    LOGGER.handlers = [handler]
    LOGGER.setLevel(logging.INFO)
    try:
        ContractValidator(Path(__file__).resolve().parent).run()
    except Exception as error:
        LOGGER.error("Contract validation failed: %s", error)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
