import database as db
import pytest
from fastapi.testclient import TestClient
from sqlalchemy import create_engine, event, inspect
from sqlalchemy.pool import StaticPool


@pytest.fixture()
def client(monkeypatch):
    monkeypatch.delenv("GEMINI_API_KEY", raising=False)
    import main
    eng = create_engine("sqlite://", connect_args={"check_same_thread": False}, poolclass=StaticPool)
    db.Base.metadata.create_all(eng)
    db.SessionLocal.configure(bind=eng)
    return TestClient(main.app)


def _session(client):
    sid = client.post("/api/v1/sessions", json={"trainee_id": "idem"}).json()["id"]
    return sid


def _samples(start_idx=0):
    return {"samples": [
        {"instrument": "left", "idx": start_idx, "t_ms": start_idx * 40, "pos": [0.0, 0.0, 0.0]},
        {"instrument": "left", "idx": start_idx + 1, "t_ms": (start_idx + 1) * 40, "pos": [0.2, 0.0, 0.0]},
    ]}


def test_sample_upload_is_idempotent_and_does_not_double_distance(client):
    sid = _session(client)
    client.post(f"/api/v1/sessions/{sid}/start")
    batch = _samples()
    assert client.post(f"/api/v1/sessions/{sid}/samples", json=batch).json() == {"accepted": 2, "duplicates": 0}
    assert client.post(f"/api/v1/sessions/{sid}/samples", json=batch).json() == {"accepted": 0, "duplicates": 2}
    stored = client.get(f"/api/v1/sessions/{sid}/samples").json()
    assert len(stored) == 2
    metrics = client.post(f"/api/v1/sessions/{sid}/complete", json={"elapsed_s": 30}).json()
    assert metrics["total_distance_m"] == 0.2


def test_event_upload_is_idempotent_and_overlapping_batch_counts_once(client):
    sid = _session(client)
    client.post(f"/api/v1/sessions/{sid}/start")
    first = {"events": [
        {"event_id": "d1", "kind": "drop", "t_ms": 10},
        {"event_id": "x1", "kind": "transfer_success", "t_ms": 20, "object_id": "p1",
         "source": [0, 0, 0], "target": [0.1, 0, 0]},
    ]}
    assert client.post(f"/api/v1/sessions/{sid}/events", json=first).json() == {"accepted": 2, "duplicates": 0}
    assert client.post(f"/api/v1/sessions/{sid}/events", json=first).json() == {"accepted": 0, "duplicates": 2}
    overlap = {"events": [
        {"event_id": "d1", "kind": "drop", "t_ms": 10},
        {"event_id": "d2", "kind": "invalid_release", "t_ms": 30},
    ]}
    assert client.post(f"/api/v1/sessions/{sid}/events", json=overlap).json() == {"accepted": 1, "duplicates": 1}
    metrics = client.post(f"/api/v1/sessions/{sid}/complete", json={"elapsed_s": 40}).json()
    assert metrics["drops"] == 1 and metrics["invalid_releases"] == 1 and metrics["successful_transfers"] == 1


def test_same_index_on_the_other_instrument_is_a_new_sample(client):
    sid = _session(client)
    one = {"samples": [{"instrument": "left", "idx": 0, "t_ms": 0, "pos": [0, 0, 0]}]}
    other = {"samples": [{"instrument": "right", "idx": 0, "t_ms": 0, "pos": [0, 0.1, 0]}]}
    assert client.post(f"/api/v1/sessions/{sid}/samples", json=one).json()["accepted"] == 1
    assert client.post(f"/api/v1/sessions/{sid}/samples", json=other).json() == {"accepted": 1, "duplicates": 0}
    assert len(client.get(f"/api/v1/sessions/{sid}/samples").json()) == 2


