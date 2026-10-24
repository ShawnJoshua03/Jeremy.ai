# Jeremy.ai

CalHacks 13. A trainee practices a focused surgical task on a Meta Quest 3, gets contextual voice coaching from an AI tutor, and can invite an instructor to observe the session and give guidance from a browser. At the end, the program measures the cost of the surgery.

## Stack

- **Meta Quest 3**: passthrough training experience
- **TigerData**: time-series storage for motion data
- **Google Gemini API**: structured exercise assessment
- **Claude**: explains performance and recommends practice
- **Moss**: search over training instructions without calling an LLM each time
- **Vultr**: hosting
- **Convex**: realtime sync between headset and browser

## Task list

- [ ] 1. Launch a passthrough Quest experience with a virtual training board anchored in front of the user
- [ ] 2. Grab a virtual surgical instrument with a Quest controller
- [ ] 3. Complete a peg transfer or precision tracing exercise
- [ ] 4. Show quantitative results: path efficiency, time, mistakes, smoothness
- [ ] 5. Replay the entire movement in a browser with errors highlighted
- [ ] 6. Ask Claude why performance suffered and get a targeted practice recommendation
- [ ] 7. Use Gemini to generate a separate structured exercise assessment
- [ ] 8. Search relevant training instructions through Moss, without sending every search to an LLM
- [ ] 9. Let a remote mentor review the session and send a coaching annotation that appears inside the headset
- [ ] 10. Show the cost of the surgery and an itemized breakdown on screen
