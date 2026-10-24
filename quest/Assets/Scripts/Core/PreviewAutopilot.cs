using System.Collections;
using System.IO;
using UnityEngine;

namespace Jeremy
{
    // Desktop preview demo mode: press Y to watch the active exercise performed automatically.
    // It drives the same desktop poses and grasp flags a mouse user does, so it exercises the real
    // detectors, scoring, uploads and HUD. Also reads optional commands from a file so the editor
    // can be driven from a terminal (see Editor/PreviewBridge.cs).
    public sealed class PreviewAutopilot : MonoBehaviour
    {
        public static string BridgeFolder => Path.Combine(Path.GetTempPath(), "jeremy-bridge");
        public float speed = 0.25f; // metres per second at the tip
        private DesktopPreview preview;
        private PegTransferRig rig;
        private Coroutine run;
        public bool Running => run != null;

        public static void Toggle(DesktopPreview preview, PegTransferRig rig)
        {
            PreviewAutopilot pilot = Ensure(preview, rig);
            if (pilot.Running) pilot.Stop(); else pilot.Play();
        }

        public static PreviewAutopilot Ensure(DesktopPreview preview, PegTransferRig rig)
        {
            var pilot = preview.GetComponent<PreviewAutopilot>();
            if (pilot == null) pilot = preview.gameObject.AddComponent<PreviewAutopilot>();
            pilot.preview = preview; pilot.rig = rig;
            return pilot;
        }

        private bool unsafeNext;
        private IEnumerator RunLapChole(LapCholeProcedure procedure, bool unsafeRun)
        {
            yield return procedure.Autoplay(unsafeRun);
            run = null;
        }

        public void Play()
        {
            if (Running) return;
            preview.externalControl = true;
            if (rig.ExerciseId == "lap_chole")
            {
                preview.externalControl = false;
                LapCholeProcedure procedure = rig.GetComponent<LapCholeProcedure>();
                if (procedure != null) run = StartCoroutine(RunLapChole(procedure, unsafeNext));
                unsafeNext = false;
                return;
            }
            run = StartCoroutine(rig.ExerciseId == "wound_closure" ? WoundClosure() : PegTransfer());
        }

        public void Stop()
        {
            if (run != null) StopCoroutine(run);
            run = null;
            if (preview != null) preview.externalControl = false;
            foreach (InstrumentController tool in rig.Instruments) tool.desktopClosed = false;
        }

        private IEnumerator Begin()
        {
            if (rig.Exercise.State != ExerciseState.Ready) { rig.Exercise.ResetExercise(); yield return null; }
            rig.Exercise.Begin();
            yield return new WaitForSeconds(0.4f);
        }

        private IEnumerator PegTransfer()
        {
            yield return Begin();
            for (int i = 0; i < PegBoard.ObjectCount && rig.Exercise.State == ExerciseState.Running; i++)
            {
                bool right = i % 2 == 1;
                Transform pose = right ? preview.RightPose : preview.LeftPose;
                InstrumentController tool = rig.Instruments[right ? 1 : 0];
                // Specimen retrieval: grasp the stone on the field, lift clear of the bag ring, drop it in the mouth.
                Vector3 source = rig.board.Source(i), target = rig.board.Target(i);
                float rim = PegBoard.BagBottom + PegBoard.BagRingHeight;
                Vector3 mouth = new Vector3(target.x, rim + 0.012f, target.z);
                yield return MoveTo(pose, source + Vector3.up * 0.035f);
                yield return MoveTo(pose, source + Vector3.up * 0.002f);
                tool.desktopClosed = true;
                yield return new WaitForSeconds(0.25f);
                yield return MoveTo(pose, source + Vector3.up * 0.065f);
                yield return MoveTo(pose, new Vector3(target.x, rim + 0.03f, target.z));
                yield return MoveTo(pose, mouth);
                tool.desktopClosed = false;
                yield return new WaitForSeconds(0.35f);
                yield return MoveTo(pose, mouth + Vector3.up * 0.03f);
            }
            Finish();
        }

