using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Jeremy
{
    // Live telemetry for the local instructor dashboard (dashboard/LOCAL.md).
    //
    // Installs itself after the first scene loads; no other file references it. While a lap chole is open it
    // publishes, to a SpacetimeDB on the instructor's PC (database "jeremy-ai-live", module in
    // dashboard/spacetimedb):
    //   - a snapshot ~8 times a second (step, tip/target/head positions, entry offset/angle/depth, pressure,
    //     jaws-on structure, findings, coach caption and the coach's read of the moment),
    //   - every recorded lap chole event, and every coach caption / sim feedback line, in order.
    // Calls are SpacetimeDB's HTTP reducer API (POST /v1/database/<db>/call/<reducer>, JSON array of args).
    //
    // Endpoint: http://127.0.0.1:3000 by default. In the editor that is the PC itself; on the Quest it reaches
    // the PC through `adb reverse tcp:3000 tcp:3000` over USB (no firewall change). To use Wi-Fi instead, put a
    // file named live-url.txt in Application.persistentDataPath with the PC's URL on the first line (for example
    // http://192.168.1.20:3000; optional second line: database name), or "off" to disable.
    //
    // Never blocks or throws into the frame: at most one snapshot request in flight (later snapshots are
    // dropped, not queued), a bounded event queue, and when the server is unreachable it goes quiet and only
    // pings every few seconds.
    [DefaultExecutionOrder(1000)]
    public sealed class LiveTelemetry : MonoBehaviour
    {
        public const string DefaultUrl = "http://127.0.0.1:3000";
        public const string DefaultDatabase = "jeremy-ai-live";
        public const string ConfigFile = "live-url.txt";

        [Tooltip("SpacetimeDB origin. Empty = live-url.txt, else http://127.0.0.1:3000.")]
        public string url = "";
        public string database = "";
        [Range(1f, 15f)] public float snapshotHz = 8f;
        public float probeSeconds = 5f;
        public bool logStatus = true;

        public bool Online { get; private set; }
        public string Status { get; private set; } = "starting";

        const int MaxQueue = 300;
        const int FailuresBeforeOffline = 3;

        PegTransferRig rig;
        LapCholeProcedure proc;
        LapCholeNarrator narrator;
        LapCholeDressing dressing;
        bool enabledByConfig = true;
        string baseUrl;
        string session = "";
        bool startSent;
        long frameSeq;
        bool frameInFlight, pumping, probing;
        int failures;
        float nextFrame, nextProbe, nextFind, nextSlow;
        readonly Queue<KeyValuePair<string, string>> queue = new Queue<KeyValuePair<string, string>>(); // reducer, body
        string lastCaption = "";
        string findings = "";
        float score = -1f;
        string coachRead = "";
        float lastHoverMm = -1f, lastHoverAngle = -1f;
        float frameTimeAvg = 1f / 72f;
        readonly HashSet<string> placedPorts = new HashSet<string>();
        bool loggedError;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (FindAnyObjectByType<LiveTelemetry>() != null) return;
            var go = new GameObject("Live telemetry");
            DontDestroyOnLoad(go);
            go.AddComponent<LiveTelemetry>();
        }

        void Awake()
        {
            ReadConfig();
            nextProbe = 0f;
        }

        void ReadConfig()
        {
            string fileUrl = null, fileDb = null;
            try
            {
                string path = Path.Combine(Application.persistentDataPath, ConfigFile);
                if (File.Exists(path))
                {
                    string[] lines = File.ReadAllLines(path);
                    if (lines.Length > 0) fileUrl = lines[0].Trim();
                    if (lines.Length > 1) fileDb = lines[1].Trim();
                }
            }
            catch (Exception) { /* unreadable config: use the defaults */ }
            string chosen = !string.IsNullOrWhiteSpace(url) ? url.Trim() : !string.IsNullOrWhiteSpace(fileUrl) ? fileUrl : DefaultUrl;
            if (string.Equals(chosen, "off", StringComparison.OrdinalIgnoreCase)) { enabledByConfig = false; Status = "off (config)"; return; }
            if (!Uri.TryCreate(chosen, UriKind.Absolute, out Uri origin) || (origin.Scheme != "http" && origin.Scheme != "https"))
            {
                enabledByConfig = false; Status = "off (bad url " + chosen + ")"; return;
            }
            baseUrl = origin.GetLeftPart(UriPartial.Authority);
            if (string.IsNullOrWhiteSpace(database)) database = !string.IsNullOrWhiteSpace(fileDb) ? fileDb : DefaultDatabase;
        }

        void Update()
        {
            if (!enabledByConfig) return;
            frameTimeAvg = Mathf.Lerp(frameTimeAvg, Mathf.Max(0.001f, Time.unscaledDeltaTime), 0.05f);
            float now = Time.unscaledTime;
            if (rig == null)
            {
                if (now < nextSlow) return;
                nextSlow = now + 1f;
                rig = FindAnyObjectByType<PegTransferRig>();
                if (rig == null) return;
                proc = rig.GetComponent<LapCholeProcedure>();
                narrator = rig.GetComponent<LapCholeNarrator>();
                if (rig.Recorder != null) rig.Recorder.Recorded += OnRecorded;
                if (proc != null) proc.Said += OnSaid;
            }
            if (!Online)
            {
                if (!probing && now >= nextProbe) StartCoroutine(Probe());
                return;
            }
            if (!LapCholeLive()) return;
            try
            {
                string attempt = rig.Exercise.AttemptId ?? "";
                if (attempt != session) BeginSession(attempt);
                if (now >= nextFind) { nextFind = now + 0.5f; RefreshSlow(); }
                string caption = narrator != null ? narrator.Caption : "";
                if (!string.IsNullOrEmpty(caption) && caption != lastCaption) Enqueue("push_caption", CaptionBody("coach", caption));
                lastCaption = caption ?? "";
                if (now >= nextFrame && !frameInFlight)
                {
                    nextFrame = now + 1f / Mathf.Clamp(snapshotHz, 1f, 15f);
                    StartCoroutine(Post("push_frame", Args(session, Snapshot()), true));
                }
            }
            catch (Exception e)
            {
                if (!loggedError) { loggedError = true; Debug.LogWarning("[LiveTelemetry] snapshot skipped: " + e.Message); }
            }
        }

        bool LapCholeLive() => rig != null && rig.LapChole && rig.Exercise != null && proc != null && proc.Active;

        void OnDestroy()
        {
            if (rig != null && rig.Recorder != null) rig.Recorder.Recorded -= OnRecorded;
            if (proc != null) proc.Said -= OnSaid;
        }

        // ---------------- session ----------------

        void BeginSession(string attempt)
        {
            session = SanitizeId(attempt);
            startSent = false;
            frameSeq = 0;
            queue.Clear();
            placedPorts.Clear();
            lastCaption = "";
            lastHoverMm = lastHoverAngle = -1f;
            var j = new Json();
            j.Str("trainee", string.IsNullOrEmpty(rig.traineeId) ? "trainee" : rig.traineeId);
            j.Str("exercise", rig.ExerciseId);
            j.Str("device", Application.isEditor ? "editor" : Application.platform == RuntimePlatform.Android ? "quest" : "desktop");
            j.Str("anatomy", AnatomyString());
            Enqueue("live_start", Args(session, j.End()));
            startSent = true;
        }

        static string SanitizeId(string id)
        {
            var sb = new StringBuilder();
            foreach (char c in id ?? "") if (char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c);
            string s = sb.Length > 0 ? sb.ToString() : "attempt";
            return s.Length > 64 ? s.Substring(0, 64) : s;
        }

        string AnatomyString()
        {
            try
            {
                if (dressing == null) dressing = FindAnyObjectByType<LapCholeDressing>();
                LapCholeAnatomy a = dressing != null ? dressing.Anatomy : null;
                if (a == null) return "";
                Transform board = rig.board.transform;
                var sb = new StringBuilder();
                void Path(string id, Vector3[] pts)
                {
                    if (pts == null || pts.Length < 2) return;
                    if (sb.Length > 0) sb.Append('|');
                    sb.Append(id).Append(':');
                    int step = Mathf.Max(1, pts.Length / 12); // ~12 points per structure is plenty for the map
                    for (int i = 0; i < pts.Length; i += step) { if (i > 0) sb.Append(';'); P(sb, board.InverseTransformPoint(a.transform.TransformPoint(pts[i]))); }
                    sb.Append(';'); P(sb, board.InverseTransformPoint(a.transform.TransformPoint(pts[pts.Length - 1])));
                }
                void Point(string id, Transform t)
                {
                    if (t == null) return;
                    if (sb.Length > 0) sb.Append('|');
                    sb.Append(id).Append(':'); P(sb, board.InverseTransformPoint(t.position));
                }
                Path("cystic_duct", a.CysticDuct);
                Path("cystic_artery", a.CysticArtery);
                Path("cbd", a.CommonBileDuct);
                Path("hepatic_artery", a.HepaticArtery);
                if (a.Neck != null && a.Fundus != null)
                {
                    if (sb.Length > 0) sb.Append('|');
                    sb.Append("gallbladder:"); P(sb, board.InverseTransformPoint(a.Neck.position)); sb.Append(';'); P(sb, board.InverseTransformPoint(a.Fundus.position));
                }
                Point("fundus", a.Fundus);
                Point("calot", a.View);
                return sb.ToString();
            }
            catch (Exception) { return ""; }
        }

        static void P(StringBuilder sb, Vector3 p)
        {
            sb.Append(p.x.ToString("F4", CultureInfo.InvariantCulture)).Append(',')
              .Append(p.y.ToString("F4", CultureInfo.InvariantCulture)).Append(',')
              .Append(p.z.ToString("F4", CultureInfo.InvariantCulture));
        }

        // ---------------- snapshot ----------------

        void RefreshSlow()
        {
            EventRecorder r = rig.Recorder;
            var sb = new StringBuilder();
            foreach (var f in r.LapFindings()) { if (sb.Length > 0) sb.Append('|'); sb.Append(f.Key).Append(f.Value ? ":M" : ":m"); }
            findings = sb.ToString();
            try { score = r.LocalMetrics().practice_score; } catch (Exception) { score = -1f; }
            coachRead = narrator != null ? narrator.LiveStatus() : "";
            placedPorts.Clear();
            foreach (RecordedEvent e in r.LapEvents) if (e.type == "port_placed" && !string.IsNullOrEmpty(e.objectId)) placedPorts.Add(e.objectId);
        }

        static readonly string[] PortIds = { "umbilical", "epigastric", "rsc_medial", "rsc_lateral" };

        string Snapshot()
        {
            Transform board = rig.board.transform;
            ExerciseStateMachine ex = rig.Exercise;
            EventRecorder rec = rig.Recorder;
            int step = Mathf.Clamp(proc.StepIndex, 0, LapCholeProcedure.StepCount - 1);
            var j = new Json();
            j.Num("seq", ++frameSeq);
            j.Str("state", ex.State.ToString());
            j.Num("elapsedS", (float)ex.ElapsedSeconds);
            j.Num("stepIndex", step);
            j.Num("stepCount", LapCholeProcedure.StepCount);
            j.Str("stepId", LapCholeProcedure.StepIdAt(step));
            j.Str("stepTitle", LapCholeProcedure.StepTitleAt(step));
            j.Num("stepsDone", rec.LapStepsCompleted);
            j.Num("major", rec.LapMajor);
            j.Num("minor", rec.LapMinor);
            j.Num("score", score);
            j.Str("findings", findings);

            if (rig.head != null)
            {
                Vector3 h = board.InverseTransformPoint(rig.head.position);
                Vector3 fwd = board.InverseTransformDirection(rig.head.forward);
                j.Vec("head", h);
                j.Num("headYaw", Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg);
            }
            InstrumentController[] tools = rig.Instruments;
            if (tools != null && tools.Length >= 2)
            {
                Hand(j, "left", tools[0], board);
                Hand(j, "right", tools[1], board);
            }

            Vector3? tip = proc.ToolTipBoard;
            LapCholeEntry entry = proc.Entry;
            bool entryActive = entry != null && entry.Active;
            if (!tip.HasValue && entryActive && tools != null && tools.Length >= 2 && tools[1].TrackingValid && tools[1].tip != null)
                tip = board.InverseTransformPoint(tools[1].tip.position);
            Vector3 target = proc.TargetBoard;
            j.Bool("tipValid", tip.HasValue);
            if (tip.HasValue) j.Vec("tip", tip.Value);
            j.Vec("target", target);
            j.Num("targetMm", tip.HasValue ? Vector3.Distance(tip.Value, target) * 1000f : -1f);
            j.Str("contact", proc.ContactStructure ?? "");
            j.Num("pressure", proc.Pressure);
            j.Num("veil", proc.Veil);
            j.Num("balloon", proc.Balloon);
            j.Num("contrast", proc.Contrast);
            j.Num("ductClips", proc.DuctClips);
            j.Num("arteryClips", proc.ArteryClips);
            j.Bool("toolInHand", proc.ToolInHand);

            j.Bool("entryActive", entryActive);
            if (entryActive)
            {
                LapCholeEntry.Spec s = entry.Current;
                if (entry.Hovering && !float.IsNaN(entry.HoverMm)) { lastHoverMm = entry.HoverMm; lastHoverAngle = entry.HoverAngle; }
                j.Str("entryTool", s != null ? s.name : "");
                j.Bool("entryInserted", entry.Inserted);
                j.Bool("entryHovering", entry.Hovering);
                j.Num("entryMm", lastHoverMm);
                j.Num("entryAngle", lastHoverAngle);
                j.Num("entryDepth", entry.Inserted ? entry.Depth : -1f);
                j.Num("entryTolMm", entry.SiteTolMm);
                j.Num("entryTolDeg", entry.AngleTolDeg);
                if (s != null)
                {
                    j.Num("entryMinDepth", s.minDepth); j.Num("entryMaxDepth", s.maxDepth); j.Num("entryDanger", s.dangerDepth);
                    j.Num("entryPopA", s.popA); j.Num("entryPopB", s.popB);
                }
                j.Num("entryPops", entry.PoppedPeritoneum ? 2 : entry.PoppedFascia ? 1 : 0);
                j.Bool("entryLined", entry.Lined);
                j.Bool("entryBlocked", entry.Blocked);
                j.Str("entryToward", entry.TowardMark ?? "");
                j.Str("entryReadout", proc.EntryReadout ?? "");
                j.Num("siteX", entry.Site.x); j.Num("siteZ", entry.Site.z);
            }
            else lastHoverMm = lastHoverAngle = -1f;

            j.Str("ports", Ports());
            j.Str("feedback", proc.Feedback ?? "");
            j.Str("coachStatus", coachRead);
            j.Str("caption", narrator != null ? narrator.Caption : "");
            if (narrator != null)
            {
                j.Num("micLevel", narrator.MicLevel);
                j.Bool("listening", narrator.Listening);
                j.Bool("onDemand", narrator.OnDemand);
            }
            j.Num("fps", 1f / frameTimeAvg);
            return j.End();
        }

        static void Hand(Json j, string side, InstrumentController tool, Transform board)
        {
            bool valid = tool != null && tool.TrackingValid && tool.tip != null;
            j.Bool(side + "Valid", valid);
            if (!valid) return;
            Vector3 p = board.InverseTransformPoint(tool.tip.position);
            j.Num(side + "X", p.x); j.Num(side + "Y", p.y); j.Num(side + "Z", p.z);
            j.Bool(side + "Closed", tool.Closed);
        }

        string Ports()
        {
            if (dressing == null) dressing = FindAnyObjectByType<LapCholeDressing>();
            var sb = new StringBuilder();
            foreach (string id in PortIds)
            {
                Vector2 site = Vector2.zero;
                bool known = false;
                if (dressing != null) foreach (LapCholeDressing.Port p in dressing.Ports) if (p.id == id) { site = p.site; known = true; break; }
                if (!known) continue;
                Append(sb, id, site.x, site.y, false);
                if (placedPorts.Contains(id)) { Vector3 at = proc.PortPositionForMap(id); Append(sb, id, at.x, at.z, true); }
            }
            return sb.ToString();
        }

        static void Append(StringBuilder sb, string id, float x, float z, bool placed)
        {
            if (sb.Length > 0) sb.Append('|');
            sb.Append(id).Append(':').Append(x.ToString("F4", CultureInfo.InvariantCulture)).Append(':')
              .Append(z.ToString("F4", CultureInfo.InvariantCulture)).Append(placed ? ":1" : ":0");
        }

        // ---------------- events and captions ----------------

        void OnRecorded(RecordedEvent e)
        {
            if (!Online || !startSent || !LapCholeLive() || e == null || !rig.Recorder.LapChole) return;
            try
            {
                var j = new Json();
                j.Num("seq", e.sequence);
                j.Num("elapsedMs", e.elapsedMs);
                j.Str("kind", e.type);
                j.Str("objectId", e.objectId ?? "");
                j.Str("instrument", e.instrument ?? "");
                j.Str("detail", e.detail ?? "");
                j.Bool("hasValue", e.hasValue); j.Num("value", e.value);
                j.Bool("hasPos", !e.noSource); j.Vec("", e.sourceLocal, "x", "y", "z");
                j.Bool("hasTarget", !e.noTarget); j.Vec("", e.targetLocal, "tx", "ty", "tz");
                j.Num("stepIndex", Mathf.Clamp(proc.StepIndex, 0, LapCholeProcedure.StepCount - 1));
                j.Str("message", proc.Feedback ?? "");
                Enqueue("push_event", Args(session, j.End()));
                nextFind = 0f; // refresh findings and score on the next frame
            }
            catch (Exception) { /* never let telemetry break the recorder's event */ }
        }

        void OnSaid(string text)
        {
            if (!Online || !startSent || !LapCholeLive() || string.IsNullOrWhiteSpace(text)) return;
            Enqueue("push_caption", CaptionBody("sim", text));
        }

        string CaptionBody(string speaker, string text)
        {
            long ms = rig != null && rig.Exercise != null ? rig.Exercise.ElapsedMs : 0;
            int step = proc != null ? Mathf.Clamp(proc.StepIndex, 0, LapCholeProcedure.StepCount - 1) : 0;
            return "[" + Quote(session) + "," + Quote(speaker) + "," + Quote(text.Length > 400 ? text.Substring(0, 400) : text) + ","
                + ms.ToString(CultureInfo.InvariantCulture) + "," + step.ToString(CultureInfo.InvariantCulture) + "]";
        }

        void Enqueue(string reducer, string body)
        {
            if (!Online) return;
            while (queue.Count >= MaxQueue) queue.Dequeue(); // oldest goes first; the dashboard can live with a gap
            queue.Enqueue(new KeyValuePair<string, string>(reducer, body));
            if (!pumping) StartCoroutine(Pump());
        }

        IEnumerator Pump()
        {
            pumping = true;
            while (queue.Count > 0 && Online)
            {
                // Sent once, in order. A rejected call is not retried; transport failures count toward going offline.
                KeyValuePair<string, string> item = queue.Dequeue();
                yield return Send(item.Key, item.Value, null);
            }
            pumping = false;
        }

        // ---------------- transport ----------------

        IEnumerator Post(string reducer, string body, bool isFrame)
        {
            if (isFrame) frameInFlight = true;
            yield return Send(reducer, body, null);
            if (isFrame) frameInFlight = false;
        }

        IEnumerator Send(string reducer, string body, Action<bool> done)
        {
            string target = baseUrl + "/v1/database/" + Uri.EscapeDataString(database) + "/call/" + reducer;
            UnityWebRequest request = null;
            try
            {
                request = new UnityWebRequest(target, "POST");
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)) { contentType = "application/json" }; // exact type: SpacetimeDB rejects others (415)
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = 3;
            }
            catch (Exception e)
            {
                request?.Dispose();
                Fail("request: " + e.Message);
                done?.Invoke(false);
                yield break;
            }
            using (request)
            {
                yield return request.SendWebRequest();
                bool transportOk = request.result != UnityWebRequest.Result.ConnectionError && request.result != UnityWebRequest.Result.DataProcessingError;
                bool ok = request.result == UnityWebRequest.Result.Success;
                if (ok) failures = 0;
                // 530 = the reducer rejected the call (SenderError): a data problem, not a connection problem.
                else if (!transportOk || request.responseCode == 404 || (request.responseCode >= 500 && request.responseCode != 530)) Fail(reducer + ": " + request.error + " " + Trim(request.downloadHandler?.text));
                else if (logStatus && !loggedError) { loggedError = true; Debug.LogWarning("[LiveTelemetry] " + reducer + " rejected: " + request.responseCode + " " + Trim(request.downloadHandler?.text)); }
                done?.Invoke(ok);
            }
        }

        static string Trim(string s) => string.IsNullOrEmpty(s) ? "" : s.Length > 160 ? s.Substring(0, 160) : s;

        void Fail(string why)
        {
            if (++failures < FailuresBeforeOffline || !Online) return;
            Online = false;
            Status = "offline: " + why;
            queue.Clear();
            nextProbe = Time.unscaledTime + probeSeconds;
            if (logStatus) Debug.Log("[LiveTelemetry] lost " + baseUrl + " (" + why + "); retrying every " + probeSeconds + " s");
        }

        IEnumerator Probe()
        {
            probing = true;
            string target = baseUrl + "/v1/database/" + Uri.EscapeDataString(database) + "/schema?version=9";
            using (var request = UnityWebRequest.Get(target))
            {
                request.timeout = 2;
                yield return request.SendWebRequest();
                bool ok = request.result == UnityWebRequest.Result.Success;
                if (ok)
                {
                    Online = true; failures = 0; session = ""; Status = "online " + baseUrl + " / " + database;
                    if (logStatus) Debug.Log("[LiveTelemetry] " + Status);
                }
                else
                {
                    Status = "waiting for " + baseUrl + " / " + database + " (" + request.error + ")";
                    nextProbe = Time.unscaledTime + probeSeconds;
                }
            }
            probing = false;
        }

        // ---------------- JSON ----------------

        static string Args(string sessionId, string json) => "[" + Quote(sessionId) + "," + Quote(json) + "]";

        static string Quote(string s)
        {
            var sb = new StringBuilder(s.Length + 8);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        /// <summary>Minimal JSON object writer. Non-finite numbers are written as -1 (JSON has no NaN).</summary>
        sealed class Json
        {
            readonly StringBuilder sb = new StringBuilder(1024);
            bool first = true;
            public Json() { sb.Append('{'); }
            void Key(string k) { if (!first) sb.Append(','); first = false; sb.Append(Quote(k)).Append(':'); }
            public void Num(string k, float v) { Key(k); sb.Append(float.IsNaN(v) || float.IsInfinity(v) ? "-1" : v.ToString("0.#####", CultureInfo.InvariantCulture)); }
            public void Num(string k, long v) { Key(k); sb.Append(v.ToString(CultureInfo.InvariantCulture)); }
            public void Num(string k, int v) { Key(k); sb.Append(v.ToString(CultureInfo.InvariantCulture)); }
            public void Bool(string k, bool v) { Key(k); sb.Append(v ? "true" : "false"); }
            public void Str(string k, string v) { Key(k); sb.Append(Quote(v ?? "")); }
            public void Vec(string prefix, Vector3 v) { Num(prefix + "X", v.x); Num(prefix + "Y", v.y); Num(prefix + "Z", v.z); }
            public void Vec(string _, Vector3 v, string kx, string ky, string kz) { Num(kx, v.x); Num(ky, v.y); Num(kz, v.z); }
            public string End() => sb.Append('}').ToString();
        }
    }
}
