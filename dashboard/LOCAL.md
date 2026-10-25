# Local live instructor dashboard

Everything runs on the instructor's PC. Nothing goes to the cloud.

```
 Quest 3 (lap chole)                         Instructor PC
 ┌──────────────────────┐   USB: adb reverse  ┌──────────────────────────────────────────┐
 │ LiveTelemetry.cs      │ ── HTTP :3000 ───▶ │ SpacetimeDB (local)  db: jeremy-ai-live   │
 │  8 Hz snapshot,       │                    │   liveSession / liveFrame / liveEvent /   │
 │  events, captions     │                    │   liveCaption  (+ call-room tables)       │
 └──────────────────────┘                    │            │ WebSocket subscription        │
 Meta Quest casting ─────── video ─────────▶ │ Browser: http://localhost:5173/#live       │
 (Camera → Cast → Computer)                   │   casting window via getDisplayMedia      │
                                              └──────────────────────────────────────────┘
```

- **Video**: the Quest casts to the PC as usual (Meta Horizon app, or `meta.com/casting` in Chrome). The dashboard asks you once to pick that window or tab (`getDisplayMedia`) and shows it large, with the live state drawn over it. Optionally it relays that view peer-to-peer (WebRTC, signaled through the same SpacetimeDB) to any other open dashboard, e.g. a second tab or a laptop on the LAN. Video is never stored.
- **Telemetry**: `quest/Assets/Scripts/Networking/LiveTelemetry.cs` installs itself and, while a lap chole is open, calls SpacetimeDB reducers over plain HTTP: `live_start` (trainee, anatomy polylines), `push_frame` (~8 Hz), `push_event` (each recorded lap chole event), `push_caption` (coach captions and sim feedback lines). The dashboard subscribes to the tables and renders them.

## Start

```powershell
.\scripts\live-local.ps1          # SpacetimeDB + module + dashboard + adb reverse, opens the browser
.\scripts\live-local.ps1 -Demo    # same, plus a fake headset publishing a looping attempt
```

The script starts `spacetime start` (127.0.0.1:3000) if it is not running, publishes `dashboard/spacetimedb` to it as `jeremy-ai-live` (always `--server http://127.0.0.1:3000`, never maincloud), runs `adb reverse tcp:3000 tcp:3000` if a headset is on USB, starts `npm run live` (Vite, port 5173, `.env.live`) and opens `http://localhost:5173/#live`. Rerun it any time; it skips what is already running. After a breaking schema change use `-Reset` (wipes live data only).

Manual equivalent:

```powershell
spacetime start --listen-addr 127.0.0.1:3000                      # window 1
cd dashboard
spacetime publish --server http://127.0.0.1:3000 jeremy-ai-live --module-path spacetimedb -y
npm run live                                                       # window 2
adb reverse tcp:3000 tcp:3000                                       # headset on USB
node scripts/fake-headset.mjs --loop --sloppy                       # optional demo data
```

The SpacetimeDB CLI is installed with `iwr https://windows.spacetimedb.com -useb | iex`. Note that its default server is `maincloud`; always pass `--server`/`-s local` for this stack.

## Headset connection

| Route | Setup | Notes |
|---|---|---|
| **USB (default)** | `adb reverse tcp:3000 tcp:3000` (the script does it) | No firewall change. The app's default endpoint `http://127.0.0.1:3000` then reaches the PC. Rerun after replugging. adb: `%USERPROFILE%\UnityEditors\<version>\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe`. |
| Wi-Fi / LAN | `.\scripts\live-local.ps1 -Lan`, then allow TCP 3000 inbound (admin PowerShell: `New-NetFirewallRule -DisplayName "SpacetimeDB 3000" -Direction Inbound -Protocol TCP -LocalPort 3000 -Action Allow -Profile Private`), then put the PC's URL in `live-url.txt` on the headset: `adb push live-url.txt /sdcard/Android/data/ai.jeremy.pegtransfer/files/live-url.txt` (one line, e.g. `http://192.168.1.20:3000`). | The headset could not reach the PC's LAN port in earlier testing (Windows Firewall), so USB is the reliable default. Network profile must be Private. |
| Unity editor (desktop preview) | nothing | `127.0.0.1:3000` is the PC itself. |

`live-url.txt` can also contain `off` to disable telemetry. Without a reachable server the component stays quiet: it pings every 5 s and sends nothing else.

## What the dashboard shows

- **Header**: live/stale/offline state, session picker (follows the newest by default), trainee, state, elapsed, steps, practice score (the sim's own offline formula; capped at 40 after a major error), major/minor counts, DB connection and frame rate.
- **Headset view**: the casting window, with the current step, a red alert when the jaws are on the common bile duct or a major error just happened, the key number for the step, and the coach's caption as subtitles. Full screen, audio toggle (when a tab was shared with audio), relay toggle.
- **Procedure**: the 15 steps grouped by phase, with live progress, per-step time, and amber/red marks for steps with minor/major findings.
- **Abdomen map** (top-down, head up, patient's right on the left): planned vs placed ports with offsets, the entry mark and its tolerance, anatomy polylines from the headset, the target, the tip and its trail. It switches to a zoomed Calot's triangle view for steps 7–15 (or pick Wall/Calot).
- **Live metrics**, per step: offset/angle against tolerance, depth with pop markers and the target/danger zones (entries); a pressure gauge with the 12–15 mmHg band (insufflation); jaws-on structure, distance to target with a sparkline, peritoneum cleared, contrast, clip counts (inside); an attempt summary at the end.
- **Events & errors**: findings (same rules as `EventRecorder.LapFindings`) and every recorded event with severity and time.
- **Coach**: the coach's read of the moment (`LapCholeNarrator.LiveStatus()`), the transcript of coach captions and sim feedback lines, the trainee's mic level, and instructor notes (stored with the attempt, not sent to the headset).

## Data model (dashboard/spacetimedb)

`live_tables.ts` defines the tables; the reducers are at the bottom of `src/index.ts`. Frames and events are sent as one JSON string argument so the Unity side needs no SpacetimeDB SDK; the module validates, caps and parses it into typed columns. Coordinates are board-local metres (origin at the navel, +x feet, +y up, +z patient's right). Retention: 600 events and 300 captions per session, 24 sessions. The dashboard regenerates bindings with:

```powershell
cd dashboard
spacetime generate --lang typescript --out-dir src/module_bindings --module-path spacetimedb
```

## Fake headset

`node scripts/fake-headset.mjs [--loop] [--sloppy] [--major] [--speed 2] [--session id] [--url http://127.0.0.1:3000] [--db jeremy-ai-live]` publishes a realistic 15-step attempt through the same HTTP reducer calls the Quest uses. `--sloppy` adds a blocked entry, an off-target port, over-insufflation, a crossing clip and a brush past the CBD; `--major` clips the CBD.

## Security

The local database accepts publishes from anyone who can reach port 3000. Keep it on 127.0.0.1 (the default) unless you use `-Lan` on a trusted network. No keys are involved.
