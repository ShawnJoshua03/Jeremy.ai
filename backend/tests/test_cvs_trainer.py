"""CVS trainer (lap_chole_cvs): scoring, narrator, report, replay, mistake cost. No network."""
import json
import math
from pathlib import Path

import httpx
import pytest
from fastapi.testclient import TestClient
from sqlalchemy import create_engine
from sqlalchemy.orm import sessionmaker
from sqlalchemy.pool import StaticPool

import convex_mirror
import cvs_scoring
import database as db
import main
import narrator
import provider_log
import report_router
import scoring
from speech import cartesia

FIXTURES = Path(__file__).parent / "fixtures"
E = scoring.Event


def ev(kind, t, object_id=None, detail=None, value=None):
    return E(kind, t, None, object_id, None, None, value, detail)


def decide(ex, detail, t):
    return ev("decision_submitted", t, ex, detail)


def started(ex, t):
    return ev("exercise_started", t, ex)


CLEAN = [started("ex_identify", 0), decide("ex_identify", "identify:cystic_duct", 10_000),
         started("ex_view_a", 11_000), decide("ex_view_a", "criteria_not_demonstrated", 20_000),
         started("ex_view_b", 21_000), decide("ex_view_b", "criteria_demonstrated", 30_000)]


@pytest.fixture(autouse=True)
def offline(monkeypatch):
    for key in ("GEMINI_API_KEY", "CARTESIA_API_KEY", "API_KEY", "X402_PAY_TO", "CONVEX_URL",
                "CONVEX_INGEST_SHARED_SECRET", "CONVEX_DEPLOY_KEY"):
        monkeypatch.delenv(key, raising=False)
    monkeypatch.setattr(narrator, "STORE", narrator.LineStore())
    provider_log.reset()
    report_router.reset_cache()

    def no_network(*args, **kwargs):
        raise AssertionError("Tests must mock provider traffic")

    monkeypatch.setattr(httpx.HTTPTransport, "handle_request", no_network)
    yield
    provider_log.reset()
    report_router.reset_cache()


@pytest.fixture
def api(monkeypatch):
    engine = create_engine("sqlite://", connect_args={"check_same_thread": False}, poolclass=StaticPool)
    db.Base.metadata.create_all(engine)
    monkeypatch.setattr(db, "SessionLocal", sessionmaker(bind=engine, expire_on_commit=False))
    with TestClient(main.app) as client:
        yield client
    engine.dispose()


def gemini(monkeypatch, handler):
    monkeypatch.setenv("GEMINI_API_KEY", "test-key")
    calls = []

    def wrapped(request):
        calls.append(json.loads(request.content))
        return handler(request)

    monkeypatch.setattr(report_router, "_client", lambda: httpx.Client(transport=httpx.MockTransport(wrapped)))
    return calls


def gemini_reply(payload):
    return httpx.Response(200, json={"candidates": [{"content": {"parts": [{"text": json.dumps(payload)}]}}]})


# ---------- scoring ----------
def test_judge_uses_backend_table_not_client_value():
    assert cvs_scoring.judge("ex_identify", "identify:cystic_duct") == (True, None)
    assert cvs_scoring.judge("ex_identify", "identify:cystic_artery") == (False, "misidentification")
    assert cvs_scoring.judge("ex_identify", "identify:common_hepatic_duct") == (False, "misidentification")
    assert cvs_scoring.judge("ex_view_a", "criteria_demonstrated") == (False, "cvs_false_positive")
    assert cvs_scoring.judge("ex_view_b", "criteria_not_demonstrated") == (False, "wrong_decision")
    assert cvs_scoring.judge("ex_view_b", "criteria_demonstrated") == (True, None)
    # unknown exercises and decisions are not assessed, never wrong
    assert cvs_scoring.judge("ex_clip_future", "criteria_demonstrated") == (None, None)
    assert cvs_scoring.judge("ex_view_a", "some_new_decision") == (None, None)
    # client value says match; backend recomputes
    a = cvs_scoring.analyze([], [started("ex_view_a", 0), ev("decision_submitted", 5000, "ex_view_a",
                                                              "criteria_demonstrated", 1.0)], 6)
    assert a.exercises[0].correct is False


