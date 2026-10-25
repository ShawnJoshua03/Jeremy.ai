# Laparoscopic cholecystectomy robot narrator

The avatar polls either endpoint, with no request body, using the existing API key headers:

- `POST /api/v1/sessions/{sid}/narrator/line` returns exactly
  `{text, step, expected_progress, actual_progress, delta, severity, repeat}`.
- `POST /api/v1/sessions/{sid}/narrator/line/audio` generates a new line and returns
  Cartesia `audio/wav`; on speech failure, HTTP 503 returns the same seven JSON
  fields plus `detail`. Display `text` from that response.

Each request advances narration, including an audio request after a text request.
Poll audio directly for spoken cues; poll JSON for text-only use. These endpoints
do not retrieve a previously generated line. Wait for each response and playback
to finish before polling again. Both endpoints share a per-session narration quota
via `ratelimit.py` (`COACH_RATE_LIMIT_PER_MIN`, default 12); audio also shares the
existing `coach_audio` quota. HTTP 429 includes `Retry-After`; unknown sessions
return 404 and exercises other than `lap_chole` return 409. Responses use `no-store`.

`step` is the earliest incomplete ID in `scoring.LC_STEPS`, or `complete` when all
15 are recorded. Progress values are percentages of the whole sequence, rounded
to two decimals: actual = unique known `step_completed` IDs / 15 * 100; expected =
min(elapsed / 600, 1) * 100; delta = actual - expected, in percentage points.
This assumes equal 40-second step targets, based on the existing scoring budget.
These are prototype practice targets, not clinical standards or validated timing.
Elapsed time uses the stored final duration, otherwise server time since start
(frozen at completion), with the latest uploaded event timestamp as a lower bound.
An unstarted session with no events has zero elapsed time. The pure function
`compute_progress(events, elapsed_s)` excludes future events and rejects invalid time.

Severity precedence:

1. `critical`: any major finding from `scoring.lap_chole_findings`; remains critical
   throughout the attempt, including after completion, and asks for instructor review.
2. `off_track`: a minor finding or failed placement within the last 40 seconds,
   or a later step completed while an earlier step is missing. Port direction,
   angle and optional depth corrections reuse `port_coach.analyze`; latest placement
   per port is used, while scoring retains its historical port-offset findings.
3. `slow`: at least one whole step behind the target pace; completed sequences are exempt.
4. `on_track`: otherwise. This describes recorded telemetry, not proof of safety.

Each line is one sentence of at most 25 words. Claude receives only computed facts
and two grounded sentence forms, and must return one of those forms exactly.
Server validation rejects any other text, including invented numbers or steps.
Missing keys, malformed responses and provider failures use the first template.
Claude is called through `backend/llm.py`;
synchronous routes run provider calls in FastAPI's thread pool, outside database
connections, so they do not block the event loop. Requests still wait for the
provider timeout/retry budget; offline feedback does not require a provider.

The last line per session is stored in a lock-protected, bounded in-memory cache
(10,000 sessions). If a proposed line matches the previous line, an alternative
sentence is returned and `repeat` is true: a duplicate was avoided, and the returned
text is safe to speak. Otherwise `repeat` is false. Both endpoints share this state.
Memory resets at restart or eviction and is local to one worker, like the existing
rate limiter. Use one worker for consistent repeat handling.

No new environment variables: existing `ANTHROPIC_API_KEY`, `ANTHROPIC_MODEL`,
`CARTESIA_API_KEY`, `CARTESIA_MODEL_ID`, and `CARTESIA_VOICE_ID` are reused.
Tests in `backend/tests/test_narrator.py` use mocked providers and an isolated database.
Live Claude, live Cartesia, and Unity avatar playback are **not verified**.
