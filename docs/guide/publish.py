#!/usr/bin/env python3
"""Publica la guía en Docmost: arma un zip desde guide.json y lo importa como un espacio.

Cada ejecución borra el espacio y lo importa de nuevo, así que Docmost no guarda nada escrito allí.
Docmost 0.96.0 solo da API keys en su edición enterprise: se entra como la UI, por /api/auth/setup
en una instancia nueva o /api/auth/login, con la cookie authToken. Su importador titula la página
por su primer encabezado, ordena hermanos por nombre de archivo y resuelve un enlace relativo a .md
solo sin #fragmento: por eso el build numera archivos y quita fragmentos. El repositorio git
del proyecto aún no tiene commits: no hay sello de commit, inventario de código ni enlaces a una forja. Solo biblioteca estándar. Uso: python publish.py [--build-only DIR].
"""
import argparse
import http.cookiejar
import json
import re
import shutil
import sys
import tempfile
import time
import urllib.error
import urllib.request
import uuid
import zipfile
from pathlib import Path, PurePosixPath

GUIDE = Path(__file__).resolve().parent
ROOT = GUIDE.parent.parent
MANIFEST = GUIDE / "guide.json"
LINK = re.compile(r"(\[(?:[^\]\\]|\\.)*\])\(([^)\s]+)\)")
DRAWS = re.compile(r"^\*?Dibuja: (.+?)\*?$")
DRAWN = re.compile(r"`([^`]+)` § ([^;]+)")
DECIDED = "> **Decidido, no construido.**"


def slug(text):
    return re.sub(r"[^a-z0-9]+", "-", text.lower()).strip("-")


def assign_paths(nodes, parent_dir, mapping, out):
    for index, node in enumerate(nodes, start=1):
        source = node.get("source", "")
        if node.get("title"):
            stem = node["title"]
        elif source.endswith("AGENTS.md"):
            stem = str(PurePosixPath(source).parent).replace(".", "project")
        else:
            stem = PurePosixPath(source).stem
        name = f"{index:02d}-{slug(stem)}"
        zip_path = PurePosixPath(parent_dir) / f"{name}.md"
        if "source" in node and "extract" not in node:
            mapping[node["source"]] = zip_path
        out.append((node, zip_path))
        if node.get("children"):
            assign_paths(node["children"], PurePosixPath(parent_dir) / name, mapping, out)


def split_fences(text):
    inside = False
    for line in text.splitlines():
        if line.lstrip().startswith("```"):
            inside = not inside
            yield line, True
            continue
        yield line, inside


def rewrite_links(line, source, zip_path, mapping, problems):
    base = PurePosixPath(source).parent

    def replace(match):
        label, target = match.group(1), match.group(2)
        if re.match(r"^[a-z]+:", target) or target.startswith("#"):
            return match.group(0)
        path_part = target.split("#", 1)[0]
        resolved = PurePosixPath(*_normalise((base / path_part).parts))
        key = resolved.as_posix()
        if key in mapping:
            relative = _relative(mapping[key], zip_path)
            return f"{label}({relative})"
        if (ROOT / key).exists():
            return f"{label[1:-1]} (`{key}`)"
        problems.append(f"{source} enlaza {target}, que no existe en el repositorio")
        return match.group(0)

    return LINK.sub(replace, line)


def _normalise(parts):
    stack = []
    for part in parts:
        if part == "..":
            stack.pop()
        elif part not in (".", ""):
            stack.append(part)
    return stack


def _relative(target, current):
    current_dir = current.parent.parts
    target_parts = target.parts
    common = 0
    while (
        common < len(current_dir)
        and common < len(target_parts)
        and current_dir[common] == target_parts[common]
    ):
        common += 1
    ups = [".."] * (len(current_dir) - common)
    return "/".join(ups + list(target_parts[common:])) or target.name


def check_draws(text, source):
    problems = []
    for line, fenced in split_fences(text):
        match = DRAWS.match(line.strip()) if not fenced else None
        if not match:
            continue
        for drawn_path, heading in DRAWN.findall(match.group(1)):
            heading = heading.strip().rstrip(".")
            page = ROOT / drawn_path
            headings = (
                {h.lstrip("#").strip() for h in page.read_text().splitlines() if h.startswith("#")}
                if page.is_file()
                else set()
            )
            if heading not in headings:
                problems.append(f"{source} dibuja {drawn_path} § {heading}, que esa página no tiene")
    return problems


def callouts(lines):
    out, block = [], None
    for line in lines + [""]:
        if block is not None:
            if line.startswith(">"):
                block.append(line[1:].lstrip())
                continue
            out += [":::warning", *block, ":::"]
            block = None
        if line.startswith(DECIDED):
            block = [line[1:].lstrip()]
            continue
        out.append(line)
    return out[:-1]


def extract(text, start, end):
    lines = text.splitlines()
    first = next(i for i, l in enumerate(lines) if l.startswith(start))
    last = next((i for i, l in enumerate(lines) if l.startswith(end) and i > first), len(lines))
    return "\n".join(lines[first:last])