        private IEnumerator WoundClosure()
        {
            yield return Begin();
            Transform driver = preview.RightPose, forceps = preview.LeftPose;
            InstrumentController driverTool = rig.Instruments[1], forcepsTool = rig.Instruments[0];
            SutureDetector suture = rig.Suture;
            float top = WoundPad.PadTop;
            var rng = new System.Random(7);
            float Jitter() => (float)(rng.NextDouble() * 2 - 1) * 0.0018f; // a human-ish 0-2.5 mm bite error
            for (int i = 0; i < WoundPad.SiteCount && rig.Exercise.State == ExerciseState.Running; i++)
            {
                float z = WoundPad.SiteZ(i);
                // 1. Forceps (left) lift and evert the near edge beside the site, and keep holding it.
                yield return MoveTo(forceps, new Vector3(-0.016f, top + 0.012f, z - 0.002f));
                yield return MoveTo(forceps, new Vector3(-0.0045f, top + 0.0025f, z - 0.002f));
                forcepsTool.desktopClosed = true;
                yield return new WaitForSeconds(0.35f);
                yield return MoveTo(forceps, new Vector3(-0.006f, top + 0.004f, z - 0.002f));
                // 2. Needle driver (right): point onto the left dot and bite in.
                Vector3 entry = WoundPad.Entry(i) + new Vector3(Jitter(), 0, Jitter()), exit = WoundPad.Exit(i) + new Vector3(Jitter(), 0, Jitter());
                yield return MoveTo(driver, entry + Vector3.up * 0.012f);
                yield return MoveTo(driver, entry + Vector3.up * 0.0012f);
                yield return Pulse(driverTool);
                // 3. Rotate through: sweep across the wound (the needle turns in the tissue) and come out at the right dot.
                yield return MoveTo(driver, Vector3.Lerp(entry, exit, 0.5f) + Vector3.up * 0.003f, 0.5f);
                yield return MoveTo(driver, exit + Vector3.up * 0.0012f, 0.5f);
                yield return Pulse(driverTool);
                forcepsTool.desktopClosed = false;
                yield return MoveTo(forceps, new Vector3(-0.03f, top + 0.02f, z - 0.02f));
                // 4. Instrument tie: hold the driver closed and circle the stitch twice, then let go.
                if (suture != null && suture.Phase == SuturePhase.Knot)
                {
                    Vector3 centre = new Vector3(0.002f, top + 0.006f, z);
                    yield return MoveTo(driver, centre + new Vector3(0.012f, 0, 0));
                    driverTool.desktopClosed = true;
                    for (float t = 0, timeout = 6f; suture.Phase == SuturePhase.Knot && t < timeout; t += Time.deltaTime)
                    {
                        float a = t / 0.75f * Mathf.PI * 2;
                        driver.position = rig.board.transform.TransformPoint(centre + new Vector3(Mathf.Cos(a) * 0.012f, 0.002f * Mathf.Sin(a * 0.5f), Mathf.Sin(a) * 0.012f));
                        yield return null;
                    }
                    driverTool.desktopClosed = false;
                }
                yield return MoveTo(driver, exit + new Vector3(0.012f, 0.02f, -0.01f));
                yield return new WaitForSeconds(0.6f);
            }
            Finish();
        }

        private static IEnumerator Pulse(InstrumentController tool)
        {
            tool.desktopClosed = true;
            yield return new WaitForSeconds(0.2f);
            tool.desktopClosed = false;
            yield return new WaitForSeconds(0.15f);
        }

        // Board-local target; eased so the motion reads like a hand, which also gives realistic path metrics.
        private IEnumerator MoveTo(Transform pose, Vector3 local, float slow = 1f)
        {
            Vector3 start = pose.position, end = rig.board.transform.TransformPoint(local);
            float duration = Mathf.Max(0.15f, Vector3.Distance(start, end) / Mathf.Max(0.02f, speed * slow));
            for (float t = 0; t < duration; t += Time.deltaTime)
            {
                float u = Mathf.SmoothStep(0, 1, t / duration);
                pose.position = Vector3.Lerp(start, end, u);
                yield return null;
            }
            pose.position = end;
        }

        private void Finish()
        {
            run = null;
            preview.externalControl = false;
        }

        // ---- terminal bridge: one command per line in jeremy-bridge/runtime.txt ----
        private float nextPoll;
        private void Update()
        {
            if (Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + 0.25f;
            string file = Path.Combine(BridgeFolder, "runtime.txt");
            if (!File.Exists(file)) return;
            string[] lines;
            try { lines = File.ReadAllLines(file); File.Delete(file); } catch (IOException) { return; }
            foreach (string raw in lines) Execute(raw.Trim());
        }

        private void Execute(string line)
        {
            if (line.Length == 0) return;
            string[] parts = line.Split(new[] { ' ' }, 2);
            string arg = parts.Length > 1 ? parts[1] : "";
            switch (parts[0])
            {
                case "autopilot": unsafeNext = arg == "unsafe"; Play(); break;
                case "stop": Stop(); break;
                case "exercise": rig.SetExercise(arg == "2" || arg == "wound_closure" ? "wound_closure" : arg == "3" || arg == "lap_chole" ? "lap_chole" : "peg_transfer"); break;
                case "reset": rig.Exercise.ResetExercise(); break;
                case "camera": { OperatingRoom room = FindFirstObjectByType<OperatingRoom>(); if (room != null) room.ApplyCameraPreset(arg); } break; // lap chole presets
                case "view": { OperatingRoom room = FindFirstObjectByType<OperatingRoom>(); if (room != null) room.ApplyFreeView(arg); } break; // "x y z tx ty tz fov", board-local
                case "shot": { OperatingRoom room = FindFirstObjectByType<OperatingRoom>(); if (room != null) room.Monitor.CaptureMainView(arg, 854, 480); } break; // main view, no HUD
                case "start": rig.Exercise.Begin(); break;
                case "coach": { CoachPanel coach = rig.GetComponent<CoachPanel>(); if (coach != null) coach.RequestCoaching(); } break;
                case "screenshot":
                    string path = arg.Length > 0 ? arg : Path.Combine(BridgeFolder, "shot.png");
                    ScreenCapture.CaptureScreenshot(path);
                    break;
                case "status":
                    File.WriteAllText(Path.Combine(BridgeFolder, "status.txt"),
                        "exercise=" + rig.ExerciseId + " state=" + rig.Exercise.State + " autopilot=" + Running);
                    break;
            }
        }
    }
}