def test_clean_run_scores_and_orders():
    a = cvs_scoring.analyze([], CLEAN, 31)
    assert [r.exercise_id for r in a.exercises] == ["ex_identify", "ex_view_a", "ex_view_b"]
    assert all(r.correct for r in a.exercises)
    assert [r.time_to_decision_s for r in a.exercises] == [10.0, 9.0, 9.0]
    assert a.movement["smoothness"] is None  # no hand samples: weight dropped, not zeroed
    assert a.score == 100.0 and a.grade == "excellent"


def test_false_positive_caps_score_and_grade():
    events = list(CLEAN)
    events[3] = decide("ex_view_a", "criteria_demonstrated", 20_000)
    a = cvs_scoring.analyze([], events, 31)
    assert a.counts["false_positive_cvs"] == 1
    assert a.score == cvs_scoring.FALSE_POSITIVE_CAP and a.grade == "needs_practice"


def test_request_instructor_is_never_penalised():
    base = cvs_scoring.analyze([], CLEAN, 31).score
    asked = CLEAN[:5] + [ev("instructor_requested", 25_000, "ex_view_b"), CLEAN[5]]
    assert cvs_scoring.analyze([], asked, 31).score == base
    only_asked = CLEAN[:5] + [decide("ex_view_b", "request_instructor", 25_000)]
    a = cvs_scoring.analyze([], only_asked, 31)
    view_b = a.exercises[2]
    assert (view_b.correct, view_b.unclear, view_b.decision) == (None, True, "request_instructor")
    assert a.score == base
    # the graded decision is the first one that is not a request
    then = only_asked + [decide("ex_view_b", "criteria_demonstrated", 28_000)]
    assert cvs_scoring.analyze([], then, 31).exercises[2].correct is True


def test_first_graded_attempt_counts_and_pace_and_hesitation():
    events = [started("ex_identify", 0), ev("structure_selected", 100_000, "cystic_artery", "ex_identify"),
              decide("ex_identify", "identify:cystic_artery", 112_500),
              decide("ex_identify", "identify:cystic_duct", 120_000)]
    a = cvs_scoring.analyze([], events, 121)
    r = a.exercises[0]
    assert (r.correct, r.error, r.time_to_decision_s, r.hesitation_s) == (False, "misidentification", 112.5, 7.5)
    # accuracy 0; pace = 1 - 37.5/75 = 0.5; decisiveness = 1 - 7.5/15 = 0.5
    assert a.score == round(100 * (0.15 * 0.5 + 0.10 * 0.5) / 0.85, 1)


def test_unknown_exercise_and_structure_pass_through():
    events = CLEAN + [started("ex_clip_future", 31_000), ev("structure_hovered", 32_000, "hepatic_artery", value=0.9),
                      decide("ex_clip_future", "clip_ok", 33_000), ev("clip_applied", 33_500, "cystic_duct")]
    a = cvs_scoring.analyze([], events, 34)
    fut = a.exercises[-1]
    assert (fut.exercise_id, fut.title, fut.correct, fut.known) == ("ex_clip_future", "ex_clip_future", None, False)
    assert fut.structures_inspected == ["hepatic_artery"] and fut.hover_dwell_s == {"hepatic_artery": 0.9}
    assert a.score == 100.0 and a.counts["not_assessed"] == 1


def line_samples(inst, n, step_ms=50, speed=0.1, valid=True, start=0):
    return [scoring.Sample(inst, i, start + i * step_ms, (speed * i * step_ms / 1000, 1.0, 0.3), valid)
            for i in range(n)]


def test_movement_paths_smoothness_idle_and_windows():
    smooth = line_samples("left", 201)  # 10 s at 0.1 m/s
    assert cvs_scoring.smoothness(smooth) == pytest.approx(1.0)
    jitter = [scoring.Sample("left", i, i * 50, (0.003 * (i % 2), 1.0, 0.3)) for i in range(40)]
    assert cvs_scoring.smoothness(jitter) < 0.5
    still = [scoring.Sample("right", i, i * 50, (0.0, 1.0, 0.3)) for i in range(40)]  # 2 s still
    assert cvs_scoring.idle_seconds(still) == 2.0
    assert cvs_scoring.idle_seconds(smooth) == 0.0
    head = line_samples("head", 101, speed=0.02)
    a = cvs_scoring.analyze(smooth + head, CLEAN, 31)
    assert a.movement["hand_path_m"] == pytest.approx(1.0, abs=1e-3)
    assert a.movement["head_path_m"] == pytest.approx(0.1, abs=1e-3)
    # ex_identify window 0-11 s: the whole 10 s hand line
    assert a.exercises[0].hand_path_m == pytest.approx(1.0, abs=1e-3)
    assert a.exercises[1].hand_path_m == 0.0


