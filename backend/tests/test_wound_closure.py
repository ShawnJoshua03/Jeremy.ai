"""Wound closure exercise (docs/WOUND_CLOSURE.md): API, scoring, cost, coach, assessment, fake Quest."""
import json
import math

import assessment
import coach
import cost
import database as db
import httpx
import pytest
import scoring
from fastapi.testclient import TestClient
from sqlalchemy import create_engine
from sqlalchemy.pool import StaticPool
from tools import fake_quest

Z = [-0.05 + 0.025 * i for i in range(5)]


def entry(i, dx=0.0, dz=0.0):
    return (-0.008 + dx, 0.012, Z[i] + dz)


def exit_(i, dx=0.0, dz=0.0):
    return (0.008 + dx, 0.012, Z[i] + dz)


def placed(i, t_ms=None, src=None, dst=None, oid="site"):
    return scoring.Event("stitch_placed", t_ms if t_ms is not None else 1000 * (i + 1), "right",
                         f"site_{i + 1}" if oid == "site" else oid, src or entry(i), dst or exit_(i))


@pytest.fixture()
def client(monkeypatch):
    monkeypatch.delenv("GEMINI_API_KEY", raising=False)
    import main
    eng = create_engine("sqlite://", connect_args={"check_same_thread": False}, poolclass=StaticPool)
    db.Base.metadata.create_all(eng)
    db.SessionLocal.configure(bind=eng)
    return TestClient(main.app)


def _running(client, exercise):
    r = client.post("/api/v1/sessions", json={"trainee_id": "wc", "exercise": exercise})
    assert r.status_code == 201 and r.json()["exercise"] == exercise
    sid = r.json()["id"]
    client.post(f"/api/v1/sessions/{sid}/start")
    return sid


# ---------- geometry ----------

def test_backend_geometry_matches_contract():
    assert scoring.WC_SITE_Z == pytest.approx([-0.05, -0.025, 0.0, 0.025, 0.05])
    assert scoring.WC_ENTRY[0] == pytest.approx((-0.008, 0.012, -0.05))
    assert scoring.WC_EXIT[4] == pytest.approx((0.008, 0.012, 0.05))
    assert (scoring.BITE_GOOD_MM, scoring.BITE_MAX_MM) == (4.0, 8.0)


# ---------- API ----------

def test_event_kinds_must_match_exercise(client):
    wc = _running(client, "wound_closure")
    peg = _running(client, "peg_transfer")
    peg_kind = {"events": [{"event_id": "e1", "kind": "drop", "t_ms": 0}]}
    wc_kind = {"events": [{"event_id": "e1", "kind": "tissue_contact", "t_ms": 0, "source": [0, 0.01, 0]}]}
    r = client.post(f"/api/v1/sessions/{wc}/events", json=peg_kind)
    assert r.status_code == 422 and "wound_closure" in r.json()["detail"]
    assert client.post(f"/api/v1/sessions/{peg}/events", json=wc_kind).status_code == 422
    assert client.post(f"/api/v1/sessions/{wc}/events", json=wc_kind).json() == {"accepted": 1, "duplicates": 0}
    # a mixed batch is rejected whole
    mixed = {"events": [wc_kind["events"][0] | {"event_id": "e2"}, peg_kind["events"][0] | {"event_id": "e3"}]}
    assert client.post(f"/api/v1/sessions/{wc}/events", json=mixed).status_code == 422
    assert len(client.get(f"/api/v1/sessions/{wc}/events").json()) == 1


@pytest.mark.parametrize("pos", [[0, 11.0, 0], [0, float("nan"), 0], [float("inf"), 0, 0]])
def test_wound_event_positions_validated(client, pos):
    wc = _running(client, "wound_closure")
    body = json.dumps({"events": [{"event_id": "e", "kind": "stitch_placed", "t_ms": 1, "object_id": "site_1",
                                   "source": pos, "target": [0.008, 0.012, -0.05]}]})
    r = client.post(f"/api/v1/sessions/{wc}/events", content=body, headers={"Content-Type": "application/json"})
    assert r.status_code == 422


