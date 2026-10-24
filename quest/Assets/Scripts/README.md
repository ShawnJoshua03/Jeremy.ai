# Peg-transfer scripts (Unity 6000.6.7f1)

All implementation and verification assets are contained in this directory. This branch initially had no `quest/` project; the Codex instructions were read from local `main:docs/AGENT_PROMPTS.md`. No scenes, package manifests, project settings, backend files, or shared worktrees were changed.

## Scene hookup

1. In the existing Unity 6.6 project, use the installed Meta Building Blocks camera rig, passthrough and controller tracking. Keep the project on OpenXR. The platform switch, Project Setup Tool, opaque fallback and Android device setup remain manual project tasks.
2. Add `PegTransferRig` to one empty GameObject with unit scale. Assign the headset camera and the left/right controller pose anchors from the SDK rig. On Play it creates the board, six objects and targets, two tip instruments, telemetry, uploader, HUD, coaching and optional command components. Do not also add duplicates of those components to the same object.
3. The generated world-space Canvas contains Start, Pause/resume, Reset, Retry upload, Recenter, Coach and Stop speaking buttons. Connect it to the installed SDK's **canvas/ray UI interaction** setup and EventSystem. `GraphicRaycaster` alone does not supply Quest controller pointer input. Use a world-space panel/ScrollRect of your own if you need larger text or scrolling results. Each action is also a public method for inspector UnityEvents. Assign explicit materials to a supplied `PegBoard` to ensure the URP shader is retained in Android builds.
4. Press Start, squeeze a trigger near an object, then release its center inside the corresponding target on the opposite side. Wrong-target/off-board/tracking-loss releases count as errors and return the object to its source for another try. Successful objects are locked and score once. Both controllers can operate independently; a peg has one owner at a time.
5. Recenter places the board 0.5 m forward and 0.35 m below the head. `MoveBoardTo(Transform)` provides a manual placement anchor. Placement is allowed only outside Running/Paused; moving the board during an attempt would corrupt its coordinate frame. Use unit scale on the board and its parents (coordinates are meters).
6. Pausing, headset focus loss and application suspension stop the exercise clock, release held objects back to source without a penalty, and insert invalid pose samples. Resume explicitly. Reset cancels the active attempt and starts a new Ready attempt with fresh IDs. The timer starts once and caps attempts at the backend's 3600-second maximum.

## Input verification

Input API names were checked against installed OpenXR **1.18.0**, Input System **1.20.0**, and Meta XR **207.0.1** package sources in the existing Unity project. `OculusTouchControllerProfile.cs` exposes `triggerPressed`, `isTracked`, and `trackingState`. The installed OpenXR Starter Assets use `InputActionReference`; these scripts use that API with optional per-hand default bindings:

```
<XRController>{LeftHand}/triggerPressed
<XRController>{LeftHand}/isTracked
<XRController>{LeftHand}/trackingState
```

Right-hand bindings use `RightHand`. Both position and rotation tracking bits must be valid. The controller transforms come from the installed rig, with a 0.12 m forward tip offset; tool transforms are never controlled remotely. For custom action references, enable their shared action map in its owner. Internally created actions are enabled/disposed by the instrument. After tracking loss, a fresh trigger squeeze is required. Controller input, physical alignment, grasp feel and ray UI require headset verification.

## Networking and offline behavior

Set `backendOrigin` to an HTTPS **origin** (no `/api/v1` suffix). Empty means offline practice. HTTP is allowed only for a loopback host in the Editor when `allowEditorHttp` is enabled. No certificate bypass, automatic redirects, provider keys or credentials are embedded. If the server adds scoped authorization, provide `ApiClient.sessionToken` at runtime.

The uploader uses the implemented API in this checkout:

```
POST /api/v1/sessions                     {trainee_id, exercise:"peg_transfer"}
POST /api/v1/sessions/{id}/start
POST /api/v1/sessions/{id}/events          {events:[...]}
POST /api/v1/sessions/{id}/samples         {samples:[...]}
POST /api/v1/sessions/{id}/complete        {elapsed_s}
GET  /api/v1/sessions/{id}/cost
```

Recorder events carry UUID `eventId`, sequence, elapsed milliseconds, controller ID and board-local positions. The API adapter maps to the server's snake-case event schema; the server does not accept/store the recorder's separate sequence or release position. Sample indices are monotonic per controller. Sampling runs at 25 Hz without fabricating catch-up samples. Invalid tracking inserts a gap; the local practice rubric matches the existing backend's formula.

