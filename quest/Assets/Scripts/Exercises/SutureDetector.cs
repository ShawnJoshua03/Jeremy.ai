using System;
using UnityEngine;

namespace Jeremy
{
    public enum SuturePhase { Idle, Entry, Exit, Knot, Done }

    /// <summary>UX feedback raised by SutureDetector (not backend events; those go through EventRecorder).</summary>
    public struct SutureFeedback
    {
        public enum Kind { Everted, EntryTaken, Placed, Tied, Missed, TooFar, Contact }
        public Kind kind;
        public int site;
        public float errorMm;     // bite error of the stitch (Placed/Tied) or distance to the mark (Missed/TooFar)
        public Vector3 local;     // board-local point to anchor a popup
        public string reason;
    }

    // Wound closure rules (docs/WOUND_CLOSURE.md). Right instrument = needle driver, left = forceps.
    // A stitch: close the needle driver on a site's entry mark, then on that site's exit mark within the timeout
    // (that records stitch_placed). With tieKnots on, the trainee then ties an instrument knot (hold the driver
    // closed and circle it around the stitch) before the next bite; the knot is visual/UX only. Closing the
    // forceps on the left wound edge everts it (visual + acknowledgement only).
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
        [Tooltip("Require an instrument knot after each bite before the next one (set by SutureGuide at runtime).")]
        public bool tieKnots;
        [Tooltip("Seconds of holding the driver closed that tie a knot without circling.")]
        public float knotHoldSeconds = 1.6f;
        /// <summary>Site index whose entry bite is taken and exit is awaited, or -1.</summary>
        public int PendingSite { get; private set; } = -1;
        public SuturePhase Phase { get; private set; } = SuturePhase.Idle;
        /// <summary>Site whose knot is being tied (Knot phase), or -1.</summary>
        public int KnotSite { get; private set; } = -1;
        public float KnotProgress { get; private set; }
        /// <summary>Instrument-tie loops thrown so far (0..2), for the thread animation.</summary>
        public float KnotTurns { get; private set; }
        /// <summary>Forceps closed on the left wound edge near the current site this frame.</summary>
        public bool EdgeGrasped { get; private set; }
        /// <summary>The current stitch had its edge everted at some point.</summary>
        public bool Everted { get; private set; }
        public Vector3 EntryHit => entryHit;
        public Vector3 ExitHit { get; private set; }
        public float ExitSecondsLeft => PendingSite < 0 || exercise == null ? 0 : Mathf.Max(0, exitTimeout - (float)(exercise.ElapsedSeconds - entryTime));
        /// <summary>The site the trainee should work on now (lowest open site in the Entry phase).</summary>
        public int CurrentSite => Phase == SuturePhase.Exit ? PendingSite : Phase == SuturePhase.Knot ? KnotSite : NextOpenSite();
        public readonly float[] StitchErrorMm = new float[WoundPad.SiteCount];
        public event Action<SutureFeedback> Feedback;
        private Vector3 entryHit;
        private double entryTime;
        private readonly bool[] previousClosed = new bool[2], inContact = new bool[2];
        private readonly float[] lastContact = { -999f, -999f };
        private float knotAngle;
        private bool knotAngleValid;

        private void Start()
        {
            // Runtime UX: needle, thread, guidance, knots. Verification drives Bite() directly without it.
            if (GetComponent<SutureGuide>() == null) gameObject.AddComponent<SutureGuide>();
        }

        public void ResetDetector()
        {
            PendingSite = -1; KnotSite = -1; KnotProgress = 0; KnotTurns = 0;
            Phase = SuturePhase.Idle; EdgeGrasped = false; Everted = false; knotAngleValid = false;
            for (int i = 0; i < WoundPad.SiteCount; i++) StitchErrorMm[i] = -1;
            for (int i = 0; i < 2; i++) { inContact[i] = false; lastContact[i] = -999f; }
            if (pad != null) { pad.SetEversion(0, 0); pad.SetGuide(-1, false); }
        }
        private void OnDisable() => ResetDetector();