def test_full_wound_session_via_api(client):
    sid = _running(client, "wound_closure")
    events = [
        {"event_id": f"s{i}", "kind": "stitch_placed", "t_ms": 1000 * (i + 1), "instrument": "right",
         "object_id": f"site_{i + 1}", "source": list(entry(i)), "target": list(exit_(i))}
        for i in range(5)
    ] + [
        {"event_id": "c1", "kind": "tissue_contact", "t_ms": 1500, "instrument": "left", "source": [0.0, 0.008, 0.0]},
        {"event_id": "m1", "kind": "stitch_missed", "t_ms": 2500, "instrument": "right", "object_id": "site_3",
         "source": [-0.008, 0.012, 0.012], "target": None},
    ]
    assert client.post(f"/api/v1/sessions/{sid}/events", json={"events": events}).json()["accepted"] == 7
    m = client.post(f"/api/v1/sessions/{sid}/complete", json={"elapsed_s": 60}).json()
    assert m["exercise"] == "wound_closure"
    assert (m["successful_transfers"], m["drops"], m["invalid_releases"], m["completion_pct"]) == (5, 1, 1, 100.0)
    assert m["mean_bite_error_mm"] == 0.0 and m["spacing_sd_mm"] == 0.0
    assert client.get(f"/api/v1/sessions/{sid}").json()["metrics"] == m
    c = client.get(f"/api/v1/sessions/{sid}/cost").json()
    assert c["lines"][-1]["quantity"] == 2  # missed + contact
    coaching = client.post(f"/api/v1/sessions/{sid}/coach").json()
    ids = {t["event_id"] for t in coaching["tips"]}
    assert {"c1", "m1"} <= ids and not any(i and i.startswith("s") for i in ids)  # stitch_placed is not an error
    assert coaching["next_exercise"]["exercise_id"] == "wound_closure"
    a = client.get(f"/api/v1/sessions/{sid}/assessment").json()
    assert [c["id"] for c in a["criteria"]] == [
        "completion", "time", "bite_accuracy", "spacing", "tissue_contacts", "path_efficiency", "bimanual_balance"]
    progress = client.get("/api/v1/trainees/wc/progress").json()
    assert progress[0]["exercise"] == "wound_closure" and progress[0]["errors"] == 2


def test_peg_metrics_keep_old_shape(client):
    sid = _running(client, "peg_transfer")
    client.post(f"/api/v1/sessions/{sid}/events", json={"events": [
        {"event_id": "x", "kind": "transfer_success", "t_ms": 5, "object_id": "peg_1",
         "source": [-0.1, 0.02, 0], "target": [0.1, 0.02, 0]}]})
    m = client.post(f"/api/v1/sessions/{sid}/complete", json={"elapsed_s": 30}).json()
    assert m["exercise"] is None and m["mean_bite_error_mm"] is None and m["spacing_sd_mm"] is None
    stored = db.SessionLocal().get(db.TrainingSession, sid).metrics
    assert set(stored) == {"completion_pct", "successful_transfers", "drops", "invalid_releases", "elapsed_s",
                           "distance_by_instrument", "total_distance_m", "path_efficiency", "practice_score"}


def test_live_preview_for_running_wound_session(client):
    sid = _running(client, "wound_closure")
    client.post(f"/api/v1/sessions/{sid}/events", json={"events": [
        {"event_id": "s1", "kind": "stitch_placed", "t_ms": 5, "object_id": "site_1",
         "source": list(entry(0)), "target": list(exit_(0))}]})
    m = client.get(f"/api/v1/sessions/{sid}/metrics").json()
    assert m["exercise"] == "wound_closure" and m["successful_transfers"] == 1 and m["completion_pct"] == 20.0


# ---------- scoring ----------

def test_perfect_closure_scores_high():
    m = scoring.compute_wound_metrics([], [placed(i) for i in range(5)], 60.0)
    assert m.successful_transfers == 5 and m.completion_pct == 100.0
    assert m.mean_bite_error_mm == 0.0 and m.spacing_sd_mm == 0.0
    assert m.drops == 0 and m.invalid_releases == 0
    # no samples -> efficiency 0; 0.40 + 0.20 + 0 + 0.15*(1-60/300) + 0.10
    assert m.practice_score == pytest.approx(100 * (0.40 + 0.20 + 0.15 * 0.8 + 0.10), abs=0.05)


def test_bite_error_is_computed_server_side():
    events = [placed(0, src=entry(0, dz=0.003)), placed(1, dst=exit_(1, dx=0.001))]
    m = scoring.compute_wound_metrics([], events, 10.0)
    assert m.mean_bite_error_mm == pytest.approx((3 + 0 + 0 + 1) / 4, abs=0.01)


def test_site_scores_once_and_missing_id_uses_nearest_site():
    events = [placed(0), placed(0, t_ms=9000, src=entry(0, dz=0.007)), placed(2, oid=None)]
    m = scoring.compute_wound_metrics([], events, 10.0)
    assert m.successful_transfers == 2 and m.completion_pct == 40.0
    assert m.mean_bite_error_mm == 0.0  # the later, worse duplicate at site_1 is ignored
    assert scoring.site_index("site_9", entry(3)) == 3 and scoring.site_index("bogus", None) is None


