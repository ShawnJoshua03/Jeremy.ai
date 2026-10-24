import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(__file__)))
os.environ["DATABASE_URL"] = "sqlite:///:memory:"

import pytest
from fastapi.testclient import TestClient
from sqlalchemy import create_engine
from sqlalchemy.pool import StaticPool

import database as db


@pytest.fixture()
def client(monkeypatch):
    monkeypatch.delenv("GEMINI_API_KEY", raising=False)
    monkeypatch.delenv("API_KEY", raising=False)
    monkeypatch.delenv("CORS_ORIGINS", raising=False)
    import main
    eng = create_engine("sqlite://", connect_args={"check_same_thread": False}, poolclass=StaticPool)
    db.Base.metadata.create_all(eng)
    db.SessionLocal.configure(bind=eng)
    return TestClient(main.app)


def test_session_list_filters_trainee_status_and_limit(client):
    ada_running = client.post("/api/v1/sessions", json={"trainee_id": "ada"}).json()["id"]
    assert client.post(f"/api/v1/sessions/{ada_running}/start").json()["status"] == "running"
    ada_ready = client.post("/api/v1/sessions", json={"trainee_id": "ada"}).json()["id"]
    bea = client.post("/api/v1/sessions", json={"trainee_id": "bea"}).json()["id"]

    listed = client.get("/api/v1/sessions").json()
    assert {row["id"] for row in listed} == {ada_running, ada_ready, bea}
    assert [row["created_at"] for row in listed] == sorted((row["created_at"] for row in listed), reverse=True)
    assert listed[0]["trainee_id"] in {"ada", "bea"} and listed[0]["status"] in {"ready", "running"}

    ada = client.get("/api/v1/sessions", params={"trainee_id": "ada", "status": ""}).json()
    assert {row["id"] for row in ada} == {ada_running, ada_ready}
    running = client.get("/api/v1/sessions", params={"trainee_id": "ada", "status": "running"}).json()
    assert [row["id"] for row in running] == [ada_running]
    assert client.get("/api/v1/sessions", params={"limit": 1}).json().__len__() == 1
    assert client.get("/api/v1/sessions", params={"trainee_id": "missing"}).json() == []
    assert client.get("/api/v1/sessions", params={"status": "paused"}).status_code == 422
    assert client.get("/api/v1/sessions", params={"limit": 0}).status_code == 422


def test_replay_reads_events_and_annotations_by_t_ms(client):
    sid = client.post("/api/v1/sessions", json={"trainee_id": "ada"}).json()["id"]
    assert client.get("/api/v1/sessions/missing/events").status_code == 404
    assert client.get("/api/v1/sessions/missing/annotations").status_code == 404
    assert client.get(f"/api/v1/sessions/{sid}/events").json() == []
    assert client.get(f"/api/v1/sessions/{sid}/annotations").json() == []

    posted = client.post(
        f"/api/v1/sessions/{sid}/events",
        json={"events": [
            {"event_id": "late", "kind": "drop", "t_ms": 80, "instrument": "right",
             "object_id": "peg-2", "source": [0.1, 0.0, 0.0], "target": [0.2, 0.0, 0.0]},
            {"event_id": "early", "kind": "transfer_success", "t_ms": 40, "instrument": "left",
             "object_id": "peg-1"},
            {"event_id": "tie", "kind": "invalid_release", "t_ms": 40, "instrument": "left",
             "object_id": "peg-1"},
        ]},
    )
    assert posted.status_code == 200
    events = client.get(f"/api/v1/sessions/{sid}/events").json()
    assert [row["event_id"] for row in events] == ["early", "tie", "late"]
    assert events[0]["t_ms"] == 40 and events[0]["kind"] == "transfer_success"
    assert events[0]["instrument"] == "left" and events[0]["object_id"] == "peg-1"
    assert events[0]["source"] is None and events[0]["target"] is None
    assert events[2]["source"] == [0.1, 0.0, 0.0] and events[2]["target"] == [0.2, 0.0, 0.0]

    late = client.post(
        f"/api/v1/sessions/{sid}/annotations",
        json={"at_ms": 90, "text": "late note", "author": "coach"},
    )
    early = client.post(
        f"/api/v1/sessions/{sid}/annotations",
        json={"at_ms": 10, "text": "early note", "author": "coach"},
    )
    same = client.post(
        f"/api/v1/sessions/{sid}/annotations",
        json={"at_ms": 10, "text": "same time", "author": "ada"},
    )
    assert late.status_code == 201 and early.status_code == 201 and same.status_code == 201
    notes = client.get(f"/api/v1/sessions/{sid}/annotations").json()
    assert [row["text"] for row in notes] == ["early note", "same time", "late note"]
    assert notes[0]["t_ms"] == 10 and notes[0]["at_ms"] == 10
    assert notes[0]["author"] == "coach"
    assert notes[0]["annotation_id"] == early.json()["annotation_id"]
    assert notes[2]["t_ms"] == 90

    other = client.post("/api/v1/sessions", json={"trainee_id": "bea"}).json()["id"]
    assert client.get(f"/api/v1/sessions/{other}/events").json() == []
    assert client.get(f"/api/v1/sessions/{other}/annotations").json() == []


