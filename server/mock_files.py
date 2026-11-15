"""Serve C1 fixture files while the habitat importer is being built."""

from __future__ import annotations

import json
from pathlib import Path

from fastapi import FastAPI
from fastapi.responses import FileResponse, JSONResponse, Response

FIXTURES = Path(__file__).resolve().parents[1] / "contracts" / "fixtures"
TILE = FIXTURES / "tile_synth_0_0_0"
ZONE = FIXTURES / "zone"
app = FastAPI(title="Martian Map file mock")


def missing() -> JSONResponse:
    """Return the C3 not-found shape for a missing fixture or old version."""
    return JSONResponse(
        {
            "title": "Not Found",
            "status": 404,
            "detail": "Fixture file not found",
            "code": "not_found",
        },
        status_code=404,
        media_type="application/problem+json",
    )


@app.get("/tiles/{zone}/{z}/{x}/{y}/{file}")
def tile_file(zone: str, z: int, x: int, y: int, file: str, v: int) -> Response:
    """Serve only files listed by the synthetic tile manifest at its current version."""
    manifest = json.loads((TILE / "tile.json").read_text(encoding="utf-8"))
    if (zone, z, x, y, v) != ("synth", 0, 0, 0, manifest["version"]):
        return missing()
    if file != "tile.json" and file not in manifest["sha256"]:
        return missing()
    path = TILE / file
    if not path.is_file():
        return missing()
    media_type = (
        "application/json; charset=utf-8" if file == "tile.json" else "application/octet-stream"
    )
    return FileResponse(path, media_type=media_type)


@app.get("/zones/{id}/pov/{file}")
def pov_file(id: str, file: str, v: int) -> Response:
    """Serve a POV JPEG when P1's zone fixture is available."""
    zone_path = ZONE / "zone.json"
    if not zone_path.is_file():
        return missing()
    zone = json.loads(zone_path.read_text(encoding="utf-8"))
    pin = next((pin for pin in zone["pov"] if pin["file"] == file and pin["version"] == v), None)
    path = ZONE / "pov" / file
    if id != zone["zone_id"] or pin is None or not path.is_file():
        return missing()
    return FileResponse(path, media_type="image/jpeg")