def test_spacing_needs_three_stitches_and_measures_unevenness():
    assert scoring.compute_wound_metrics([], [placed(0), placed(1)], 5).spacing_sd_mm is None
    shifted = [placed(0), placed(1, src=entry(1, dz=0.005), dst=exit_(1, dz=0.005)), placed(2)]
    m = scoring.compute_wound_metrics([], shifted, 5)
    assert m.spacing_sd_mm == pytest.approx(5.0, abs=0.01)  # gaps 30 and 20 mm -> SD 5


def test_misses_and_contacts_counted_as_errors():
    events = [placed(0),
              scoring.Event("stitch_missed", 10, "right", "site_2", entry(1, dz=0.01), None),
              scoring.Event("stitch_missed", 20, "right", "site_2", entry(1, dz=0.01), None),
              scoring.Event("tissue_contact", 30, "left", None, (0.0, 0.008, 0.0), None)]
    m = scoring.compute_wound_metrics([], events, 10)
    assert (m.successful_transfers, m.invalid_releases, m.drops) == (1, 2, 1)


def test_ideal_path_uses_bites_and_site_travel():
    # right hand traces exactly entry0->exit0->entry1->exit1; left hand moves the same site-to-site leg
    pts = [entry(0), exit_(0), entry(1), exit_(1)]
    samples = [scoring.Sample("right", k, 40 * k, p) for k, p in enumerate(pts)]
    samples += [scoring.Sample("left", 0, 0, (-0.014, 0.014, Z[0])), scoring.Sample("left", 1, 40, (-0.014, 0.014, Z[1]))]
    m = scoring.compute_wound_metrics(samples, [placed(0), placed(1)], 10)
    travel = math.dist(exit_(0), entry(1))
    ideal = 0.016 * 2 + 2 * travel
    actual = 0.016 * 2 + travel + 0.025
    assert m.path_efficiency == pytest.approx(min(1.0, ideal / actual))
    assert m.total_distance_m == pytest.approx(actual)


def test_dispatch_keeps_peg_scoring():
    ev = [scoring.Event("transfer_success", 1, "left", "peg_1", (0, 0, 0), (0.1, 0, 0))]
    assert scoring.compute_for_exercise("peg_transfer", [], ev, 5) == scoring.compute_metrics([], ev, 5)
    assert type(scoring.compute_for_exercise("peg_transfer", [], ev, 5)) is scoring.Metrics


# ---------- cost ----------

def test_wound_cost_uses_assumed_suture_line():
    c = cost.estimate_cost(120, 2, 0.5, exercise="wound_closure")
    inst = c.lines[2]
    assert inst.assumed is True and inst.source is None and "assumed" in inst.label.lower()
    assert inst.total_usd == cost.ASSUMED_SUTURE_SET_COST
    assert c.lines[0].unit_usd == cost.OR_RATE_PER_MIN["inpatient"]
    assert c.lines[3].quantity == 2


def test_peg_cost_unchanged_by_exercise_param():
    assert cost.estimate_cost(150, 2, 0.6) == cost.estimate_cost(150, 2, 0.6, exercise="peg_transfer")
    assert cost.estimate_cost(150, 2, 0.6).lines[2].total_usd == 48.80


# ---------- coach and assessment ----------

WC = {"exercise": "wound_closure", "completion_pct": 100.0, "successful_transfers": 5, "drops": 0,
      "invalid_releases": 0, "elapsed_s": 90.0, "distance_by_instrument": {"left": 0.4, "right": 0.6},
      "total_distance_m": 1.0, "path_efficiency": 0.6, "practice_score": 85.0,
      "mean_bite_error_mm": 2.0, "spacing_sd_mm": 1.0}


@pytest.mark.parametrize("override,drill", [
    ({}, "wound_closure"),
    ({"drops": 3}, "wound_closure_tissue_handling"),
    ({"mean_bite_error_mm": 9.0}, "wound_closure_bite_accuracy"),
    ({"invalid_releases": 2}, "wound_closure_bite_accuracy"),
    ({"spacing_sd_mm": 7.0}, "wound_closure_spacing"),
])
def test_wound_drill_choice(monkeypatch, override, drill):
    monkeypatch.delenv("GEMINI_API_KEY", raising=False)
    c = coach.generate({**WC, **override}, [])
    assert c.next_exercise_id == drill and c.next_exercise.exercise_id == "wound_closure"


