# Martian Map

A tablet app for an astronaut on the surface of Mars: a 3D map that grows on its own from NASA
data, can be edited, and plans the fastest safe route. Built for the NASA Space Apps Challenge
2026, *Interplanetary Survival Guide: Martian Map*.

The design and the team plan live in the team guide, under `docs/guide/`. The guide is written in
Spanish.

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
