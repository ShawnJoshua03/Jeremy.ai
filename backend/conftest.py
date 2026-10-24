import pytest


@pytest.fixture(autouse=True)
def _clear_api_key(monkeypatch):
    """A shell API_KEY would reject every /api/v1 call. Tests that need one set it."""
    monkeypatch.delenv("API_KEY", raising=False)
