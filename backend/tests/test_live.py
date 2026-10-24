import contextlib
import json
import time

import database as db
import pytest
from fastapi.testclient import TestClient
from sqlalchemy import create_engine
from sqlalchemy.pool import StaticPool
from starlette.websockets import WebSocketDisconnect

from live.hub import HUB, MAX_FRAME_BYTES, QUEUE_LIMIT

JPEG = bytes([1]) + (0).to_bytes(8, "little") + b"\xff\xd8frame"
PCM = bytes([2]) + (0).to_bytes(8, "little") + b"\x00\x01"
HELLO = json.dumps(
    {"type": "hello", "video": {"w": 640, "h": 480, "fps": 10}, "audio": {"rate": 24000, "channels": 1}, "passthrough": True}
)
PUB = "/api/v1/sessions/{sid}/live/publish"
WATCH = "/api/v1/sessions/{sid}/live/watch"


@pytest.fixture()
def app_client(monkeypatch):
    monkeypatch.delenv("GEMINI_API_KEY", raising=False)
    monkeypatch.delenv("API_KEY", raising=False)
    monkeypatch.setenv("CORS_ORIGINS", "https://dash.example")
    import main

    eng = create_engine("sqlite://", connect_args={"check_same_thread": False}, poolclass=StaticPool)
    db.Base.metadata.create_all(eng)
    db.SessionLocal.configure(bind=eng)
    HUB.rooms.clear()
    # One shared portal, so every WebSocket runs on the same event loop.
    with TestClient(main.app) as client:
        yield client
    HUB.rooms.clear()


def _sid(client, trainee="trainee"):
    return client.post("/api/v1/sessions", json={"trainee_id": trainee}).json()["id"]


def _json(ws):
    return json.loads(ws.receive_text())


def _drain_status_and_hello(ws, hello=True):
    """A viewer first gets its status, then the cached hello (if any)."""
    assert _json(ws)["type"] == "status"
    if hello:
        assert _json(ws)["type"] == "hello"


def test_publish_reaches_viewers_and_late_joiner_gets_cache(app_client):
    sid = _sid(app_client)
    with app_client.websocket_connect(PUB.format(sid=sid)) as pub:
        pub.send_text(HELLO)
        with app_client.websocket_connect(WATCH.format(sid=sid)) as a:
            _drain_status_and_hello(a)
            with app_client.websocket_connect(WATCH.format(sid=sid)) as b:
                _drain_status_and_hello(b)
                pub.send_bytes(JPEG)
                pub.send_bytes(PCM)
                assert a.receive_bytes() == JPEG
                assert a.receive_bytes() == PCM
                assert b.receive_bytes() == JPEG
                assert b.receive_bytes() == PCM
        with app_client.websocket_connect(WATCH.format(sid=sid)) as late:
            _drain_status_and_hello(late)
            assert late.receive_bytes() == JPEG


def test_status_reports_live_viewers_and_passthrough(app_client):
    sid = _sid(app_client)
    assert app_client.get(f"/api/v1/sessions/{sid}/live").json() == {"live": False, "viewers": 0, "passthrough": False}
    with app_client.websocket_connect(PUB.format(sid=sid)) as pub:
        pub.send_text(HELLO)
        with app_client.websocket_connect(WATCH.format(sid=sid)):
            body = app_client.get(f"/api/v1/sessions/{sid}/live").json()
            assert body == {"live": True, "viewers": 1, "passthrough": True}


def test_second_publisher_replaces_first(app_client):
    sid = _sid(app_client)
    first = app_client.websocket_connect(PUB.format(sid=sid))
    p1 = first.__enter__()
    try:
        with app_client.websocket_connect(PUB.format(sid=sid)) as p2:
            with pytest.raises(WebSocketDisconnect) as closed:
                while True:
                    p1.receive_text()
            assert closed.value.code == 4409
            p2.send_text(HELLO)
    finally:
        with contextlib.suppress(WebSocketDisconnect):
            first.__exit__(None, None, None)


def test_publish_requires_api_key_when_set(app_client, monkeypatch):
    sid = _sid(app_client)
    monkeypatch.setenv("API_KEY", "secret")
    with pytest.raises(WebSocketDisconnect) as wrong:
        with app_client.websocket_connect(PUB.format(sid=sid), headers={"X-API-Key": "nope"}) as ws:
            ws.receive_text()
    assert wrong.value.code == 4401
    with app_client.websocket_connect(PUB.format(sid=sid), headers={"X-API-Key": "secret"}) as ws:
        ws.send_text(HELLO)