def test_cors_uses_env_origins(client, monkeypatch):
    default = "http://localhost:5173"
    ok = client.get("/health", headers={"Origin": default})
    assert ok.headers["access-control-allow-origin"] == default
    blocked = client.get("/health", headers={"Origin": "http://evil.example"})
    assert blocked.status_code == 200
    assert "access-control-allow-origin" not in blocked.headers

    monkeypatch.setenv("CORS_ORIGINS", "http://dashboard.local, http://127.0.0.1:5173")
    custom = client.get("/api/v1/sessions", headers={"Origin": "http://dashboard.local"})
    assert custom.status_code == 200
    assert custom.headers["access-control-allow-origin"] == "http://dashboard.local"
    loopback = client.get("/health", headers={"Origin": "http://127.0.0.1:5173"})
    assert loopback.headers["access-control-allow-origin"] == "http://127.0.0.1:5173"
    stale = client.get("/health", headers={"Origin": default})
    assert "access-control-allow-origin" not in stale.headers

    preflight = client.options(
        "/api/v1/sessions",
        headers={
            "Origin": "http://dashboard.local",
            "Access-Control-Request-Method": "POST",
            "Access-Control-Request-Headers": "content-type,x-api-key",
        },
    )
    assert preflight.status_code == 200
    assert preflight.headers["access-control-allow-origin"] == "http://dashboard.local"
    allowed = preflight.headers["access-control-allow-headers"].lower()
    assert "content-type" in allowed and "x-api-key" in allowed


def test_client_request_id_returns_the_existing_session(client):
    first = client.post("/api/v1/sessions", json={"trainee_id": "ada", "client_request_id": "req-1"})
    assert first.status_code == 201
    created = first.json()
    assert "client_request_id" not in created
    sid = created["id"]
    assert client.post(f"/api/v1/sessions/{sid}/start").json()["status"] == "running"

    again = client.post("/api/v1/sessions", json={
        "trainee_id": "ada",
        "exercise": "peg_transfer",
        "client_request_id": "  req-1  ",
    })
    assert again.status_code == 200
    replayed = again.json()
    assert replayed["id"] == sid and replayed["status"] == "running"
    assert replayed["trainee_id"] == "ada" and replayed["exercise"] == "peg_transfer"
    assert "client_request_id" not in replayed

    clash = client.post("/api/v1/sessions", json={"trainee_id": "bea", "client_request_id": "req-1"})
    assert clash.status_code == 409
    assert clash.json()["detail"] == "client_request_id already exists"

    running = client.post("/api/v1/sessions", json={"trainee_id": "ada", "client_request_id": "req-run"}).json()["id"]
    assert client.post(f"/api/v1/sessions/{running}/start").status_code == 200
    sample = {"samples": [{"instrument": "left", "idx": 0, "t_ms": 0, "pos": [0, 0, 0]}]}
    assert client.post(f"/api/v1/sessions/{running}/samples", json=sample).json() == {"accepted": 1, "duplicates": 0}
    retry = client.post("/api/v1/sessions", json={"trainee_id": "ada", "client_request_id": "req-run"})
    assert retry.status_code == 200 and retry.json()["id"] == running and retry.json()["status"] == "running"
    assert client.post(f"/api/v1/sessions/{running}/samples", json=sample).json() == {"accepted": 0, "duplicates": 1}

    omitted = client.post("/api/v1/sessions", json={"trainee_id": "ada"}).json()["id"]
    blank = client.post("/api/v1/sessions", json={"trainee_id": "ada", "client_request_id": "   "}).json()["id"]
    assert len({sid, omitted, blank}) == 3
    assert client.post("/api/v1/sessions", json={"trainee_id": "ada", "client_request_id": "bad id"}).status_code == 422
    assert client.post("/api/v1/sessions", json={"trainee_id": "ada", "client_request_id": "a" * 65}).status_code == 422


