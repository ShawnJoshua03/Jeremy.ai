using UnityEngine;

namespace Jeremy
{
    // Selection lasts for this launch. Attempts freeze their mode, including offline upload retries.
    public static class LapDifficulty
    {
        public enum Mode { Normal, Demo }
        public static Mode Selected { get; private set; }
        public static Mode Current { get; private set; }
        static string attemptId;
        public static float Scale => Current == Mode.Demo ? 2f : 1f;
        // Insufflation climbs slowly enough to stop inside the 12-15 mmHg window (3 mmHg: 1.5 s normal, 3 s demo).
        public static float PressureRate => Current == Mode.Demo ? 1f : 2f;
        // Demo skips the optional intraoperative cholangiogram and the ductotomy it needs (backend: scoring.DEMO_SKIPPED_STEPS).
        public static bool SkipsStep(string id) => Current == Mode.Demo && (id == "ductotomy" || id == "cholangiogram");
        public static string WireValue => Current == Mode.Demo ? "demo" : "normal";
        public static string Label(Mode mode) => mode == Mode.Demo ? "Demo (easier)" : "Normal";
        public static string Summary => Current == Mode.Demo ? "DEMO TOLERANCES" : "Normal";
        public static string CardLabel(bool ready) => ready ? Label(Selected)
            : Label(Current) + (Selected != Current ? " | Next: " + Label(Selected) : "");
        public static void Toggle() => Selected = Selected == Mode.Normal ? Mode.Demo : Mode.Normal;
        public static void BeginAttempt(string id)
        {
            if (attemptId == id) return; // Resume keeps the attempt's original mode.
            attemptId = id;
            Current = Selected;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { Selected = Current = Mode.Normal; attemptId = null; }
    }
}
