# Voice coach: automatic cues and push-to-talk (headset wiring)

For the Unity owner (`quest/Assets/Scripts/`). Backend: `backend/port_coach_router.py` (one-way cues) and `backend/voice_chat_router.py` (conversation). Sessions with exercise `lap_chole` or `port_placement` only.

**Status.** The backend routes are tested in-process with Gemini, Cartesia TTS and Cartesia STT mocked (`backend/tests/test_voice_chat.py`, `backend/tools/voice_chat_demo.py`). Everything on the headset side in this document is **not verified**: Quest microphone capture, the RECORD_AUDIO permission flow, WAV upload, playback, timing, and live STT accuracy on Quest mic audio. Live Gemini and live Cartesia (TTS and STT) are also **not verified**.

Every number the coach says comes from `port_coach.py`, which is deterministic. The conversation layer only chooses which measured facts to read out. It never changes a score or stored data. The tolerances are prototype practice targets, not clinical standards.

## (a) When cues fire automatically

| Trigger | Call | Notes |
|---|---|---|
| A `port_placed` event upload was acknowledged (`POST /events` returned 200) | `POST /api/v1/sessions/{sid}/coach/placement/audio` with `{"cue": "latest"}` | Speaks the cue for the port just placed. Also call `POST /coach/placement` and show `cues[-1].text`. |
| The fourth port is placed, or the attempt completes | `POST /coach/placement/audio` with `{"cue": "debrief"}` | The debrief replaces any unplayed port cue. |

Rules:

- **Never** fire a cue on idle, or while the trainee's instrument is moving. Wait until the instrument tip velocity has been near zero for about 0.5 s, or until the trocar is released. The threshold is a guess: tune it on the headset.
- **Queue of one.** Keep at most one pending cue. A newer cue replaces a stale cue that has not started playing. A cue that is already playing finishes, unless the trainee presses Stop.
- **Always show the text** on the HUD (`reply_text` or the cue `text`), whether or not audio plays.
- Keep a **Stop speaking** control (a button or pinch on the HUD) that stops the AudioSource and clears the queue.
- Auto-cues are **muted while push-to-talk is held** and while a conversation reply is playing.
- Rate limit: the audio routes share `COACH_RATE_LIMIT_PER_MIN` (default 12 per session per minute). A 429 means: show the text and skip the audio.

## (b) Push-to-talk

1. **Permission.** Request `android.permission.RECORD_AUDIO` at runtime (`UnityEngine.Android.Permission.RequestUserPermission(Permission.Microphone)`) before the first recording. If it is denied, hide the mic button and offer typed or preset questions instead.
2. **Start.** Begin when a controller button is held (for example the B button) or the HUD mic button is pinched: `Microphone.Start(null, false, 10, 16000)`. That records up to 10 s at 16 kHz, using the default device, which is the Quest microphone. Show a "listening" indicator. Stop or duck any coach audio that is playing, and mute auto-cues.
3. **Stop.** On release, read `Microphone.GetPosition(null)` and call `Microphone.End(null)`. Take the samples up to that position, downmix to mono if needed, convert float samples to 16-bit PCM little-endian, and write a 44-byte RIFF/WAVE header (PCM, 1 channel, 16000 Hz, 16 bits). Drop clips shorter than about 0.3 s.
4. **Ask.** `POST /api/v1/sessions/{sid}/coach/talk` with the WAV as the raw body and `Content-Type: audio/wav`. Show `transcript` (as "You: ...") and `reply_text` on the HUD straight away.
5. **Speak.** If `audio_available` is true, `POST /coach/talk/audio` with `{"turn": <turn>}`. This speaks the stored reply and does not transcribe again. Play the returned WAV (44.1 kHz mono 16-bit PCM) with `DownloadHandlerAudioClip` / `AudioType.WAV`, or parse it into an `AudioClip`.
6. **Resume.** Un-mute auto-cues after playback ends or Stop is pressed.