def test_dedupe_at_scale_uses_conflict_insert(client):
    sid = _session(client)
    path = f"/api/v1/sessions/{sid}"

    def samples(start, count=1000):
        return {"samples": [
            {"instrument": "left", "idx": start + i, "t_ms": (start + i) * 40, "pos": [0.001 * i, 0.0, 0.0]}
            for i in range(count)
        ]}

    def events(start, count=1000):
        return {"events": [
            {"event_id": f"scale-{start + i}", "kind": "drop", "t_ms": start + i}
            for i in range(count)
        ]}

    for start in (0, 1000, 2000):
        assert client.post(path + "/samples", json=samples(start)).json() == {"accepted": 1000, "duplicates": 0}
    for start in (0, 1000):
        assert client.post(path + "/events", json=events(start)).json() == {"accepted": 1000, "duplicates": 0}

    bind = db.SessionLocal.kw["bind"]
    assert "ix_samples_session_t_ms" in {item["name"] for item in inspect(bind).get_indexes("samples")}
    sql: list[str] = []

    def record(_conn, _cursor, statement, _parameters, _context, _executemany):
        sql.append(statement)

    event.listen(bind, "before_cursor_execute", record)
    try:
        sample_replay = client.post(path + "/samples", json=samples(0)).json()
        repeated = samples(3000, 2)
        repeated["samples"].append(dict(repeated["samples"][0]))
        sample_overlap = client.post(path + "/samples", json=repeated).json()
        event_replay = client.post(path + "/events", json=events(0)).json()
    finally:
        event.remove(bind, "before_cursor_execute", record)

    assert sample_replay == {"accepted": 0, "duplicates": 1000}
    assert sample_overlap == {"accepted": 2, "duplicates": 1}
    assert event_replay == {"accepted": 0, "duplicates": 1000}
    lowered = [statement.lower() for statement in sql]
    assert any("on conflict" in statement and "samples" in statement for statement in lowered)
    assert any("on conflict" in statement and "events" in statement for statement in lowered)
    assert not any(statement.lstrip().startswith("select") and " from samples" in statement for statement in lowered)
    assert not any(statement.lstrip().startswith("select") and " from events" in statement for statement in lowered)
    assert len(client.get(path + "/samples").json()) == 3002


def test_postgres_url_uses_psycopg_driver():
    assert db.normalize_database_url("postgres://db.example:5432/surg") == "postgresql+psycopg://db.example:5432/surg"
    assert db.normalize_database_url("postgresql://db.example/surg") == "postgresql+psycopg://db.example/surg"
    unchanged = "postgresql+psycopg://db.example/surg"
    assert db.normalize_database_url(unchanged) == unchanged
    assert db.normalize_database_url("sqlite:///:memory:") == "sqlite:///:memory:"


def test_invalid_state_transitions(client):
    missing = "/api/v1/sessions/missing"
    assert client.post(missing + "/start").status_code == 404
    assert client.post(missing + "/complete", json={"elapsed_s": 1}).status_code == 404

    sid = _session(client)
    assert client.get(f"/api/v1/sessions/{sid}/metrics").status_code == 409
    assert client.get(f"/api/v1/sessions/{sid}/cost").status_code == 409
    assert client.post(f"/api/v1/sessions/{sid}/coach").status_code == 409
    assert client.post(f"/api/v1/sessions/{sid}/complete", json={"elapsed_s": 1}).json()["detail"] == "cannot complete from ready"

    started = client.post(f"/api/v1/sessions/{sid}/start").json()
    again = client.post(f"/api/v1/sessions/{sid}/start").json()
    assert again["status"] == "running"
    # SQLite round-trip drops the trailing Z; the instant must be the same.
    assert again["started_at"].rstrip("Z") == started["started_at"].rstrip("Z")

    client.post(f"/api/v1/sessions/{sid}/samples", json=_samples())
    first = client.post(f"/api/v1/sessions/{sid}/complete", json={"elapsed_s": 12}).json()
    second = client.post(f"/api/v1/sessions/{sid}/complete", json={"elapsed_s": 99}).json()
    assert second == first and second["elapsed_s"] == 12

    assert client.post(f"/api/v1/sessions/{sid}/start").status_code == 409
    assert client.post(f"/api/v1/sessions/{sid}/samples", json=_samples(10)).status_code == 409
    assert client.post(
        f"/api/v1/sessions/{sid}/events",
        json={"events": [{"event_id": "late", "kind": "drop", "t_ms": 1}]},
    ).status_code == 409
    assert client.get(f"/api/v1/sessions/{sid}").json()["status"] == "completed"