def test_metrics_mapping():
    events = list(CLEAN)
    events[1] = decide("ex_identify", "identify:gallbladder", 10_000)
    events[3] = decide("ex_view_a", "criteria_demonstrated", 20_000)
    m = cvs_scoring.compute_metrics(line_samples("head", 10), events, 31)
    assert (m.successful_transfers, m.drops, m.invalid_releases, m.completion_pct) == (1, 1, 1, 100.0)
    assert m.exercise == "lap_chole_cvs" and m.details["grade"] == "needs_practice"
    assert "smoothness" not in m.details  # Convex details are flat scalars only
    assert all(isinstance(v, (bool, int, float, str)) for v in m.details.values())


# ---------- narrator ----------
def test_narrator_pace_and_severity():
    p = narrator.compute_cvs_progress([started("ex_identify", 0)], 10)
    assert (p.step, p.severity, p.actual_progress) == ("ex_identify", "on_track", 0)
    assert p.expected_progress == round(10 / 225 * 100, 2)
    assert narrator.compute_cvs_progress([started("ex_identify", 0)], 74.9).severity == "on_track"
    assert narrator.compute_cvs_progress([started("ex_identify", 0)], 75).severity == "slow"
    wrong = [started("ex_identify", 0), decide("ex_identify", "identify:cystic_artery", 5000)]
    p = narrator.compute_cvs_progress(wrong, 6)
    assert (p.step, p.severity, p.finding_codes) == ("ex_view_a", "off_track", ("misidentification",))
    fp = CLEAN[:3] + [decide("ex_view_a", "criteria_demonstrated", 20_000), started("ex_view_b", 21_000)]
    p = narrator.compute_cvs_progress(fp, 22)
    assert (p.step, p.severity) == ("ex_view_b", "critical")
    assert "bile duct" in narrator.sentences(p)[0]
    asked = [started("ex_view_a", 0), ev("instructor_requested", 10_000, "ex_view_a")]
    p = narrator.compute_cvs_progress(asked, 80)
    assert p.severity == "on_track" and "instructor" in p.cue
    done = narrator.compute_cvs_progress(CLEAN, 31)
    assert (done.step, done.actual_progress) == ("complete", 100)
    for p in (narrator.compute_cvs_progress(fp, 22), narrator.compute_cvs_progress([], 100)):
        assert all(len(s.split()) <= 25 for s in narrator.sentences(p))


# ---------- report prose ----------
def analysis_fp():
    events = list(CLEAN)
    events[3] = decide("ex_view_a", "criteria_demonstrated", 20_000)
    return cvs_scoring.analyze([], events, 31)


def good_prose(a, summary="You scored 40 out of 100 after calling view A safe too early."):
    return {"exercises": [{"exercise_id": r.exercise_id, "review": f"Review of {r.title}.", "tip": "Look again."}
                          for r in a.exercises],
            "summary": summary, "strengths": ["Careful identification."], "focus_next": ["Check the cystic plate."]}


def test_prose_from_gemini_is_used(monkeypatch):
    a = analysis_fp()
    calls = gemini(monkeypatch, lambda r: gemini_reply(good_prose(a)))
    prose, source = report_router.generate_prose(a)
    assert source == "gemini" and prose["summary"].startswith("You scored 40")
    config = calls[0]["generationConfig"]
    assert config["thinkingConfig"] == {"thinkingBudget": 0}
    assert "usd" not in json.dumps(calls[0]).lower()  # money is never sent to the model


def test_invented_number_falls_back(monkeypatch):
    a = analysis_fp()
    gemini(monkeypatch, lambda r: gemini_reply(good_prose(a, "You scored 73 out of 100.")))
    prose, source = report_router.generate_prose(a)
    assert source == "fallback" and prose == report_router.template(a)


