# 150-second infomercial pitch

Written for: the Jeremy.ai team and judges at CalHacks 13. Delivered as a live demo with a recorded backup.

Tone: infomercial energy in the delivery, straight facts in the claims. Say "illustrative" for every dollar figure and "practice score" for the rubric. Do not say "clinically validated", and do not say a sponsor integration works unless its row in [SPONSOR_EVIDENCE.md](SPONSOR_EVIDENCE.md) is verified.

## The setup that makes it fit: split screen

Run the headset view on the left and the instructor dashboard on the right for the whole demo. Every sponsor feature then appears on screen without a cut.

## Run of show (2:30)

| Time | Beat | Words |
|---|---|---|
| 0:00–0:10 | **Pain** | "Every surgeon learned on somebody's body. Tired of practicing on people, and guessing how you did?" |
| 0:10–0:22 | **Proof** | "We asked nine medical and bio people. Nine out of nine said yes. They wanted feedback they can trust and a coach they can ask." (show `survey-interest.svg`) |
| 0:22–0:30 | **Reveal** | "Jeremy.ai. A gallbladder surgery lab on your desk. Quest 3, bare hands, nobody at risk." |
| 0:30–1:55 | **Watch this** | The eight beats below, about 10 seconds each. |
| 1:55–2:10 | **Why trust it** | "The score is plain code. Same attempt, same score. Claude only explains. If any service fails, the app falls back and says so." |
| 2:10–2:30 | **Close** | "More reps. Nobody hurt. Know exactly why. Jeremy.ai: jeremywantstobeasurgeon.us." Team names on screen. |

## Demo beats (0:30–1:55): functionality and every track

Each beat shows one working feature and names the sponsor in a few words.

| # | Time | What you do | What the judge sees | Track |
|---|---|---|---|---|
| 1 | 0:30 | Put on the headset, pinch a tool off the tray with either hand. Say "Normal or Demo mode, tool in either hand." | Passthrough room, hand tracking, Normal/Demo toggle | **Meta Quest** |
| 2 | 0:40 | Step 2: pinch and hold, release in the green band. | The abdomen domes, the gauge fills, and the step turns green | Functionality |
| 3 | 0:50 | The narrator speaks a line. Let it play with no talking over it. | A robot guide with a subtitle and spoken audio | **Cartesia** (TTS) |
| 4 | 1:00 | Jump to a finished session. Read three numbers: steps, major errors, practice score. | Scorecard from the deterministic scorer | Deterministic scoring |
| 5 | 1:10 | Ask by voice: "Why did I lose points?" | Transcript, then a Claude answer, then the answer spoken | **Cartesia** (STT + TTS), **Anthropic** |
| 6 | 1:25 | On the dashboard, drop a note and click highlight. | The note arrives in the headset within a second | **Convex** |
| 7 | 1:35 | Open the replay with errors highlighted, then the mistake-cost report. | Itemized dollars with sources and the "assumed risk" flag | Cost model |
| 8 | 1:45 | Point at the price rows: "these supply prices come from an Apify Actor". | Source URL and retrieved date on each price | **Apify** |

Only do a beat live if you have run it on the exact build you are showing. Anything you have not run goes in as a recording, and you say "here's the recording." As of the last check these were not verified end to end: voice questions from the headset, Convex with a real session, the Apify refresh feeding the cost model, and the headset-to-API round trip.

## Judge questions and answers

Answers use only what the repo proves. The status column says how safe the answer is.

