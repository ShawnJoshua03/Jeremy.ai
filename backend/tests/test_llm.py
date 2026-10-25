"""llm.complete_json against a fake anthropic.Anthropic client; no network or real key."""
from types import SimpleNamespace

import anthropic
import pytest

import llm

SCHEMA = {"type": "object", "properties": {"text": {"type": "string"}}, "required": ["text"]}


class FakeAnthropic:
    """Records the constructor and messages.create kwargs; returns or raises `result`."""

    def __init__(self, result):
        self.result = result
        self.init: dict = {}
        self.create: dict = {}
        self.closed = False
        self.messages = SimpleNamespace(create=self._create)

    def __call__(self, **kwargs):
        self.init = kwargs
        return self

    def _create(self, **kwargs):
        self.create = kwargs
        if isinstance(self.result, BaseException):
            raise self.result
        return self.result

    def close(self):
        self.closed = True


def response(text, stop_reason="end_turn"):
    return SimpleNamespace(stop_reason=stop_reason, content=[SimpleNamespace(type="text", text=text)])


@pytest.fixture
def fake(monkeypatch):
    monkeypatch.setenv("ANTHROPIC_API_KEY", "test-key")

    def install(result):
        client = FakeAnthropic(result)
        monkeypatch.setattr(anthropic, "Anthropic", client)
        return client
    return install


def test_json_object_is_parsed_with_structured_output(fake, monkeypatch):
    monkeypatch.setenv("ANTHROPIC_MODEL", "claude-test")
    client = fake(response('{"text": "Hold the grasper steady."}'))
    assert llm.complete_json("prompt", SCHEMA, max_tokens=50, timeout_s=3) == {"text": "Hold the grasper steady."}
    assert client.init["timeout"] == 3 and client.closed
    assert client.create["model"] == "claude-test" and client.create["max_tokens"] == 50
    assert client.create["output_config"]["format"] == {"type": "json_schema", "schema": SCHEMA}
    assert client.create["messages"] == [{"role": "user", "content": "prompt"}]


@pytest.mark.parametrize("result", [
    response('{"text": "partial', "max_tokens"),
    response("", "refusal"),
    response("not json"),
    response('["a list"]'),
    SimpleNamespace(stop_reason="end_turn", content=[]),
])
def test_refusal_truncation_or_non_object_is_unavailable(fake, result):
    fake(result)
    with pytest.raises(llm.LLMUnavailable):
        llm.complete_json("prompt", SCHEMA)


def test_api_error_is_unavailable_and_client_closed(fake):
    client = fake(anthropic.APIConnectionError(request=None))
    with pytest.raises(llm.LLMUnavailable, match="APIConnectionError"):
        llm.complete_json("prompt", SCHEMA)
    assert client.closed


def test_no_key_is_unavailable_without_a_client(monkeypatch):
    monkeypatch.delenv("ANTHROPIC_API_KEY", raising=False)
    monkeypatch.setattr(anthropic, "Anthropic", lambda **_: pytest.fail("no client without a key"))
    assert not llm.configured() and llm.model() == llm.DEFAULT_MODEL
    with pytest.raises(llm.LLMUnavailable):
        llm.complete_json("prompt", SCHEMA)