def test_watch_needs_valid_token_when_api_key_set(app_client, monkeypatch):
    sid = _sid(app_client)
    other = _sid(app_client, "other")
    monkeypatch.setenv("API_KEY", "secret")
    headers = {"X-API-Key": "secret"}
    token = app_client.post(f"/api/v1/sessions/{sid}/live/viewer-token", headers=headers).json()["token"]
    assert token
    other_token = app_client.post(f"/api/v1/sessions/{other}/live/viewer-token", headers=headers).json()["token"]
    forged = token.split(".")[0] + "." + "0" * 64

    def refused(query):
        with pytest.raises(WebSocketDisconnect) as closed:
            with app_client.websocket_connect(WATCH.format(sid=sid) + query) as ws:
                ws.receive_text()
        return closed.value.code

    assert refused("") == 4401
    assert refused("?token=garbage") == 4401
    assert refused("?token=" + forged) == 4401
    assert refused("?token=" + other_token) == 4401
    with app_client.websocket_connect(WATCH.format(sid=sid) + "?token=" + token) as ws:
        assert _json(ws)["type"] == "status"


def test_token_expiry_and_session_binding(app_client):
    from live.router import check_viewer_token, make_viewer_token

    key = "secret"
    stale = make_viewer_token("sid1", key, now=time.time() - 10_000)
    assert check_viewer_token("sid1", stale, key) is False
    fresh = make_viewer_token("sid1", key)
    assert check_viewer_token("sid1", fresh, key) is True
    assert check_viewer_token("sid2", fresh, key) is False


def test_viewer_token_is_empty_without_api_key(app_client):
    sid = _sid(app_client)
    assert app_client.post(f"/api/v1/sessions/{sid}/live/viewer-token").json()["token"] == ""


def test_unknown_session_is_4404(app_client):
    with pytest.raises(WebSocketDisconnect) as pub:
        with app_client.websocket_connect(PUB.format(sid="missing")) as ws:
            ws.receive_text()
    assert pub.value.code == 4404
    with pytest.raises(WebSocketDisconnect) as watch:
        with app_client.websocket_connect(WATCH.format(sid="missing")) as ws:
            ws.receive_text()
    assert watch.value.code == 4404
    assert app_client.get("/api/v1/sessions/missing/live").status_code == 404


def test_oversized_frame_closes_with_4413(app_client):
    sid = _sid(app_client)
    with app_client.websocket_connect(PUB.format(sid=sid)) as pub:
        pub.send_text(HELLO)
        pub.send_bytes(bytes([1]) + b"\x00" * MAX_FRAME_BYTES)
        with pytest.raises(WebSocketDisconnect) as closed:
            while True:
                pub.receive_text()
        assert closed.value.code == 4413


def test_disallowed_origin_is_refused(app_client):
    sid = _sid(app_client)
    with pytest.raises(WebSocketDisconnect) as closed:
        with app_client.websocket_connect(WATCH.format(sid=sid), headers={"Origin": "https://evil.example"}) as ws:
            ws.receive_text()
    assert closed.value.code == 4403
    with app_client.websocket_connect(WATCH.format(sid=sid), headers={"Origin": "https://dash.example"}) as ws:
        assert _json(ws)["type"] == "status"


def test_slow_viewer_does_not_block_the_publisher(app_client):
    sid = _sid(app_client)
    total = QUEUE_LIMIT * 3
    with app_client.websocket_connect(PUB.format(sid=sid)) as pub:
        pub.send_text(HELLO)
        with app_client.websocket_connect(WATCH.format(sid=sid)) as fast:
            _drain_status_and_hello(fast)
            with app_client.websocket_connect(WATCH.format(sid=sid)) as idle:
                _drain_status_and_hello(idle)  # idle never reads after this
                for i in range(total):
                    pub.send_bytes(bytes([2]) + i.to_bytes(8, "little") + b"x")
                last = None
                for _ in range(total):
                    last = fast.receive_bytes()
                assert last[1:9] == (total - 1).to_bytes(8, "little")
