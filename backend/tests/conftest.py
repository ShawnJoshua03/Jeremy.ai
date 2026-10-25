"""Test bootstrap: make backend/ importable and pin DATABASE_URL before any module imports it."""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(__file__)))
os.environ["DATABASE_URL"] = "sqlite:///:memory:"

import pytest  # noqa: E402


@pytest.fixture(autouse=True)
def _offline_services(monkeypatch):
    """Never use local credentials or real HTTP transports, including optional providers."""
    import main  # load_dotenv runs here before clearing credentials
    import httpx

    for key in ("API_KEY", "ANTHROPIC_API_KEY", "GEMINI_API_KEY", "CARTESIA_API_KEY",
                "CONVEX_URL", "CONVEX_DEPLOY_KEY", "CONVEX_INGEST_SHARED_SECRET"):
        monkeypatch.delenv(key, raising=False)

    def blocked(*args, **kwargs):
        raise AssertionError("Real network transport is disabled in tests")

    monkeypatch.setattr(httpx.HTTPTransport, "handle_request", blocked)
    monkeypatch.setattr(httpx.AsyncHTTPTransport, "handle_async_request", blocked)


@pytest.fixture(autouse=True)
def _fresh_rate_limits():
    """Coach rate-limit counters are process-global; start every test clean."""
    import ratelimit
    ratelimit.reset()
    yield
    ratelimit.reset()
