# Martian Map

A tablet app for an astronaut on the surface of Mars: a 3D map that grows on its own from NASA
data, can be edited, and plans the fastest safe route. Built for the NASA Space Apps Challenge
2026, *Interplanetary Survival Guide: Martian Map*.

The design and the team plan live in the team guide, under `docs/guide/`. The guide is written in
Spanish.

## Repository layout

| Folder | Owner | Content |
|---|---|---|
| `contracts/` | each contract's producer | schemas, fixtures and `openapi.yaml` |
| `ingest/` | P1 | the Earth CLI |
| `server/` | P2 | the habitat server |
| `Routing/`, `Edits/` | P3 | pure C# packages (netstandard2.1) |
| `tablet/` | P4 | the Unity project: `Assets/Terrain/` (P4), `Assets/FX/` (P5), `Assets/HUD/` (P6), `Assets/Scenes/Main.unity` (P4) |
| `docs/` | P4 | the team guide |

## Toolchain

Pinned for the 48 h; nobody upgrades during the event.

| Tool | Version |
|---|---|
| Unity | **6000.3.28f1** (changeset `8f082d2fe476`) with Android Build Support, OpenJDK and Android SDK & NDK |
| .NET SDK | 10.0.404 (`global.json`) |
| Node | ≥ 24.18 |
| Prism | 5.16.0 (`npx @stoplight/prism-cli@5.16.0`) |
| Python | 3.12, with `uv` |
| Git LFS | any recent |

### Unity merge tool

Run once per machine, after installing Unity through the Hub (Linux path shown; on Windows use
`C:/Program Files/Unity/Hub/Editor/6000.3.28f1/Editor/Data/Tools/UnityYAMLMerge.exe`):

```bash
git config --global merge.unityyamlmerge.name "Unity SmartMerge"
```

```bash
git config --global merge.unityyamlmerge.driver "$HOME/Unity/Hub/Editor/6000.3.28f1/Editor/Data/Tools/UnityYAMLMerge merge -p %O %B %A %A"
```

### Tablet project

Open `tablet/` with Unity 6000.3.28f1. The menu `MartianMap` has:

- **Run skeleton setup**: layers, Android player settings (IL2CPP ARM64, Vulkan + GLES3, HTTP
  allowed), the `Terrain` and `HUD` prefabs and `Main.unity`. Idempotent.
- **Sync fixtures**: copies `contracts/fixtures/` into `Assets/StreamingAssets/fixtures/` (also
  runs on project open and before every build; that folder is not in git).
- **Build Android APK**: writes `tablet/Builds/martian-map.apk`.

`Assets/StreamingAssets/config.default.json` holds the habitat URLs (Prism on port 4010, file stub
on 4011). Replace the placeholder IP with the habitat laptop's IP on the team router.

## Read the guide

The chapters are plain Markdown in `docs/guide/chapters/`, in the reading order of
`docs/guide/guide.json`, so they can be read on GitHub. To read them with the diagrams drawn, run
the local Docmost instance.

You need Docker with Compose and Python 3. Nothing has to be installed with `pip`.

1. Clone the repository and enter the guide folder.

   ```bash
   git clone https://github.com/JCarlosHidalgo/martian-map.git
   cd martian-map/docs/guide
   ```

2. Start Docmost with its Postgres and Redis. The committed `.env` already holds every setting.

   ```bash
   docker compose up -d
   ```

3. Wait until it answers. The first run takes longer because it pulls the images.

   ```bash
   curl -fsS http://localhost:3200/api/health
   ```

4. Publish the guide. On a new instance this creates the admin user, imports the pages and prints
   the address.

   ```bash
   python3 publish.py
   ```

5. Open <http://localhost:3200/s/martian-map> and sign in.

   | Field | Value |
   |---|---|
   | Email | `nasa@mail.com` |
   | Password | `admin1234` |

After a `git pull` that changes the guide, run step 4 again: it replaces the whole space. For that
reason a correction goes to `docs/guide/chapters/` and is never typed into Docmost.

To stop the instance, run `docker compose down`. Add `-v` to delete its volumes and start from
scratch.

The credentials above are public on purpose. They are meant for an instance that only listens on
`localhost`. An instance that others can reach over the network starts from
`docs/guide/.env.example`, with its own secrets, and that file is not committed.

How the guide tooling works, and the rules for editing it: `docs/guide/AGENTS.md`.