Events upload on the next queue iteration; samples batch about once a second, at most 1000 per request. The queue retains stable IDs until acknowledgement, drains telemetry before completion, retries network/408/429/5xx errors with bounded exponential delay, and exposes permanent rejections for manual retry. Local simulation never awaits HTTP. A snapshot is saved under `Application.persistentDataPath/jeremy-upload-queue.json` at most once a second during practice, immediately on completion/cancel, and after acknowledgements. `.tmp`/`.bak` files provide interrupted-write recovery. An abrupt process kill may lose up to one second of live data; interrupted unfinished attempts are cancelled on reload because physical state cannot be resumed.

Limits are 16 pending attempts, 2048 pending events and 12000 pending samples per attempt. The first bounded telemetry window is retained. On overflow, practice and local metrics continue, an error is shown, and server scoring is withheld because it would be based on incomplete data. Such attempts remain on disk for inspection; Retry upload does not erase that warning. Cancelled attempts are not completed on the server. There is no cancellation endpoint in the current contract.

The server deduplicates events by `event_id` and samples by `(instrument, idx)`; start and complete are retry-safe. **Session creation itself is not idempotent in this backend**: a lost create response can leave an unused server session. Fixing that requires a backend contract change outside this task. The stored server ID prevents duplication after an acknowledged create. Queue storage contains practice telemetry only, not tokens.

## Feedback and optional instructor commands

Local results always remain available. Server results replace them after successful completion; the HUD displays every cost line with its source, assumed-risk labels and the backend disclaimer. Costs are illustrative, not billing data or validated patient-risk estimates.

`CoachPanel` uses the text contract present on local `main`: `POST .../coach` returning `{summary,tips:[{text,event_id}],source}`. This older checkout has no coach route, so unavailable responses show a text fallback. Automatic requests default off. `CoachAudioPlayer.PlayUrl` accepts an HTTPS audio URL and provides cancellation and a text fallback on failure. The current coach response contains no audio URL; audio is intentionally not auto-requested against an invented route or format. Connect it when the speech adapter has a tested contract.

`CommandPoller.pollingEnabled` defaults **off**, since this checkout has no command/ack endpoints. Its explicit proposed adapter contract is:

```
GET .../commands
{"commands":[{"commandId":"cmd-1","type":"highlight_target",
              "targetId":"target_1","expiresAt":"2026-10-24T18:00:00Z"}]}
POST .../commands/{commandId}/ack
{"status":"applied"|"rejected","message":"..."}
```

Supported types: `highlight_target`, `add_annotation` (`message`, optional `targetId`), `request_pause`, `request_retry`, and `assign_exercise` (`exerciseId:"peg_transfer"`). Expired, unsupported, wrong-target and invalid-state commands are rejected. IDs are remembered before acknowledgement so redelivery cannot repeat an action within the current session. Annotation text disables rich-text parsing. Polling stops on 404/405. Connect this adapter only after the backend implements and authorizes this contract. Commands never move tools.

## Verification

Verified on 2026-10-24 with Unity **6000.6.7f1**: runtime and editor assemblies compiled with **zero C# compiler errors/warnings**; **37 play-mode assertions passed**, including the real local FastAPI round trip against a disposable SQLite database. The backend served create/start/events/samples/complete/cost successfully. The failed-connection case deliberately produced a connection-refused log before recovering. The isolated editor also logged an unrelated `UnityEditor.Search.SearchDatabase.EnumerateAll` startup `ArgumentOutOfRangeException`; it did not prevent compilation or the checks. No production backend or shared Unity project was modified.

Run the isolated Unity compile/play-mode checks from PowerShell:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File quest/Assets/Scripts/Tests~/Verify.ps1
# With a running copy of this backend, optionally:
powershell -NoProfile -ExecutionPolicy Bypass -File quest/Assets/Scripts/Tests~/Verify.ps1 -BackendUrl http://127.0.0.1:18763
```

`-InstalledProject` points to the existing project whose cached Input System/uGUI sources should be used; `-Unity` can override the editor executable. The default editor version is fixed at 6000.6.7f1. The harness writes only to ignored `Tests~/UnityProject~/` and `Tests~/verification.log`; it does not modify the installed project. Its exit code is nonzero on failure and its log contains `JEREMY VERIFICATION PASSED` on success. The test runner is Editor-only and is excluded from player builds.

Checks cover state/timer guards, transformed board coordinates, matching/wrong-target/drop/tracking-loss releases, duplicate scoring, tracking gaps, command rejection, HTTPS policy, queue bounds and duration limits. With a backend URL, they also exercise offline persistence, restore, a failed connection followed by retry, duplicate event ingestion, real metrics, queue draining and four itemized cost lines. The temporary API session uses trainee `unity-verification`; use a disposable test database.

Not covered by headless checks: Android/IL2CPP build, on-device rendering or interaction, passthrough, XR tracking and action binding at runtime, UI ray wiring, actual Gemini/Cartesia calls, remote HTTPS deployment, instructor command transport, and physical instrument calibration.
