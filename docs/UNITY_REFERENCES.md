# Unity / Quest reference repos

No open-source project combines Quest passthrough, controller-held laparoscopic tools and a peg transfer task, so we build on Meta's samples and borrow ideas from the surgical repos.

## Meta / Quest foundations

| Repo | Use for |
|---|---|
| [oculus-samples/Unity-StarterSamples](https://developers.meta.com/vr/documentation/unity/unity-sample-starter-passthrough/) | Passthrough scene setup and MR/VR fallback toggle (`Assets/StarterSamples/Usage/Passthrough/Scenes/`) |
| [Meta Interaction SDK samples](https://developers.meta.com/horizon/code-samples/unity/) | Controller grab interactions, MR Tabletop sample scene (closest to our board) |
| [oculus-samples/Unity-Discover](https://github.com/oculus-samples/Unity-Discover) | Passthrough, Interaction SDK, spatial anchors (needs Unity 6000.0.59f2+) |
| [Unity-Technologies/mr-example-meta-openxr](https://github.com/Unity-Technologies/mr-example-meta-openxr) | Alternative stack using XR Interaction Toolkit (not Meta's SDK) |
| [ugavrclass2026/MultiInteractionExample2026](https://github.com/ugavrclass2026/MultiInteractionExample2026) | From-scratch controller grab on Quest 3 (Unity 6.3, OpenXR, no ISDK) |

Meta Building Blocks also provide Passthrough, Controller Tracking and Grab Interaction.

## Surgical training references

| Repo | Use for |
|---|---|
| [WestCoastGod/XR-CV-Forceps-Tracking-Unity](https://github.com/WestCoastGod/XR-CV-Forceps-Tracking-Unity) | Closest match: Unity + Quest 3 + laparoscopic forceps + grasping virtual objects (uses ArUco/OpenCV tracking, which we do not need) |
| [Mbjaradat/LapSim-AI](https://github.com/Mbjaradat/LapSim-AI) | A peg transfer task implementation (webcam/web, not Unity); reference for task logic |
| [ahmadcr17dev/VR-LAPSSIM-SIMULATOR](https://github.com/ahmadcr17dev/VR-LAPSSIM-SIMULATOR) | Unity/C# VR laparoscopic simulator (appendectomy) |
| [OpenTrainer](https://github.com/BME-Open-Index/OpenTrainer) | Physical peg transfer dimensions for realistic board layout |

Check each repo's license before copying code. zajg/laparoscopic-simulator is GPL-3.0, so avoid copying from it.
