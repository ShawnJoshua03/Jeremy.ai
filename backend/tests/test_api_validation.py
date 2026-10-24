import json
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(__file__)))
os.environ["DATABASE_URL"] = "sqlite:///:memory:"

import pytest
from fastapi.testclient import TestClient
from sqlalchemy import create_engine

import database as db


@pytest.fixture()
def client(tmp_path, monkeypatch):
    monkeypatch.delenv("GEMINI_API_KEY", raising=False)
    import main
    eng = create_engine(f"sqlite:///{tmp_path / 'api_validation.db'}",
                        connect_args={"check_same_thread": False})
    db.Base.metadata.create_all(eng)
    db.SessionLocal.configure(bind=eng)
    return TestClient(main.app)


def _sid(client, trainee="valid"):
    return client.post("/api/v1/sessions", json={"trainee_id": trainee}).json()["id"]


def _running(client):
    sid = _sid(client)
    client.post(f"/api/v1/sessions/{sid}/start")
    return sid


def test_non_finite_positions_rejected(client):
    sid = _running(client)
    for pos in (["NaN", 0, 0], ["Infinity", 0, 0], [0, "-Infinity", 0]):
        body = {"samples": [{"instrument": "left", "idx": 0, "t_ms": 0, "pos": pos}]}
        assert client.post(f"/api/v1/sessions/{sid}/samples", json=body).status_code == 422, pos
        assert client.get(f"/api/v1/sessions/{sid}/samples").json() == []


def _post_raw_elapsed(client, url, elapsed):
    # httpx json= rejects non-finite floats; send raw JSON instead
    body = json.dumps({"elapsed_s": elapsed}, allow_nan=True)
    return client.post(url, content=body, headers={"content-type": "application/json"})


def test_non_finite_elapsed_rejected(client):
    sid = _running(client)
    for elapsed in (float("nan"), math.inf, -math.inf):
        r = _post_raw_elapsed(client, f"/api/v1/sessions/{sid}/complete", elapsed)
        assert r.status_code == 422, f"{elapsed} -> {r.status_code}"
    assert client.get(f"/api/v1/sessions/{sid}").json()["status"] == "running"


def test_out_of_range_positions_rejected(client):
    sid = _running(client)
    for pos in ([10.01, 0, 0], [-10.01, 0, 0], [0, 10.001, 0], [0, 0, -10.001], [1e9, 0, 0]):
        body = {"samples": [{"instrument": "left", "idx": 0, "t_ms": 0, "pos": pos}]}
        assert client.post(f"/api/v1/sessions/{sid}/samples", json=body).status_code == 422, pos
    edge = {"samples": [{"instrument": "left", "idx": 0, "t_ms": 0, "pos": [10, -10, 10]}]}
    assert client.post(f"/api/v1/sessions/{sid}/samples", json=edge).json()["accepted"] == 1


def test_oversize_batches_rejected(client):
    sid = _running(client)
    big_samples = {"samples": [
        {"instrument": "left", "idx": i, "t_ms": i, "pos": [0, 0, 0]} for i in range(1001)
    ]}
    assert client.post(f"/api/v1/sessions/{sid}/samples", json=big_samples).status_code == 422
    big_events = {"events": [
        {"event_id": f"e{i}", "kind": "drop", "t_ms": i} for i in range(1001)
    ]}
    assert client.post(f"/api/v1/sessions/{sid}/events", json=big_events).status_code == 422
    max_samples = {"samples": [
        {"instrument": "left", "idx": i, "t_ms": i, "pos": [0, 0, 0]} for i in range(1000)
    ]}
    assert client.post(f"/api/v1/sessions/{sid}/samples", json=max_samples).json()["accepted"] == 1000
    max_events = {"events": [
        {"event_id": f"e{i}", "kind": "drop", "t_ms": i} for i in range(1000)
    ]}
    assert client.post(f"/api/v1/sessions/{sid}/events", json=max_events).json()["accepted"] == 1000


def test_bad_enums_rejected(client):
    assert client.post("/api/v1/sessions", json={"trainee_id": "t", "exercise": "wound_closure"}).status_code == 422
    sid = _running(client)
    bad_inst = {"samples": [{"instrument": "middle", "idx": 0, "t_ms": 0, "pos": [0, 0, 0]}]}
    assert client.post(f"/api/v1/sessions/{sid}/samples", json=bad_inst).status_code == 422
    bad_kind = {"events": [{"event_id": "e1", "kind": "explosion", "t_ms": 0}]}
    assert client.post(f"/api/v1/sessions/{sid}/events", json=bad_kind).status_code == 422
    bad_setting = client.get(f"/api/v1/sessions/{sid}/cost?setting=hospital")
    assert bad_setting.status_code == 422
    bad_instruments = client.get(f"/api/v1/sessions/{sid}/cost?instruments=single_use")
    assert bad_instruments.status_code == 422


