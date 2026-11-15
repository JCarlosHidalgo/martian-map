"""Integration tests for the real catalogue and file endpoints with synthetic data."""

from __future__ import annotations

import hashlib
import json
import shutil
from pathlib import Path

import pytest
from fastapi.testclient import TestClient

from habitat import create_app

FIXTURE_TILE = Path(__file__).resolve().parents[1] / "contracts" / "fixtures" / "tile_synth_0_0_0"


def stage_synthetic_zone(data_root: Path) -> bytes:
    """Stage a small test zone until P1 and P5 provide their contract fixtures."""
    root = data_root / "zones" / "synth"
    tile_root = root / "tiles" / "0" / "0" / "0"
    shutil.copytree(FIXTURE_TILE, tile_root)
    tile_hash = hashlib.sha256((tile_root / "tile.json").read_bytes()).hexdigest()
    zone = {
        "zone_id": "synth",
        "name": "Synthetic test zone",
        "tiles": [{"z": 0, "x": 0, "y": 0, "version": 1, "sha256": tile_hash}],
        "pov": [{"file": "synthetic.jpg", "version": 1}],
    }
    (root / "zone.json").write_text(json.dumps(zone), encoding="utf-8")
    (root / "conditions.json").write_text(
        json.dumps({"schema_version": 1, "basis": "modelled", "value": 6.0}), encoding="utf-8"
    )
    (root / "pov").mkdir()
    image = b"\xff\xd8\xff\xd9"
    (root / "pov" / "synthetic.jpg").write_bytes(image)
    return image


def test_catalogue_and_files_survive_restart(tmp_path: Path) -> None:
    image = stage_synthetic_zone(tmp_path)
    with TestClient(create_app(tmp_path)) as client:
        assert client.get("/zones?since=0").json() == {"next_since": 0, "zones": []}
        assert client.app.state.store.publish_zone("synth", 1) == 1
        assert client.get("/zones?since=0").json() == {
            "next_since": 1,
            "zones": [{"zone_id": "synth", "name": "Synthetic test zone", "zone_version": 1}],
        }
        assert client.get("/zones?since=1").json() == {"next_since": 1, "zones": []}
        detail = client.get("/zones/synth")
        assert detail.status_code == 200
        assert detail.json()["zone_version"] == 1
        assert detail.json()["conditions"]["basis"] == "modelled"
        tile = client.get("/tiles/synth/0/0/0/height.r16?v=1")
        assert tile.status_code == 200
        assert (
            hashlib.sha256(tile.content).hexdigest()
            == json.loads((FIXTURE_TILE / "tile.json").read_text())["sha256"]["height.r16"]
        )
        assert "etag" not in tile.headers
        assert "last-modified" not in tile.headers
        assert client.get("/tiles/synth/0/0/0/tile.json?v=1").status_code == 200
        assert client.get("/zones/synth/pov/synthetic.jpg?v=1").content == image
        assert client.get("/tiles/synth/0/0/0/height.r16?v=2").status_code == 404
        assert client.get("/zones/synth/pov/synthetic.jpg?v=2").status_code == 404
    with TestClient(create_app(tmp_path)) as client:
        assert client.get("/zones?since=0").json()["next_since"] == 1
        assert client.app.state.store.publish_zone("synth", 2) == 2
        assert client.get("/zones?since=1").json()["zones"][0]["zone_version"] == 2
        assert client.get("/tiles/synth/0/0/0/height.r16?v=1").status_code == 200
        with pytest.raises(ValueError, match="increase"):
            client.app.state.store.publish_zone("synth", 2)


def test_errors_are_problem_json(tmp_path: Path) -> None:
    stage_synthetic_zone(tmp_path)
    with TestClient(create_app(tmp_path)) as client:
        assert client.app.state.store.publish_zone("synth", 1) == 1
        for url, status in [
            ("/zones?since=-1", 400),
            ("/zones/synth/tiles", 404),
            ("/zones/unknown", 404),
            ("/tiles/synth/0/0/0/hidden.bin?v=1", 404),
            ("/tiles/synth/0/0/0/height.r16?v=abc", 400),
        ]:
            response = client.get(url)
            assert response.status_code == status
            assert response.headers["content-type"] == "application/problem+json"
            assert response.json()["code"]
        (tmp_path / "zones" / "synth" / "conditions.json").unlink()
        assert client.get("/zones/synth").status_code == 503
        manifest = tmp_path / "zones" / "synth" / "tiles" / "0" / "0" / "0" / "tile.json"
        manifest.write_text(manifest.read_text(encoding="utf-8") + " ", encoding="utf-8")
        assert client.get("/tiles/synth/0/0/0/height.r16?v=1").status_code == 503