def test_wound_drills_allowlisted():
    ids = set(coach.drills_for("wound_closure"))
    assert {"wound_closure", "wound_closure_bite_accuracy", "wound_closure_spacing"} <= ids
    assert not ids & set(coach.drills_for("peg_transfer"))


def test_wound_fallback_summary_and_tips(monkeypatch):
    monkeypatch.delenv("GEMINI_API_KEY", raising=False)
    errs = [{"event_id": "m1", "kind": "stitch_missed", "t_ms": 4000, "instrument": "right"},
            {"event_id": "c1", "kind": "tissue_contact", "t_ms": 6000, "instrument": "left"}]
    c = coach.generate({**WC, "drops": 1, "invalid_releases": 1, "mean_bite_error_mm": 5.5}, errs,
                       question="how was my bite accuracy?")
    assert "5 of 5 stitches" in c.summary and "5.5 mm" in c.summary and "1 tissue contact " in c.summary
    assert "transfer" not in c.summary
    assert [t.event_id for t in c.tips[:2]] == ["m1", "c1"]
    assert "missed stitch" in c.tips[0].text and "wound edge" in c.tips[1].text
    assert "bite accuracy" in c.answer.lower()


def test_gemini_prompt_names_exercise_and_only_offers_its_drills(monkeypatch):
    monkeypatch.setenv("GEMINI_API_KEY", "k")
    seen = []
    out = {"summary": "ok", "tips": [], "next_exercise_id": "peg_transfer_economy"}
    payload = {"candidates": [{"content": {"parts": [{"text": json.dumps(out)}]}}]}

    def handler(req):
        seen.append(json.loads(req.content))
        return httpx.Response(200, json=payload)
    c = coach.generate({**WC, "drops": 2}, [], httpx.Client(transport=httpx.MockTransport(handler)))
    prompt = seen[0]["contents"][0]["parts"][0]["text"]
    assert "wound-closure" in prompt and "peg_transfer_economy" not in prompt
    enum = seen[0]["generationConfig"]["responseSchema"]["properties"]["next_exercise_id"]["enum"]
    assert all(e.startswith("wound_closure") for e in enum)
    assert c.source == "gemini" and c.next_exercise_id == "wound_closure_tissue_handling"  # peg drill rejected


def test_peg_prompt_still_peg(monkeypatch):
    assert "peg-transfer" in coach._prompt({"x": 1}, [])
    assert coach.choose_drill({**WC, "exercise": None}, "peg_transfer").startswith("peg_transfer")


def test_wound_assessment_levels():
    a = assessment.assess({**WC, "drops": 2, "mean_bite_error_mm": 5.0, "spacing_sd_mm": None})
    crit = {c.id: c for c in a.criteria}
    assert crit["tissue_contacts"].level == "needs_work"
    assert crit["bite_accuracy"].level == "developing" and crit["bite_accuracy"].unit == "mm"
    assert crit["spacing"].level == "not_assessed" and "3 placed" in crit["spacing"].rule
    assert crit["time"].level == "proficient"
    assert "Prototype" in a.thresholds_note
    unfinished = assessment.assess({**WC, "completion_pct": 60.0, "successful_transfers": 3})
    assert {c.id: c for c in unfinished.criteria}["time"].level == "not_assessed"


# ---------- fake quest ----------

def test_wound_trace_shape():
    samples, events, elapsed = fake_quest.build_wound_trace()
    kinds = [e["kind"] for e in events]
    assert kinds.count("stitch_placed") == 5 and kinds.count("stitch_missed") == 1
    assert kinds.count("tissue_contact") == 1
    assert {s["instrument"] for s in samples} == {"left", "right"}
    assert len(samples) > 1000 and elapsed == samples[-1]["t_ms"] / 1000


def test_wound_replay_against_testclient(client):
    report = fake_quest.replay(client, exercise="wound_closure")
    n = len(fake_quest.build_wound_trace()[0])
    assert report["sample_upload"] == {"accepted": n, "duplicates": 0}
    m = report["metrics"]
    assert (m["successful_transfers"], m["drops"], m["invalid_releases"]) == (5, 1, 1)
    assert 0.5 < m["mean_bite_error_mm"] < 4.0 and m["spacing_sd_mm"] > 0
    assert 0 < m["path_efficiency"] < 1 and 0 <= m["practice_score"] <= 100
    assert report["cost"]["lines"][2]["assumed"] is True
    assert "5 of 5 stitches" in report["coach"]["summary"]
    assert report["assessment"]["criteria"][2]["id"] == "bite_accuracy"
