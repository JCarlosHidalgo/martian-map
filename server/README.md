# Habitat server

From `server/`, run `uv sync --locked` and then `uv run uvicorn habitat:app --host 0.0.0.0 --port 8000`.
The default persistent data directory is `server/data/`, which is ignored by Git. Set
`MM_DATA_DIR` to another directory when Earth and habitat share a different disk location.
The catalogue is `catalog.sqlite3`; imported zone files live under
`zones/<zone_id>/zone.json`, `conditions.json`, `tiles/<z>/<x>/<y>/`, and `pov/`.

The server starts with an empty catalogue. Once a complete zone has been installed on disk,
the bundle importer will call `ZoneStore.publish_zone(zone_id, zone_version)` to make it visible.
This method adopts Earth's version and advances the global `since` cursor. The importer and edit
endpoints are separate milestones. Until those exist, the live server serves the four read routes;
Prism remains the mock for `POST /edits` and `GET /zones/{id}/changes`.

Run `uv run pytest -q` for the synthetic integration tests. They stage temporary zone and
conditions JSON because the shared C1 zone and C6 conditions fixtures have not arrived yet.
