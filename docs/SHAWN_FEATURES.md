# Shawn's branch: what it adds

Branch `shawn`, built on Atharv's GallbladderTrainer starter view. The operation is a **laparoscopic cholecystectomy** (gallbladder removal). "Verified" means it was run on 2026-10-24; everything else is marked **not verified**.

## Live coaching while you operate
- **Robot guide that talks to you.** A small holographic robot head floats beside the anatomy, faces you, and speaks short cues in a calm Cartesia voice. Its eyes turn green (on track), amber (slow) or red (off track or critical), and its mouth follows the audio.
- **"How far along should you be?"** For each exercise the backend works out expected progress (target pace 75 s per exercise) against actual progress, and assigns a severity. It's deterministic, with no AI involved. Calling a view "safe" when the critical view of safety is not shown triggers a critical warning, because that call is the classic cause of bile duct injury. Asking the instructor is never penalised.
- **Claude picks the words.** Claude (Haiku 5.5) can only choose between two pre-written sentences grounded in the facts, so it cannot invent a step or a number. The voice always says exactly the subtitle on screen. While nothing changes, no new model call is made.

## Recording, review and report
- **Movement recording.** Head and both hands are recorded at 20 Hz in workstation-local space, so recordings survive re-centering and future model changes.
- **End-of-session report** (`GET /report`). It includes a score (0–100) and grade, one card per exercise (decision vs expected, time to decision, structures inspected, hesitation, review and tip), movement stats (path length, smoothness, idle time), strengths and what to focus on next. Every number is computed deterministically. Claude writes only the prose, and any number it writes that is not in the facts throws its text out in favour of the template.
- **Spoken summary** (`POST /report/audio`). Cartesia reads the summary. Audio generation starts as soon as the report exists, so playback is usually instant.
- **Replay** (`GET /replay`). Recorded hand and head paths come back downsampled to 10 Hz with event markers. The headset shows them as glowing trails, and the dashboard shows a 2D replay with a scrubber.

## What mistakes cost
- **Per-mistake cost.** Each mistake gets an illustrative dollar cost from sourced OR-time, adverse-event and complication figures. Requesting the instructor costs $0.
- **Apify prices.** Supply prices (clip cartridge, trocar) come from a cited price snapshot, and Apify can refresh it.
- **Coinbase x402.** The free summary shows the total. The itemized report costs 0.01 USDC on Base Sepolia testnet and returns HTTP 402 until a payment is verified and settled.
- **Money-count animation.** On the headset report panel and on the dashboard, the total ticks up from $0 as each cost line drops in. The "illustrative, not billing data" disclaimer stays visible.

## The API crew
Five small avatars show which service did what: **Claude** (writes the words), **Cartesia** (the voice), **Apify** (fetched prices), **Coinbase x402** (payment), **Convex** (live sync to the instructor). They animate only when that service actually did the work this session, and they show "fallback", "snapshot" or "off" otherwise. The backend reports this per session (`providers`, `provider_events`).

## Platform changes
- **Gemini removed; Claude replaces it.** All text generation goes through `backend/llm.py` (official Anthropic SDK, `claude-haiku-5-5`, `ANTHROPIC_API_KEY`). The Gemini key was free-tier (20 requests/day) and ran out in testing, and there is no Gemini sponsor track.
- **Frozen contract** for the trainer (`docs/CONTRACT_CVS.md`): stable structure, decision and exercise IDs. Unknown IDs pass through as "not assessed", so Atharv's upcoming procedures plug in without breaking endpoints.
- **Backend cleanup.** Routers use normal imports, and a readable NaN check replaces the old trick.
- **Tests never call paid services.** They clear provider keys, so a developer `.env` no longer makes the suite call Claude, Cartesia, Convex or Apify.
- **Instructor dashboard.** It has a CVS report view, a 2D movement replay, the crew with the money counter, and an x402 "get itemized report" button that shows the payment requirements without faking a payment.
- **Docs.** Testing without a headset, demo script, Cartesia and x402 submission notes.

## Verification status (2026-10-24)
| Piece | Status |
|---|---|
| Backend test suite | See the commit message for the final count |
| Claude narrator lines | Verified live: `provider.text: "claude"`, about 2 s |
| Claude review report | Verified live: generated in about 5.9 s, passed the number guard |
| Cartesia narrator and summary speech | Verified live. The summary took up to about 27 s before the prewarm; the prewarm is not verified live |
| Convex mirror | Verified live (`convex: mirrored`) |
| Dashboard | `npm run build` passes; not opened in a browser |
| Unity (bridge, recorder, guide, report panel, crew) | Written, not compiled in this branch. A cold compile hit errors only in Meta's audio SDK package (`MetaXRAcousticMap.cs`). Not verified on a headset |
| x402 settlement | Not verified (needs `X402_PAY_TO` and a funded testnet wallet) |
| Apify live refresh | Not verified (snapshot only) |
