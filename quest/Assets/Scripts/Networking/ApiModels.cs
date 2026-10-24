using System;
using System.Collections.Generic;
using UnityEngine;

namespace Jeremy
{
    [Serializable] public sealed class SessionCreate { public string trainee_id; public string exercise = "peg_transfer"; }
    [Serializable] public sealed class SessionResponse { public string id; }
    [Serializable] public sealed class CompleteRequest { public double elapsed_s; }
    [Serializable] public sealed class EventBatch { public List<ApiEvent> events = new List<ApiEvent>(); }
    [Serializable] public sealed class SampleBatch { public List<PoseSample> samples = new List<PoseSample>(); }
    [Serializable] public sealed class ApiEvent
    {
        public string event_id, kind, instrument, object_id;
        public long t_ms;
        public float[] source, target;
    }
    [Serializable] public sealed class PoseSample
    {
        public string instrument, interaction;
        public int idx;
        public long t_ms;
        public float[] pos, quat;
        public bool valid;
    }
    [Serializable] public sealed class RecordedEvent
    {
        public string eventId, type, instrument, objectId;
        public long sequence, elapsedMs;
        public Vector3 positionLocal, sourceLocal, targetLocal;
        public ApiEvent ToApi() => new ApiEvent { event_id = eventId, kind = type, t_ms = elapsedMs,
            instrument = instrument, object_id = objectId, source = Coordinates.Array(sourceLocal), target = Coordinates.Array(targetLocal) };
    }
    [Serializable] public sealed class InstrumentDistances { public float left, right; }
    [Serializable] public sealed class Metrics
    {
        public float completion_pct, total_distance_m, path_efficiency, practice_score;
        public double elapsed_s;
        public int successful_transfers, drops, invalid_releases;
        public InstrumentDistances distance_by_instrument;
    }
    [Serializable] public sealed class CostLine
    {
        public string label, source;
        public double quantity, unit_usd, total_usd;
        public bool assumed;
    }
    [Serializable] public sealed class CostBreakdown
    {
        public double total_usd, counterfactual_clean_usd, avoidable_usd;
        public string disclaimer;
        public CostLine[] lines;
    }
    [Serializable] public sealed class CoachingTip { public string text, event_id; }
    [Serializable] public sealed class Coaching { public string summary, source; public CoachingTip[] tips; }
    public static class Coordinates
    {
        public static float[] Array(Vector3 value) => new[] { value.x, value.y, value.z };
        public static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        public static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public static bool InRange(Vector3 value) => Finite(value) && Mathf.Abs(value.x) <= 10 && Mathf.Abs(value.y) <= 10 && Mathf.Abs(value.z) <= 10;
    }
}
