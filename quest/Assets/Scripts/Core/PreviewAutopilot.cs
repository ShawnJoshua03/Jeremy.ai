using System.Collections;
using System.IO;
using UnityEngine;

namespace Jeremy
{
    // Desktop preview demo mode: press Y to watch port placement performed automatically.
    // It drives the same desktop poses and grasp flags a mouse user does, so it exercises the real
    // detectors, scoring, uploads and HUD. Also reads optional commands from a file so the editor
    // can be driven from a terminal (see Editor/PreviewBridge.cs).
    public sealed class PreviewAutopilot : MonoBehaviour
    {
        public static string BridgeFolder => Path.Combine(Path.GetTempPath(), "jeremy-bridge");
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
            preview.externalControl = false;
            LapCholeProcedure procedure = rig.GetComponent<LapCholeProcedure>();
            if (procedure != null) run = StartCoroutine(RunLapChole(procedure, unsafeNext));
            unsafeNext = false;
        }

        public void Stop()
        {
            if (run != null) StopCoroutine(run);
            run = null;
            if (preview != null) preview.externalControl = false;
            foreach (InstrumentController tool in rig.Instruments) tool.desktopClosed = false;
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
