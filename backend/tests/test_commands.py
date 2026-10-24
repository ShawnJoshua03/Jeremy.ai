import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(__file__)))
os.environ["DATABASE_URL"] = "sqlite:///:memory:"

import pytest
from fastapi.testclient import TestClient
from sqlalchemy import create_engine
from sqlalchemy.pool import StaticPool

import database as db

HIGHLIGHT = {
    "commandId": "cmd_018",
    "type": "highlight_target",
    "targetId": "target_2",
    "expiresAt": "2026-10-24T18:00:00Z",
    "message": "",
    "exerciseId": "",
}


@pytest.fixture()
def client(monkeypatch):
    monkeypatch.delenv("GEMINI_API_KEY", raising=False)
    import main
    eng = create_engine("sqlite://", connect_args={"check_same_thread": False}, poolclass=StaticPool)
    db.Base.metadata.create_all(eng)
    db.SessionLocal.configure(bind=eng)
    return TestClient(main.app)


def _sid(client, trainee="trainee"):
    return client.post("/api/v1/sessions", json={"trainee_id": trainee}).json()["id"]


def test_missing_session_is_404(client):
    missing = "/api/v1/sessions/missing/commands"
    assert client.get(missing).status_code == 404
    assert client.post(missing, json=HIGHLIGHT).status_code == 404
    ack = client.post(missing + "/cmd_018/ack", json={"status": "applied", "message": "Applied locally"})
    assert ack.status_code == 404
    assert ack.json()["detail"] == "session not found"


def test_poll_ack_and_first_ack_wins(client):
    sid = _sid(client)
    created = client.post(f"/api/v1/sessions/{sid}/commands", json=HIGHLIGHT)
    assert created.status_code == 201
    assert created.json() == HIGHLIGHT
    listed = client.get(f"/api/v1/sessions/{sid}/commands")
    assert listed.status_code == 200
    assert listed.json() == {"commands": [HIGHLIGHT]}
    ack = {"status": "applied", "message": "Applied locally"}
    assert client.post(f"/api/v1/sessions/{sid}/commands/cmd_018/ack", json=ack).json() == ack
    assert client.get(f"/api/v1/sessions/{sid}/commands").json() == {"commands": []}
    again = client.post(
        f"/api/v1/sessions/{sid}/commands/cmd_018/ack",
        json={"status": "rejected", "message": "later"},
    )
    assert again.status_code == 200
    assert again.json() == ack


def test_same_create_is_idempotent_and_a_different_body_conflicts(client):
    sid = _sid(client)
    path = f"/api/v1/sessions/{sid}/commands"
    assert client.post(path, json=HIGHLIGHT).status_code == 201
    replay = client.post(path, json=HIGHLIGHT)
    assert replay.status_code == 200 and replay.json() == HIGHLIGHT
    other = dict(HIGHLIGHT, type="request_pause")
    conflict = client.post(path, json=other)
    assert conflict.status_code == 409
    assert conflict.json()["detail"] == "command already exists"


def test_commands_stay_on_their_session(client):
    one = _sid(client, "one")
    two = _sid(client, "two")
    client.post(f"/api/v1/sessions/{one}/commands", json=HIGHLIGHT)
    assert client.get(f"/api/v1/sessions/{two}/commands").json() == {"commands": []}
    missing = client.post(f"/api/v1/sessions/{two}/commands/cmd_018/ack", json={"status": "rejected"})
    assert missing.status_code == 404
    assert missing.json()["detail"] == "command not found"


def test_invalid_command_and_ack_are_422(client):
    sid = _sid(client)
    path = f"/api/v1/sessions/{sid}/commands"
    bad_type = dict(HIGHLIGHT, type="move_tool")
    assert client.post(path, json=bad_type).status_code == 422
    extra = dict(HIGHLIGHT, commandId="cmd_019", transform={"x": 1})
    assert client.post(path, json=extra).status_code == 422
    assert client.post(path, json=dict(HIGHLIGHT, commandId="cmd_020", expiresAt="tomorrow")).status_code == 422
    client.post(path, json=dict(HIGHLIGHT, commandId="cmd_021"))
    assert client.post(f"{path}/cmd_021/ack", json={"status": "moved"}).status_code == 422
