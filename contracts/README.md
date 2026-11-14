# Contract validation

Run these commands from the repository root in Windows PowerShell after installing `uv`:

```powershell
uv python install 3.12
uvx --from ruff==0.14.14 ruff check contracts/
uvx --from ruff==0.14.14 ruff format --check contracts/
uv run --locked contracts/validate.py
uv run --locked contracts/test_validate.py
git diff --check
```

`uv run contracts/validate.py` also works. PEP 723 metadata installs the script's dependencies
automatically. The committed script lockfiles record resolved dependencies; `--locked` rejects
outdated lockfiles. Refresh them intentionally after editing dependency metadata:

```powershell
uv lock --script contracts/validate.py
uv lock --script contracts/test_validate.py
```

To apply Python formatting and safe lint fixes:

```powershell
uvx --from ruff==0.14.14 ruff format contracts/
uvx --from ruff==0.14.14 ruff check --fix contracts/
```

## Fixture assignments

`schemas/map.json` assigns JSON documents to schema filenames. Patterns are case-sensitive,
relative to `contracts/`, and use POSIX `/` separators on every platform. Wildcards match one
path segment, never a directory separator. Its schema requires
the complete agreed assignment table. Each existing JSON fixture must match exactly one entry.
Every schema is checked as Draft 2020-12. Relative schema references are resolved from the local
schema registry, with format validation enabled and no automatic network retrieval.

Missing fixture directories and a missing `openapi.yaml` are allowed before H0. A schema named
in the map may be absent until its first fixture exists. Existing malformed schemas always fail.

Non-JSON payloads are not JSON Schema documents. Tile rasters and POV images must be referenced
by their manifests and pass SHA-256 verification. Bundle payloads must also match `files[]`,
file sizes, and `payload_oxum`. Unreferenced assets and unknown fixture files fail. The two
non-manifest golden formats in the guide are recognized explicitly:

- `fixtures/routing/costgrid_synth.bin`: must be nonempty; P3 tests its binary layout and contents.
- `fixtures/edits/expected_NN.sha256`: one lowercase 64-character hexadecimal hash, with an
  optional final LF, and a corresponding `edits_NN.json`. P3 verifies the hash after `apply()`;
  this tool does not implement the edit engine.

All contract fixtures must contain real data, never Git LFS pointers. `contracts/.gitattributes`
unsets the LFS filter within this directory and preserves binary payload bytes.

## Hashes and copies

Tile payload paths are relative to their `tile.json`. Standalone zones resolve tile manifests
by `zone_id` and `z/x/y` against original tile fixtures; bundled zones resolve them under
`data/tiles/z/x/y/`. POV files are under the zone's `pov/`. Bundle paths are relative to
`bundle.json` and must stay under `data/`. Hashes must be 64 lowercase hexadecimal characters.

Every file under `fixtures/bundle_synth/data/` must be byte-identical to its original:
`zone/zone.json`, `conditions/conditions.json`, the original tile directory, or `zone/pov/`.

`fixtures/api/zone.json` is a response wrapper with embedded `zone` and `conditions` JSON values.
Each value must preserve its original JSON value bytes, including internal whitespace, key
order, number spelling, and escaping. Only whitespace outside the JSON value is excluded.
Embed the original value text directly; re-indenting or reserializing it can change these bytes.
This check applies to the fixture producer; consumers may parse JSON normally.

## OpenAPI

When present, `openapi.yaml` must be valid OpenAPI 3.1. Media-type examples are also checked
against their response or request schema, including local references to fixture files,
inline examples, and local Example Object references. Example references stay inside
`contracts/`. External network examples are not downloaded.

## Tests

`test_validate.py` creates temporary schemas and fixtures. It exercises successful validation
and failures caused by malformed assignments, schema violations, hash corruption, inconsistent
copies, missing payloads, LFS pointers, and invalid OpenAPI examples. It creates no product
fixtures and does not depend on Unity, the server, ingestion, routing, or editing code.
