import convex_mirror
import database as db
import httpx
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
MIRROR = "http://127.0.0.1:9"
TOKEN = "test-deploy-key"


@pytest.fixture()
def client(monkeypatch):
    monkeypatch.delenv("GEMINI_API_KEY", raising=False)
    monkeypatch.delenv("CONVEX_URL", raising=False)
    monkeypatch.delenv("CONVEX_DEPLOY_KEY", raising=False)
    import main
    eng = create_engine("sqlite://", connect_args={"check_same_thread": False}, poolclass=StaticPool)
    db.Base.metadata.create_all(eng)
    db.SessionLocal.configure(bind=eng)
    return TestClient(main.app)


def _sid(client):
    return client.post("/api/v1/sessions", json={"trainee_id": "mirror"}).json()["id"]


def _capture(monkeypatch, raises=False):
    calls = []

    def post(url, json, headers, timeout, follow_redirects):
        calls.append({"url": url, "json": json, "headers": headers, "timeout": timeout,
                      "follow_redirects": follow_redirects})
        if raises:
            raise httpx.ConnectError("convex down")
        return httpx.Response(204)

    monkeypatch.setattr(convex_mirror.httpx, "post", post)
    return calls


def test_mirror_is_a_noop_when_env_is_unset(client, monkeypatch):
    calls = _capture(monkeypatch)
    sid = _sid(client)
    assert client.post(
        f"/api/v1/sessions/{sid}/events",
        json={"events": [{"event_id": "e1", "kind": "drop", "t_ms": 10}]},
    ).json()["accepted"] == 1
    monkeypatch.setenv("CONVEX_URL", MIRROR)
    assert client.post(
        f"/api/v1/sessions/{sid}/events",
        json={"events": [{"event_id": "e2", "kind": "drop", "t_ms": 20}]},
    ).json()["accepted"] == 1
    assert calls == []


def test_event_command_and_annotation_are_mirrored(client, monkeypatch):
    monkeypatch.setenv("CONVEX_URL", MIRROR)
    monkeypatch.setenv("CONVEX_DEPLOY_KEY", TOKEN)
    calls = _capture(monkeypatch)
    sid = _sid(client)
    calls.clear()  # creating the session mirrors its row; this test is about the rest
    created = client.post(f"/api/v1/sessions/{sid}/commands", json=HIGHLIGHT)
    assert created.status_code == 201
    assert created.text.find(TOKEN) == -1
    event = client.post(
        f"/api/v1/sessions/{sid}/events",
        json={"events": [{"event_id": "drop-1", "kind": "drop", "t_ms": 40, "instrument": "left",
                          "source": [0, 0, 0], "target": [0.1, 0, 0]}]},
    )
    assert event.json() == {"accepted": 1, "duplicates": 0}
    note = client.post(
        f"/api/v1/sessions/{sid}/annotations",
        json={"at_ms": 40, "text": "  watch the release  ", "author": "ada"},
    )
    assert note.status_code == 201
    body = note.json()
    assert body["text"] == "watch the release" and body["author"] == "ada" and body["at_ms"] == 40
    ack = client.post(
        f"/api/v1/sessions/{sid}/commands/cmd_018/ack",
        json={"status": "applied", "message": "Applied locally"},
    )
    assert ack.json()["status"] == "applied"

    assert [call["url"] for call in calls] == [f"{MIRROR}/v1/mirror"] * 4
    assert all(call["headers"]["Authorization"] == "Bearer " + TOKEN for call in calls)
    assert all(call["follow_redirects"] is False for call in calls)
    command = calls[0]["json"]
    assert command["commands"][0]["type"] == "highlight"
    assert command["commands"][0]["state"] == "queued"
    assert command["commands"][0]["commandId"] == "cmd_018"
    assert command["events"] == [] and command["annotations"] == []
    mirrored_event = calls[1]["json"]["events"][0]
    assert mirrored_event["eventId"] == "drop-1" and mirrored_event["tMs"] == 40
    assert mirrored_event["source"] == [0, 0, 0]
    assert calls[2]["json"]["annotations"][0]["text"] == "watch the release"
    assert calls[2]["json"]["annotations"][0]["author"] == "ada"
    note_command = calls[2]["json"]["commands"][0]
    assert note_command["type"] == "annotation" and note_command["state"] == "queued"
    assert calls[3]["json"]["commands"][0]["state"] == "acknowledged"
    listed = client.get(f"/api/v1/sessions/{sid}/commands").json()["commands"]
    assert [c["type"] for c in listed] == ["add_annotation"]
    assert listed[0]["message"] == "watch the release"