def render(node, zip_path, mapping, problems):
    if "source" in node:
        source = node["source"]
        text = (ROOT / source).read_text()
        if "extract" in node:
            text = f"# {node['title']}\n\n" + extract(text, *node["extract"])
        elif source.endswith(".yaml"):
            text = f"# {node['title']}\n\n```yaml\n{text.rstrip()}\n```"
        lines = [
            line if fenced else rewrite_links(line, source, zip_path, mapping, problems)
            for line, fenced in split_fences(text)
        ]
        body = "\n".join(callouts(lines))
    else:
        body = f"# {node['title']}\n\n{node['intro']}"
    return body.rstrip("\n") + "\n"


def build(target):
    manifest = json.loads(MANIFEST.read_text())
    mapping, pages = {}, []
    assign_paths(manifest["pages"], "", mapping, pages)
    problems = []
    for node, _ in pages:
        if "source" in node and node["source"].endswith(".md"):
            problems += check_draws((ROOT / node["source"]).read_text(), node["source"])
    rendered = [(zip_path, render(node, zip_path, mapping, problems)) for node, zip_path in pages]
    if problems:
        sys.exit("\n".join(problems))
    for zip_path, text in rendered:
        out = target / zip_path
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_text(text)
    return manifest["space"]


class Docmost:
    def __init__(self, base):
        self.base = base.rstrip("/")
        self.opener = urllib.request.build_opener(
            urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar())
        )

    def post(self, path, payload=None, body=None, content_type="application/json"):
        data = body if body is not None else json.dumps(payload or {}).encode()
        request = urllib.request.Request(
            f"{self.base}/api/{path}", data=data, headers={"Content-Type": content_type}
        )
        with self.opener.open(request, timeout=120) as response:
            raw = response.read()
        parsed = json.loads(raw) if raw else {}
        return parsed.get("data", parsed)

    def sign_in(self, env):
        credentials = {"email": env["DOCMOST_ADMIN_EMAIL"], "password": env["DOCMOST_ADMIN_PASSWORD"]}
        try:
            self.post("auth/login", credentials)
        except urllib.error.HTTPError:
            self.post(
                "auth/setup",
                {
                    **credentials,
                    "name": env["DOCMOST_ADMIN_NAME"],
                    "workspaceName": env["DOCMOST_WORKSPACE"],
                },
            )

    def replace_space(self, space):
        listed = self.post("spaces", {"limit": 100})
        for existing in listed.get("items", []):
            if existing["slug"] == space["slug"]:
                self.post("spaces/delete", {"spaceId": existing["id"]})
        for _ in range(20):
            try:
                return self.post("spaces/create", space)["id"]
            except urllib.error.HTTPError:
                time.sleep(1)
        sys.exit(f"no se pudo crear el espacio {space['slug']}")

    def import_zip(self, space_id, archive):
        boundary = uuid.uuid4().hex
        parts = []
        for name, value in (("spaceId", space_id), ("source", "generic")):
            parts.append(
                f'--{boundary}\r\nContent-Disposition: form-data; name="{name}"\r\n\r\n{value}\r\n'.encode()
            )
        parts.append(
            f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="guide.zip"\r\n'
            "Content-Type: application/zip\r\n\r\n".encode()
            + archive.read_bytes()
            + f"\r\n--{boundary}--\r\n".encode()
        )
        task = self.post(
            "pages/import-zip",
            body=b"".join(parts),
            content_type=f"multipart/form-data; boundary={boundary}",
        )
        for _ in range(120):
            state = self.post("file-tasks/info", {"fileTaskId": task["id"]})
            if state["status"] == "success":
                return
            if state["status"] == "failed":
                sys.exit(f"la importación falló: {state.get('errorMessage')}")
            time.sleep(1)
        sys.exit("la importación no terminó en dos minutos")

    def make_readers(self, space_id):
        groups = self.post("groups", {"limit": 100}).get("items", [])
        members = self.post("spaces/members", {"spaceId": space_id, "limit": 100})
        present = {m["id"]: m for m in members.get("items", []) if m.get("type") == "group"}
        missing = [g["id"] for g in groups if g.get("isDefault") and g["id"] not in present]
        if missing:
            self.post(
                "spaces/members/add",
                {"spaceId": space_id, "role": "reader", "userIds": [], "groupIds": missing},
            )
        for group_id, member in present.items():
            if member.get("role") != "reader":
                self.post(
                    "spaces/members/change-role",
                    {"spaceId": space_id, "groupId": group_id, "role": "reader"},
                )


def read_env():
    env = {}
    for line in (GUIDE / ".env").read_text().splitlines():
        if "=" in line and not line.lstrip().startswith("#"):
            key, value = line.split("=", 1)
            env[key.strip()] = value.strip()
    return env


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--build-only", type=Path)
    args = parser.parse_args()
    if args.build_only:
        if args.build_only.exists():
            shutil.rmtree(args.build_only)
        build(args.build_only)
        print(args.build_only)
        return
    env = read_env()
    with tempfile.TemporaryDirectory() as scratch:
        tree = Path(scratch) / "guide"
        space = build(tree)
        archive = Path(scratch) / "guide.zip"
        with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as zipped:
            for file in sorted(tree.rglob("*.md")):
                zipped.write(file, file.relative_to(tree).as_posix())
        docmost = Docmost(env["APP_URL"])
        docmost.sign_in(env)
        space_id = docmost.replace_space(space)
        docmost.import_zip(space_id, archive)
        docmost.make_readers(space_id)
    print(f"{env['APP_URL']}/s/{space['slug']}")


if __name__ == "__main__":
    main()
