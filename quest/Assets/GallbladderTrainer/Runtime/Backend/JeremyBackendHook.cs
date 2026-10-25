using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GallbladderTrainer.Backend
{
    public enum BackendState { Idle, Connecting, Active, Retrying, LocalOnly, Completing, Completed }

    /// <summary>What this device saw for one exercise (local copy, for the offline summary and the report panel).</summary>
    public sealed class LocalDecision
    {
        public string ExerciseId, DecisionId, SelectedStructureId, Detail;
        public ResultStatus Result;
        public float TimeToDecisionS;
    }

    /// <summary>
    /// Bridges the trainer to the Jeremy.ai backend (contract v1, exercise "lap_chole_cvs").
    /// Creates and starts a session, mirrors TrainingEvents as backend events (batched every 250 ms), uploads
    /// MovementRecorder samples (~1 s batches), queues everything in memory while offline and retries with backoff.
    /// Exercise and structure ids are passed through unchanged, so new procedures need no code here.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class JeremyBackendHook : MonoBehaviour, ITrainingBackendHook
    {
        public const string ExerciseName = "lap_chole_cvs";

        [Header("Backend (config asset wins over the fields below when assigned)")]
        [SerializeField] JeremyBackendConfig config;
        [SerializeField] string baseUrl = "http://127.0.0.1:8000";
        [Tooltip("Optional session-API key (X-API-Key). Never put AI or speech provider keys on the headset.")]
        [SerializeField] string apiKey = "";
        [SerializeField] string traineeId = "quest-trainee";
        [SerializeField] int timeoutSeconds = 10;

        [Header("Behaviour")]
        [SerializeField] bool connectOnStart = true;
        [SerializeField] float eventFlushSeconds = 0.25f;
        [SerializeField] float sampleFlushSeconds = 1f;
        [SerializeField] float minHoverDwellSeconds = 0.3f;
        [SerializeField] float maxBackoffSeconds = 30f;
        [SerializeField] bool persistOutbox = true;

        public JeremyApi Api { get; private set; }
        public BackendState State { get; private set; }
        public string SessionId { get; private set; }
        public string StatusText { get; private set; }
        public string TraineeId { get { return config != null ? config.traineeId : traineeId; } }
        public string CurrentExerciseId { get; private set; }
        public TrainingMode CurrentMode { get; private set; }
        public int PendingCount { get { return outbox != null ? outbox.Count : 0; } }
        public readonly Dictionary<string, LocalDecision> Decisions = new Dictionary<string, LocalDecision>();
        public readonly LocalReplay Replay = new LocalReplay();
        public bool HasBackendSession { get { return SessionId != null; } }
        public bool AllAnswered
        {
            get
            {
                foreach (var ex in ExerciseLibrary.All) if (!Decisions.ContainsKey(ex.Id)) return false;
                return ExerciseLibrary.All.Length > 0;
            }
        }

        /// <summary>Milliseconds since this training session started on the device (the contract's t_ms).</summary>
        public long ElapsedMs { get { return sessionStart < 0 ? 0 : (long)((Time.realtimeSinceStartupAsDouble - sessionStart) * 1000.0); } }

        public event Action<BackendState> StateChanged;
        public event Action<string> SessionReady;
        public event Action<DecisionEvent> DecisionRecorded;
        public event Action EventsFlushed;

        Outbox outbox;
        string clientRequestId;
        double sessionStart = -1;
        float backoff = 1f, nextEventFlush, nextSampleFlush, retryAt;
        bool flushingEvents, flushingSamples;
        DecisionEvent lastDecisionRef;
        string lastSelectId; int lastSelectFrame = -1;
        readonly Dictionary<string, float> hoverStart = new Dictionary<string, float>();
        readonly Dictionary<string, float> exerciseStart = new Dictionary<string, float>();
        long eventSeq;

        // ------------------------------------------------------------------ lifecycle

        void Awake()
        {
            if (config == null) config = Resources.Load<JeremyBackendConfig>(JeremyBackendConfig.ResourceName);
            Api = new JeremyApi(ResolveBaseUrl(), config != null ? config.apiKey : apiKey, config != null ? config.timeoutSeconds : timeoutSeconds);
            string file = persistOutbox ? Path.Combine(Application.persistentDataPath, "jeremy_outbox.txt") : null;
            outbox = new Outbox(file);
            int restored = outbox.LoadPersisted();
            if (restored > 0) Debug.Log("[Jeremy] Restored " + restored + " unsent items from a previous run.");
            clientRequestId = "q" + Guid.NewGuid().ToString("N").Substring(0, 24);
            SetState(BackendState.Idle, "Not connected");
        }

        void OnEnable()
        {
            TrainingEvents.DecisionSubmitted += OnDecisionSubmitted;
            TrainingEvents.StructureSelected += OnStructureSelected;
            TrainingEvents.ModeChanged += OnModeChanged;
            TrainingEvents.ExerciseChanged += OnExerciseChanged;
            TrainingEvents.StructureHovered += OnStructureHovered;
        }

        void OnDisable()
        {
            TrainingEvents.DecisionSubmitted -= OnDecisionSubmitted;
            TrainingEvents.StructureSelected -= OnStructureSelected;
            TrainingEvents.ModeChanged -= OnModeChanged;
            TrainingEvents.ExerciseChanged -= OnExerciseChanged;
            TrainingEvents.StructureHovered -= OnStructureHovered;
            if (outbox != null) outbox.SaveIfDirty(true);
        }

        void OnApplicationPause(bool paused) { if (paused && outbox != null) outbox.SaveIfDirty(true); }
        void OnApplicationQuit() { if (outbox != null) outbox.SaveIfDirty(true); }

        void Start()
        {
            sessionStart = Time.realtimeSinceStartupAsDouble;
            if (CurrentExerciseId == null && ExerciseLibrary.All.Length > 0) OnExerciseChanged(ExerciseLibrary.All[0].Id);
            if (connectOnStart) Connect();
        }

        void Update()
        {
            if (State == BackendState.Retrying && SessionId == null && Time.unscaledTime >= retryAt) Connect();
            float now = Time.unscaledTime;
            if (!flushingEvents && now >= nextEventFlush) { nextEventFlush = now + eventFlushSeconds; StartFlush(Outbox.Events); }
            if (!flushingSamples && now >= nextSampleFlush) { nextSampleFlush = now + sampleFlushSeconds; StartFlush(Outbox.Samples); }
            outbox.SaveIfDirty(false);
        }

        string ResolveBaseUrl()
        {
            // Desktop testing convenience: -jeremyBackend=http://host:8000 or JEREMY_BACKEND_URL override the asset.
            foreach (string arg in Environment.GetCommandLineArgs())
                if (arg.StartsWith("-jeremyBackend=")) return arg.Substring("-jeremyBackend=".Length);
            string env = Environment.GetEnvironmentVariable("JEREMY_BACKEND_URL");
            if (!string.IsNullOrEmpty(env)) return env;
            return config != null ? config.baseUrl : baseUrl;
        }

        void SetState(BackendState s, string text)
        {
            bool changed = s != State || text != StatusText;
            State = s; StatusText = text;
            if (changed && StateChanged != null) StateChanged(s);
        }

        // ------------------------------------------------------------------ session lifecycle

        public void Connect()
        {
            if (SessionId != null || State == BackendState.Connecting || State == BackendState.LocalOnly) return;
            if (!Api.IsConfigured) { SetState(BackendState.LocalOnly, "No backend configured: local practice only"); return; }
            StartCoroutine(ConnectRoutine());
        }

        IEnumerator ConnectRoutine()
        {
            SetState(BackendState.Connecting, "Connecting to " + Api.BaseUrl);
            string body = new Json.Writer().BeginObject()
                .Field("trainee_id", TraineeId).Field("exercise", ExerciseName).Field("client_request_id", clientRequestId)
                .EndObject().ToString();
            ApiResponse r = null;
            yield return Api.Send("POST", "/sessions", body, x => r = x);
            string sid = r.Ok ? Json.Str(Json.Parse(r.Body), "id") : null;
            if (sid == null) { yield return Fail(r, "create session"); yield break; }

            r = null;
            yield return Api.Send("POST", JeremyApi.SessionPath(sid) + "/start", "{}", x => r = x);
            // 409 = already started (idempotent retry after a lost response).
            if (!r.Ok && r.Code != 409) { yield return Fail(r, "start session"); yield break; }

            SessionId = sid;
            backoff = 1f;
            outbox.BindSession(sid);
            SetState(BackendState.Active, "Live: session " + Short(sid));
            if (SessionReady != null) SessionReady(sid);
        }

        IEnumerator Fail(ApiResponse r, string what)
        {
            if (r != null && !r.Retryable)
            {
                // A permanent rejection (e.g. 401 wrong key, 422 backend without lap_chole_cvs) will not fix itself.
                SetState(BackendState.LocalOnly, "Backend refused to " + what + " (HTTP " + r.Code + "): local practice only");
                Debug.LogWarning("[Jeremy] " + what + " failed permanently: HTTP " + r.Code + " " + Trim(r.Body));
                yield break;
            }
            retryAt = Time.unscaledTime + backoff;
            SetState(BackendState.Retrying, "Offline (" + (r != null && r.Code > 0 ? "HTTP " + r.Code : "no connection") +
                "): retrying in " + Mathf.CeilToInt(backoff) + " s, events are queued");
            backoff = Mathf.Min(backoff * 2f, maxBackoffSeconds);
        }

        /// <summary>Flushes the queue, then posts /complete. done(true) when the backend accepted completion.</summary>
        public IEnumerator Complete(Action<bool, ApiResponse> done)
        {
            if (SessionId == null)
            {
                SetState(BackendState.LocalOnly, "Session finished locally (backend was not reachable)");
                if (done != null) done(false, null);
                yield break;
            }
            BackendState before = State;
            SetState(BackendState.Completing, "Uploading the last events");
            float deadline = Time.unscaledTime + 8f;
            while (Time.unscaledTime < deadline && outbox.CountKind(Outbox.Events, SessionId) + outbox.CountKind(Outbox.Samples, SessionId) > 0)
            {
                if (!flushingEvents) StartFlush(Outbox.Events);
                if (!flushingSamples) StartFlush(Outbox.Samples);
                yield return null;
            }
            float elapsed = Mathf.Clamp(ElapsedMs / 1000f, 0f, 3600f);
            ApiResponse r = null;
            yield return Api.Send("POST", JeremyApi.SessionPath(SessionId) + "/complete",
                new Json.Writer().BeginObject().Field("elapsed_s", (double)elapsed).EndObject().ToString(), x => r = x);
            bool ok = r.Ok || r.Code == 409; // 409 = already completed
            if (ok) SetState(BackendState.Completed, "Session complete: " + Short(SessionId));
            else SetState(before == BackendState.Completing ? BackendState.Active : before, "Could not complete the session (" + Describe(r) + ")");
            if (done != null) done(ok, r);
        }

        // ------------------------------------------------------------------ outbound queue

        public void FlushNow() { nextEventFlush = 0f; nextSampleFlush = 0f; }

        void StartFlush(string kind)
        {
            string sid = outbox.FirstSession(kind);
            if (sid == null || Time.unscaledTime < retryAt && sid == SessionId && State == BackendState.Retrying) return;
            StartCoroutine(FlushRoutine(kind, sid));
        }

        IEnumerator FlushRoutine(string kind, string sid)
        {
            if (kind == Outbox.Events) flushingEvents = true; else flushingSamples = true;
            List<Outbox.Item> batch = outbox.Peek(kind, sid, 500);
            var w = new Json.Writer().BeginObject().BeginArray(kind);
            foreach (var it in batch) w.Raw(it.Json);
            string body = w.EndArray().EndObject().ToString();
            ApiResponse r = null;
            yield return Api.Send("POST", JeremyApi.SessionPath(sid) + "/" + kind, body, x => r = x);
            if (r.Ok)
            {
                outbox.Commit(batch);
                backoff = 1f;
                if (State == BackendState.Retrying && SessionId != null) SetState(BackendState.Active, "Live: session " + Short(SessionId));
                if (kind == Outbox.Events && EventsFlushed != null) EventsFlushed();
            }
            else if (r.Code == 404 || r.Code == 409 || r.Code == 410)
            {
                // Session gone or already completed: these items can never be accepted.
                outbox.DropSession(sid, kind);
                Debug.LogWarning("[Jeremy] Dropped queued " + kind + " for session " + Short(sid) + ": HTTP " + r.Code);
            }
            else if (!r.Retryable)
            {
                outbox.Commit(batch); // malformed for this backend; do not block the queue forever
                Debug.LogWarning("[Jeremy] Backend rejected " + batch.Count + " " + kind + ": HTTP " + r.Code + " " + Trim(r.Body));
            }
            else
            {
                float wait = backoff;
                backoff = Mathf.Min(backoff * 2f, maxBackoffSeconds);
                if (sid == SessionId) SetState(BackendState.Retrying, "Offline (" + Describe(r) + "): " + outbox.Count + " items queued, retrying in " + Mathf.CeilToInt(wait) + " s");
                retryAt = Time.unscaledTime + wait;
                if (kind == Outbox.Events) nextEventFlush = retryAt; else nextSampleFlush = retryAt;
            }
            if (kind == Outbox.Events) flushingEvents = false; else flushingSamples = false;
        }

        /// <summary>Queues one movement sample (already serialized by MovementRecorder).</summary>
        public void EnqueueSample(string json)
        {
            if (State == BackendState.Completed || State == BackendState.Completing) return;
            outbox.Add(SessionId, Outbox.Samples, json);
        }

        /// <summary>Queues one backend event. Unknown kinds/ids are passed through; the backend decides what it scores.</summary>
        public void EnqueueEvent(string kind, string objectId, string detail, double? value)
        {
            long t = ElapsedMs;
            Replay.AddEvent(t, kind, objectId, detail);
            if (State == BackendState.Completed || State == BackendState.Completing) return;
            var w = new Json.Writer().BeginObject()
                .Field("event_id", clientRequestId.Substring(0, 12) + "-" + (++eventSeq))
                .Field("kind", kind).Field("t_ms", t);
            if (objectId != null) w.Field("object_id", Clip(objectId));
            if (detail != null) w.Field("detail", Clip(detail));
            if (value.HasValue) w.Field("value", value);
            outbox.Add(SessionId, Outbox.Events, w.EndObject().ToString());
        }

        static string Clip(string s) { return s.Length <= 64 ? s : s.Substring(0, 64); }

        // ------------------------------------------------------------------ trainer events

        public void OnDecisionSubmitted(DecisionEvent e)
        {
            // The controller raises TrainingEvents and also calls its assigned hook with the same instance.
            if (e == null || ReferenceEquals(e, lastDecisionRef)) return;
            lastDecisionRef = e;
            string detail = !string.IsNullOrEmpty(e.DecisionId) ? e.DecisionId
                : "identify:" + (string.IsNullOrEmpty(e.SelectedStructureId) ? "none" : e.SelectedStructureId);
            double? value = null;
            switch (e.Result)
            {
                case ResultStatus.Match: value = 1; break;
                case ResultStatus.Mismatch: value = 0; break;
                case ResultStatus.Unclear: value = 0.5; break;
            }
            EnqueueEvent("decision_submitted", e.ExerciseId, detail, value);
            if (e.DecisionId == DecisionIds.RequestInstructor) EnqueueEvent("instructor_requested", e.ExerciseId, null, null);

            float started;
            float ttd = exerciseStart.TryGetValue(e.ExerciseId ?? "", out started) ? Time.realtimeSinceStartup - started : ElapsedMs / 1000f;
            Decisions[e.ExerciseId ?? "unknown"] = new LocalDecision
            {
                ExerciseId = e.ExerciseId, DecisionId = e.DecisionId, SelectedStructureId = e.SelectedStructureId,
                Detail = detail, Result = e.Result, TimeToDecisionS = ttd
            };
            FlushNow();
            if (DecisionRecorded != null) DecisionRecorded(e);
        }

        public void OnStructureSelected(string structureId)
        {
            if (structureId == lastSelectId && Time.frameCount == lastSelectFrame) return;
            lastSelectId = structureId; lastSelectFrame = Time.frameCount;
            EnqueueEvent("structure_selected", structureId, CurrentExerciseId, null);
        }

        void OnModeChanged(TrainingMode m)
        {
            CurrentMode = m;
            EnqueueEvent("mode_changed", null, m.ToString().ToLowerInvariant(), null);
        }

        void OnExerciseChanged(string id)
        {
            CurrentExerciseId = id;
            exerciseStart[id ?? ""] = Time.realtimeSinceStartup;
            hoverStart.Clear();
            EnqueueEvent("exercise_started", id, null, null);
        }

        void OnStructureHovered(string id, bool entered)
        {
            if (string.IsNullOrEmpty(id)) return;
            float t0;
            if (entered) { if (!hoverStart.ContainsKey(id)) hoverStart[id] = Time.realtimeSinceStartup; return; }
            if (!hoverStart.TryGetValue(id, out t0)) return;
            hoverStart.Remove(id);
            float dwell = Time.realtimeSinceStartup - t0;
            if (dwell >= minHoverDwellSeconds) EnqueueEvent("structure_hovered", id, null, Math.Round(dwell, 2));
        }

        // ------------------------------------------------------------------ helpers

        public static string Short(string sid) { return sid == null ? "-" : sid.Length <= 8 ? sid : sid.Substring(0, 8); }
        static string Trim(string s) { return s == null ? "" : s.Length <= 200 ? s : s.Substring(0, 200); }
        static string Describe(ApiResponse r) { return r == null ? "no response" : r.Code == 0 ? "no connection" : "HTTP " + r.Code; }
    }
}