@pytest.mark.parametrize("handler", [
    lambda r: (_ for _ in ()).throw(httpx.ReadTimeout("slow", request=r)),
    lambda r: httpx.Response(503),
    lambda r: gemini_reply({"exercises": [], "summary": "x", "strengths": ["a"], "focus_next": ["b"]}),
    lambda r: httpx.Response(200, json={"candidates": []}),
])
def test_gemini_failures_fall_back(monkeypatch, handler):
    a = analysis_fp()
    gemini(monkeypatch, handler)
    assert report_router.generate_prose(a) == (report_router.template(a), "fallback")


def test_number_guard_accepts_rounded_facts():
    facts = {"score": 97.24, "t": 11.5, "title": "1. Anatomy identification"}
    assert report_router.number_guard(["You scored 97 and 97.2, took 11.5 s, step 1."], facts)
    assert not report_router.number_guard(["You took 13 s."], facts)


def test_template_has_only_fact_numbers():
    a = analysis_fp()
    prose = report_router.template(a)
    texts = [prose["summary"], *prose["strengths"], *prose["focus_next"]]
    texts += [t for e in prose["exercises"] for t in (e["review"], e["tip"])]
    assert report_router.number_guard(texts, report_router.facts(a))


# ---------- replay ----------
def test_replay_downsamples_to_10_hz():
    samples = line_samples("left", 201) + line_samples("head", 201) + [
        scoring.Sample("right", 0, 0, (0, 0, 0), valid=False)]
    tracks = report_router.downsample(samples)
    assert len(tracks["left"]) == 101 and len(tracks["head"]) == 101 and tracks["right"] == []
    assert all(b[0] - a[0] >= 100 for a, b in zip(tracks["left"], tracks["left"][1:]))
    assert tracks["left"][-1] == [10_000, 1.0, 1.0, 0.3]


# ---------- HTTP end to end ----------
def run_fixture(api, data, batch=1000):
    sid = api.post("/api/v1/sessions", json={"trainee_id": data["trainee_id"], "exercise": "lap_chole_cvs",
                                             "client_request_id": data["trainee_id"]}).json()["id"]
    assert api.post(f"/api/v1/sessions/{sid}/start").status_code == 200
    samples = [{"instrument": inst, "idx": i, "t_ms": row[0], "pos": row[1:], "quat": [0, 0, 0, 1],
                "valid": True, "interaction": "idle"}
               for inst, rows in data["tracks"].items() for i, row in enumerate(rows)]
    for i in range(0, len(samples), batch):
        r = api.post(f"/api/v1/sessions/{sid}/samples", json={"samples": samples[i:i + batch]})
        assert r.status_code == 200, r.text
    r = api.post(f"/api/v1/sessions/{sid}/events", json={"events": data["events"]})
    assert r.status_code == 200, r.text
    assert api.get(f"/api/v1/sessions/{sid}/report").status_code == 409
    line = api.post(f"/api/v1/sessions/{sid}/narrator/line").json()
    assert line["provider"] == {"text": "fallback"}
    metrics = api.post(f"/api/v1/sessions/{sid}/complete", json={"elapsed_s": data["elapsed_s"]})
    assert metrics.status_code == 200, metrics.text
    return sid, metrics.json()


@pytest.mark.parametrize("name", ["clean", "false_positive"])
def test_golden_session_end_to_end(api, name):
    data = json.loads((FIXTURES / f"cvs_{name}.json").read_text())
    want = data["expected"]
    sid, metrics = run_fixture(api, data)
    assert metrics["practice_score"] == want["score"] and metrics["exercise"] == "lap_chole_cvs"
    assert metrics["details"]["grade"] == want["grade"]

    report = api.get(f"/api/v1/sessions/{sid}/report").json()
    assert (report["score"], report["grade"], report["source"]) == (want["score"], want["grade"], "fallback")
    assert report["movement"] == want["movement"]
    keys = ("exercise_id", "decision", "expected", "correct", "unclear", "time_to_decision_s",
            "structures_inspected", "hover_dwell_s", "hesitation_s")
    assert [{k: e[k] for k in keys} for e in report["exercises"]] == want["exercises"]
    assert all(e["review"] and e["tip"] and e["title"] for e in report["exercises"])
    cost = report["mistake_cost"]
    assert cost["total_usd"] == want["mistake_cost_total_usd"]
    assert [line["label"] for line in cost["lines"]] == want["mistake_cost_labels"]
    assert cost["paid_detail_requires_x402"] is True and cost["report_url"].endswith("/mistake-cost/report")
    assert "not billing data" in report["disclaimer"]
    assert report["providers"] == {"gemini": "off", "cartesia": "off", "apify": report["providers"]["apify"],
                                   "x402": "off", "convex": "off"}
    assert report["providers"]["apify"] in ("snapshot", "off")
    assert api.get(f"/api/v1/sessions/{sid}/report").json() == report  # cached, consistent

    summary = api.get(f"/api/v1/sessions/{sid}/mistake-cost/summary").json()
    assert summary["total_usd"] == want["mistake_cost_total_usd"]

    replay = api.get(f"/api/v1/sessions/{sid}/replay").json()
    assert replay["duration_ms"] == int(data["elapsed_s"] * 1000)
    assert set(replay["tracks"]) == {"left", "right", "head"}
    for track in replay["tracks"].values():
        assert len(track) <= replay["duration_ms"] / 100 + 1
    assert [e["kind"] for e in replay["events"]] == [e["kind"] for e in sorted(data["events"], key=lambda e: e["t_ms"])]