Limits: at most 1.5 MB per upload, 0.2 to 15 s of audio, and 16-bit PCM WAV at 8 to 48 kHz, mono preferred. Ten seconds at 16 kHz mono is about 320 KB. Typed text up to 300 characters also works: `{"text": "..."}`.

Follow-ups work because the server keeps a short per-session history: "how do I fix that?", "and the next one?", "say that again". That history lives **in server memory only**: 12 turns per session, cleared after 30 minutes idle, at most 500 sessions. It resets whenever the backend restarts, as the free-tier SQLite does. If `/coach/talk/audio` returns 404 for a turn, show the text only.

What the coach understands, with no Gemini key: repeat, why was that wrong, how far off was I (with an optional port name; the default is the port last discussed, otherwise the latest port), how do I fix it, which port was worst, how am I doing (debrief), what should I practise, what are the tolerances, and next or previous port. Port names it recognises: Veress / needle, umbilical / camera, epigastric, medial (midclavicular), lateral (axillary). "Subcostal" on its own makes the coach ask which one. Anything else gets a short help sentence. If `GEMINI_API_KEY` is set, Gemini may reword an answer to an open question. Its reply is discarded if it contains digits or any number that is not in the measured facts.

## (c) Examples

```http
POST /api/v1/sessions/{sid}/coach/talk
Content-Type: audio/wav

<16 kHz mono 16-bit WAV bytes>
```

```json
{"transcript": "how do I fix that", "intent": "fix", "port": "epigastric",
 "reply_text": "For the epigastric port, move about twenty-two millimetres toward the head.",
 "speak": true, "audio_available": true, "turn": 2, "source": "template"}
```

Typed or preset question, asking for the audio link as well:

```http
POST /api/v1/sessions/{sid}/coach/talk?audio=true
Content-Type: application/json

{"text": "and the next one?"}
```

```json
{"transcript": "and the next one?", "intent": "next", "port": "rsc_medial",
 "reply_text": "Medial subcostal port was nineteen millimetres off the site, to the patient's left. Move about nineteen millimetres to the patient's right.",
 "speak": true, "audio_available": true, "turn": 3, "source": "template",
 "audio_url": "/api/v1/sessions/{sid}/coach/talk/audio", "audio_request": {"turn": 3}}
```

```http
POST /api/v1/sessions/{sid}/coach/talk/audio
Content-Type: application/json

{"turn": 3}
```

That returns `200 audio/wav`. `{}` speaks the latest reply.

`/coach/talk/audio` also accepts the same input as `/coach/talk` (WAV or `{"text": ...}`). It then runs a new turn and returns the WAV directly. The turn's details come back in the `X-Coach-Turn`, `X-Coach-Intent`, `X-Coach-Reply` and `X-Coach-Transcript` headers, which are percent-encoded. That saves one round trip, but the text arrives only with the audio, so the two-call flow above is preferred.

## (d) Errors

| Status | Meaning | Headset action |
|---|---|---|
| 404 | Unknown session, or a turn that is no longer in memory | Show the text you already have |
| 409 | Not a `lap_chole` / `port_placement` session | Hide the mic button |
| 413 | Audio over 1.5 MB | Record shorter clips (cap at 10 s) |
| 415 | Body is neither JSON nor audio/wav | Fix the Content-Type |
| 422 | Empty transcript ("nothing was heard"), invalid WAV, too short or too long, or empty text | Show "I didn't catch that", and let the trainee retry |
| 429 | Rate limited (`Retry-After` header) | Show the text and skip the audio |
| 503 on `/coach/talk` | Speech recognition unavailable (no key or provider error). The body still has `reply_text` asking the trainee to retry or type | Show `reply_text` and offer typed or preset questions |
| 503 on `/coach/talk/audio` or `/coach/placement/audio` | Text-to-speech unavailable. The body keeps `reply_text` | **Text only**: show `reply_text`, play nothing |

Raw audio is never stored or logged by the backend. Only the transcript text goes into the in-memory history.