def test_unknown_session_ids(client):
    missing = "/api/v1/sessions/0000000000000000"
    assert client.get(missing).status_code == 404
    assert client.get(missing + "/samples").status_code == 404
    assert client.post(missing + "/samples", json={"samples": []}).status_code == 404
    assert client.post(missing + "/events", json={"events": []}).status_code == 404
    assert client.post(missing + "/start").status_code == 404
    assert client.post(missing + "/complete", json={"elapsed_s": 1}).status_code == 404
    assert client.get(missing + "/metrics").status_code == 404
    assert client.get(missing + "/cost").status_code == 404
    assert client.post(missing + "/coach").status_code == 404
    assert client.get("/api/v1/trainees/0000000000000000/progress").json() == []


def test_complete_twice_is_idempotent(client):
    sid = _running(client)
    client.post(f"/api/v1/sessions/{sid}/samples", json={
        "samples": [{"instrument": "left", "idx": 0, "t_ms": 0, "pos": [0, 0, 0]},
                    {"instrument": "left", "idx": 1, "t_ms": 50, "pos": [0.2, 0, 0]}]})
    client.post(f"/api/v1/sessions/{sid}/events", json={
        "events": [{"event_id": "e1", "kind": "drop", "t_ms": 60}]})
    first = client.post(f"/api/v1/sessions/{sid}/complete", json={"elapsed_s": 30})
    assert first.status_code == 200
    second = client.post(f"/api/v1/sessions/{sid}/complete", json={"elapsed_s": 999})
    third = client.post(f"/api/v1/sessions/{sid}/complete", json={"elapsed_s": 30})
    assert second.status_code == 200 and third.status_code == 200
    assert second.json() == first.json() == third.json()
    assert client.get(f"/api/v1/sessions/{sid}").json()["status"] == "completed"
    assert client.get(f"/api/v1/sessions/{sid}/metrics").json() == first.json()


def test_events_after_completion_rejected(client):
    sid = _running(client)
    client.post(f"/api/v1/sessions/{sid}/complete", json={"elapsed_s": 10})
    late_event = {"events": [{"event_id": "late", "kind": "drop", "t_ms": 999}]}
    assert client.post(f"/api/v1/sessions/{sid}/events", json=late_event).status_code == 409
    late_sample = {"samples": [{"instrument": "left", "idx": 99, "t_ms": 999, "pos": [0, 0, 0]}]}
    assert client.post(f"/api/v1/sessions/{sid}/samples", json=late_sample).status_code == 409
    assert client.post(f"/api/v1/sessions/{sid}/start").status_code == 409


def test_event_geometry_is_not_validated(client):
    sid = _running(client)
    client.post(f"/api/v1/sessions/{sid}/samples", json={
        "samples": [{"instrument": "left", "idx": 0, "t_ms": 0, "pos": [0, 0, 0]},
                    {"instrument": "left", "idx": 1, "t_ms": 50, "pos": [0.2, 0, 0]}]})
    nan_event = {"events": [{"event_id": "bad", "kind": "transfer_success", "t_ms": 60,
                             "object_id": "o1", "source": ["NaN", 0, 0], "target": [0.1, 0, 0]}]}
    assert client.post(f"/api/v1/sessions/{sid}/events", json=nan_event).status_code == 200
    far_event = {"events": [{"event_id": "far", "kind": "transfer_success", "t_ms": 70,
                             "object_id": "o2", "source": [1e9, 0, 0], "target": [0, 0, 0]}]}
    assert client.post(f"/api/v1/sessions/{sid}/events", json=far_event).status_code == 200
    done = client.post(f"/api/v1/sessions/{sid}/complete", json={"elapsed_s": 10})
    assert done.status_code == 200
    if done.status_code == 200:
        m = done.json()
        assert math.isfinite(m["path_efficiency"]) and 0 <= m["path_efficiency"] <= 1
        assert math.isfinite(m["practice_score"]) and 0 <= m["practice_score"] <= 100
