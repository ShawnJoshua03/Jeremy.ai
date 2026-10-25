"""Live telemetry relay: calls are buffered in order and handed to the bridge by sequence."""
from fastapi.testclient import TestClient

import live_relay
import main


def test_relay_buffers_calls_in_order():
    live_relay._streams.clear()
    c = TestClient(main.app)
    assert c.get("/api/v1/live/v1/database/jeremy-ai-live/schema").status_code == 200
    for i in range(3):
        r = c.post("/api/v1/live/v1/database/jeremy-ai-live/call/push_frame", content=f'["frame {i}"]',
                   headers={"Content-Type": "application/json"})
        assert r.status_code == 200
    got = c.get("/api/v1/live/poll", params={"db": "jeremy-ai-live", "after": 0, "wait": 0}).json()
    assert [i["args"] for i in got["items"]] == ['["frame 0"]', '["frame 1"]', '["frame 2"]']
    again = c.get("/api/v1/live/poll", params={"db": "jeremy-ai-live", "after": got["seq"], "wait": 0}).json()
    assert again["items"] == []


def test_relay_rejects_bad_input():
    c = TestClient(main.app)
    assert c.post("/api/v1/live/v1/database/bad name/call/x", content="[]").status_code in (400, 404)
    assert c.post("/api/v1/live/v1/database/db/call/push_frame", content="{}").status_code == 400
    assert c.post("/api/v1/live/v1/database/db/call/push_frame", content="[" + "x" * 70000 + "]").status_code == 413
