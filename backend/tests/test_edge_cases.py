import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(__file__)))
os.environ["DATABASE_URL"] = "sqlite:///:memory:"

import database as db
import pytest
import scoring
from fastapi.testclient import TestClient
from sqlalchemy import create_engine
from sqlalchemy.pool import StaticPool


def S(i, t, x, valid=True, inst="left"):
    return scoring.Sample(inst, i, t, (x, 0.0, 0.0), valid)


def test_empty_run_scores_time_and_clean_errors_only():
    m = scoring.compute_metrics([], [], 0)
    assert m.successful_transfers == 0 and m.total_distance_m == 0
    assert m.path_efficiency == 0 and m.practice_score == 30.0


def test_time_and_error_budgets_clamp_the_score_to_zero():
    drops = [scoring.Event("drop", i) for i in range(7)]
    m = scoring.compute_metrics([], drops, 9999)
    assert m.drops == 7 and m.practice_score == 0.0


def test_completion_percent_caps_at_six_objects_but_the_count_does_not():
    ev = [scoring.Event("transfer_success", i, "left", f"o{i}", (0, 0, 0), (0.1, 0, 0)) for i in range(8)]
    m = scoring.compute_metrics([], ev, 0)
    assert m.successful_transfers == 8 and m.completion_pct == 100
    assert 0 <= m.practice_score <= 100


def test_gap_boundary_unsorted_indexes_and_separate_instruments():
    assert math.isclose(scoring.travel_distance([S(0, 0, 0), S(1, 250, 0.2)]), 0.2)
    assert math.isclose(scoring.travel_distance([S(0, 0, 0), S(1, 251, 0.2)]), 0.0)
    assert math.isclose(scoring.travel_distance([S(0, 100, 0), S(1, 100, 0.5)]), 0.0)
    assert math.isclose(scoring.travel_distance([S(1, 50, 0.4), S(0, 0, 0.0)]), 0.4)
    samples = [S(0, 0, 0, inst="left"), S(1, 40, 0.1, inst="left"),
               S(0, 0, 0, inst="right"), S(1, 40, 0.25, inst="right")]
    m = scoring.compute_metrics(samples, [], 10)
    assert math.isclose(m.distance_by_instrument["left"], 0.1)
    assert math.isclose(m.distance_by_instrument["right"], 0.25)
    assert math.isclose(m.total_distance_m, 0.35)


def test_efficiency_clamps_when_the_ideal_path_is_longer_than_travel():
    ev = [scoring.Event("transfer_success", i, "left", f"o{i}", (0, 0, 0), (1, 0, 0)) for i in range(6)]
    m = scoring.compute_metrics([S(0, 0, 0), S(1, 50, 0.01)], ev, 0)
    assert m.path_efficiency == 1.0 and m.practice_score == 100.0


def test_unknown_kinds_anonymous_ids_and_missing_geometry():
    ev = [
        scoring.Event("whoops", 1),
        scoring.Event("drop", 2),
        scoring.Event("transfer_success", 5),
        scoring.Event("transfer_success", 5),
        scoring.Event("transfer_success", 6),
        scoring.Event("transfer_success", 7, "left", "peg", None, None),
    ]
    m = scoring.compute_metrics([S(0, 0, 0), S(1, 50, 0.2)], ev, -10)
    assert m.drops == 1 and m.invalid_releases == 0
    assert m.successful_transfers == 3  # one anon timestamp, a second timestamp, and "peg"
    assert m.path_efficiency == 0.0  # no source/target pairs contribute ideal distance
    assert m.practice_score == round(100 * (0.45 * (3 / 6) + 0.20 + 0.10 * (1 - 1 / 6)), 1)


def test_invalid_samples_add_no_distance():
    assert scoring.travel_distance([S(0, 0, 0, valid=False), S(1, 50, 1, valid=False)]) == 0


@pytest.fixture()
def client(monkeypatch):
    monkeypatch.delenv("GEMINI_API_KEY", raising=False)
    import main
    eng = create_engine("sqlite://", connect_args={"check_same_thread": False}, poolclass=StaticPool)
    db.Base.metadata.create_all(eng)
    db.SessionLocal.configure(bind=eng)
    return TestClient(main.app)


def _sid(client, trainee="edge"):
    return client.post("/api/v1/sessions", json={"trainee_id": trainee}).json()["id"]


