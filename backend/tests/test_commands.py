import database as db
import pytest
from fastapi.testclient import TestClient
from sqlalchemy import create_engine
from sqlalchemy.pool import StaticPool

HIGHLIGHT = {
    "commandId": "cmd_018",
    "type": "highlight_target",
    "targetId": "target_2",
    "expiresAt": "2099-01-01T00:00:00Z",
    "message": "",
    "exerciseId": "",
}


@pytest.fixture()
def client(monkeypatch):
    monkeypatch.delenv("ANTHROPIC_API_KEY", raising=False)
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


def test_note_is_queued_for_the_headset(client):
    sid = _sid(client)
    note = client.post(f"/api/v1/sessions/{sid}/annotations",
                       json={"at_ms": 1200, "text": "  Slow your release  ", "author": "Instructor"})
    assert note.status_code == 201
    command_id = note.json()["command_id"]
    assert command_id == "ann_" + note.json()["annotation_id"]
    pending = client.get(f"/api/v1/sessions/{sid}/commands").json()["commands"]
    assert len(pending) == 1
    cmd = pending[0]
    assert cmd["commandId"] == command_id and cmd["type"] == "add_annotation"
    assert cmd["message"] == "Slow your release" and cmd["targetId"] == ""
    assert cmd["expiresAt"].endswith("Z")
    ack = client.post(f"/api/v1/sessions/{sid}/commands/{command_id}/ack",
                      json={"status": "applied", "message": "Applied locally"})
    assert ack.status_code == 200
    assert client.get(f"/api/v1/sessions/{sid}/commands").json() == {"commands": []}
    assert [a["text"] for a in client.get(f"/api/v1/sessions/{sid}/annotations").json()] == ["Slow your release"]


def test_note_can_skip_the_headset(client):
    sid = _sid(client)
    note = client.post(f"/api/v1/sessions/{sid}/annotations",
                       json={"at_ms": 0, "text": "review later", "author": "Instructor", "send_to_headset": False})
    assert note.status_code == 201 and note.json()["command_id"] is None
    assert client.get(f"/api/v1/sessions/{sid}/commands").json() == {"commands": []}


def test_dashboard_command_shape_is_accepted(client):
    """Mirrors dashboard/src/api.ts commandBody()."""
    sid = _sid(client)
    for body in (
        {"type": "highlight_target", "targetId": "target_3"},
        {"type": "request_pause", "targetId": ""},
        {"type": "request_retry", "targetId": ""},
    ):
        payload = {"commandId": "dash_" + body["type"], "expiresAt": "2099-01-01T00:05:00.000Z",
                   "message": "", "exerciseId": "", **body}
        assert client.post(f"/api/v1/sessions/{sid}/commands", json=payload).status_code == 201
    types = [c["type"] for c in client.get(f"/api/v1/sessions/{sid}/commands").json()["commands"]]
    assert types == ["highlight_target", "request_pause", "request_retry"]


def test_expired_commands_are_not_polled_and_show_in_history(client):
    sid = _sid(client)
    old = dict(HIGHLIGHT, commandId="old", expiresAt="2000-01-01T00:00:00Z")
    assert client.post(f"/api/v1/sessions/{sid}/commands", json=old).status_code == 201
    for i in range(101):  # more than one poll page of expired rows
        body = dict(HIGHLIGHT, commandId=f"stale_{i}", expiresAt="2000-01-01T00:00:00Z")
        client.post(f"/api/v1/sessions/{sid}/commands", json=body)
    fresh = dict(HIGHLIGHT, commandId="fresh")
    client.post(f"/api/v1/sessions/{sid}/commands", json=fresh)
    rejected = dict(HIGHLIGHT, commandId="nope", targetId="target_9")
    client.post(f"/api/v1/sessions/{sid}/commands", json=rejected)
    polled = [c["commandId"] for c in client.get(f"/api/v1/sessions/{sid}/commands").json()["commands"]]
    assert polled == ["fresh", "nope"]
    client.post(f"/api/v1/sessions/{sid}/commands/fresh/ack", json={"status": "applied", "message": "Applied locally"})
    client.post(f"/api/v1/sessions/{sid}/commands/nope/ack", json={"status": "rejected", "message": "Unknown target"})
    history = client.get(f"/api/v1/sessions/{sid}/commands/history").json()["commands"]
    assert len(history) == 50
    by_id = {c["commandId"]: c for c in history}
    assert by_id["nope"]["state"] == "rejected" and by_id["nope"]["ackMessage"] == "Unknown target"
    assert by_id["fresh"]["state"] == "applied"
    assert by_id["stale_100"]["state"] == "expired"
    assert history[0]["commandId"] == "nope" and history[0]["createdAt"]
    assert client.get("/api/v1/sessions/missing/commands/history").status_code == 404