def test_annotation_command_maps_to_an_annotation(client, monkeypatch):
    monkeypatch.setenv("CONVEX_URL", MIRROR + "/v1/mirror")
    monkeypatch.setenv("CONVEX_DEPLOY_KEY", TOKEN)
    calls = _capture(monkeypatch)
    sid = _sid(client)
    calls.clear()  # creating the session mirrors its row; this test is about the rest
    body = dict(HIGHLIGHT, commandId="note-1", type="add_annotation", message="Slow down", targetId="")
    assert client.post(f"/api/v1/sessions/{sid}/commands", json=body).status_code == 201
    assert calls[0]["url"] == MIRROR + "/v1/mirror"
    snapshot = calls[0]["json"]
    assert snapshot["commands"][0]["type"] == "annotation"
    assert snapshot["annotations"][0]["annotationId"] == "note-1"
    assert snapshot["annotations"][0]["text"] == "Slow down"
    removed = dict(HIGHLIGHT, commandId="ex-1", type="assign_exercise", exerciseId="peg_transfer")
    assert client.post(f"/api/v1/sessions/{sid}/commands", json=removed).status_code == 422  # exercise choice is gone


def test_convex_failure_keeps_the_write(client, monkeypatch):
    monkeypatch.setenv("CONVEX_URL", MIRROR)
    monkeypatch.setenv("CONVEX_DEPLOY_KEY", TOKEN)
    _capture(monkeypatch, raises=True)
    sid = _sid(client)
    assert client.post(
        f"/api/v1/sessions/{sid}/events",
        json={"events": [{"event_id": "e1", "kind": "drop", "t_ms": 1}]},
    ).json()["accepted"] == 1
    note = client.post(
        f"/api/v1/sessions/{sid}/annotations",
        json={"at_ms": 1, "text": "still saved", "author": "ada"},
    )
    assert note.status_code == 201
    assert client.post(f"/api/v1/sessions/{sid}/commands", json=HIGHLIGHT).status_code == 201


def test_status_changes_are_mirrored(client, monkeypatch):
    monkeypatch.setenv("CONVEX_URL", MIRROR)
    monkeypatch.setenv("CONVEX_DEPLOY_KEY", TOKEN)
    calls = _capture(monkeypatch)
    sid = _sid(client)
    client.post(f"/api/v1/sessions/{sid}/start")
    other = _sid(client)
    client.post(f"/api/v1/sessions/{other}/cancel")
    client.post(f"/api/v1/sessions/{sid}/complete", json={"elapsed_s": 3})
    statuses = [(c["json"]["sessions"][0]["sessionId"], c["json"]["sessions"][0]["status"]) for c in calls]
    assert statuses == [(sid, "ready"), (sid, "running"), (other, "ready"), (other, "cancelled"), (sid, "completed")]
    assert all(c["json"]["events"] == [] and c["json"]["commands"] == [] for c in calls)


def test_metrics_ack_status_and_secret_name(client, monkeypatch, caplog):
    monkeypatch.setenv("CONVEX_URL", MIRROR)
    monkeypatch.setenv("CONVEX_INGEST_SHARED_SECRET", "new-secret")
    calls = _capture(monkeypatch)
    sid = _sid(client)
    client.post(f"/api/v1/sessions/{sid}/start")
    client.post(f"/api/v1/sessions/{sid}/complete", json={"elapsed_s": 3})
    assert calls[-1]["headers"]["Authorization"] == "Bearer new-secret"
    mirrored = calls[-1]["json"]["sessions"][0]
    assert mirrored["status"] == "completed" and mirrored["metrics"]["elapsedS"] == 3
    assert set(mirrored["metrics"]) == {"completionPct", "successfulTransfers", "drops", "invalidReleases",
                                        "elapsedS", "distanceByInstrument", "totalDistanceM",
                                        "pathEfficiency", "practiceScore"}
    client.post(f"/api/v1/sessions/{sid}/commands", json=HIGHLIGHT)
    client.post(f"/api/v1/sessions/{sid}/commands/cmd_018/ack", json={"status": "rejected", "message": "No active exercise"})
    acked = calls[-1]["json"]["commands"][0]
    assert acked["state"] == "acknowledged" and acked["ackStatus"] == "rejected"


def test_rejected_snapshot_is_logged(client, monkeypatch, caplog):
    import convex_mirror
    monkeypatch.setenv("CONVEX_URL", MIRROR)
    monkeypatch.setenv("CONVEX_INGEST_SHARED_SECRET", "wrong")
    monkeypatch.setattr(convex_mirror.httpx, "post", lambda *a, **k: httpx.Response(401))
    with caplog.at_level("WARNING", logger="convex_mirror"):
        _sid(client)
    assert "HTTP 401" in caplog.text
