# RUNBOOK

Local demo sequence for the FastAPI backend, the fake Quest replay, and the
coaching endpoints. Commands are PowerShell from the repository root
(`C:\Users\shawn\orca\workspaces\Jeremy.ai\speech-opencode`) unless noted.
Recorded 2026-10-24 on Windows 11, PowerShell 5.1, Python 3.13.5.

## Environment variables

| Variable | Needed by | Behavior |
|---|---|---|
| `CARTESIA_API_KEY` | `POST /coach/audio` | Required for audio. Unset or blank -> 503; the client must keep showing text. |
| `CARTESIA_MODEL_ID` | `POST /coach/audio` | Optional. Default `sonic-3.6`. |
| `CARTESIA_VOICE_ID` | `POST /coach/audio` | Optional. Default `db6b0ed5-d5d3-463d-ae85-518a07d3c2b4`. |
| `ANTHROPIC_API_KEY` | `POST /coach`, ask-the-coach, voice chat | Optional. Unset or blank -> deterministic rule-based fallback (`source: "fallback"`). |
| `ANTHROPIC_MODEL` | all LLM text | Optional. Default `claude-haiku-5-5`. |
| `ASK_CLAUDE_BUDGET_USD` | ask-the-coach | Optional. Estimated spend cap, default 5. Claude is used whenever `ANTHROPIC_API_KEY` is set and the cap is not reached. |
| `DATABASE_URL` | backend startup | Optional. Default `sqlite:///./surgimentor.db` relative to `backend/`. |

Two precedence rules matter:

- `backend/main.py` calls `load_dotenv(override=True)`. If a `.env` exists, its values
  win over PowerShell `$env:` values. Set keys in `.env` when it exists.
- Do not leave `DATABASE_URL=` blank in `.env`. It overrides the default with an empty
  string and startup fails with `Could not parse SQLAlchemy URL`. Use
  `DATABASE_URL=sqlite:///./surgimentor.db` for local SQLite.

The sequence below assumes there is no `.env`. It runs in text-only mode: no Anthropic
key and no Cartesia key.

## 1. Create the venv (only if `backend/.venv` is missing)

```powershell
cd backend
python -m venv .venv
.\.venv\Scripts\python -m pip install -r requirements.txt
cd ..
```

## 2. Run the tests

```powershell
cd backend
.\.venv\Scripts\python -m pytest -q
```

Expected: `66 passed`. Warnings about `on_event` and Starlette's TestClient are
existing and non-fatal.

## 3. Start the backend

In terminal A:

```powershell
cd backend
$env:CARTESIA_API_KEY = ''
$env:ANTHROPIC_API_KEY = ''
.\.venv\Scripts\python -m uvicorn main:app --host 127.0.0.1 --port 8000
```

Keep the working directory at `backend/`: `main.py` imports sibling modules such as
`database` and `coach`. Wait for `Uvicorn running on http://127.0.0.1:8000`.
Interactive docs: `http://127.0.0.1:8000/docs`.

Check it from terminal B:

```powershell
Invoke-RestMethod http://127.0.0.1:8000/health
```

Expected: `status = ok`.

## 4. Run the fake Quest replay

In terminal B, while the server is running:

```powershell
cd backend
.\.venv\Scripts\python tools\fake_quest.py --base-url http://127.0.0.1:8000
```

This creates a session, uploads a 6-object trace with 2 drops, completes it, and
prints metrics, cost, and coach JSON. It does not call Cartesia. Its `/coach` call
uses Claude only if the server has a key; otherwise it returns the fallback.

## 5. Call `/coach` for the replay session

`fake_quest.py` does not print the session id, so read the latest completed session
for the `fake-quest` trainee:

```powershell
$sid = (Invoke-RestMethod 'http://127.0.0.1:8000/api/v1/trainees/fake-quest/progress')[-1].session_id
Invoke-RestMethod -Method Post "http://127.0.0.1:8000/api/v1/sessions/$sid/coach" |
    ConvertTo-Json -Depth 5
```

Expected with no `ANTHROPIC_API_KEY`: a summary and up to four tips with
`"source": "fallback"`. `POST` takes no request body. Run `fake_quest.py` again and
re-read `$sid`; ids change on every run.

## 6. Call `/coach/audio`

With `CARTESIA_API_KEY` empty (as started in step 3), this must return 503 and keep
the text visible:

```powershell
$body = '{"text":"Focus on controlled placement."}'
try {
    Invoke-WebRequest -Method Post "http://127.0.0.1:8000/api/v1/sessions/$sid/coach/audio" `
        -ContentType 'application/json' -Body $body -OutFile "$env:TEMP\coach.wav" | Out-Null
    "saved $env:TEMP\coach.wav"
} catch {
    "HTTP $([int]$_.Exception.Response.StatusCode): $($_.ErrorDetails.Message)"
}
```

Expected: `HTTP 503: {"detail":"Speech is unavailable; use text feedback."}` and no
file created. To request real audio, put a real `CARTESIA_API_KEY` (and optionally
`CARTESIA_MODEL_ID` / `CARTESIA_VOICE_ID`) in `.env` or the server environment,
restart the backend, and rerun the same block. The success path writes a 44.1 kHz
signed 16-bit PCM WAV and returns `200`, `Content-Type: audio/wav`,
`Cache-Control: no-store`.

Validation checks, if needed: blank text, an extra field, or text over 2,000
characters -> 422; an unknown session id -> 404 before any Cartesia call.

## 7. Stop

In terminal A, press `Ctrl+C`. Uvicorn stops the listener but leaves the local
`backend/surgimentor.db` file (gitignored) in place.

## Vultr (not run here)

The production commands are in `deploy/README.md`: `sudo bash deploy/setup.sh "$PWD"`,
`sudoedit /etc/jeremy/backend.env`, `sudo systemctl restart jeremy-api`, then
`curl --fail http://127.0.0.1:8000/health`. No VM, systemd unit, Caddy certificate,
or DNS setup was available in this environment, so none of those commands were run.

## What was verified, and what was not

Verified locally on 2026-10-24:

- `pytest -q`: 66 passed.
- `uvicorn main:app --host 127.0.0.1 --port 8000`: started, `/health` returned
  `{"status":"ok"}`.
- `fake_quest.py`: uploaded 600 samples, 8 events (6 successes + 2 drops); metrics
  `successful_transfers=6`, `drops=2`, `path_efficiency=0.532365...`,
  `practice_score=84.6`; cost and fallback coach JSON returned.
- `/coach` with no Anthropic key: 200, `source: "fallback"`.
- `/coach/audio` with no Cartesia key: 503 with the generic detail and
  `Cache-Control: no-store`.
- `/coach/audio` validation: blank/extra field/2,001 characters -> 422; unknown
  session -> 404.
- With a fake Cartesia key, an upstream 401 became the same generic 503; the uvicorn
  log contained no key or provider body.

Not verified:

- Successful live Cartesia synthesis (HTTP 200 WAV) and playback on the Quest: no
  real `CARTESIA_API_KEY` was available.
- Live Claude coaching: no `ANTHROPIC_API_KEY`; only the rule-based fallback ran.
- Vultr provisioning, systemd startup/hardening, Caddy HTTPS, and remote access.