def test_api_key_gates_v1_when_env_is_set(client, monkeypatch):
    monkeypatch.setenv("API_KEY", "test-key")
    assert client.get("/health").json() == {"status": "ok"}
    assert client.get("/health", headers={"X-API-Key": "nope"}).status_code == 200

    missing = client.post(
        "/api/v1/sessions",
        json={"trainee_id": "ada"},
        headers={"Origin": "http://localhost:5173"},
    )
    assert missing.status_code == 401
    assert missing.json() == {"detail": "invalid api key"}
    assert missing.headers["access-control-allow-origin"] == "http://localhost:5173"
    wrong = client.get("/api/v1/sessions", headers={"X-API-Key": "nope"})
    assert wrong.status_code == 401 and wrong.json()["detail"] == "invalid api key"

    preflight = client.options(
        "/api/v1/sessions",
        headers={
            "Origin": "http://localhost:5173",
            "Access-Control-Request-Method": "POST",
            "Access-Control-Request-Headers": "content-type,x-api-key",
        },
    )
    assert preflight.status_code == 200
    assert preflight.headers["access-control-allow-origin"] == "http://localhost:5173"

    created = client.post(
        "/api/v1/sessions",
        json={"trainee_id": "ada"},
        headers={"X-API-Key": "test-key"},
    )
    assert created.status_code == 201
    sid = created.json()["id"]
    assert client.get(f"/api/v1/sessions/{sid}/events", headers={"X-API-Key": "test-key"}).status_code == 200

    monkeypatch.setenv("API_KEY", "   ")
    assert client.post("/api/v1/sessions", json={"trainee_id": "ada"}).status_code == 201


def test_api_key_accepts_bearer_from_quest(client, monkeypatch):
    """The Quest ApiClient sends the key as X-API-Key; bearer is also accepted."""
    monkeypatch.setenv("API_KEY", "test-key")
    ok = client.get("/api/v1/sessions", headers={"Authorization": "Bearer test-key"})
    assert ok.status_code == 200
    assert client.get("/api/v1/sessions", headers={"Authorization": "Bearer nope"}).status_code == 401
    assert client.get("/api/v1/sessions", headers={"Authorization": "test-key"}).status_code == 401
    assert client.get("/api/v1/sessions", headers={"X-API-Key": "tést".encode("latin-1")}).status_code == 401


def test_session_list_carries_stored_metrics(client):
    sid = client.post("/api/v1/sessions", json={"trainee_id": "ada"}).json()["id"]
    assert client.get(f"/api/v1/sessions/{sid}").json()["metrics"] is None
    client.post(f"/api/v1/sessions/{sid}/start")
    done = client.post(f"/api/v1/sessions/{sid}/complete", json={"elapsed_s": 12}).json()
    listed = {row["id"]: row for row in client.get("/api/v1/sessions").json()}
    assert listed[sid]["metrics"] == done
    assert client.get(f"/api/v1/sessions/{sid}").json()["metrics"] == done


def test_timestamps_read_back_as_utc(client):
    """SQLite drops tzinfo; every read must still carry UTC so browsers do not shift it."""
    sid = client.post("/api/v1/sessions", json={"trainee_id": "ada"}).json()["id"]
    client.post(f"/api/v1/sessions/{sid}/start")
    client.post(f"/api/v1/sessions/{sid}/complete", json={"elapsed_s": 1})
    for row in (client.get(f"/api/v1/sessions/{sid}").json(), client.get("/api/v1/sessions").json()[0]):
        for key in ("created_at", "started_at", "completed_at"):
            assert row[key].endswith(("Z", "+00:00")), (key, row[key])