def test_false_positive_line_is_bile_duct_risk(api):
    data = json.loads((FIXTURES / "cvs_false_positive.json").read_text())
    sid, _ = run_fixture(api, data)
    lines = api.get(f"/api/v1/sessions/{sid}/report").json()["mistake_cost"]["lines"]
    assert lines[0] == {"label": "Bile duct injury risk: Prepared view A called criteria demonstrated",
                        "usd": 196.26, "source": lines[0]["source"], "assumed": True}
    assert lines[1]["usd"] == 0 and lines[1]["assumed"] is False  # request_instructor costs nothing


def test_misidentification_cost_uses_adverse_event_assumption():
    a = cvs_scoring.analyze([], [started("ex_identify", 0), decide("ex_identify", "identify:cystic_artery", 1000)], 2)
    import mistake_cost_router
    report = mistake_cost_router.build_cvs_report("sid", a)
    assert report["total_usd"] == 138.57
    assert report["mistakes"][0]["label"] == "Misidentification: Anatomy identification, cystic artery selected"


def test_report_gemini_once_and_providers(api, monkeypatch):
    data = json.loads((FIXTURES / "cvs_false_positive.json").read_text())
    sid, _ = run_fixture(api, data)

    def reply(request):
        facts = json.loads(json.loads(request.content)["contents"][0]["parts"][0]["text"].split("Facts:\n", 1)[1])
        return gemini_reply({"exercises": [{"exercise_id": e["exercise_id"], "review": "Good look.", "tip": "Go on."}
                                           for e in facts["exercises"]],
                             "summary": "View A was called safe too early.", "strengths": ["Asked for help."],
                             "focus_next": ["Check the cystic plate."]})

    calls = gemini(monkeypatch, reply)
    first = api.get(f"/api/v1/sessions/{sid}/report").json()
    second = api.get(f"/api/v1/sessions/{sid}/report").json()
    assert len(calls) == 1 and first == second
    assert first["source"] == "gemini" and first["providers"]["gemini"] == "used"
    assert {"t_ms": 46000, "provider": "gemini", "action": "review report written"} in first["provider_events"]

    monkeypatch.setattr(cartesia, "text_to_audio", lambda text: b"RIFF....WAVEdata")
    audio = api.post(f"/api/v1/sessions/{sid}/report/audio")
    assert audio.status_code == 200 and audio.headers["x-speech-provider"] == "cartesia"
    assert api.get(f"/api/v1/sessions/{sid}/report").json()["providers"]["cartesia"] == "used"


def test_report_audio_unavailable_returns_summary(api):
    data = json.loads((FIXTURES / "cvs_clean.json").read_text())
    sid, _ = run_fixture(api, data)
    r = api.post(f"/api/v1/sessions/{sid}/report/audio")
    assert r.status_code == 503 and r.json()["summary"]
    assert api.get(f"/api/v1/sessions/{sid}/report").json()["providers"]["cartesia"] == "unavailable"


def test_convex_and_x402_reflect_real_activity(api, monkeypatch):
    monkeypatch.setenv("CONVEX_URL", "https://convex.example")
    monkeypatch.setenv("CONVEX_INGEST_SHARED_SECRET", "s")
    monkeypatch.setattr(convex_mirror.httpx, "post", lambda *a, **k: httpx.Response(200))
    monkeypatch.setenv("X402_PAY_TO", "0x" + "1" * 40)
    data = json.loads((FIXTURES / "cvs_clean.json").read_text())
    sid, _ = run_fixture(api, data)
    providers = api.get(f"/api/v1/sessions/{sid}/report").json()["providers"]
    assert (providers["convex"], providers["x402"]) == ("mirrored", "unpaid")
    provider_log.record(sid, "x402", "itemized report paid", "paid")
    assert api.get(f"/api/v1/sessions/{sid}/report").json()["providers"]["x402"] == "paid"


