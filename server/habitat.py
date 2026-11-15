"""Read-only C3 habitat API over the persistent catalogue and imported zone files."""

from __future__ import annotations

import hashlib
import asyncio
import json
import logging
import os
import re
from contextlib import asynccontextmanager
from pathlib import Path

from fastapi import FastAPI, Query, Request
from fastapi.exceptions import RequestValidationError
from fastapi.responses import FileResponse, JSONResponse, Response
from starlette.exceptions import HTTPException as StarletteHTTPException

from store import ZoneStore
from importer import BundleImporter

ZONE_ID = re.compile(r"[a-z][a-z0-9-]{1,31}\Z")
FILE_NAME = re.compile(r"[a-zA-Z0-9_.-]+\Z")
DEFAULT_DATA = Path(__file__).resolve().parent / "data"
LOG = logging.getLogger("habitat")


def problem(status: int, code: str, detail: str) -> JSONResponse:
    """Return the shared RFC 9457 error shape."""
    titles = {
        400: "Bad Request",
        404: "Not Found",
        422: "Unprocessable Content",
        503: "Service Unavailable",
    }
    return JSONResponse(
        {"title": titles[status], "status": status, "detail": detail, "code": code},
        status_code=status,
        media_type="application/problem+json",
    )


def read_json(path: Path) -> dict | None:
    """Read an imported JSON file without rewriting its contents."""
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
        return value if isinstance(value, dict) else None
    except (OSError, UnicodeError, json.JSONDecodeError):
        return None


def valid_name(value: str) -> bool:
    """Accept one safe path segment, never a parent directory."""
    return value not in (".", "..") and FILE_NAME.fullmatch(value) is not None


def serve_file(path: Path, media_type: str) -> FileResponse:
    """Send a versioned file without ETag or HTTP cache headers."""
    response = FileResponse(path, media_type=media_type, stat_result=path.stat())
    if "etag" in response.headers:
        del response.headers["etag"]
    if "last-modified" in response.headers:
        del response.headers["last-modified"]
    response.headers["cache-control"] = "no-store"
    return response


def create_app(data_root: Path | None = None, spool_root: Path | None = None) -> FastAPI:
    """Build the service with a configurable on-disk catalogue and zone directory."""
    root = data_root or Path(os.environ.get("MM_DATA_DIR", DEFAULT_DATA))
    store = ZoneStore(root)
    spool = spool_root or Path(os.environ.get("MM_SPOOL", root / "spool"))
    importer = BundleImporter(store, spool)

    @asynccontextmanager
    async def lifespan(_: FastAPI):
        store.initialize()
        importer.initialize()
        stop = asyncio.Event()

        async def scan() -> None:
            while not stop.is_set():
                try:
                    await asyncio.to_thread(importer.poll_once)
                except Exception:
                    LOG.exception("spool scan failed")
                try:
                    await asyncio.wait_for(stop.wait(), timeout=1)
                except TimeoutError:
                    pass

        task = asyncio.create_task(scan())
        try:
            yield
        finally:
            stop.set()
            await task

    app = FastAPI(title="Martian Map habitat", lifespan=lifespan)
    app.state.store = store
    app.state.importer = importer

    @app.exception_handler(RequestValidationError)
    async def invalid_request(_: Request, error: RequestValidationError) -> JSONResponse:
        location = error.errors()[0]["loc"][0]
        status = 422 if location == "body" else 400
        code = "invalid_body" if status == 422 else "bad_parameter"
        return problem(status, code, "Request does not match the API schema")

    @app.exception_handler(StarletteHTTPException)
    async def unknown_route(_: Request, error: StarletteHTTPException) -> JSONResponse:
        if error.status_code == 404:
            return problem(404, "not_found", "Path not found")
        return problem(400, "bad_parameter", "Unsupported request")

    @app.get("/zones")
    def list_zones(since: int = Query(default=0, ge=0)) -> Response:
        return JSONResponse(store.list_zones(since), media_type="application/json; charset=utf-8")

    @app.get("/zones/{id}")
    def get_zone(id: str) -> Response:
        if ZONE_ID.fullmatch(id) is None:
            return problem(400, "bad_parameter", "Invalid zone id")
        version = store.zone_version(id)
        if version is None:
            return problem(404, "not_found", "Zone not found")
        root = store.zones_root / id
        zone = read_json(root / "zone.json")
        conditions = read_json(root / "conditions.json")
        if zone is None or conditions is None:
            return problem(503, "habitat_unavailable", "Zone files are not ready")
        return JSONResponse(
            {"zone_version": version, "zone": zone, "conditions": conditions},
            media_type="application/json; charset=utf-8",
        )

    @app.get("/tiles/{zone}/{z}/{x}/{y}/{file}")
    def get_tile(zone: str, z: int, x: int, y: int, file: str, v: int = Query(ge=1)) -> Response:
        if ZONE_ID.fullmatch(zone) is None or not valid_name(file) or min(z, x, y) < 0:
            return problem(400, "bad_parameter", "Invalid tile path")
        if store.zone_version(zone) is None:
            return problem(404, "not_found", "Zone not found")
        zone_document = read_json(store.zones_root / zone / "zone.json")
        if zone_document is None:
            return problem(503, "habitat_unavailable", "Zone files are not ready")
        reference = next(
            (
                t
                for t in zone_document.get("tiles", [])
                if (t.get("z"), t.get("x"), t.get("y")) == (z, x, y)
            ),
            None,
        )
        if reference is None or reference.get("version") != v:
            return problem(404, "not_found", "Tile version not found")
        root = store.zones_root / zone / "tiles" / str(z) / str(x) / str(y)
        manifest_path = root / "tile.json"
        manifest = read_json(manifest_path)
        if manifest is None:
            return problem(503, "habitat_unavailable", "Tile manifest is not ready")
        if manifest.get("version") != v:
            return problem(503, "habitat_unavailable", "Tile version is inconsistent")
        if hashlib.sha256(manifest_path.read_bytes()).hexdigest() != reference.get("sha256"):
            return problem(503, "habitat_unavailable", "Tile manifest hash is inconsistent")
        if file != "tile.json" and file not in manifest.get("sha256", {}):
            return problem(404, "not_found", "Tile file not found")
        path = root / file
        if not path.is_file():
            return problem(404, "not_found", "Tile file not found")
        media_type = (
            "application/json; charset=utf-8" if file == "tile.json" else "application/octet-stream"
        )
        return serve_file(path, media_type)

    @app.get("/zones/{id}/pov/{file}")
    def get_pov(id: str, file: str, v: int = Query(ge=1)) -> Response:
        if ZONE_ID.fullmatch(id) is None or not valid_name(file):
            return problem(400, "bad_parameter", "Invalid POV path")
        if store.zone_version(id) is None:
            return problem(404, "not_found", "Zone not found")
        zone = read_json(store.zones_root / id / "zone.json")
        if zone is None:
            return problem(503, "habitat_unavailable", "Zone files are not ready")
        pin = next(
            (p for p in zone.get("pov", []) if p.get("file") == file and p.get("version") == v),
            None,
        )
        if pin is None:
            return problem(404, "not_found", "POV version not found")
        path = store.zones_root / id / "pov" / file
        if not path.is_file():
            return problem(404, "not_found", "POV image not found")
        return serve_file(path, "image/jpeg")

    return app


app = create_app()
