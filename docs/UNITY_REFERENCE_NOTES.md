# Unity reference notes

Shallow clones (`--depth 1`, `GIT_LFS_SKIP_SMUDGE=1`) are in `C:/Users/shawn/refs`, outside this repo. Read this file instead of the clones. `zajg/laparoscopic-simulator` (GPL-3.0) was not cloned. Copy a file into `quest/` only when the license below allows it, and keep that notice with the copy.

## Unity-StarterSamples

https://github.com/oculus-samples/Unity-StarterSamples
License: MIT (`LICENSE`, Meta Platforms).

- `Assets/StarterSamples/Usage/Passthrough/Scenes/EnableDisablePassthrough.unity`
- `Assets/StarterSamples/Usage/Passthrough/Scenes/PassthroughAtStartup.unity`
- `Assets/StarterSamples/Usage/Passthrough/Scripts/EnableDisablePassthroughController.cs`
- `Assets/StarterSamples/Usage/Passthrough/Scripts/PassthroughAtStartupController.cs`

Button One toggles MR and VR. MR sets `OVRManager.instance.isInsightPassthroughEnabled`, enables `OVRPassthroughLayer`, and fades renderer `_InvertedAlpha` from 0 to 1. VR fades to 0, then disables the layer and Insight. If the resumed toggle is on, the fade waits for `passthroughLayerResumed`. On device, startup enables the layer only when `OVRManager.IsPassthroughRecommended()` is true, so a launch from VR home stays black. That is the opaque fallback. The peg board stays an ordinary object in front of the layer.

## Unity-InteractionSDK-Samples

https://github.com/oculus-samples/Unity-InteractionSDK-Samples
License: Meta Platform Technologies SDK License (`LICENSE.txt`). These scripts are not MIT. Do not copy them in.

- `Assets/ShowcaseSamples/MRTabletop/Scenes/MRGamePortal.unity`
- `Assets/ShowcaseSamples/MRTabletop/Scripts/Interaction/ControllerHandGrabButtonOverrides.cs`
- `Assets/ShowcaseSamples/MRTabletop/Scripts/Interaction/ConstrainedTransformer.cs`
- `Assets/ShowcaseSamples/MRTabletop/Scripts/Tabletop/TabletopSpawnPose.cs`
- `Assets/ShowcaseSamples/MRTabletop/Scripts/Tabletop/TabletopGrabInertia.cs`

Palm grabs follow the controller grip and pinch grabs follow the trigger; both overrides clear on disable. `TabletopSpawnPose` waits until the center-eye anchor moves, places the target in front of the head, and repeats that on `OVRManager.display.RecenteredPose`. `ConstrainedTransformer` clamps a grab to `maxDistance` and filters pose jitter. `TabletopGrabInertia` slides a release on XZ with linear drag and a local distance cap. Our rig already grabs by trigger radius. Borrow the in-front-of-head placement and the button-edge select. Leave the ISDK components out.

## Unity-Discover

https://github.com/oculus-samples/Unity-Discover
License: MIT (`LICENSE` and `Assets/Discover/LICENSE`). `Assets/Photon/LICENSE` is Exit Games. Leave Photon out.

- `Assets/Discover/Scenes/Examples/SimpleMRScene.unity`
- `Documentation/ProjectStructure.md`
- `Assets/Discover/Scripts/SpatialAnchors/SpatialAnchorManager.cs`
- `Assets/Discover/Scripts/AppsManager.cs`

Passthrough here is scene and rig setup. `SimpleMRScene` is the documented MR startup scene and needs Unity 6000.0.59f2 or newer. `SpatialAnchorManager.SaveAnchor` waits until `OVRSpatialAnchor.Uuid` is set, calls `SaveAnchorAsync`, and stores the uuid. `AppsManager` adds an `OVRSpatialAnchor` when the user places an icon. That is how to lock the peg board in the room. Our board only recenters on the head and does not persist an anchor.

## mr-example-meta-openxr

https://github.com/Unity-Technologies/mr-example-meta-openxr
License: Unity Companion License (`LICENSE.md`). `README.md` says plane detection and passthrough appear only in an Android build, not in editor Play.

- `Assets/Scenes/SampleScene.unity`
- `Assets/MRExampleAssets/Scripts/GoalManager.cs`
- `Assets/MRExampleAssets/Scripts/PlaneTrackingModeVisualizer.cs`
- `Assets/Samples/XR Interaction Toolkit/2.5.1/Hands Interaction Demo/HandsDemoSceneAssets/Scripts/ObjectResetPlane.cs`

The sample scene's MR Interaction Setup owns the XR camera, controller and hand interactors, one `ARSession`, and an `ARPlaneManager`. After the headset Room Setup, the plane classes that classify are Bed, Table, and Floor. `GoalManager` steps through FindSurfaces and TapSurface, then enables the plane manager. `ObjectResetPlane` stores each start pose and, every `m_CheckDuration` seconds, snaps anything below the plane back and zeroes the rigidbody. Our detector already returns a missed peg to its source without physics.

## MultiInteractionExample2026

