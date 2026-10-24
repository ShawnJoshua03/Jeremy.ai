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
