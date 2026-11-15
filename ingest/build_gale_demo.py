"""Build a small, real HiRISE Gale zone for the presentation.

Inputs are downloaded from the product linked in README. The raw IMG/JP2 files
stay outside Git; only the 512 m square C1/C1b bundle is committed.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
from datetime import datetime, timezone
from pathlib import Path

import numpy as np
import rasterio
from rasterio.enums import Resampling
from rasterio.transform import from_origin
from rasterio.warp import reproject

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "tablet/Assets/StreamingAssets/zones/gale"
LAT, LON, RADIUS = -4.81, 137.385, 3396190
PRODUCT = "https://hirise.lpl.arizona.edu/dtm/PSP_009149_1750"
TILES = 5
HALF = TILES * 256 // 2
SIZE = TILES * 256 + 1  # 25 adjacent tiles, 1.28 x 1.28 km total.


def write_json(path: Path, value: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("height", type=Path, help="HiRISE DTEEC IMG")
    parser.add_argument("ortho", type=Path, help="Matching HiRISE RED_A ORTHO JP2")
    parser.add_argument("inertia", type=Path, help="ASU THEMIS qualitative TI GeoTIFF")
    parser.add_argument("night_ir", type=Path, help="ASU THEMIS night IR GeoTIFF")
    args = parser.parse_args()

    # Source is Mars equirectangular, central meridian 137.41. At this small
    # extent the corresponding projected coordinates are an accurate ENU grid.
    # East-west scale is corrected for the local latitude by resampling to 1 m.
    y0 = RADIUS * math.radians(LAT)
    source_x = RADIUS * math.radians(LON - 137.41)
    source_transform = from_origin(source_x - HALF / math.cos(math.radians(LAT)), y0 + HALF, 1 / math.cos(math.radians(LAT)), 1)
    # The source's projection has standard parallel 0. The destination grid is
    # expressed in those coordinates; zone.json supplies the local ENU origin.
    height = np.full((SIZE, SIZE), np.nan, dtype=np.float32)
    ortho = np.zeros((SIZE, SIZE), dtype=np.uint8)
    inertia = np.zeros((SIZE, SIZE), dtype=np.uint8)
    night_ir = np.zeros((SIZE, SIZE), dtype=np.uint8)
    with rasterio.open(args.height) as src:
        height_crs = src.crs
        reproject(rasterio.band(src, 1), height, src_transform=src.transform,
                  src_crs=src.crs, src_nodata=src.nodata,
                  dst_transform=source_transform, dst_crs=src.crs,
                  dst_nodata=np.nan, resampling=Resampling.bilinear)
    with rasterio.open(args.ortho) as src:
        reproject(rasterio.band(src, 1), ortho, src_transform=src.transform,
                  src_crs=src.crs, src_nodata=src.nodata,
                  dst_transform=source_transform, dst_crs=src.crs,
                  dst_nodata=0, resampling=Resampling.average)
    for source_path, target in ((args.inertia, inertia), (args.night_ir, night_ir)):
        with rasterio.open(source_path) as src:
            reproject(rasterio.band(src, 1), target, src_transform=src.transform,
                      src_crs=src.crs, src_nodata=src.nodata,
                      dst_transform=source_transform,
                      dst_crs=height_crs,
                      dst_nodata=0, resampling=Resampling.nearest)

    valid = np.isfinite(height) & (height > -1e30)
    if valid.mean() < 0.95 or np.count_nonzero(ortho) < SIZE * SIZE * 0.9:
        raise SystemExit("Chosen square is not covered by both HiRISE products")
    if np.count_nonzero(inertia) < SIZE * SIZE * 0.9 or np.count_nonzero(night_ir) < SIZE * SIZE * 0.9:
        raise SystemExit("Chosen square is not covered by both THEMIS products")

    # Slopes and local relief are derived from the measured DTM. Hazard rules
    # follow docs/guide/chapters/convenciones.md (not a measured hazard layer).
    filled = np.where(valid, height, np.nanmedian(height[valid]))
    north, east = np.gradient(filled)
    slope = np.degrees(np.arctan(np.hypot(east, north)))
    padded = np.pad(filled, 1, mode="edge")
    windows = np.lib.stride_tricks.sliding_window_view(padded, (3, 3))
    rough_cm = (windows.max(axis=(-1, -2)) - windows.min(axis=(-1, -2))) * 100
    slope8 = np.where(valid, np.clip(np.ceil(slope), 0, 255), 0).astype(np.uint8)
    rough8 = np.where(valid, np.clip(np.ceil(rough_cm), 0, 255), 0).astype(np.uint8)
    hazard = np.where(~valid | (slope > 15) | (rough_cm > 61), 255, 0).astype(np.uint8)
    height_offset = float(np.floor(height[valid].min()))
    height_scale = max(0.001, float((height[valid].max() - height_offset) / 65534))
    height16 = np.where(valid, np.clip(np.rint((filled - height_offset) / height_scale), 0, 65534), 65535).astype("<u2")

    data = OUT / "data"
    tile_refs = []
    for ty in range(TILES):
        for tx in range(TILES):
            # y=0 is south; arrays are north-up.
            row, col = (TILES - 1 - ty) * 256, tx * 256
            folder = data / "tiles" / "0" / str(tx) / str(ty)
            folder.mkdir(parents=True, exist_ok=True)
            arrays = {"height.r16": height16, "slope.r8": slope8,
                      "roughness.r8": rough8, "hazard.r8": hazard, "ortho.r8": ortho,
                      "thermal.r8": inertia, "thermal_ir.r8": night_ir}
            for name, array in arrays.items():
                (folder / name).write_bytes(array[row:row + 257, col:col + 257].tobytes())
            tile = {
                "schema_version": 1, "zone_id": "gale", "z": 0, "x": tx, "y": ty,
                "size_px": 257, "m_per_px": 1.0,
                "origin_enu_m": [-HALF + tx * 256, -HALF + ty * 256],
                "height": {"file": "height.r16", "dtype": "uint16", "scale": height_scale,
                           "offset": height_offset, "nodata": 65535, "row_order": "north_up",
                           "endian": "le", "vertical_datum": "areoid"},
                "rasters": {"slope": {"dtype": "uint8", "unit": "deg"},
                            "roughness": {"dtype": "uint8", "unit": "cm", "window_m": 3},
                            "hazard": {"dtype": "uint8"}},
                "version": 2, "layers": [{"id": "ortho", "file": "ortho.r8"},
                                         {"id": "thermal", "file": "thermal.r8"},
                                         {"id": "thermal_ir", "file": "thermal_ir.r8"}],
                "source_ids": ["mro-hirise-gale-dtm", "mro-hirise-gale-ortho",
                               "ody-themis-qual-ti", "ody-themis-night-ir"],
                "sha256": {name: digest(folder / name) for name in arrays},
            }
            write_json(folder / "tile.json", tile)
            tile_refs.append({"z": 0, "x": tx, "y": ty, "version": 2,
                              "sha256": digest(folder / "tile.json")})

    zone = {
        "schema_version": 1, "zone_id": "gale", "name": "Gale · HiRISE (1,28 km)",
        "crs": "IAU_2015:49900", "origin_lat_deg": LAT, "origin_lon_deg": LON,
        "origin_height_m": float(height[HALF, HALF]),
        "projection": "equirectangular_local", "radius_m": RADIUS,
        "habitat_enu_m": [0, 0], "extent_enu_m": [-HALF, -HALF, HALF, HALF],
        "base_m_per_px": 1.0, "tiles": tile_refs,
        "layers": [{"id": "ortho", "dtype": "uint8", "source_id": "mro-hirise-gale-ortho",
                    "qualitative": True},
                   {"id": "thermal", "dtype": "uint8", "source_id": "ody-themis-qual-ti",
                    "qualitative": True},
                   {"id": "thermal_ir", "dtype": "uint8", "source_id": "ody-themis-night-ir",
                    "qualitative": True}],
        "layer_sources": [
            {"id": "mro-hirise-gale-dtm", "kind": "raster", "adapter": "pds-http",
             "mission": "MRO", "instrument": "HiRISE",
             "product_id": "DTEEC_009149_1750_009294_1750_U01", "resolution_m": 1.01,
             "time_range": ["2008-07-09T00:00:00Z", "2008-07-20T00:00:00Z"],
             "license": "CC0-1.0", "url": PRODUCT, "units": "m",
             "vertical_datum": "areoid", "qualitative": False},
            {"id": "mro-hirise-gale-ortho", "kind": "raster", "adapter": "pds-http",
             "mission": "MRO", "instrument": "HiRISE",
             "product_id": "PSP_009149_1750_RED_A_01_ORTHO", "resolution_m": 0.25,
             "time_range": ["2008-07-09T00:00:00Z", "2008-07-09T00:00:00Z"],
             "license": "CC0-1.0", "url": PRODUCT, "qualitative": True},
            {"id": "ody-themis-qual-ti", "kind": "raster", "adapter": "pds-http",
             "mission": "Mars Odyssey", "instrument": "THEMIS",
             "product_id": "Gale_crater_THEMIS_qualTI", "resolution_m": 100,
             "time_range": ["2006-07-14T00:00:00Z", "2006-07-14T00:00:00Z"],
             "license": "NASA media guidelines; crédito por ítem",
             "url": "https://themis.mars.asu.edu/landingsites/mslsite_42",
             "qualitative": True},
            {"id": "ody-themis-night-ir", "kind": "raster", "adapter": "pds-http",
             "mission": "Mars Odyssey", "instrument": "THEMIS",
             "product_id": "Gale_crater_THEMIS_nightIR", "resolution_m": 100,
             "time_range": ["2006-07-14T00:00:00Z", "2006-07-14T00:00:00Z"],
             "license": "NASA media guidelines; crédito por ítem",
             "url": "https://themis.mars.asu.edu/landingsites/mslsite_42",
             "qualitative": True},
        ], "pov": []}
    write_json(data / "zone.json", zone)
    files = []
    for path in sorted(data.rglob("*")):
        if path.is_file() and path.suffix != ".meta":
            rel = path.relative_to(OUT).as_posix()
            files.append({"path": rel, "size": path.stat().st_size,
                          "sha256": digest(path),
                          "media_type": "application/json" if path.suffix == ".json" else "application/octet-stream"})
    now = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
    bundle = {"schema_version": 1, "bundle_id": "01K2G4MARS0000000000000001",
              "source": "earth", "zone_id": "gale", "zone_version": 2,
              "kind": "zone_full", "created_utc": now, "delay_s": 0,
              "deliver_at_utc": now, "files": files,
              "payload_oxum": f"{sum(f['size'] for f in files)}.{len(files)}"}
    write_json(OUT / "bundle.json", bundle)
    print(f"Wrote {OUT}: {TILES * TILES} real HiRISE tiles, {valid.mean():.1%} DTM coverage, "
          f"{np.count_nonzero(hazard == 255) / hazard.size:.1%} derived hazard")


if __name__ == "__main__":
    main()
