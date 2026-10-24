import io
import json
import sys
import wave
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

import database as db
import httpx
import pytest
from fastapi.testclient import TestClient
from speech import cartesia
from sqlalchemy import create_engine
from sqlalchemy.orm import sessionmaker
from sqlalchemy.pool import StaticPool


@pytest.fixture
def wav():
    buffer = io.BytesIO()
    with wave.open(buffer, "wb") as audio:
        audio.setparams((1, 2, 44100, 0, "NONE", "not compressed"))
        audio.writeframes(b"\x00\x00" * 100)
    return buffer.getvalue()


@pytest.fixture
def provider(monkeypatch):
    real_client = httpx.Client
    monkeypatch.setenv("CARTESIA_API_KEY", "test-secret")
    monkeypatch.delenv("CARTESIA_MODEL_ID", raising=False)
    monkeypatch.delenv("CARTESIA_VOICE_ID", raising=False)

    def install(handler):
        def client(**kwargs):
            assert kwargs["timeout"].connect == 5.0
            assert kwargs["timeout"].read == 30.0
            assert kwargs["follow_redirects"] is False
            return real_client(transport=httpx.MockTransport(handler), **kwargs)
        monkeypatch.setattr(cartesia.httpx, "Client", client)
    return install


@pytest.fixture
def client(monkeypatch):
    import main
    engine = create_engine("sqlite://", connect_args={"check_same_thread": False}, poolclass=StaticPool)
    db.Base.metadata.create_all(engine)
    monkeypatch.setattr(db, "engine", engine)
    monkeypatch.setattr(db, "SessionLocal", sessionmaker(bind=engine, expire_on_commit=False))
    # Startup uses the isolated database too.
    with TestClient(main.app, raise_server_exceptions=True) as api:
        yield api
    engine.dispose()


def test_cartesia_contract(provider, wav):
    def respond(request):
        assert request.method == "POST"
        assert str(request.url) == "https://api.cartesia.ai/tts/bytes"
        assert request.headers["authorization"] == "Bearer test-secret"
        assert request.headers["cartesia-version"] == "2026-08-14"
        assert json.loads(request.content) == {
            "model_id": "sonic-3.6", "transcript": "Move slowly.",
            "voice": cartesia.DEFAULT_VOICE,
            "output_format": {"container": "wav", "encoding": "pcm_s16le", "sample_rate": 44100},
            "language": "en",
        }
        return httpx.Response(200, content=wav)
    provider(respond)
    assert cartesia.text_to_audio("Move slowly.") == wav


def test_server_configuration(provider, monkeypatch, wav):
    monkeypatch.setenv("CARTESIA_MODEL_ID", "sonic-3")
    monkeypatch.setenv("CARTESIA_VOICE_ID", "configured-voice")
    def respond(request):
        body = json.loads(request.content)
        assert body["model_id"] == "sonic-3"
        assert body["voice"] == "configured-voice"
        return httpx.Response(200, content=wav)
    provider(respond)
    assert cartesia.text_to_audio("Hello") == wav


@pytest.mark.parametrize("key", [None, "", "  "])
def test_missing_key_never_calls_provider(monkeypatch, provider, key):
    if key is None:
        monkeypatch.delenv("CARTESIA_API_KEY", raising=False)
    else:
        monkeypatch.setenv("CARTESIA_API_KEY", key)
    provider(lambda _: pytest.fail("HTTP must not be called"))
    with pytest.raises(cartesia.SpeechUnavailable):
        cartesia.text_to_audio("Hello")


@pytest.mark.parametrize("text", ["", "  \n", "a" * 2001, None])
def test_adapter_rejects_invalid_text(provider, text):
    provider(lambda _: pytest.fail("HTTP must not be called"))
    with pytest.raises(ValueError):
        cartesia.text_to_audio(text)


@pytest.mark.parametrize("status", [302, 401, 429, 500, 503])
def test_upstream_errors_are_sanitized(provider, status):
    provider(lambda _: httpx.Response(status, text="test-secret private provider details"))
    with pytest.raises(cartesia.SpeechUnavailable) as error:
        cartesia.text_to_audio("Hello")
    assert str(error.value) == "Speech is unavailable; use text feedback."


@pytest.mark.parametrize("error", [httpx.ReadTimeout, httpx.ConnectError, httpx.RemoteProtocolError])
def test_network_failure(provider, error):
    def fail(request):
        raise error("private details", request=request)
    provider(fail)
    with pytest.raises(cartesia.SpeechUnavailable):
        cartesia.text_to_audio("Hello")


@pytest.mark.parametrize("content", [b"", b'{"error": "bad"}', b"RIFFbad"])
def test_invalid_audio(provider, content):
    provider(lambda _: httpx.Response(200, content=content))
    with pytest.raises(cartesia.SpeechUnavailable):
        cartesia.text_to_audio("Hello")


def test_audio_size_limit(provider, monkeypatch, wav):
    monkeypatch.setattr(cartesia, "MAX_AUDIO_BYTES", len(wav) - 1)
    provider(lambda _: httpx.Response(200, content=wav))
    with pytest.raises(cartesia.SpeechUnavailable):
        cartesia.text_to_audio("Hello")


def test_route_success(client, provider, wav):
    provider(lambda _: httpx.Response(200, content=wav))
    sid = client.post("/api/v1/sessions", json={"trainee_id": "speech-test"}).json()["id"]
    response = client.post(f"/api/v1/sessions/{sid}/coach/audio", json={"text": "Move slowly."})
    assert response.status_code == 200
    assert response.headers["content-type"] == "audio/wav"
    assert response.headers["cache-control"] == "no-store"
    assert response.content == wav


def test_unknown_session_does_not_call_provider(client, provider):
    provider(lambda _: pytest.fail("HTTP must not be called"))
    assert client.post("/api/v1/sessions/missing/coach/audio", json={"text": "Hello"}).status_code == 404


@pytest.mark.parametrize("body", [{}, {"text": ""}, {"text": "  "}, {"text": "a" * 2001},
                                  {"text": "Hello", "api_key": "untrusted"}, {"text": 123}])
def test_route_validation(client, provider, body):
    provider(lambda _: pytest.fail("HTTP must not be called"))
    assert client.post("/api/v1/sessions/any/coach/audio", json=body).status_code == 422


@pytest.mark.parametrize("failure", ["missing_key", "upstream", "timeout", "invalid_audio"])
def test_route_text_fallback(client, provider, monkeypatch, failure):
    def respond(request):
        if failure == "missing_key":
            pytest.fail("HTTP must not be called")
        if failure == "timeout":
            raise httpx.ReadTimeout("secret", request=request)
        return httpx.Response(503 if failure == "upstream" else 200, content=b"secret")
    provider(respond)
    if failure == "missing_key":
        monkeypatch.delenv("CARTESIA_API_KEY")
    sid = client.post("/api/v1/sessions", json={"trainee_id": "speech-test"}).json()["id"]
    response = client.post(f"/api/v1/sessions/{sid}/coach/audio", json={"text": "Hello"})
    assert response.status_code == 503
    assert response.json() == {"detail": "Speech is unavailable; use text feedback."}
    assert response.headers["cache-control"] == "no-store"
