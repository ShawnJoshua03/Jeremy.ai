using System;
using System.Collections.Generic;
using UnityEngine;

namespace Jeremy
{
    public sealed class EventRecorder : MonoBehaviour
    {
        public ExerciseStateMachine exercise;
        [Tooltip("Active exercise id; set by PegTransferRig. Only that exercise's event kinds are recorded (the backend rejects others).")]
        public string exerciseId = PegTransferRig.PegTransferId;
        // Wound closure reuses the shared counters: transfers = stitches placed, drops = tissue contacts, invalid = missed.
        public int SuccessfulTransfers { get; private set; }
        public int Drops { get; private set; }
        public int InvalidReleases { get; private set; }
        public int StitchesPlaced => SuccessfulTransfers;
        public int TissueContacts => Drops;
        public int StitchesMissed => InvalidReleases;
        public bool WoundClosure => exerciseId == WoundPad.ExerciseId;
        public int TargetCount => WoundClosure ? WoundPad.SiteCount : LapChole ? LapSteps.Length : PegBoard.ObjectCount;
        public long Sequence { get; private set; }
        public event Action<RecordedEvent> Recorded;
        public event Action<PoseSample> Sampled;
        private readonly HashSet<string> scored = new HashSet<string>();
        private readonly Dictionary<string, PoseSample> previous = new Dictionary<string, PoseSample>();
        private readonly List<RecordedEvent> stitches = new List<RecordedEvent>(); // placed, in time order
        private float leftDistance, rightDistance, idealDistance;