| Question | Answer | Safe? |
|---|---|---|
| **Is it clinically validated?** | "No. The practice score is a fixed rubric we wrote. Thresholds are prototype values. A validation study with a training program is the next step." | Yes, true. |
| **Does the AI grade you?** | "No. Scoring is plain Python, same input, same score. Claude only explains, from numbers the backend computed." | Yes. Point to `backend/scoring.py`. |
| **What if the AI makes something up?** | "Output is schema-checked, every cited event is verified against the real attempt, and spoken lines are rejected if they contain numbers we didn't supply. If it fails, a rule-based coach answers." | Yes, tested with mocked calls. The live Claude path is not verified, so don't say you tested it live. |
| **How is it different from existing VR surgical simulators?** | "The big lap simulators are lab equipment in a $50–100k price tier. Osso VR is on Quest but sold as enterprise contracts and mostly orthopedic. Touch Surgery is free but it's a phone app with no hands. We're the gallbladder procedure on a $499 headset, with your hands, a score you can audit, a coach you can ask out loud, and an instructor watching live." | Yes, with the sourcing in [Competitors](#competitors). |
| **Isn't this just Osso VR?** | "Osso is the leader, with validation studies and hundreds of programs. Its public library is mostly orthopedics and its prices aren't published. We focus on one general-surgery procedure, an explainable score, a voice coach and a live instructor link. They're a company; we're a 36-hour prototype." | Yes. Concede their strengths first. |
| **Why not just use Touch Surgery? It's free.** | "Touch Surgery is great for learning the steps, and it's validated for this exact operation. But you tap a phone. Here your hands do the motion, we measure it, and an instructor can watch." | Yes. |
| **Does simulation even help?** | "SAGES summarizes a trial where VR-trained residents were 29% faster in a real gallbladder operation and five times less likely to injure the gallbladder or burn the wrong tissue. We don't claim our sim does that yet." | Cite SAGES (Seymour et al.). Don't claim the result for Jeremy.ai. |
| **Has a surgeon reviewed it?** | "Not yet. We surveyed nine medical and bio people. A surgeon review of the anatomy and steps is the next step." | Yes, true. Don't overstate the survey. |
| **What does it cost to run?** | "A Quest 3 is $499 retail. The backend runs on a free tier today. Per-seat pricing isn't decided." | Yes. Headset price per Meta's list price. |
| **Why should I trust the dollar figures?** | "Every rate has a citation: OR minutes from *JAMA Surgery*, supply prices from product pages. One number, the chance a simulated error becomes a real event, is our assumption and is flagged on screen. All figures are illustrative." | Yes. See [COST_DATA.md](COST_DATA.md). |
| **Is the anatomy accurate?** | "It's stylized, modeled on a standard procedure animation. Accuracy was the second request in our survey, so a surgeon review is on the roadmap." | Yes, honest. |
| **No haptics. Does it feel like surgery?** | "No force feedback. It trains the sequence, the safe placement and the economy of motion, not the feel of tissue. A foam torso under passthrough is the cheap next step." | Yes. |
| **Does it work offline?** | "The simulation runs on the headset. Uploads go to the API." | Careful. Offline upload buffering is not verified on a device. |
| **What about privacy?** | "We store structured events only. No headset video, no patient data, and no microphone recordings. Keys stay on the server." | Yes. Voice audio is not kept. |
| **Who pays, and what's the business model?** | "Medical schools and residency programs buying seats, and individual trainees as a low-cost option. We surveyed nine people and haven't validated pricing yet." | Be upfront that it's unvalidated. |
| **Why is instructor access open?** | "A single shared key gates the API, and instructor routes don't check identity yet. That's a known gap. Per-session invite tokens are designed but not built." | Yes, say it first if asked about security. |
| **Why these sponsors?** | "Cartesia is the voice, Claude is the tutor, Convex is the live instructor view, Apify keeps the cost inputs sourced, and Quest is the platform." | Yes. |
| **What's real and what's simulated in this demo?** | Say it unprompted at the start: "The headset sim is real, the session you see scored was recorded earlier, and every dollar figure is illustrative." | Yes. |
| **Does it scale?** | "It's demo-grade: Render's free tier and SQLite. The code supports Postgres through `DATABASE_URL`, which we haven't run." | Yes. |
| **How do you know the score reflects real skill?** | "We don't yet. We know it's reproducible and explainable. Whether it predicts real performance is what a study would test." | Yes. |
| **What would you build next?** | "Side-by-side technique video, virtual patients with anatomical variation, and attending sign-off. Those were the top survey requests we didn't build." | Yes. |

## Competitors

Checked 2026-10-25 with web search. Prices are list prices or third-party estimates, not quotes. Say "about" or "in the tier of" and never say a competitor price as fact.

| Product | What it is | Price signal | Their strength | Where we differ |
|---|---|---|---|---|
| **LapSim / LAP Mentor** (Surgical Science) | Full laparoscopic VR rig with instruments and a cart | "Price on request". HealthySimulation puts LapSim ST in the $50,001–$100,000 tier | Haptics, real instruments, long validation record | We're a headset and hands, not lab equipment. No haptics on our side |
| **LAP-X VR** (Medical-X) | Lower-cost laparoscopic VR trainer | $13,844 list at GTSimulators | Real instrument handles | Ours costs one headset. No physical instruments on our side |
| **Box trainers** (Laparo, Simulab, EndoSim) | Physical box with a camera and real tools | About $1,000–$4,000 list | Real tools and real tactile feel | They train hand skills, but don't explain your score or connect an instructor live |
| **Osso VR** | VR surgical training on Quest with hand tracking | Enterprise contracts, prices unpublished. Third-party estimates vary widely | Market leader, validation studies, hundreds of programs, device-maker content | Library is mostly orthopedics. We focus on lap chole with a voice coach and a live instructor |
| **FundamentalXR** (formerly FundamentalVR) | VR with haptic hardware | Haptic hardware adds cost | Physical feel through haptics | We need no extra hardware |
| **PrecisionOS** | VR orthopedic training | Third-party estimates only | Large orthopedic library | Different specialty |
| **Touch Surgery** (Medtronic) | Free phone app for procedure steps | Free | Validated for lap chole (*Surgical Endoscopy*), very large library | Phone taps, no hand motion, no instructor link |
| **LapBot Safe Chole** | Free phone game with an AI that marks safe and unsafe dissection zones | Free | Validated AI feedback on safe dissection | Phone only, no hands |

**Our honest position:** "Not the most realistic and not the most validated. The cheapest way to put your own hands through a gallbladder operation, with a score you can audit, a coach you can ask, and an instructor who can watch from anywhere."

**Where competitors beat us, so say it before a judge does:** haptics and real instruments (LapSim, box trainers), validation evidence (Touch Surgery, Osso, LapSim), and breadth of procedures (Osso, Touch Surgery).

Sources: [HealthySimulation laparoscopic simulators](https://www.healthysimulation.com/laparoscopic-simulator/), [GTSimulators LAP-X VR](https://www.gtsimulators.com/products/lap-x-vr-laparoscopic-simulator-mxfp0901), [GTSimulators laparoscopic trainers](https://www.gtsimulators.com/collections/laparo-simulators), [Reality Atlas: VR platforms for surgical training](https://www.reality-atlas.com/blog/best-vr-platforms-surgical-training), [MedAI Verdict on Osso VR](https://medaiverdict.com/tools/osso-vr), [Medtronic Touch Surgery](https://www.medtronic.com/en-us/healthcare-professionals/specialties/touch-surgery/simulations.html), [Touch Surgery lap chole validation](https://www.researchgate.net/publication/314455179_Validation_of_the_mobile_serious_game_application_Touch_Surgery_for_cognitive_training_and_assessment_of_laparoscopic_cholecystectomy), [LapBot Safe Chole](https://link.springer.com/article/10.1007/s00464-024-11068-3), [SAGES value assessment](https://www.sages.org/publications/tavac/surgical-simulation-value-individualization/), [Meta Quest 3S (Wikipedia)](https://en.wikipedia.org/wiki/Meta_Quest_3S).

## Backup plan

- **Record the whole run first.** One take with the headset view and the dashboard side by side. Any beat that fails live gets swapped for the recording.
- **If the headset fails:** use the Unity desktop preview with mouse and keyboard (hold Space for CO2). Say it is the desktop preview.
- **If the API sleeps** (Render's free tier takes about 50 seconds): open `/health` two minutes before you start.
- **If Claude is unreachable:** the rule-based coach answers. Say "that's the fallback, and it's by design."

## Lines to avoid

- "Clinically validated" or "proven to improve surgical skill."
- "Gemini" anywhere. The tutor is Claude (Haiku 5.5).
- Any dollar figure without "illustrative".
- "Live" for anything you're replaying from a recording.
- A sponsor "works" unless you ran it on this build.

## Rehearsal checklist

- [ ] Time it with a stopwatch. The target is 2:30, and 2:15 is better.
- [ ] Land the green band on step 2 three times in a row on Normal and on Demo.
- [ ] Rehearse the spoken question until the transcript is right on the first try, in a loud room.
- [ ] Confirm the headset audio is loud enough for the room.
- [ ] Have each answer in the table ready in one sentence.
- [ ] Keep the recording one click away.