def test_missing_session_and_illegal_transitions(client):
    missing = "/api/v1/sessions/missing"
    assert client.get(missing).status_code == 404
    assert client.post(missing + "/start").status_code == 404
    assert client.post(missing + "/complete", json={"elapsed_s": 1}).status_code == 404
    assert client.get(missing + "/cost").status_code == 404
    assert client.post(missing + "/coach").status_code == 404

    sid = _sid(client)
    assert client.post(f"/api/v1/sessions/{sid}/complete", json={"elapsed_s": 1}).status_code == 409
    assert client.get(f"/api/v1/sessions/{sid}/metrics").status_code == 409
    assert client.post(f"/api/v1/sessions/{sid}/coach").status_code == 409
    assert client.post("/api/v1/sessions", json={"trainee_id": ""}).status_code == 422

    assert client.post(f"/api/v1/sessions/{sid}/start").json()["status"] == "running"
    assert client.post(f"/api/v1/sessions/{sid}/start").json()["status"] == "running"
    assert client.get(f"/api/v1/sessions/{sid}/metrics").status_code == 200
    assert client.post(f"/api/v1/sessions/{sid}/coach").status_code == 409


def test_coordinate_bounds_and_duplicate_event_ids(client):
    sid = _sid(client)
    ok = {"samples": [{"instrument": "left", "idx": 0, "t_ms": 0, "pos": [10, -10, 10]}]}
    assert client.post(f"/api/v1/sessions/{sid}/samples", json=ok).status_code == 200
    far = {"samples": [{"instrument": "left", "idx": 1, "t_ms": 10, "pos": [10.01, 0, 0]}]}
    assert client.post(f"/api/v1/sessions/{sid}/samples", json=far).status_code == 422
    huge = {"samples": [{"instrument": "left", "idx": 2, "t_ms": 20, "pos": [1e9, 0, 0]}]}
    assert client.post(f"/api/v1/sessions/{sid}/samples", json=huge).status_code == 422

    batch = {"events": [
        {"event_id": "e1", "kind": "drop", "t_ms": 1},
        {"event_id": "e1", "kind": "invalid_release", "t_ms": 2},
    ]}
    assert client.post(f"/api/v1/sessions/{sid}/events", json=batch).json() == {"accepted": 1, "duplicates": 1}
    assert client.post(f"/api/v1/sessions/{sid}/events", json=batch).json()["accepted"] == 0
    assert client.post(f"/api/v1/sessions/{sid}/events", json={"events": []}).json()["accepted"] == 0
    bad_kind = {"events": [{"event_id": "e2", "kind": "explosion", "t_ms": 3}]}
    assert client.post(f"/api/v1/sessions/{sid}/events", json=bad_kind).status_code == 422


def test_elapsed_bounds_closed_session_and_coach_fallback(client):
    sid = _sid(client)
    client.post(f"/api/v1/sessions/{sid}/start")
    assert client.post(f"/api/v1/sessions/{sid}/complete", json={"elapsed_s": -1}).status_code == 422
    assert client.post(f"/api/v1/sessions/{sid}/complete", json={"elapsed_s": 3600.01}).status_code == 422

    events = {"events": [
        {"event_id": "ok1", "kind": "transfer_success", "t_ms": 1000, "object_id": "p1",
         "source": [0, 0, 0], "target": [0.1, 0, 0]},
        {"event_id": "drop1", "kind": "drop", "t_ms": 1500, "instrument": "left"},
    ]}
    assert client.post(f"/api/v1/sessions/{sid}/events", json=events).json()["accepted"] == 2
    done = client.post(f"/api/v1/sessions/{sid}/complete", json={"elapsed_s": 90}).json()
    assert done["successful_transfers"] == 1 and done["drops"] == 1

    assert client.post(f"/api/v1/sessions/{sid}/samples", json={"samples": []}).status_code == 409
    assert client.post(f"/api/v1/sessions/{sid}/events", json={"events": []}).status_code == 409
    assert client.get(f"/api/v1/sessions/{sid}/cost?setting=nope").status_code == 422

    body = client.post(f"/api/v1/sessions/{sid}/coach").json()
    assert body["source"] == "fallback"
    assert body["summary"] == "1 of 6 transfers in 90s with 1 errors. Practice score 30/100."
    ids = [t["event_id"] for t in body["tips"]]
    assert "drop1" in ids and "ok1" not in ids
    assert any("indirect" in t["text"] for t in body["tips"])