        public void ResetRecording()
        {
            SuccessfulTransfers = Drops = InvalidReleases = 0;
            Sequence = 0;
            leftDistance = rightDistance = idealDistance = 0;
            scored.Clear(); previous.Clear(); stitches.Clear(); lapEvents.Clear();
            LapStepsCompleted = LapMajor = LapMinor = 0;
        }
        public bool Record(string kind, string instrument, string objectId, Vector3 source, Vector3 target, Vector3 position, bool hasTarget = true)
        {
            if (exercise.State != ExerciseState.Running || (instrument != "left" && instrument != "right")) return false;
            bool valid = WoundClosure ? kind == "stitch_placed" || kind == "stitch_missed" || kind == "tissue_contact"
                : kind == "transfer_success" || kind == "drop" || kind == "invalid_release";
            if (!valid) return false;
            if (!Coordinates.InRange(source) || !Coordinates.InRange(target) || !Coordinates.InRange(position)) return false;
            if (kind == "transfer_success" || kind == "stitch_placed")
            {
                if (string.IsNullOrEmpty(objectId) || !hasTarget || !scored.Add(objectId)) return false;
                SuccessfulTransfers++;
                if (kind == "transfer_success") idealDistance += Vector3.Distance(source, target);
            }
            else if (kind == "drop" || kind == "tissue_contact") Drops++;
            else InvalidReleases++;
            var value = new RecordedEvent { eventId = Guid.NewGuid().ToString("N"), sequence = ++Sequence,
                elapsedMs = exercise.ElapsedMs, type = kind, instrument = instrument, objectId = objectId,
                sourceLocal = source, targetLocal = target, positionLocal = position, noTarget = !hasTarget };
            if (kind == "stitch_placed") stitches.Add(value);
            Recorded?.Invoke(value);
            return true;
        }
        // ---- port placement (docs/PORT_PLACEMENT.md): the four ports of a laparoscopic cholecystectomy ----
        // The backend's port_placement exercise accepts only these two kinds, so nothing else is recorded or uploaded.
        public bool LapChole => exerciseId == PegTransferRig.PortPlacementId;
        public static readonly string[] LapSteps = { "umbilical", "epigastric", "rsc_medial", "rsc_lateral" };
        private static readonly HashSet<string> LapKinds = new HashSet<string> { "port_placed", "landmark" };
        private readonly List<RecordedEvent> lapEvents = new List<RecordedEvent>();
        public int LapStepsCompleted { get; private set; }
        public int LapMajor { get; private set; }
        public int LapMinor { get; private set; }
        public IReadOnlyList<RecordedEvent> LapEvents => lapEvents;
        /// <summary>Record a port-placement event. source/target null = sent as null; value null = sent as null.</summary>
        public bool RecordLap(string kind, string objectId, string instrument, Vector3? source, Vector3? target, float? value, string detail = null)
        {
            if (exercise.State != ExerciseState.Running || !LapChole || !LapKinds.Contains(kind)) return false;
            if ((source.HasValue && !Coordinates.InRange(source.Value)) || (target.HasValue && !Coordinates.InRange(target.Value))) return false;
            if (value.HasValue && !Coordinates.Finite(value.Value)) return false;
            if (detail != null && detail.Length > 64) detail = detail.Substring(0, 64);
            var e = new RecordedEvent { eventId = Guid.NewGuid().ToString("N"), sequence = ++Sequence, elapsedMs = exercise.ElapsedMs, type = kind,
                instrument = instrument == "left" || instrument == "right" ? instrument : "", objectId = objectId ?? "",
                sourceLocal = source ?? Vector3.zero, targetLocal = target ?? Vector3.zero, positionLocal = source ?? Vector3.zero,
                noSource = !source.HasValue, noTarget = !target.HasValue, hasValue = value.HasValue, value = value ?? 0f, detail = detail ?? "" };
            lapEvents.Add(e);
            LapTally();
            Recorded?.Invoke(e);
            return true;
        }
        // Offline mirror of the backend's site-accuracy finding: a port more than 15 mm or 20 degrees off its ideal site.
        private static bool PortOffTarget(RecordedEvent e) =>
            (!e.noSource && !e.noTarget && Vector3.Distance(e.sourceLocal, e.targetLocal) * 1000 > 15) || (e.hasValue && Mathf.Abs(e.value) > 20);
        private void LapTally()
        {
            var placed = new HashSet<string>();
            var off = new HashSet<string>();
            foreach (RecordedEvent e in lapEvents)
            {
                if (e.type != "port_placed") continue;
                placed.Add(e.objectId);
                if (PortOffTarget(e)) off.Add(e.objectId); else off.Remove(e.objectId); // a retry replaces the earlier placement
            }
            LapStepsCompleted = placed.Count; LapMajor = 0; LapMinor = off.Count;
            SuccessfulTransfers = placed.Count; Drops = 0; InvalidReleases = off.Count;
        }
        // Offline approximation of the backend's port-placement score; the server result replaces it after upload.
        // Major errors (camera first, epigastric vessel zone, blind entry) are judged only by the server.
        private Metrics LapMetrics()
        {
            float completion = Mathf.Clamp01(LapStepsCompleted / (float)LapSteps.Length);
            var last = new Dictionary<string, float>();
            foreach (RecordedEvent e in lapEvents)
            {
                if (e.type != "port_placed") continue;
                float mm = Vector3.Distance(e.sourceLocal, e.targetLocal) * 1000, deg = e.hasValue ? Mathf.Abs(e.value) : 0;
                last[e.objectId] = Mathf.Clamp01(1 - 0.5f * mm / 15 - 0.5f * deg / 20);
            }
            float accuracy = 0;
            foreach (float v in last.Values) accuracy += v / last.Count;
            float score = 100 * (0.55f * completion + 0.35f * accuracy + 0.10f * Mathf.Clamp01(1 - (float)exercise.ElapsedSeconds / 300));
            float distance = leftDistance + rightDistance;
            return new Metrics { exercise = PegTransferRig.PortPlacementId, completion_pct = completion * 100, successful_transfers = LapStepsCompleted,
                drops = LapMajor, invalid_releases = LapMinor, elapsed_s = exercise.ElapsedSeconds, total_distance_m = distance,
                distance_by_instrument = new InstrumentDistances { left = leftDistance, right = rightDistance },
                practice_score = (float)Math.Round(Mathf.Clamp(score, 0, 100), 1) };
        }
        public void RecordSample(PoseSample sample)
        {
            if (!sample.valid) previous.Remove(sample.instrument);
            else
            {
                if (previous.TryGetValue(sample.instrument, out PoseSample last))
                {
                    long delta = sample.t_ms - last.t_ms;
                    if (delta > 0 && delta <= 250)
                    {
                        float distance = Vector3.Distance(ToVector(last.pos), ToVector(sample.pos));
                        if (sample.instrument == "left") leftDistance += distance; else rightDistance += distance;
                    }
                }
                previous[sample.instrument] = sample;
            }
            Sampled?.Invoke(sample);
        }
        private static Vector3 ToVector(float[] value) => new Vector3(value[0], value[1], value[2]);
        public Metrics LocalMetrics()
        {
            if (WoundClosure) return WoundMetrics();
            if (LapChole) return LapMetrics();
            float distance = leftDistance + rightDistance;
            float efficiency = distance > 0 ? Mathf.Clamp01(idealDistance / distance) : 0;
            float completion = Mathf.Clamp01(SuccessfulTransfers / 6f);
            float score = 100 * (0.45f * completion + 0.25f * efficiency
                + 0.20f * Mathf.Clamp01(1 - (float)exercise.ElapsedSeconds / 300)
                + 0.10f * Mathf.Clamp01(1 - (Drops + InvalidReleases) / 6f));
            return new Metrics { completion_pct = completion * 100, successful_transfers = SuccessfulTransfers,
                drops = Drops, invalid_releases = InvalidReleases, elapsed_s = exercise.ElapsedSeconds,
                total_distance_m = distance, distance_by_instrument = new InstrumentDistances { left = leftDistance, right = rightDistance },
                path_efficiency = efficiency, practice_score = (float)Math.Round(score, 1) };
        }
        // Offline mirror of backend scoring.compute_wound_metrics; the server result replaces it after upload.
        private Metrics WoundMetrics()
        {
            float bites = 0, ideal = 0;
            int biteCount = 0;
            var centres = new List<Vector3>();
            for (int i = 0; i < stitches.Count; i++)
            {
                RecordedEvent e = stitches[i];
                if (!WoundPad.TryTargetIndex(e.objectId, out int site)) continue;
                bites += Vector3.Distance(e.sourceLocal, WoundPad.Entry(site)) + Vector3.Distance(e.targetLocal, WoundPad.Exit(site));
                biteCount += 2;
                centres.Add((e.sourceLocal + e.targetLocal) * 0.5f);
                ideal += Vector3.Distance(e.sourceLocal, e.targetLocal);
            }
            float distance = leftDistance + rightDistance;
            int hands = Mathf.Clamp((leftDistance > 0 ? 1 : 0) + (rightDistance > 0 ? 1 : 0), 1, 2);
            for (int i = 1; i < stitches.Count; i++) ideal += hands * Vector3.Distance(stitches[i - 1].targetLocal, stitches[i].sourceLocal);
            float efficiency = distance > 0 ? Mathf.Clamp01(ideal / distance) : 0;
            float meanBite = biteCount > 0 ? bites / biteCount * 1000 : -1;
            centres.Sort((a, b) => a.z.CompareTo(b.z));
            float spacing = -1;
            if (centres.Count >= 3)
            {
                var gaps = new List<float>();
                for (int i = 1; i < centres.Count; i++) gaps.Add(Vector3.Distance(centres[i - 1], centres[i]) * 1000);
                float mean = 0, variance = 0;
                foreach (float gap in gaps) mean += gap / gaps.Count;
                foreach (float gap in gaps) variance += (gap - mean) * (gap - mean) / gaps.Count;
                spacing = Mathf.Sqrt(variance);
            }
            float completion = Mathf.Clamp01(SuccessfulTransfers / (float)WoundPad.SiteCount);
            float accuracy = meanBite >= 0 ? Mathf.Clamp01(1 - meanBite / (WoundPad.MaxBite * 1000)) : 0;
            float score = 100 * (0.40f * completion + 0.20f * accuracy + 0.15f * efficiency
                + 0.15f * Mathf.Clamp01(1 - (float)exercise.ElapsedSeconds / 300)
                + 0.10f * Mathf.Clamp01(1 - (Drops + InvalidReleases) / 5f));
            return new Metrics { exercise = WoundPad.ExerciseId, completion_pct = completion * 100, successful_transfers = SuccessfulTransfers,
                drops = Drops, invalid_releases = InvalidReleases, elapsed_s = exercise.ElapsedSeconds,
                total_distance_m = distance, distance_by_instrument = new InstrumentDistances { left = leftDistance, right = rightDistance },
                path_efficiency = efficiency, practice_score = (float)Math.Round(Mathf.Clamp(score, 0, 100), 1),
                mean_bite_error_mm = meanBite, spacing_sd_mm = spacing };
        }
    }
}
