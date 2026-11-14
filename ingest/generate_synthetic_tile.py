
from pathlib import Path

# Size of the terrain grid
GRID_SIZE = 257

# Resolution in meters
METERS_PER_PIXEL = 1.0

# Output folder
OUTPUT_DIR = Path("contracts/fixtures/tile_synth_0_0_0")

print("=== MARTIAN MAP: SYNTHETIC TILE ===")
print("Grid size:", GRID_SIZE, "x", GRID_SIZE)
print("Resolution:", METERS_PER_PIXEL, "meter per pixel")
print("Output folder:", OUTPUT_DIR)


# Generate synthetic terrain elevations
heights = []

for row in range(GRID_SIZE):
    terrain_row = []

    for col in range(GRID_SIZE):
        east = col - 128
        north = 128 - row

        height = 20 * (east + 128) / 256 + 5 * (north + 128) / 256

        terrain_row.append(height)

    heights.append(terrain_row)

print("Northwest corner:", heights[0][0])
print("Northeast corner:", heights[0][256])
print("Southwest corner:", heights[256][0])
print("Southeast corner:", heights[256][256])


import struct

# Create the output directory
OUTPUT_DIR.mkdir(parents=True, exist_ok=True)

# Height encoding defined by contract C1
HEIGHT_SCALE = 1 / 1024

# Save the terrain elevations as unsigned 16-bit integers
height_file = OUTPUT_DIR / "height.r16"

with height_file.open("wb") as file:
    for terrain_row in heights:
        for height in terrain_row:
            encoded_height = round(height / HEIGHT_SCALE)
            file.write(struct.pack("<H", encoded_height))

print("Height file created:", height_file)
print("File size:", height_file.stat().st_size, "bytes")


# Read the first elevation sample
with height_file.open("rb") as file:
    first_bytes = file.read(2)
    first_value = struct.unpack("<H", first_bytes)[0]

decoded_height = first_value * HEIGHT_SCALE

print("First stored value:", first_value)
print("Decoded height:", decoded_height, "meters")


# Generate the synthetic hazard raster
hazard_file = OUTPUT_DIR / "hazard.r8"

with hazard_file.open("wb") as file:
    for row in range(GRID_SIZE):
        for col in range(GRID_SIZE):
            east = col - 128
            north = 128 - row

            hazard = 0

            # Red hazard in the northeast
            if 40 <= east <= 100 and 40 <= north <= 100:
                hazard = 255

            # Amber hazard in the southwest
            if -100 <= east <= -60 and -100 <= north <= -60:
                hazard = 128

            # Missing data in the southeast
            if 113 <= east <= 128 and -128 <= north <= -113:
                hazard = 255

            file.write(struct.pack("B", hazard))

print("Hazard file created:", hazard_file)
print("Hazard file size:", hazard_file.stat().st_size, "bytes")


# Generate the synthetic slope raster
slope_file = OUTPUT_DIR / "slope.r8"

with slope_file.open("wb") as file:
    for row in range(GRID_SIZE):
        for col in range(GRID_SIZE):
            east = col - 128
            north = 128 - row

            slope = 0

            if 40 <= east <= 100 and 40 <= north <= 100:
                slope = 20

            file.write(struct.pack("B", slope))

print("Slope file created:", slope_file)
print("Slope file size:", slope_file.stat().st_size, "bytes")


# Generate the synthetic roughness raster
roughness_file = OUTPUT_DIR / "roughness.r8"

with roughness_file.open("wb") as file:
    for row in range(GRID_SIZE):
        for col in range(GRID_SIZE):
            roughness = 0
            file.write(struct.pack("B", roughness))

print("Roughness file created:", roughness_file)
print("Roughness file size:", roughness_file.stat().st_size, "bytes")

import hashlib
import json

def calculate_sha256(file_path):
    with open(file_path, "rb") as file:
        return hashlib.sha256(file.read()).hexdigest()

print("\n=== FILE HASHES ===")

for filename in ["height.r16", "slope.r8", "roughness.r8", "hazard.r8"]:
    file_path = OUTPUT_DIR / filename
    print(filename, calculate_sha256(file_path))


# Generate synthetic orthographic layer
# Generate synthetic orthographic layer
ortho_file = OUTPUT_DIR / "ortho.r8"

with ortho_file.open("wb") as file:
    for row in range(GRID_SIZE):
        for column in range(GRID_SIZE):
            value = min(column, 255)

            # Southeast corner: no data
            if row >= 241 and column >= 241:
                value = 0

            file.write(struct.pack("<B", value))

print("Ortho file created:", ortho_file)
print("Ortho file size:", ortho_file.stat().st_size, "bytes")

# Generate synthetic thermal inertia layer
thermal_file = OUTPUT_DIR / "thermal.r8"

with thermal_file.open("wb") as file:
    for row in range(GRID_SIZE):
        for column in range(GRID_SIZE):
            value = min(256 - row, 255)
            file.write(struct.pack("<B", value))

print("Thermal file created:", thermal_file)
print("Thermal file size:", thermal_file.stat().st_size, "bytes")

# Generate synthetic thermal infrared layer
thermal_ir_file = OUTPUT_DIR / "thermal_ir.r8"

with thermal_ir_file.open("wb") as file:
    for row in range(GRID_SIZE):
        for column in range(GRID_SIZE):
            checker = (row // 32 + column // 32) % 2
            value = 64 if checker == 0 else 192
            file.write(struct.pack("<B", value))

print("Thermal IR file created:", thermal_ir_file)
print("Thermal IR file size:", thermal_ir_file.stat().st_size, "bytes")

# Update tile.json with file hashes
tile_json_path = OUTPUT_DIR / "tile.json"

with tile_json_path.open("r", encoding="utf-8") as file:
    tile_data = json.load(file)

# Register the synthetic orthographic layer
tile_data["layers"] = [
    {
        "id": "ortho",
        "file": "ortho.r8"
    },
    {
        "id": "thermal",
        "file": "thermal.r8"
    },
    {
        "id": "thermal_ir",
        "file": "thermal_ir.r8"
    }
]

tile_data["sha256"] = {}

for filename in [
    "height.r16",
    "slope.r8",
    "roughness.r8",
    "hazard.r8",
    "ortho.r8",
    "thermal.r8",
    "thermal_ir.r8"
]:
    file_path = OUTPUT_DIR / filename
    tile_data["sha256"][filename] = calculate_sha256(file_path)

with tile_json_path.open("w", encoding="utf-8") as file:
    json.dump(tile_data, file, indent=2, ensure_ascii=False)
    file.write("\n")

print("tile.json updated with SHA-256 hashes!")