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
