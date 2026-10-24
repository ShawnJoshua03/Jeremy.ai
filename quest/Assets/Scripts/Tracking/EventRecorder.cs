using System;
using System.Collections.Generic;
using UnityEngine;

namespace Jeremy
{
    public sealed class EventRecorder : MonoBehaviour
    {
        public ExerciseStateMachine exercise;
        public int SuccessfulTransfers { get; private set; }
        public int Drops { get; private set; }
        public int InvalidReleases { get; private set; }
        public long Sequence { get; private set; }
        public event Action<RecordedEvent> Recorded;
        public event Action<PoseSample> Sampled;
        private readonly HashSet<string> scored = new HashSet<string>();
        private readonly Dictionary<string, PoseSample> previous = new Dictionary<string, PoseSample>();
        private float leftDistance, rightDistance, idealDistance;

        public void ResetRecording()
        {
            SuccessfulTransfers = Drops = InvalidReleases = 0;
            Sequence = 0;
            leftDistance = rightDistance = idealDistance = 0;
            scored.Clear(); previous.Clear();
        }
        public bool Record(string kind, string instrument, string objectId, Vector3 source, Vector3 target, Vector3 position)
        {
            if (exercise.State != ExerciseState.Running || (instrument != "left" && instrument != "right")) return false;
            if (kind != "transfer_success" && kind != "drop" && kind != "invalid_release") return false;
            if (!Coordinates.InRange(source) || !Coordinates.InRange(target) || !Coordinates.InRange(position)) return false;
            if (kind == "transfer_success")
            {
                if (string.IsNullOrEmpty(objectId) || !scored.Add(objectId)) return false;
                SuccessfulTransfers++;
                idealDistance += Vector3.Distance(source, target);
            }
            else if (kind == "drop") Drops++;
            else InvalidReleases++;
            Recorded?.Invoke(new RecordedEvent { eventId = Guid.NewGuid().ToString("N"), sequence = ++Sequence,
                elapsedMs = exercise.ElapsedMs, type = kind, instrument = instrument, objectId = objectId,
                sourceLocal = source, targetLocal = target, positionLocal = position });
            return true;
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
    }
}
