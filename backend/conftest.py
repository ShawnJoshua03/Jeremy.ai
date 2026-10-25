import pytest


PROVIDER_KEYS = ("ANTHROPIC_API_KEY", "ANTHROPIC_MODEL", "CARTESIA_API_KEY", "CONVEX_URL", "CONVEX_DEPLOY_KEY",
                 "APIFY_TOKEN", "APIFY_PRICING_ACTOR")


@pytest.fixture(autouse=True)
def _clear_api_key(monkeypatch):
    """A shell API_KEY would reject every /api/v1 call. Tests that need one set it.

    main.py loads .env with override=True, so without this a developer's real keys would make the
    suite call Claude, Cartesia, Convex and Apify live (slow, flaky, and it spends API credit).
    """
    monkeypatch.delenv("API_KEY", raising=False)
    for name in PROVIDER_KEYS:
        monkeypatch.delenv(name, raising=False)