        private void Update()
        {
            if (exercise == null || board == null || pad == null || recorder == null || instruments == null) return;
            bool running = exercise.State == ExerciseState.Running;
            if (running && Phase == SuturePhase.Idle) Phase = SuturePhase.Entry;
            bool grasped = false;
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
                    if (Phase == SuturePhase.Knot && tieKnots) TieGesture(tool.Closed, local);
                    else if (pressed) Bite(local);
                }
                else if (tool.Closed && NearLeftEdge(local)) grasped = true;
            }
            SetGrasp(grasped && running && (Phase == SuturePhase.Entry || Phase == SuturePhase.Exit));
            if (running && PendingSite >= 0 && exercise.ElapsedSeconds - entryTime > exitTimeout)
            {
                int site = PendingSite;
                PendingSite = -1;
                Phase = SuturePhase.Entry;
                recorder.Record("stitch_missed", "right", WoundPad.SiteId(site), entryHit, Vector3.zero, entryHit, false);
                Raise(SutureFeedback.Kind.Missed, site, -1, WoundPad.Exit(site), "Time ran out before the needle came out");
            }
            pad.SetGuide(running && Phase != SuturePhase.Knot ? CurrentSite : -1, Phase == SuturePhase.Exit);
        }
        private bool NearLeftEdge(Vector3 local)
        {
            int site = CurrentSite;
            float z = site >= 0 ? WoundPad.SiteZ(site) : local.z;
            return local.x < -0.0005f && local.x > -0.016f && Mathf.Abs(local.z - z) < 0.016f
                && Mathf.Abs(local.z) < WoundPad.IncisionHalfLength && local.y < WoundPad.PadTop + 0.02f;
        }
        private void SetGrasp(bool grasped)
        {
            if (grasped && !EdgeGrasped && !Everted)
            {
                Everted = true;
                int site = CurrentSite;
                Raise(SutureFeedback.Kind.Everted, site, 0, new Vector3(-0.004f, WoundPad.PadTop, site >= 0 ? WoundPad.SiteZ(site) : 0), null);
            }
            EdgeGrasped = grasped;
            int current = CurrentSite;
            pad.SetEversion(grasped ? 1 : 0, current >= 0 ? WoundPad.SiteZ(current) : 0);
        }
        private void CheckContact(int slot, string side, Vector3 local)
        {
            if (WoundPad.InExclusionZone(local))
            {
                if (inContact[slot]) return;
                inContact[slot] = true; // one entry into the zone = one event
                if (Time.unscaledTime - lastContact[slot] < contactInterval) return;
                lastContact[slot] = Time.unscaledTime;
                if (recorder.Record("tissue_contact", side, null, local, Vector3.zero, local, false))
                    Raise(SutureFeedback.Kind.Contact, -1, 0, local, side);
            }
            else if (local.y > WoundPad.PadTop + 0.002f || Mathf.Abs(local.x) > WoundPad.ExclusionHalfWidth + 0.002f
                || Mathf.Abs(local.z) > WoundPad.IncisionHalfLength + 0.002f) inContact[slot] = false;
        }
        // Instrument tie: hold the driver closed near the stitch; circling it around the stitch throws the loops
        // faster (two full circles tie it on their own), holding still ties it in knotHoldSeconds.
        private void TieGesture(bool closed, Vector3 local)
        {
            if (KnotSite < 0) return;
            if (!closed) { knotAngleValid = false; return; }
            Vector3 centre = new Vector3(0, WoundPad.PadTop, WoundPad.SiteZ(KnotSite));
            Vector2 offset = new Vector2(local.x - centre.x, local.z - centre.z);
            float gain = Time.deltaTime / Mathf.Max(0.2f, knotHoldSeconds);
            if (offset.magnitude > 0.002f && offset.magnitude < 0.06f)
            {
                float angle = Mathf.Atan2(offset.y, offset.x) * Mathf.Rad2Deg;
                if (knotAngleValid) gain += Mathf.Abs(Mathf.DeltaAngle(knotAngle, angle)) / 720f;
                knotAngle = angle; knotAngleValid = true;
            }
            KnotProgress = Mathf.Clamp01(KnotProgress + gain);
            KnotTurns = Mathf.Min(2, KnotProgress * 2.2f);
            if (KnotProgress >= 1) Tie();
        }
        /// <summary>Finish the knot of the stitch awaiting one (also used by verification and the autopilot).</summary>
        public void Tie()
        {
            if (KnotSite < 0) return;
            int site = KnotSite;
            KnotSite = -1; KnotProgress = 0; KnotTurns = 0; knotAngleValid = false;
            pad.MarkTied(site);
            Phase = recorder.StitchesPlaced >= WoundPad.SiteCount ? SuturePhase.Done : SuturePhase.Entry;
            Everted = false;
            Raise(SutureFeedback.Kind.Tied, site, StitchErrorMm[site], pad.KnotPoint(site), null);
            if (Phase == SuturePhase.Done) exercise.Complete();
        }
        /// <summary>Needle driver closed at a board-local point. Public so verification can drive it without XR input.</summary>
        public void Bite(Vector3 local)
        {
            if (exercise == null || exercise.State != ExerciseState.Running) return;
            if (tieKnots && Phase == SuturePhase.Knot) return;
            if (!WoundPad.OverPad(local) || local.y < WoundPad.PadTop - 0.01f || local.y > WoundPad.PadTop + biteHeightTolerance) return;
            Vector3 hit = new Vector3(local.x, WoundPad.PadTop, local.z); // the bite lands on the skin surface
            if (PendingSite < 0)
            {
                int site = Nearest(hit, out float distance);
                if (site < 0) return;
                if (distance > attemptRadius) { Raise(SutureFeedback.Kind.TooFar, site, distance * 1000, hit, "Aim at the glowing left dot"); return; }
                if (distance <= WoundPad.MaxBite)
                {
                    PendingSite = site; entryHit = hit; entryTime = exercise.ElapsedSeconds;
                    Phase = SuturePhase.Exit;
                    Raise(SutureFeedback.Kind.EntryTaken, site, distance * 1000, hit, null);
                }
                else
                {
                    recorder.Record("stitch_missed", "right", WoundPad.SiteId(site), hit, Vector3.zero, hit, false);
                    Raise(SutureFeedback.Kind.Missed, site, distance * 1000, hit, "Entry too far from the dot");
                }
                return;
            }
            float error = Vector3.Distance(hit, WoundPad.Exit(PendingSite));
            if (error > attemptRadius) { Raise(SutureFeedback.Kind.TooFar, PendingSite, error * 1000, hit, "Come out at the glowing right dot"); return; }
            int pending = PendingSite;
            PendingSite = -1;
            Phase = SuturePhase.Entry;
            if (error > WoundPad.MaxBite)
            {
                recorder.Record("stitch_missed", "right", WoundPad.SiteId(pending), entryHit, hit, hit);
                Raise(SutureFeedback.Kind.Missed, pending, error * 1000, hit, "Exit too far from the dot");
                return;
            }
            if (!recorder.Record("stitch_placed", "right", WoundPad.SiteId(pending), entryHit, hit, hit)) return;
            ExitHit = hit;
            StitchErrorMm[pending] = WoundPad.BiteErrorMm(pending, entryHit, hit);
            pad.MarkPlaced(pending, entryHit, hit);
            Raise(SutureFeedback.Kind.Placed, pending, StitchErrorMm[pending], hit, null);
            KnotSite = pending; KnotProgress = 0; KnotTurns = 0;
            if (tieKnots) { Phase = SuturePhase.Knot; return; }
            Tie();
        }
        private void Raise(SutureFeedback.Kind kind, int site, float error, Vector3 local, string reason) =>
            Feedback?.Invoke(new SutureFeedback { kind = kind, site = site, errorMm = error, local = local, reason = reason });
        private int NextOpenSite()
        {
            if (pad == null) return -1;
            for (int i = 0; i < WoundPad.SiteCount; i++) if (!pad.IsPlaced(i)) return i;
            return -1;
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
