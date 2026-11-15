# Habitat server

From `server/`, run `uv sync --locked` and then `uv run uvicorn habitat:app --host 0.0.0.0 --port 8000`.
The default persistent data directory is `server/data/`, which is ignored by Git. Set
`MM_DATA_DIR` to another directory when Earth and habitat share a different disk location.
The catalogue is `catalog.sqlite3`; imported zone files live under
`zones/<zone_id>/zone.json`, `conditions.json`, `tiles/<z>/<x>/<y>/`, and `pov/`.

The server starts with an empty catalogue. The importer scans `MM_SPOOL/incoming/` every second;
by default the spool is `server/data/spool/`. Earth writes complete, uncompressed C1b tar files
there. After the delivery time, the importer verifies the inventory and hashes, installs the zone,
records `bundle_id`, adopts Earth's `zone_version`, and advances the global `since` cursor.
Imported and duplicate bundles move to `done/`; rejected bundles move to `rejected/` with a
`<filename>.reason.json`. Earth alone owns `spool/state/`.

The C1b, C1 zone and C6 conditions schemas and shared bundle fixture are still pending from their
producers. `importer.py` marks the exact points where those documents must be validated once they
arrive. The synthetic tests exercise the documented shape in the meantime.

`POST /edits` validates against the shared Edit schema and stores accepted edits in SQLite.
`GET /zones/{id}/changes` sends current winners with a resumable global sequence cursor.
For now, a zone publication delivers a conservative snapshot of all current tile references and
conditions on its final page. When C1/C6 fixtures arrive, this must be narrowed to the tile
versions and conditions that actually changed. The golden merge fixture is tested through HTTP;
the C# `Edits.Apply` equivalence test remains with P3.

Run `uv run pytest -q` for the synthetic integration tests. They stage temporary zone and
conditions JSON because the shared C1 zone and C6 conditions fixtures have not arrived yet.
