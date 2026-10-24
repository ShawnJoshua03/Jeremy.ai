import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(__file__)))
os.environ["DATABASE_URL"] = "sqlite:///:memory:"

import pytest
from fastapi.testclient import TestClient
from sqlalchemy.pool import StaticPool

import database as db
import scoring


def S(i, t, x, valid=True, inst="left"):
    return scoring.Sample(inst, i, t, (x, 0.0, 0.0), valid)


def test_distance_straight_line():
    assert math.isclose(scoring.travel_distance([S(0, 0, 0), S(1, 50, 0.1), S(2, 100, 0.3)]), 0.3)


def test_distance_does_not_bridge_invalid_or_large_gaps():
    samples = [S(0, 0, 0), S(1, 50, 0.1), S(2, 100, 9, valid=False), S(3, 150, 0.2), S(4, 1000, 5.0)]
    assert math.isclose(scoring.travel_distance(samples), 0.1)


def test_metrics_counts_and_no_double_score():
    ev = [
        scoring.Event("transfer_success", 1, "left", "o1", (0, 0, 0), (0.1, 0, 0)),
        scoring.Event("transfer_success", 2, "left", "o1", (0, 0, 0), (0.1, 0, 0)),  # duplicate object
        scoring.Event("drop", 3), scoring.Event("invalid_release", 4),
    ]
    m = scoring.compute_metrics([S(0, 0, 0), S(1, 50, 0.2)], ev, 60)
    assert m.successful_transfers == 1 and m.drops == 1 and m.invalid_releases == 1
    assert math.isclose(m.path_efficiency, 0.5)
    assert 0 <= m.practice_score <= 100


def test_perfect_run_scores_high():
    ev = [scoring.Event("transfer_success", i, "left", f"o{i}", (0, 0, 0), (0.1, 0, 0)) for i in range(6)]
    samples = [S(i, i * 50, 0.1 * (i % 2)) for i in range(2)]
    m = scoring.compute_metrics(samples, ev, 0)
    assert m.completion_pct == 100 and m.practice_score > 90


@pytest.fixture()
def client():
    import main
    from sqlalchemy import create_engine
    eng = create_engine("sqlite://", connect_args={"check_same_thread": False}, poolclass=StaticPool)
    db.Base.metadata.create_all(eng)
    db.SessionLocal.configure(bind=eng)
    return TestClient(main.app)


def test_full_session_flow_idempotent(client):
    sid = client.post("/api/v1/sessions", json={"trainee_id": "t1"}).json()["id"]
    assert client.post(f"/api/v1/sessions/{sid}/start").json()["status"] == "running"
    batch = {"samples": [
        {"instrument": "left", "idx": 0, "t_ms": 0, "pos": [0, 0, 0]},
        {"instrument": "left", "idx": 1, "t_ms": 50, "pos": [0.2, 0, 0]},
    ]}
    assert client.post(f"/api/v1/sessions/{sid}/samples", json=batch).json()["accepted"] == 2
    assert client.post(f"/api/v1/sessions/{sid}/samples", json=batch).json()["duplicates"] == 2
    ev = {"events": [{"event_id": "e1", "kind": "transfer_success", "t_ms": 60, "object_id": "o1",
                      "source": [0, 0, 0], "target": [0.1, 0, 0]}]}
    assert client.post(f"/api/v1/sessions/{sid}/events", json=ev).json()["accepted"] == 1
    r1 = client.post(f"/api/v1/sessions/{sid}/complete", json={"elapsed_s": 30}).json()
    r2 = client.post(f"/api/v1/sessions/{sid}/complete", json={"elapsed_s": 99}).json()
    assert r1 == r2 and r1["successful_transfers"] == 1
    assert client.get("/api/v1/trainees/t1/progress").json()[0]["session_id"] == sid


def test_rejects_bad_coordinates(client):
    sid = client.post("/api/v1/sessions", json={"trainee_id": "t1"}).json()["id"]
    client.post(f"/api/v1/sessions/{sid}/start")
    bad = {"samples": [{"instrument": "left", "idx": 0, "t_ms": 0, "pos": [99, 0, 0]}]}
    assert client.post(f"/api/v1/sessions/{sid}/samples", json=bad).status_code == 422


def test_cost_endpoint(client):
    sid = client.post("/api/v1/sessions", json={"trainee_id": "t2"}).json()["id"]
    client.post(f"/api/v1/sessions/{sid}/start")
    assert client.get(f"/api/v1/sessions/{sid}/cost").status_code == 409
    client.post(f"/api/v1/sessions/{sid}/complete", json={"elapsed_s": 120})
    body = client.get(f"/api/v1/sessions/{sid}/cost").json()
    assert body["total_usd"] > 0 and len(body["lines"]) == 4
