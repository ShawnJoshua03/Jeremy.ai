# Laparoscopic cholecystectomy robot narrator

`PegTransferRig` creates `RobotNarrator` beside the operating field, under the board
so it follows recentering. The head uses primitive meshes and runtime materials;
it faces the trainee and has its own AudioSource and world-space subtitle panel.
It does not require the main HUD to be enabled.

The narrator uses the rig's `ApiClient` and current upload attempt. It polls only
after that attempt's `lap_chole` server session has started and while the local
exercise is running. The default interval is eight seconds (clamped to six to
ten), with one request sequence at a time. Long requests, long speech clips and
existing recorded step guidance can extend the interval.

Requests use the existing origin policy and backend authentication helpers:

```text
POST /api/v1/sessions/{sid}/narrator/line        {}
  -> {"text":"...","step":"...","severity":"on_track","repeat":false}
POST /api/v1/sessions/{sid}/narrator/line/audio  {}
  -> audio/wav
```

The audio route is expected to return the most recently generated line for this
session. A repeated line never triggers an audio request. The exact backend text
is shown without rich-text parsing, including while audio loads or if speech
returns 503, a network error, or an invalid clip. A missing narrator route leaves
the existing local procedure guidance available and is retried at the normal
interval. No provider calls or provider credentials are added to Unity.

Eyes are green for `on_track`, amber for `slow`, red for `off_track`/`critical`,
and neutral blue for an unknown severity. Mouth opening and eye size follow the
AudioSource's RMS amplitude. Pause, completion, cancellation, reset, and disable
stop the voice and clear the subtitle. Late responses cannot speak for a previous
attempt. Existing recorded step guidance takes priority over narration.

Run the isolated Unity compile and mock-backend checks when Unity is free:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File quest/Assets/Scripts/Tests~/VerifyNarrator.ps1
```

The harness compiles the scripts with installed cached Unity/Meta packages and
uses a loopback fake narrator that returns JSON, HTTP 503, and a generated WAV.
It writes only under `Tests~/NarratorUnityProject~/` and its ignored log, and
does not build an APK or contact speech/AI providers. It exits with code 2 if
another Unity process is running.

Verified on 2026-10-24 with Unity 6000.6.7f1: compilation completed without C#
errors and all 27 mock-backend assertions passed (exit code 0). Existing project
and SDK deprecation warnings remain. The editor also logged its unrelated
`UnityEditor.Search.SearchDatabase.EnumerateAll` startup exception; verification
completed successfully. The headless scene has no AudioListener, so the WAV
check verifies decoding and clip ownership, not audible playback or animation.

Real narrator backend integration, Cartesia synthesis, Quest playback, visual
placement/readability, and amplitude animation on the headset are **not verified**.
