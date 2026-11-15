import json
import hashlib
from pathlib import Path
from datetime import datetime, timezone, timedelta

bundle_dir = Path("contracts/fixtures/bundle_synth")
data_dir = bundle_dir / "data"

# O bundle completo depende do arquivo oficial C6
conditions = data_dir / "conditions.json"

if not conditions.is_file():
    raise SystemExit(
        "PENDENTE: conditions.json ainda nao foi recebido. "
        "Nao gerar bundle incompleto."
    )

files = []

for path in sorted(data_dir.rglob("*")):
    if not path.is_file():
        continue

    relative = path.relative_to(bundle_dir).as_posix()

    if path.suffix == ".json":
        media_type = "application/json"
    elif path.suffix.lower() in (".jpg", ".jpeg"):
        media_type = "image/jpeg"
    else:
        media_type = "application/octet-stream"

    files.append({
        "path": relative,
        "size": path.stat().st_size,
        "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
        "media_type": media_type
    })

now = datetime.now(timezone.utc)
deliver_at = now

# ULID: 26 caracteres Crockford Base32
import os

alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"

def encode_ulid(value):
    result = ""
    for _ in range(26):
        result = alphabet[value & 31] + result
        value >>= 5
    return result

timestamp_ms = int(now.timestamp() * 1000)
random_part = int.from_bytes(os.urandom(10), "big")
bundle_id = encode_ulid((timestamp_ms << 80) | random_part)

manifest = {
    "schema_version": 1,
    "bundle_id": bundle_id,
    "source": "earth",
    "zone_id": "synth",
    "zone_version": 1,
    "kind": "zone_full",
    "created_utc": now.isoformat().replace("+00:00", "Z"),
    "delay_s": 0,
    "deliver_at_utc": deliver_at.isoformat().replace("+00:00", "Z"),
    "files": files,
    "payload_oxum": f"{sum(f['size'] for f in files)}.{len(files)}"
}

output = bundle_dir / "bundle.json"
output.write_text(
    json.dumps(manifest, indent=2, ensure_ascii=False) + "\n",
    encoding="utf-8"
)

print("Bundle manifest gerado:", output)
print("Arquivos:", len(files))
print("Payload Oxum:", manifest["payload_oxum"])
print("Bundle ID:", bundle_id)
