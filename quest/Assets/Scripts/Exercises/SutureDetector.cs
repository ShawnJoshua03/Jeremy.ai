using UnityEngine;

namespace Jeremy
{
    // Wound closure rules (docs/WOUND_CLOSURE.md). Right instrument = needle driver, left = forceps.
    // A stitch: close the needle driver on a site's entry mark, then on that site's exit mark within the timeout.
    // Runs after InstrumentController so tip poses and closed states are current. Enabled only in wound closure.
    [DefaultExecutionOrder(60)]
    public sealed class SutureDetector : MonoBehaviour
    {
        public ExerciseStateMachine exercise;
        public PegBoard board;
        public WoundPad pad;
        public EventRecorder recorder;
        public InstrumentController[] instruments;
        public float exitTimeout = 10;
        [Tooltip("A close counts as a bite when the tip is this far above the pad top at most (metres).")]
        public float biteHeightTolerance = 0.03f;
        [Tooltip("Closes farther than this from any open mark are ignored instead of counted as a missed bite.")]
        public float attemptRadius = 0.014f;
        public float contactInterval = 0.5f;
        /// <summary>Site index whose entry bite is taken and exit is awaited, or -1.</summary>
        public int PendingSite { get; private set; } = -1;
        private Vector3 entryHit;
        private double entryTime;
        private readonly bool[] previousClosed = new bool[2], inContact = new bool[2];
        private readonly float[] lastContact = { -999f, -999f };

        public void ResetDetector()
        {
            PendingSite = -1;
            for (int i = 0; i < 2; i++) { inContact[i] = false; lastContact[i] = -999f; }
            if (pad != null) pad.HidePending();
        }
        private void OnDisable() => ResetDetector();

        private void Update()
        {
            if (exercise == null || board == null || pad == null || recorder == null || instruments == null) return;
            bool running = exercise.State == ExerciseState.Running;
            for (int i = 0; i < instruments.Length && i < 2; i++)
            {
                InstrumentController tool = instruments[i];
                if (tool == null) continue;
                if (!running || !tool.TrackingValid || tool.tip == null)
                {
                    previousClosed[i] = tool.Closed; // Reacquisition requires a fresh squeeze or pinch.
                    inContact[i] = false;
                    continue;
                }
                Vector3 local = board.transform.InverseTransformPoint(tool.tip.position);
                CheckContact(i, tool.instrument, local);
                bool pressed = tool.Closed && !previousClosed[i];
                previousClosed[i] = tool.Closed;
                if (tool.instrument == "right")
                {
                    if (pressed) Bite(local);
                    if (PendingSite >= 0) pad.ShowPending(entryHit, local);
                }
            }
            if (running && PendingSite >= 0 && exercise.ElapsedSeconds - entryTime > exitTimeout)
            {
                int site = PendingSite;
                PendingSite = -1;
                pad.HidePending();
                recorder.Record("stitch_missed", "right", WoundPad.SiteId(site), entryHit, Vector3.zero, entryHit, false);
            }
        }
        private void CheckContact(int slot, string side, Vector3 local)
        {
            if (WoundPad.InExclusionZone(local))
            {
                if (inContact[slot]) return;
                inContact[slot] = true; // one entry into the zone = one event
                if (Time.unscaledTime - lastContact[slot] < contactInterval) return;
                lastContact[slot] = Time.unscaledTime;
                recorder.Record("tissue_contact", side, null, local, Vector3.zero, local, false);
            }
            else if (local.y > WoundPad.PadTop + 0.002f || Mathf.Abs(local.x) > WoundPad.ExclusionHalfWidth + 0.002f
                || Mathf.Abs(local.z) > WoundPad.IncisionHalfLength + 0.002f) inContact[slot] = false;
        }
        /// <summary>Needle driver closed at a board-local point. Public so verification can drive it without XR input.</summary>
        public void Bite(Vector3 local)
        {
            if (exercise == null || exercise.State != ExerciseState.Running) return;
            if (!WoundPad.OverPad(local) || local.y < WoundPad.PadTop - 0.01f || local.y > WoundPad.PadTop + biteHeightTolerance) return;
            Vector3 hit = new Vector3(local.x, WoundPad.PadTop, local.z); // the bite lands on the skin surface
            if (PendingSite < 0)
            {
                int site = Nearest(hit, out float distance);
                if (site < 0 || distance > attemptRadius) return;
                if (distance <= WoundPad.MaxBite) { PendingSite = site; entryHit = hit; entryTime = exercise.ElapsedSeconds; }
                else recorder.Record("stitch_missed", "right", WoundPad.SiteId(site), hit, Vector3.zero, hit, false);
                return;
            }
            float error = Vector3.Distance(hit, WoundPad.Exit(PendingSite));
            if (error > attemptRadius) return; // not an exit attempt; the timeout still applies
            int pending = PendingSite;
            PendingSite = -1;
            pad.HidePending();
            if (error > WoundPad.MaxBite) { recorder.Record("stitch_missed", "right", WoundPad.SiteId(pending), entryHit, hit, hit); return; }
            if (!recorder.Record("stitch_placed", "right", WoundPad.SiteId(pending), entryHit, hit, hit)) return;
            pad.MarkPlaced(pending, entryHit, hit);
            if (recorder.StitchesPlaced >= WoundPad.SiteCount) exercise.Complete();
        }
        private int Nearest(Vector3 hit, out float distance)
        {
            int best = -1;
            distance = float.MaxValue;
            for (int i = 0; i < WoundPad.SiteCount; i++)
            {
                if (pad.IsPlaced(i)) continue;
                float d = Vector3.Distance(hit, WoundPad.Entry(i));
                if (d < distance) { distance = d; best = i; }
            }
            return best;
        }
    }
}
