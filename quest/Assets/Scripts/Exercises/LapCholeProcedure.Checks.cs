#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEngine;

namespace Jeremy
{
    public sealed partial class LapCholeProcedure
    {
        // Batch entry point: -executeMethod Jeremy.LapCholeProcedure.VerifyControls
        public static void VerifyControls()
        {
            var root = new GameObject("Lap controls checks");
            root.SetActive(false);
            LapCholeKit materials = null;
            LapCholeEntry probe = null;
            LapCholeProcedure proc = null;
            int checks = 0;
            void Check(bool ok, string message)
            {
                if (!ok) throw new Exception(message);
                checks++;
            }
            try
            {
                if (LapDifficulty.Selected != LapDifficulty.Mode.Normal) LapDifficulty.Toggle();
                LapDifficulty.BeginAttempt("normal-check");
                var normal = LapCholeEntry.SpecFor("veress");
                Check(normal.siteTolMm == 15f && normal.angleTolDeg == 35f && normal.minDepth == 22f
                    && normal.maxDepth == 38f && normal.dangerDepth == 45f, "Normal entry defaults changed");
                LapDifficulty.Toggle();
                LapDifficulty.BeginAttempt("normal-check");
                Check(LapDifficulty.WireValue == "normal" && normal.siteTolMm == 15f, "Resume changed attempt difficulty");
                LapDifficulty.BeginAttempt("demo-check");
                Check(normal.siteTolMm == 30f && normal.angleTolDeg == 70f && normal.minDepth == 14f
                    && normal.maxDepth == 46f, "Demo tolerance windows are not doubled");
                var queue = root.AddComponent<UploadQueue>();
                queue.BeginAttempt("buffered-demo", "checks", PegTransferRig.LapCholeId, LapDifficulty.WireValue);
                LapDifficulty.Toggle();
                LapDifficulty.BeginAttempt("next-normal");
                queue.BeginAttempt("buffered-demo", "checks", PegTransferRig.LapCholeId, LapDifficulty.WireValue);
                Check(queue.Find("buffered-demo").difficulty == "demo", "Retry overwrote buffered attempt difficulty");
                string json = JsonUtility.ToJson(queue.Find("buffered-demo"));
                Check(JsonUtility.FromJson<PendingAttempt>(json).difficulty == "demo", "Offline difficulty did not round trip");

                materials = new LapCholeKit();
                probe = new LapCholeEntry(root.transform, materials, (x, z) => 0f);
                bool Enter(float offset)
                {
                    probe.Begin(LapCholeEntry.SpecFor("veress"), Vector3.zero, Vector3.up);
                    probe.Step(new Vector3(offset, 0.15f, 0), Vector3.down, true, 0.02f, out _);
                    bool done = false;
                    for (int k = 0; k < 40; k++)
                        done |= probe.Step(new Vector3(offset, 0.1f, 0), Vector3.down, true, 0.02f, out _);
                    return done;
                }
                Check(!Enter(0.02f) && probe.Blocked, "Normal incorrectly accepted a 20 mm Veress offset");
                LapDifficulty.Toggle(); LapDifficulty.BeginAttempt("demo-entry");
                Check(Enter(0.02f), "Demo did not accept a 20 mm Veress offset");

                var rig = root.AddComponent<PegTransferRig>();
                rig.desktopPreview = false;
                rig.board = root.AddComponent<PegBoard>();
                var state = root.AddComponent<ExerciseStateMachine>();
                typeof(PegTransferRig).GetProperty("Exercise").SetValue(rig, state);
                var recorder = root.AddComponent<EventRecorder>(); recorder.exercise = state; recorder.exerciseId = PegTransferRig.LapCholeId;
                typeof(PegTransferRig).GetProperty("Recorder").SetValue(rig, recorder);
                var left = root.AddComponent<InstrumentController>(); left.instrument = "left";
                var rightObject = new GameObject("Right check"); rightObject.transform.SetParent(root.transform); rightObject.SetActive(false);
                var right = rightObject.AddComponent<InstrumentController>(); right.instrument = "right";
                typeof(PegTransferRig).GetField("instruments", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(rig, new[] { left, right });
                proc = root.AddComponent<LapCholeProcedure>(); proc.rig = rig;
                proc.dressing = root.AddComponent<LapCholeDressing>();
                proc.BuildProps(); proc.EnsureEntry();
                proc.simHands = proc.simTracked = proc.simLeftTracked = true;
                proc.simGrip = proc.simLeftGrip = Vector3.up;
                proc.UpdateToolOwnership();
                proc.simLeftGrip = proc.TraySlot("veress"); proc.simLeftPinch = true;
                proc.UpdateToolOwnership();
                Check(proc.entryOwner == 0 && proc.toolInHand && proc.ActionHand == "left", "Left hand could not pick up entry tool");
                Check(!proc.CanAskCoach(), "Coach accepted tool pickup pinch");
                proc.simGrip = proc.simLeftGrip + Vector3.up * 0.02f; proc.simPinch = true;
                proc.UpdateToolOwnership();
                Check(proc.entryOwner == 1 && proc.inputConsumed[0] && proc.inputConsumed[1], "Entry handover failed or leaked an action");
                proc.simPinch = proc.simLeftPinch = false; proc.UpdateToolOwnership();
                proc.simGrip = proc.TraySlot("veress"); proc.simPinch = true; proc.UpdateToolOwnership();
                Check(proc.entryOwner == -1 && !proc.toolInHand, "Entry tool could not return to tray");

                proc.entry.End(); proc.StepIndex = 6;
                proc.simPinch = proc.simLeftPinch = false; proc.simGrip = proc.simLeftGrip = Vector3.up;
                proc.UpdateToolOwnership();
                proc.simLeftGrip = proc.toolSlots[proc.grasper]; proc.simLeftPinch = true;
                proc.UpdateToolOwnership();
                proc.simGrip = proc.toolSlots[proc.clipper]; proc.simPinch = true;
                proc.UpdateToolOwnership();
                Check(proc.heldTools[0] == proc.grasper && proc.heldTools[1] == proc.clipper, "Two-handed instrument ownership failed");
                proc.TakeTool(0, proc.scissors);
                Check(proc.heldTools[0] == proc.grasper, "A hand acquired two tools");
                proc.StepIndex = 8; proc.UpdateToolOwnership();
                Check(proc.actionHand == 1, "Clip action did not select its actual owner");
                proc.ReturnTool(0);
                proc.simLeftPinch = proc.simPinch = false; proc.simLeftGrip = Vector3.up; proc.UpdateToolOwnership();
                proc.simLeftGrip = proc.simGrip + Vector3.up * 0.02f; proc.simLeftPinch = true;
                proc.UpdateToolOwnership();
                Check(proc.heldTools[0] == proc.clipper && proc.heldTools[1] == null && proc.ActionHand == "left", "Clipper handover retained old owner");
                state.ResetExercise(); state.Begin();
                RecordedEvent captured = null; recorder.Recorded += value => captured = value;
                proc.Complete("check");
                Check(captured != null && captured.instrument == "left", "Recorded step used the wrong hand");
                proc.ReturnTool(0); proc.simLeftGrip = Vector3.up;
                Check(proc.CanAskCoach(), "Empty left hand away from tools cannot ask coach");
                proc.simLeftGrip = proc.toolSlots[proc.clipper];
                Check(!proc.CanAskCoach(), "Empty left hand near a tray tool can ask coach");
                Debug.Log("LAP CONTROLS CHECKS PASSED: " + checks);
            }
            finally
            {
                if (proc != null) { proc.entry?.Destroy(); proc.kit?.Destroy(); }
                probe?.Destroy(); materials?.Destroy();
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
#endif
