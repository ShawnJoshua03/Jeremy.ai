using System;
using System.Collections.Generic;
using UnityEngine;

namespace Jeremy
{
    // client_request_id = attemptId, so a retried create after a lost response returns the same session.
    [Serializable] public sealed class SessionCreate { public string trainee_id; public string exercise = "peg_transfer"; public string client_request_id; }
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
        public bool noTarget; // stitch_missed without an exit bite, tissue_contact: sent as target null
        public ApiEvent ToApi() => new ApiEvent { event_id = eventId, kind = type, t_ms = elapsedMs,
            instrument = instrument, object_id = objectId, source = Coordinates.Array(sourceLocal), target = noTarget ? null : Coordinates.Array(targetLocal) };
    }
    [Serializable] public sealed class InstrumentDistances { public float left, right; }
    [Serializable] public sealed class Metrics
    {
        public float completion_pct, total_distance_m, path_efficiency, practice_score;
        public double elapsed_s;
        public int successful_transfers, drops, invalid_releases;
        public InstrumentDistances distance_by_instrument;
        // Wound closure only (successful_transfers = stitches, drops = tissue contacts, invalid_releases = missed).
        // Negative means unknown: UploadQueue maps the backend's null to -1.
        public string exercise;
        public float mean_bite_error_mm = -1, spacing_sd_mm = -1;
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
    [Serializable] public sealed class CoachDrill { public string id, title, description, exercise_id; }
    [Serializable] public sealed class Coaching { public string summary, source; public CoachingTip[] tips; public string answer, next_exercise_id; public CoachDrill next_exercise; }
    public static class Coordinates
    {
        public static float[] Array(Vector3 value) => new[] { value.x, value.y, value.z };
        public static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        public static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public static bool InRange(Vector3 value) => Finite(value) && Mathf.Abs(value.x) <= 10 && Mathf.Abs(value.y) <= 10 && Mathf.Abs(value.z) <= 10;
    }
}
