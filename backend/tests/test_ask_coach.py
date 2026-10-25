"""Ask-the-coach route: transcription, answer, speech. Providers are mocked."""
import base64

import httpx
import pytest
from fastapi.testclient import TestClient
from sqlalchemy import create_engine
from sqlalchemy.pool import StaticPool

import ask_coach
import database as db
import main
import ratelimit
from speech import cartesia, stt

WAV = b"RIFF" + b"\x00" * 4 + b"WAVE" + b"\x00" * 40


@pytest.fixture()
def client(monkeypatch):
    ratelimit.reset()
    for key in ("CARTESIA_API_KEY", "ANTHROPIC_API_KEY"):
        monkeypatch.delenv(key, raising=False)
    monkeypatch.setattr(httpx.HTTPTransport, "handle_request", lambda *a, **k: (_ for _ in ()).throw(AssertionError("network")))
    monkeypatch.setattr(cartesia, "text_to_audio", lambda text, **k: WAV)
    eng = create_engine("sqlite://", connect_args={"check_same_thread": False}, poolclass=StaticPool)
    db.Base.metadata.create_all(eng)
    db.SessionLocal.configure(bind=eng)
    return TestClient(main.app)


def session(c: TestClient) -> str:
    r = c.post("/api/v1/sessions", json={"trainee_id": "t1", "exercise": "lap_chole"})
    assert r.status_code == 201
    return r.json()["id"]


CTX = {"step": "Step 4 of 15 · Epigastric port", "what": "Place the 10 mm trocar below the breastbone.",
       "how": "Tip on the mark, follow the guide, push until it pops.",
       "status": "You're 18 mm from the mark, toward the feet. Move toward the head.", "errors": "0 major, 0 minor"}


def test_text_question_uses_live_status(client):
    sid = session(client)
    r = client.post(f"/api/v1/sessions/{sid}/coach/ask", json={"text": "am I doing this right?", "context": CTX})
    assert r.status_code == 200
    body = r.json()
    assert body["intent"] == "check" and body["source"] == "template"
    assert "18 mm" in body["reply"] and "toward the head" in body["reply"]
    assert base64.b64decode(body["audio_wav_b64"]) == WAV


def test_audio_question_is_transcribed(client, monkeypatch):
    monkeypatch.setattr(stt, "transcribe", lambda wav: "what's next")
    sid = session(client)
    r = client.post(f"/api/v1/sessions/{sid}/coach/ask",
                    json={"audio_wav_b64": base64.b64encode(WAV).decode(), "context": CTX, "speak": False})
    assert r.status_code == 200
    assert r.json()["transcript"] == "what's next" and r.json()["intent"] == "next"
    assert r.json()["audio_wav_b64"] is None


def test_stt_down_is_503_and_empty_is_422(client, monkeypatch):
    def down(wav):
        raise stt.SpeechUnavailable("x")
    monkeypatch.setattr(stt, "transcribe", down)
    sid = session(client)
    assert client.post(f"/api/v1/sessions/{sid}/coach/ask", json={"audio_wav_b64": base64.b64encode(WAV).decode()}).status_code == 503
    monkeypatch.setattr(stt, "transcribe", lambda wav: "")
    assert client.post(f"/api/v1/sessions/{sid}/coach/ask", json={"audio_wav_b64": base64.b64encode(WAV).decode()}).status_code == 422
    assert client.post("/api/v1/sessions/nope/coach/ask", json={"text": "hi"}).status_code == 404


def test_speech_failure_still_returns_text(client, monkeypatch):
    def fail(text, **k):
        raise cartesia.SpeechUnavailable("x")
    monkeypatch.setattr(cartesia, "text_to_audio", fail)
    sid = session(client)
    r = client.post(f"/api/v1/sessions/{sid}/coach/ask", json={"text": "why do we do this", "context": CTX})
    assert r.status_code == 200 and r.json()["audio_wav_b64"] is None and "epigastric" in r.json()["reply"].lower()


def _fake_text(monkeypatch, text, fail=False):
    import llm
    calls = []

    def fake(prompt, **kw):
        calls.append(prompt)
        if fail:
            raise llm.LLMUnavailable("down")
        return llm.Completion(text, 10, 10)

    monkeypatch.setattr(llm, "complete_text", fake)
    return calls


def test_claude_reply_with_unmeasured_number_is_discarded(monkeypatch):
    c = ask_coach.SimContext(**CTX)
    _fake_text(monkeypatch, "You're 42 mm off.")
    assert ask_coach.claude_answer("am I right?", c) is None
    reply, source = ask_coach.answer("am I right?", c)
    assert source == "template"


def test_claude_reply_using_measured_numbers_is_used(monkeypatch):
    c = ask_coach.SimContext(**CTX)
    _fake_text(monkeypatch, "Close. You're 18 mm toward the feet, so slide toward the head.")
    reply, source = ask_coach.answer("am I right?", c)
    assert source == "claude" and "18 mm" in reply


def test_claude_failure_falls_back_to_template(monkeypatch):
    c = ask_coach.SimContext(**CTX)
    calls = _fake_text(monkeypatch, "", fail=True)
    reply, source = ask_coach.answer("am I right?", c)
    assert source == "template" and calls and reply


def test_complete_text_skips_without_key_and_over_budget(monkeypatch, tmp_path):
    import llm
    monkeypatch.delenv("ANTHROPIC_API_KEY", raising=False)
    with pytest.raises(llm.LLMUnavailable):
        llm.complete_text("hi")
    monkeypatch.setenv("ANTHROPIC_API_KEY", "k")
    spend = tmp_path / "spend.json"
    spend.write_text('{"usd": 9.0}')
    monkeypatch.setenv("ASK_CLAUDE_SPEND_FILE", str(spend))
    monkeypatch.setenv("ASK_CLAUDE_BUDGET_USD", "5")
    with pytest.raises(llm.LLMUnavailable, match="spend cap"):
        llm.complete_text("hi")


@pytest.mark.parametrize("q,intent", [("am I doing this right", "check"), ("what now", "next"), ("how do I do this", "how"),
                                      ("why are we doing this", "why"), ("how am I doing overall", "score"), ("say that again", "repeat")])
def test_intents(q, intent):
    assert ask_coach.intent_of(q) == intent


@pytest.mark.parametrize("text,yes", [("am I doing this right", True), ("coach what now", True), ("where does this go?", True),
                                      ("okay", False), ("yeah sure thing", False), ("pass me the coffee", False)])
def test_hands_free_filter(text, yes):
    assert ask_coach.addressed_to_coach(text) == yes


def test_hands_free_ignores_chatter(client):
    sid = session(client)
    r = client.post(f"/api/v1/sessions/{sid}/coach/ask", json={"text": "pass me the coffee", "hands_free": True})
    assert r.status_code == 200 and r.json()["intent"] == "ignored" and r.json()["reply"] == ""


@pytest.mark.parametrize("q", ["what does this operation do", "What is this step?", "explain this", "what am I doing"])
def test_explain_intent_answers_the_step(q, monkeypatch):
    for key in ("ANTHROPIC_API_KEY",):
        monkeypatch.delenv(key, raising=False)
    c = ask_coach.SimContext(**CTX)
    assert ask_coach.intent_of(q) == "explain"
    reply, source = ask_coach.answer(q, c)
    assert source == "template" and "trocar" in reply.lower()
