# Coaching audio

`text_to_audio(text)` in `cartesia.py` returns WAV bytes. It uses the existing
`httpx` dependency and reads `CARTESIA_API_KEY` on each call. Optional server
settings: `CARTESIA_MODEL_ID` (default `sonic-3.6`) and `CARTESIA_VOICE_ID`
(default Skylar, `db6b0ed5-d5d3-463d-ae85-518a07d3c2b4`).

Request format checked on 2026-10-24 against the official
[bytes API](https://docs.cartesia.ai/api-reference/tts/bytes) and
[model/voice documentation](https://docs.cartesia.ai/build-with-cartesia/tts-models/latest).
API version is pinned to `2026-08-14`: Bearer authentication, a voice ID string,
and `output_format` set to WAV / pcm_s16le / 44100 Hz. Older API examples using
`voice: {mode: "id", id: "..."}` target a different API version.

For an existing session, send the displayed coaching text:

```http
POST /api/v1/sessions/{session_id}/coach/audio
Content-Type: application/json

{"text":"Focus on controlled placement in the next round."}
```

- 200: binary `audio/wav`, `Cache-Control: no-store`.
- 404: session does not exist; no provider request is made.
- 422: missing, blank, non-string, or over-2,000-character text, or extra fields.
- 503: missing key, upstream rejection, timeout/network failure, invalid WAV
  signature, or response above 10 MiB. Keep showing the original text.

Display text first, request speech separately, and play successful responses as
WAV (Unity `AudioType.WAV`). This route does not generate coaching or call
Claude; pass its spoken feedback as `text`. The request is synchronous in a
FastAPI worker thread, with a 5-second connection timeout and 30-second network
inactivity timeout. There are no automatic synthesis retries or saved files.
The one-line dynamic router import in `main.py` keeps registration within the
assigned edit boundary without importing `main` from the router.

Session existence is checked; session authorization and paid-request rate limits
are not implemented in this baseline API. Use a trusted demo network/access
gateway until those shared backend features are added. The service accepts
client-supplied text; it does not verify that it originated from Claude.

Run from the repository root:

```sh
python -m pytest -q backend/tests backend/speech/tests
```

Tests mock Cartesia HTTP transport and use an isolated SQLite database. Real
Cartesia synthesis and Quest audio playback have not been verified.
