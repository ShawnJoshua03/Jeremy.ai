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
