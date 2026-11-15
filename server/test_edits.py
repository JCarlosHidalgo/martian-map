"""Exercise the M1 merge golden through M5's persistent HTTP API."""

from __future__ import annotations

import copy
import json
from pathlib import Path

from fastapi.testclient import TestClient

from habitat import create_app
from test_habitat import stage_synthetic_zone

GOLDEN = Path(__file__).resolve().parents[1] / "contracts" / "fixtures" / "merge"


def test_golden_acks_winners_and_resume_after_restart(tmp_path: Path) -> None:
    incoming = json.loads((GOLDEN / "log_in.json").read_text())["edits"]
    expected = json.loads((GOLDEN / "log_merged.json").read_text())["edits"]
    stage_synthetic_zone(tmp_path)
    with TestClient(create_app(tmp_path)) as client:
        client.app.state.store.publish_zone("synth", 1)
        response = client.post("/edits", json={"edits": incoming})
        assert response.status_code == 200
        acks = response.json()["acks"]
        assert len(acks) == len(incoming)
        assert acks[3] == {
            "uuid": incoming[3]["uuid"],
            "status": "rejected",
            "code": "confirmation_required",
        }
        assert all(ack["status"] == "accepted" for n, ack in enumerate(acks) if n != 3)
        assert client.app.state.store.current_winners("synth") == expected
        snapshot = client.get("/zones/synth/changes?since=0").json()
        assert snapshot["next_since"] == 1
        assert snapshot["has_more"] is False
        assert len(snapshot["tiles"]) == 1
        assert snapshot["conditions"]["basis"] == "modelled"
        cursor = snapshot["next_since"]
        seen = []
        while True:
            page = client.get(f"/zones/synth/changes?since={cursor}&limit=2")
            assert page.status_code == 200
            data = page.json()
            seen.extend(data["edits"])
            assert data["tiles"] == []
            assert data["next_since"] > cursor
            cursor = data["next_since"]
            if not data["has_more"]:
                break
        assert {edit["uuid"] for edit in seen} == {edit["uuid"] for edit in expected}
        assert client.get(f"/zones/synth/changes?since={cursor}").json()["edits"] == []
        previous_cursor = client.get("/zones?since=0").json()["next_since"]
        assert (
            client.post("/edits", json={"edits": [incoming[0]]}).json()["acks"][0]["status"]
            == "duplicate"
        )
        assert client.get("/zones?since=0").json()["next_since"] == previous_cursor
    with TestClient(create_app(tmp_path)) as client:
        assert client.app.state.store.current_winners("synth") == expected
        assert client.get(f"/zones/synth/changes?since={cursor}").json()["edits"] == []


def test_rejected_edits_validation_and_batch_limits(tmp_path: Path) -> None:
    incoming = json.loads((GOLDEN / "log_in.json").read_text())["edits"]
    stage_synthetic_zone(tmp_path)
    with TestClient(create_app(tmp_path)) as client:
        client.app.state.store.publish_zone("synth", 1)
        unknown = copy.deepcopy(incoming[0])
        unknown["zone_id"] = "missing"
        assert client.post("/edits", json={"edits": [unknown]}).json()["acks"] == [
            {"uuid": unknown["uuid"], "status": "rejected", "code": "unknown_zone"}
        ]
        assert client.get("/zones?since=0").json()["next_since"] == 1
        malformed = copy.deepcopy(incoming[0])
        malformed["author"] = "ev1\n"
        assert client.post("/edits", json={"edits": [malformed]}).status_code == 422
        assert client.post("/edits", json={"edits": [incoming[0]] * 201}).status_code == 413
        assert client.post("/edits", content=b"{" + b" " * (1024 * 1024)).status_code == 413
        changed_creator = copy.deepcopy(incoming[3])
        changed_creator["uuid"] = "aaaaaaaa-1111-4111-8111-111111111111"
        changed_creator["creator"] = "ev2"
        batch = [incoming[0], changed_creator]
        assert client.post("/edits", json={"edits": batch}).status_code == 422
        assert client.app.state.store.current_winners("synth") == []


def test_bundle_snapshot_is_not_skipped_between_edit_pages(tmp_path: Path) -> None:
    incoming = json.loads((GOLDEN / "log_in.json").read_text())["edits"]
    stage_synthetic_zone(tmp_path)
    with TestClient(create_app(tmp_path)) as client:
        client.app.state.store.publish_zone("synth", 1)
        client.post("/edits", json={"edits": incoming})
        boundary = client.app.state.store.publish_zone("synth", 2)
        later = copy.deepcopy(incoming[2])
        later["uuid"] = "dddddddd-1111-4111-8111-111111111111"
        later["entity_id"] = "dddddddd-dddd-4ddd-8ddd-dddddddddddd"
        later["lamport"] = 6
        assert client.post("/edits", json={"edits": [later]}).status_code == 200
        cursor = 1
        delivered = []
        while True:
            page = client.get(f"/zones/synth/changes?since={cursor}&limit=2").json()
            delivered.extend(page["edits"])
            cursor = page["next_since"]
            if not page["has_more"]:
                assert page["conditions"]["basis"] == "modelled"
                assert len(page["tiles"]) == 1
                assert cursor == boundary
                break
        assert later["uuid"] not in {edit["uuid"] for edit in delivered}
        following = client.get(f"/zones/synth/changes?since={cursor}").json()
        assert following["edits"] == [later]
