"""Test bootstrap: make backend/ importable and pin DATABASE_URL before any module imports it."""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(__file__)))
os.environ["DATABASE_URL"] = "sqlite:///:memory:"

import pytest  # noqa: E402


@pytest.fixture(autouse=True)
def _fresh_rate_limits():
    """Coach rate-limit counters are process-global; start every test clean."""
    import ratelimit
    ratelimit.reset()
    yield
    ratelimit.reset()


class FakeClaude:
    """Stands in for llm.complete_json. Each reply is a dict (returned), an exception (raised),
    or a callable(prompt, schema) whose result is used the same way. The last reply repeats."""

    def __init__(self):
        self.replies: list = []
        self.calls: list[dict] = []

    def reply(self, *replies) -> "FakeClaude":
        self.replies = list(replies)
        return self

    @property
    def prompts(self) -> list[str]:
        return [c["prompt"] for c in self.calls]

    def __call__(self, prompt, schema, **kwargs):
        self.calls.append({"prompt": prompt, "schema": schema, **kwargs})
        r = self.replies.pop(0) if len(self.replies) > 1 else (self.replies[0] if self.replies else {})
        if callable(r) and not isinstance(r, type):
            r = r(prompt, schema)
        if isinstance(r, BaseException) or (isinstance(r, type) and issubclass(r, BaseException)):
            raise r
        return r


@pytest.fixture()
def claude(monkeypatch):
    """Claude configured (fake key) with llm.complete_json replaced by a FakeClaude. No network."""
    import llm
    fake = FakeClaude()
    monkeypatch.setenv("ANTHROPIC_API_KEY", "test-key")
    monkeypatch.setattr(llm, "complete_json", fake)
    return fake
