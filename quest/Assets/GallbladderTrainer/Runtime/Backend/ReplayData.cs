using System.Collections.Generic;
using UnityEngine;

namespace GallbladderTrainer.Backend
{
    /// <summary>Replay in the contract's shape: tracks of [t_ms, x, y, z] (workstation-local metres) plus events.</summary>
    public sealed class ReplayData
    {
        public struct Point { public long T; public Vector3 P; }
        public struct Mark { public long T; public string Kind, ObjectId, Detail; }

        public long DurationMs;
        public readonly Dictionary<string, List<Point>> Tracks = new Dictionary<string, List<Point>>();
        public readonly List<Mark> Events = new List<Mark>();
        public string Source = "backend";

        /// <summary>Parses GET /sessions/{sid}/replay. Unknown track names are kept (future instruments).</summary>
        public static ReplayData FromJson(string body)
        {
            object root = Json.Parse(body);
            if (root == null) return null;
            var d = new ReplayData { DurationMs = (long)Json.Num(root, "duration_ms") };
            var tracks = Json.Obj(root, "tracks");
            if (tracks != null)
            {
                foreach (var kv in tracks)
                {
                    var list = new List<Point>();
                    var rows = kv.Value as List<object>;
                    if (rows != null)
                        foreach (object row in rows)
                        {
                            var a = row as List<object>;
                            if (a == null || a.Count < 4 || !(a[0] is double) || !(a[1] is double) || !(a[2] is double) || !(a[3] is double)) continue;
                            list.Add(new Point { T = (long)(double)a[0], P = new Vector3((float)(double)a[1], (float)(double)a[2], (float)(double)a[3]) });
                        }
                    d.Tracks[kv.Key] = list;
                }
            }
            var evs = Json.Arr(root, "events");
            if (evs != null)
                foreach (object e in evs)
                    d.Events.Add(new Mark { T = (long)Json.Num(e, "t_ms"), Kind = Json.Str(e, "kind", ""), ObjectId = Json.Str(e, "object_id"), Detail = Json.Str(e, "detail") });
            foreach (var t in d.Tracks.Values) if (t.Count > 0 && t[t.Count - 1].T > d.DurationMs) d.DurationMs = t[t.Count - 1].T;
            return d;
        }
    }

    /// <summary>
    /// The same recording kept on the device at 10 Hz, so Replay still works when the backend was offline.
    /// </summary>
    public sealed class LocalReplay
    {
        const int MaxPointsPerTrack = 36000; // one hour at 10 Hz
        readonly ReplayData data = new ReplayData { Source = "device" };
        readonly Dictionary<string, long> lastT = new Dictionary<string, long>();

        public ReplayData Data { get { return data; } }

        public void AddPoint(string track, long t, Vector3 p)
        {
            long prev;
            if (lastT.TryGetValue(track, out prev) && t - prev < 100) return;
            lastT[track] = t;
            List<ReplayData.Point> list;
            if (!data.Tracks.TryGetValue(track, out list)) data.Tracks[track] = list = new List<ReplayData.Point>();
            if (list.Count >= MaxPointsPerTrack) return;
            list.Add(new ReplayData.Point { T = t, P = p });
            if (t > data.DurationMs) data.DurationMs = t;
        }

        public void AddEvent(long t, string kind, string objectId, string detail)
        {
            data.Events.Add(new ReplayData.Mark { T = t, Kind = kind, ObjectId = objectId, Detail = detail });
            if (t > data.DurationMs) data.DurationMs = t;
        }
    }
}