def test_convex_failure_is_not_mirrored(api, monkeypatch):
    monkeypatch.setenv("CONVEX_URL", "https://convex.example")
    monkeypatch.setenv("CONVEX_INGEST_SHARED_SECRET", "s")
    monkeypatch.setattr(convex_mirror.httpx, "post", lambda *a, **k: httpx.Response(500))
    sid, _ = run_fixture(api, json.loads((FIXTURES / "cvs_clean.json").read_text()))
    assert api.get(f"/api/v1/sessions/{sid}/report").json()["providers"]["convex"] == "off"


def test_narrator_audio_header_and_cvs_route(api, monkeypatch):
    sid = api.post("/api/v1/sessions", json={"trainee_id": "t", "exercise": "lap_chole_cvs"}).json()["id"]
    api.post(f"/api/v1/sessions/{sid}/start")
    api.post(f"/api/v1/sessions/{sid}/events", json={"events": [
        {"event_id": "a", "kind": "exercise_started", "t_ms": 0, "object_id": "ex_view_a"},
        {"event_id": "b", "kind": "decision_submitted", "t_ms": 5000, "object_id": "ex_view_a",
         "detail": "criteria_demonstrated", "value": 0}]})
    line = api.post(f"/api/v1/sessions/{sid}/narrator/line").json()
    assert (line["severity"], line["step"]) == ("critical", "ex_identify")
    monkeypatch.setattr(cartesia, "text_to_audio", lambda text: b"RIFF....WAVEdata")
    audio = api.post(f"/api/v1/sessions/{sid}/narrator/line/audio")
    assert audio.status_code == 200 and audio.headers["x-speech-provider"] == "cartesia"


def test_event_kinds_and_head_samples(api):
    peg = api.post("/api/v1/sessions", json={"trainee_id": "t"}).json()["id"]
    r = api.post(f"/api/v1/sessions/{peg}/events", json={"events": [
        {"event_id": "a", "kind": "exercise_started", "t_ms": 0, "object_id": "ex_identify"}]})
    assert r.status_code == 422
    sid = api.post("/api/v1/sessions", json={"trainee_id": "t", "exercise": "lap_chole_cvs"}).json()["id"]
    api.post(f"/api/v1/sessions/{sid}/start")
    reserved = [{"event_id": k, "kind": k, "t_ms": 1} for k in
                ("step_completed", "clip_applied", "structure_cut", "injury", "critical_view")]
    assert api.post(f"/api/v1/sessions/{sid}/events", json={"events": reserved}).json()["accepted"] == 5
    r = api.post(f"/api/v1/sessions/{sid}/samples", json={"samples": [
        {"instrument": "head", "idx": 0, "t_ms": 0, "pos": [0, 1.6, 0], "quat": [0, 0, 0, 1],
         "valid": True, "interaction": "idle"}]})
    assert r.json()["accepted"] == 1
    assert api.get(f"/api/v1/sessions/{sid}/replay").json()["tracks"]["head"] == [[0, 0.0, 1.6, 0.0]]
    m = api.post(f"/api/v1/sessions/{sid}/complete", json={"elapsed_s": 2}).json()
    assert m["practice_score"] == 0.0 and m["details"]["exercises"] == 0
    assert api.get(f"/api/v1/sessions/{sid}/report").json()["exercises"] == []


def test_report_rejects_other_exercises(api):
    sid = api.post("/api/v1/sessions", json={"trainee_id": "t", "exercise": "lap_chole"}).json()["id"]
    api.post(f"/api/v1/sessions/{sid}/start")
    api.post(f"/api/v1/sessions/{sid}/complete", json={"elapsed_s": 1})
    assert api.get(f"/api/v1/sessions/{sid}/report").status_code == 409
    assert api.get("/api/v1/sessions/nope/report").status_code == 404
    assert api.get(f"/api/v1/sessions/{sid}/replay").status_code == 200