https://github.com/ugavrclass2026/MultiInteractionExample2026
No root `LICENSE`. `Assets/Scripts/Hand.cs` and `Grabbable.cs` have no license header. Do not copy them. Rocketbox and controller models under `Assets/AvatarDemo` and `Assets/VirtualPerson` are MIT.

- `Assets/Scripts/Hand.cs`
- `Assets/Scripts/Grabbable.cs`
- `README.md`

Quest 3, Unity 6000.3, OpenXR, Input System, no Interaction SDK. A grip press on the nearest `Grabbable` inside `grabRadius` (0.1 m) grabs it. A second hand's grip steals it by releasing the first. `FixedUpdate` drives the rigidbody to a hold target. Release keeps the fastest velocity sample from the last `releaseWindow` (0.03 s). Highlight uses a refcount so two hands can hover one object. Our instruments already use a radius and one owner. The pattern to keep is the press edge, not a held button, and one owner at a time.

## XR-CV-Forceps-Tracking-Unity

https://github.com/WestCoastGod/XR-CV-Forceps-Tracking-Unity
License: MIT (`LICENSE`, Copyright 2025 CUHK_FYP_BaichuanZeng). Keep that notice if a script is copied. Leave the ArUco tracker out.

- `Assets/Scripts/ForcepsController.cs`
- `Assets/Scripts/CustomXRDirectInteractor.cs`
- `Assets/Scripts/UpperClamp.cs`
- `Assets/Scripts/LowerClamp.cs`

Grip closes both jaws. Grip release calls `ReleaseGrabbedObjects`, which `SelectExit`s each `XRGrabInteractable`. `TryGrabObject` selects only an object that is already inside a jaw trigger and has `XRGrabInteractable`. A 0.5 s cooldown blocks an immediate re-grab. `GetAttachTransform` picks the closest of several attach points and keeps that point until release. Our tip is one 0.12 m offset. A jaw attach locked at grasp is the piece to adapt.

## LapSim-AI

https://github.com/Mbjaradat/LapSim-AI
License: Apache-2.0 (`LICENSE`). The webcam and vision stack are not needed.

- `src/lapsim_ai/simulator/tasks/peg_transfer.py`
- `src/lapsim_ai/simulator/grasp.py`
- `blender/scripts/peg_config.py`

`GraspWorld` captures on a close edge (`jaw <= 0.25` after a more open jaw) within 9 mm, keeps the grasp offset, and releases at jaw `>= 0.65`. One owner per object. `_place` marks CORRECT only when the release is within 6 mm radius and 12 mm height of that object's own target and the seat is empty. A source seat is a return. Anything else is DROPPED onto `seat_height` at a free XY, with no bounce. The task starts on the first hold and completes when every object is CORRECT. `peg_config.py` is metres: board 106 x 86 x 8 mm, peg radius 1.8 mm, peg height 8 mm, six rings in two columns of three. Our target radius is 27 mm and our API kinds stay `transfer_success`, `drop`, and `invalid_release`.

## VR-LAPSSIM-SIMULATOR

https://github.com/ahmadcr17dev/VR-LAPSSIM-SIMULATOR
No `LICENSE` file. `README.md` limits use to education and research and asks for contact before commercial use. Do not copy the scripts.

- `Assets/Scripts/Forcep1Snap.cs`
- `Assets/Scripts/ForcepDesktop.cs`
- `Assets/Scripts/ScoreManager.cs`

This is an appendectomy. `Forcep1Snap` locks the tool when the shaft tip is within 5 cm of a port and the grab has been released: the body becomes kinematic and the grab component is disabled. `ForcepsDesktop` pivots a shaft between `minDepth` 0.02 m and `maxDepth` 0.15 m. `LockForcepsAfterGrab` zeroes move, rotation, and depth. `ScoreManager` ignores points until `OnProcedureStarted`. The useful pattern is a port pivot with a depth clamp, and a score that starts only after the task starts. Our samples already run only while the exercise is Running.

## OpenTrainer

https://github.com/BME-Open-Index/OpenTrainer
License: CC BY-SA 4.0 (`LICENSE.txt`). The trocar in `Appendix B - Design Files/OpenTrainer/3D Print/Trochar/LICENSE.txt` is CC BY-NC-SA 4.0. Leave that trocar out. Do not commit the CAD. ShareAlike applies if the peg model itself is adapted.

- `Appendix B - Design Files/Training Modules/Task I - Peg Transfers/Peg Transfers Module.step`
- `Appendix B - Design Files/Training Modules/Task I - Peg Transfers/Peg.stl`

The STEP file declares `SI_UNIT(.MILLI.,.METRE.)`. Its 5344 cartesian points span 155.7 mm in X, 93.1 mm in Z, and 23.0 mm in Y. `Peg.stl` (2894 triangles) spans about 21.6 mm, 20.3 mm, and 15.0 mm on X, Z, and Y. Our runtime board is 0.38 x 0.44 m with peg centers 65 mm apart, which is a demo layout. Attribute OpenTrainer if those measured sizes are used.

## Leave out

ArUco, webcam hands, Photon, Interaction SDK package code, and the GPL laparoscopic simulator.
