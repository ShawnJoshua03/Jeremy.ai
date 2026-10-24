import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(__file__)))

import json

import httpx

import coach

M = {"successful_transfers": 4, "drops": 1, "invalid_releases": 0, "elapsed_s": 120.0,
     "path_efficiency": 0.4, "practice_score": 61.0}
ERR = [{"event_id": "e1", "kind": "drop", "t_ms": 5000, "instrument": "left"}]


def test_fallback_without_key(monkeypatch):
    monkeypatch.delenv("GEMINI_API_KEY", raising=False)
    c = coach.generate(M, ERR)
    assert c.source == "fallback" and c.tips[0].event_id == "e1"


def _client(payload, status=200):
    def h(req):
        return httpx.Response(status, json=payload)
    return httpx.Client(transport=httpx.MockTransport(h))


def test_gemini_unknown_event_id_dropped(monkeypatch):
    monkeypatch.setenv("GEMINI_API_KEY", "k")
    out = {"summary": "Good work.", "tips": [{"text": "a", "event_id": "e1"}, {"text": "b", "event_id": "fake"}]}
    payload = {"candidates": [{"content": {"parts": [{"text": json.dumps(out)}]}}]}
    c = coach.generate(M, ERR, _client(payload))
    assert c.source == "gemini"
    assert [t.event_id for t in c.tips] == ["e1", None]


def test_gemini_failure_falls_back(monkeypatch):
    monkeypatch.setenv("GEMINI_API_KEY", "k")
    assert coach.generate(M, ERR, _client({}, 500)).source == "fallback"
    assert coach.generate(M, ERR, _client({"candidates": [{"content": {"parts": [{"text": "not json"}]}}]})).source == "fallback"
    assert coach.generate(M, ERR, _client({"candidates": []})).source == "fallback"


def test_fallback_caps_at_four_tips_and_omits_path_tip_at_half_efficiency(monkeypatch):
    monkeypatch.delenv("GEMINI_API_KEY", raising=False)
    errs = [{"event_id": f"e{i}", "kind": "drop", "t_ms": 1000 * (i + 1)} for i in range(5)]
    low = coach.generate({**M, "path_efficiency": 0.49}, errs)
    assert len(low.tips) == 4
    assert [t.event_id for t in low.tips[:3]] == ["e0", "e1", "e2"]
    assert low.tips[3].event_id is None and "indirect" in low.tips[3].text
    high = coach.generate({**M, "path_efficiency": 0.5}, errs)
    assert len(high.tips) == 3 and all(t.event_id for t in high.tips)
    quiet = coach.generate({**M, "path_efficiency": 0.8, "drops": 0, "invalid_releases": 0}, [])
    assert quiet.tips == [] and quiet.source == "fallback"


def test_gemini_schema_miss_and_blank_event_ids(monkeypatch):
    monkeypatch.setenv("GEMINI_API_KEY", "k")
    cases = [
        {"summary": "x" * 401, "tips": []},
        {"summary": "ok", "tips": [{"text": "t", "event_id": "e1"}] * 5},
        {"summary": "ok", "tips": [{"text": "y" * 241, "event_id": "e1"}]},
        {"tips": []},
    ]
    for raw in cases:
        payload = {"candidates": [{"content": {"parts": [{"text": json.dumps(raw)}]}}]}
        assert coach.generate(M, ERR, _client(payload)).source == "fallback"
    kept = {"summary": "Fine.", "tips": [{"text": "a", "event_id": None}, {"text": "b", "event_id": ""}]}
    payload = {"candidates": [{"content": {"parts": [{"text": json.dumps(kept)}]}}]}
    c = coach.generate(M, ERR, _client(payload))
    assert c.source == "gemini" and [t.event_id for t in c.tips] == [None, None]


def test_gemini_transport_error_falls_back(monkeypatch):
    monkeypatch.setenv("GEMINI_API_KEY", "k")

    def handler(request):
        raise httpx.ConnectError("down")

    assert coach.generate(M, ERR, httpx.Client(transport=httpx.MockTransport(handler))).source == "fallback"
