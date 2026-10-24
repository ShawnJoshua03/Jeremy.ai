# Cartesia submission

Jeremy.ai, CalHacks 13. A trainee practices a laparoscopic cholecystectomy (gallbladder removal, exercise id `lap_chole`) on a Quest 3. Both hands stay on instruments, so the coaching that matters has to be heard. Cartesia is the voice of that companion.

Judges weigh the problem, the use of Cartesia, execution, and impact. This note separates what has been run from what is only specified. No API keys.

## Setup

From the repo root, copy `.env.example` to `.env`. The speech adapter reads:

| Variable | Role |
|---|---|
| `CARTESIA_API_KEY` | Required for audio. Unset or blank: the route returns 503 and the client keeps the text |
| `CARTESIA_MODEL_ID` | Optional. Default `sonic-3.6` |
| `CARTESIA_VOICE_ID` | Optional. Default is the voice id in [RUNBOOK.md](RUNBOOK.md) (Skylar). A separate avatar voice id is optional and is not set in `.env.example` |

```powershell
cd backend
python -m venv .venv; .\.venv\Scripts\pip install -r requirements.txt
.\.venv\Scripts\python -m uvicorn main:app --reload
```

Interactive docs: `http://127.0.0.1:8000/docs`. The key stays in the server environment. The Quest app and the instructor dashboard do not receive it.

The existing audio route, for text the client is already showing:

```http
POST /api/v1/sessions/{session_id}/coach/audio
Content-Type: application/json

{"text":"Clip only the cystic duct. Leave the common bile duct alone."}
```

`200` is `audio/wav` with `Cache-Control: no-store`. `503` means show the text. Details: [../backend/speech/README.md](../backend/speech/README.md).

## What Cartesia does, and why voice is the product

The integration is the Cartesia TTS bytes API (`https://api.cartesia.ai/tts/bytes`), API version `2026-08-14`, checked against Cartesia's docs on 2026-10-24. The call in `backend/speech/cartesia.py` sends:

- model `sonic-3.6` (or `CARTESIA_MODEL_ID`)
- one voice id
- language `en`
- output WAV, `pcm_s16le`, 44.1 kHz

Streamed WAV size fields arrive as `0xFFFFFFFF`. `finalize_wav_sizes` writes real sizes so Unity's decoder accepts the file. There is no automatic paid retry. Audio is not written to disk.

**Verified** on 2026-10-24 from a PC: `POST /coach/audio` returned a 44.1 kHz mono WAV ([STATUS.md](STATUS.md)). Playback on a headset is **not verified**.

**Designed, not verified.** During the fifteen lap-chole steps a robot avatar polls:

- `POST /api/v1/sessions/{sid}/narrator/line` → `{text, step, expected_progress, actual_progress, delta, severity, repeat}`
- `POST /api/v1/sessions/{sid}/narrator/line/audio` → the same WAV contract, or 503 with the sentence left on screen

`severity` is `on_track`, `slow`, `off_track`, or `critical`. The backend computes expected progress against actual progress for the current step. Gemini may turn that structure into one sentence of at most 25 words. It may not invent steps or numbers. If Gemini or the key is missing, a template sentence is spoken instead. The same line is not repeated twice in a row. The headset fetches audio only when `repeat` is false, drives the mouth from the amplitude, and tints the eyes by severity. A 503 keeps the subtitle.

Voice is the core of the drill. The trainee is looking through the laparoscope, not at a dashboard. A paragraph on a panel is the wrong channel. One calm sentence, tied to the step they are on, is the channel they can use. Text remains the fallback so a missing key does not stop the exercise.

This pass uses Cartesia for speech synthesis. It does not use Cartesia speech-to-text, a voice agent, voice cloning, or emotion controls. The Quest client never calls Cartesia.

`narrator.py` and `narrator_router.py` are not in this worktree. No avatar script is under `quest/Assets/Scripts/`. Do not tell a judge the companion spoke.

## Known limitations

- Narrator routes, narrator audio, and the Unity avatar: **not verified**.
- Headset playback of `/coach/audio`: **not verified**. The PC WAV check does not prove the Quest `AudioSource` path.
- No barge-in, no trainee microphone, no streaming conversational session. The companion is one short line every few seconds.
- Speech fails closed: missing key, timeout, non-WAV body, or a body over 10 MiB becomes 503 and a generic message. The upstream body is not returned.
- Rate limits on coach audio are in-process. Narrator rate limiting is specified to reuse `backend/ratelimit.py` and has not been run.
- The practice score and the dollar figures are deterministic backend outputs. Cartesia only speaks text it is given.

## Privacy and responsible use

- `CARTESIA_API_KEY` lives in the server environment or `.env`, which is not committed. It is not on the headset and not in the browser bundle.
- The adapter does not store audio. Responses send `Cache-Control: no-store`.
- Errors do not include the key, the transcript, or the provider body (`backend/speech/cartesia.py`).
- The product is a training simulation. It is not a medical device, and a spoken cue is not clinical instruction. The score is a practice rubric in `backend/scoring.py`.
- The trainee's voice is not captured. There is no speech-to-text in this build.
