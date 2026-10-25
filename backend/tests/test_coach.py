import coach
import llm

M = {"successful_transfers": 4, "drops": 1, "invalid_releases": 0, "elapsed_s": 120.0,
     "path_efficiency": 0.4, "practice_score": 61.0}
ERR = [{"event_id": "e1", "kind": "drop", "t_ms": 5000, "instrument": "left"}]


def test_fallback_without_key(monkeypatch):
    monkeypatch.delenv("ANTHROPIC_API_KEY", raising=False)
    c = coach.generate(M, ERR)
    assert c.source == "fallback" and c.tips[0].event_id == "e1"


def test_claude_unknown_event_id_dropped(claude):
    claude.reply({"summary": "Good work.", "tips": [{"text": "a", "event_id": "e1"}, {"text": "b", "event_id": "fake"}]})
    c = coach.generate(M, ERR)
    assert c.source == "claude"
    assert [t.event_id for t in c.tips] == ["e1", None]
    assert claude.calls[0]["schema"]["type"] == "object"  # standard lowercase JSON Schema


def test_claude_failure_falls_back(claude):
    claude.reply(llm.LLMUnavailable("APIStatusError"))
    assert coach.generate(M, ERR).source == "fallback"
    claude.reply(llm.LLMUnavailable("response was not a JSON object"))
    assert coach.generate(M, ERR).source == "fallback"
    claude.reply({})
    assert coach.generate(M, ERR).source == "fallback"


def test_fallback_caps_at_four_tips_and_omits_path_tip_at_half_efficiency(monkeypatch):
    monkeypatch.delenv("ANTHROPIC_API_KEY", raising=False)
    errs = [{"event_id": f"e{i}", "kind": "drop", "t_ms": 1000 * (i + 1)} for i in range(5)]
    low = coach.generate({**M, "path_efficiency": 0.49}, errs)
    assert len(low.tips) == 4
    assert [t.event_id for t in low.tips[:3]] == ["e0", "e1", "e2"]
    assert low.tips[3].event_id is None and "indirect" in low.tips[3].text
    high = coach.generate({**M, "path_efficiency": 0.5}, errs)
    assert len(high.tips) == 3 and all(t.event_id for t in high.tips)
    quiet = coach.generate({**M, "path_efficiency": 0.8, "drops": 0, "invalid_releases": 0}, [])
    assert quiet.tips == [] and quiet.source == "fallback"


def test_claude_schema_miss_and_blank_event_ids(claude):
    cases = [
        {"summary": "x" * 401, "tips": []},
        {"summary": "ok", "tips": [{"text": "t", "event_id": "e1"}] * 5},
        {"summary": "ok", "tips": [{"text": "y" * 241, "event_id": "e1"}]},
        {"tips": []},
    ]
    for raw in cases:
        claude.reply(raw)
        assert coach.generate(M, ERR).source == "fallback"
    claude.reply({"summary": "Fine.", "tips": [{"text": "a", "event_id": None}, {"text": "b", "event_id": ""}]})
    c = coach.generate(M, ERR)
    assert c.source == "claude" and [t.event_id for t in c.tips] == [None, None]


def test_claude_unavailable_falls_back(claude):
    """Transport errors, timeouts and overload all surface as LLMUnavailable; the trainee still gets coaching."""
    claude.reply(llm.LLMUnavailable("APITimeoutError"))
    c = coach.generate(M, ERR)
    assert c.source == "fallback" and c.tips[0].event_id == "e1" and len(claude.calls) == 1


def test_claude_not_called_without_key(monkeypatch, claude):
    monkeypatch.delenv("ANTHROPIC_API_KEY")
    assert coach.generate(M, ERR).source == "fallback" and claude.calls == []
