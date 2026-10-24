using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Jeremy
{
    [Serializable] public sealed class PendingAttempt
    {
        public string attemptId, traineeId, sessionId, blocked;
        public bool started, wantsComplete, cancelled, completed, finished, telemetryIncomplete, cancelSent;
        public double elapsedSeconds;
        public int discardedSamples;
        public List<ApiEvent> events = new List<ApiEvent>();
        public List<PoseSample> samples = new List<PoseSample>();
        public Metrics metrics;
        public CostBreakdown cost;
        [NonSerialized] public double retryAt, sampleUploadAt;
        [NonSerialized] public int failures;
    }
    [Serializable] public sealed class QueueSnapshot { public List<PendingAttempt> attempts = new List<PendingAttempt>(); }

    public sealed class UploadQueue : MonoBehaviour
    {
        public ApiClient api;
        public int maxAttempts = 16;
        public int maxSamplesPerAttempt = 12000;
        public int maxEventsPerAttempt = 2048;
        public string Status { get; private set; } = "Offline";
        public event Action<PendingAttempt> ResultsAvailable;
        public IReadOnlyList<PendingAttempt> Attempts => snapshot.attempts;
        private QueueSnapshot snapshot = new QueueSnapshot();
        private string queuePath;
        private bool dirty;
        private double nextSave;
        private Coroutine pump;
        private string localOnlyAttemptId;

        public void Initialize(string path = null)
        {
            if (pump != null) return;
            queuePath = path ?? Path.Combine(Application.persistentDataPath, "jeremy-upload-queue.json");
            Load();
            pump = StartCoroutine(Pump());
        }
        public PendingAttempt Find(string attemptId) => snapshot.attempts.Find(x => x.attemptId == attemptId);
        /// <summary>Newest attempt whose server session was started, or null.</summary>
        public PendingAttempt LatestStarted() => snapshot.attempts.FindLast(x => x.started && !string.IsNullOrEmpty(x.sessionId));
        public bool BeginAttempt(string attemptId, string traineeId)
        {
            if (Find(attemptId) != null) return true;
            if (localOnlyAttemptId == attemptId) return false; // Resume must not upload only the tail of an unbuffered attempt.
            snapshot.attempts.RemoveAll(x => x.finished);
            if (snapshot.attempts.Count >= Math.Max(1, maxAttempts))
            {
                Status = "Upload storage full: this attempt is local only.";
                localOnlyAttemptId = attemptId;
                return false;
            }
            snapshot.attempts.Add(new PendingAttempt { attemptId = attemptId, traineeId = traineeId });
            dirty = true;
            return true;
        }
        public void EnqueueEvent(string attemptId, RecordedEvent value)
        {
            PendingAttempt pending = Find(attemptId);
            if (pending == null || pending.wantsComplete || pending.cancelled) return;
            if (pending.events.Count >= Math.Max(1, maxEventsPerAttempt))
            {
                pending.telemetryIncomplete = true;
                pending.blocked = "Event capacity exceeded; server scoring withheld. Local practice continues.";
            }
            else pending.events.Add(value.ToApi());
            dirty = true;
        }
        public void EnqueueSample(string attemptId, PoseSample value)
        {
            PendingAttempt pending = Find(attemptId);
            if (pending == null || pending.wantsComplete || pending.cancelled) return;
            // Never evict an in-flight prefix. Retain the first bounded window and explicitly withhold
            // authoritative server scoring if telemetry is lost, instead of returning inflated efficiency.
            if (pending.samples.Count >= Math.Max(1, maxSamplesPerAttempt))
            {
                pending.discardedSamples++;
                pending.telemetryIncomplete = true;
                pending.blocked = "Sample capacity exceeded; server scoring withheld. Local practice continues.";
            }
            else pending.samples.Add(value);
            dirty = true;
        }
        public void EndAttempt(string attemptId, double elapsedSeconds, bool cancelled)
        {
            PendingAttempt pending = Find(attemptId);
            if (pending == null) return;
            pending.elapsedSeconds = Math.Min(3600, Math.Max(0, elapsedSeconds));
            pending.cancelled = cancelled;
            pending.wantsComplete = !cancelled;
            dirty = true;
            Save();
        }
        public void RetryBlocked()
        {
            foreach (PendingAttempt pending in snapshot.attempts)
                if (!pending.telemetryIncomplete) { pending.blocked = null; pending.retryAt = 0; pending.failures = 0; }
            dirty = true;
        }
        private IEnumerator Pump()
        {
            while (true)
            {
                if (api == null || !api.IsConfigured)
                {
                    PendingAttempt blocked = snapshot.attempts.Find(x => !string.IsNullOrEmpty(x.blocked));
                    Status = blocked != null ? blocked.blocked : "Offline: attempts buffered locally";
                    yield return new WaitForSecondsRealtime(1); continue;
                }
                bool worked = false;
                foreach (PendingAttempt pending in snapshot.attempts.ToArray())
                {
                    if (pending.finished || pending.retryAt > Time.realtimeSinceStartupAsDouble) continue;
                    if (pending.cancelled)
                    {
                        // Close the server session so the instructor view does not show it running forever.
                        if (!string.IsNullOrEmpty(pending.sessionId) && !pending.cancelSent)
                        {
                            worked = true;
                            yield return SendCancel(pending);
                            continue;
                        }
                        pending.finished = true; dirty = true; continue;
                    }
                    if (!string.IsNullOrEmpty(pending.blocked)) { Status = pending.blocked; continue; }
                    if (!HasWork(pending)) continue;
                    worked = true;
                    yield return UploadNext(pending);
                }
                if (!worked) yield return new WaitForSecondsRealtime(0.1f);
                else yield return null;
            }
        }
        private IEnumerator SendCancel(PendingAttempt pending)
        {
            ApiResult result = null;
            yield return api.Send("POST", ApiClient.SessionPath(pending.sessionId) + "/cancel", "{}", value => result = value);
            // 404/409 mean there is nothing left to close. Give up after a few network failures; it is best effort.
            if (result.Success || result.Status == 404 || result.Status == 409 || ++pending.failures >= 3)
            {
                pending.cancelSent = true;
                pending.failures = 0;
            }
            else pending.retryAt = Time.realtimeSinceStartupAsDouble + 2.0 * pending.failures;
            dirty = true;
        }
        private bool HasWork(PendingAttempt pending) => string.IsNullOrEmpty(pending.sessionId) || !pending.started
            || pending.events.Count > 0 || (pending.samples.Count > 0 && (pending.wantsComplete || Time.realtimeSinceStartupAsDouble >= pending.sampleUploadAt))
            || pending.wantsComplete;

        private IEnumerator UploadNext(PendingAttempt pending)
        {
            string method = "POST", path, body;
            int eventCount = 0, sampleCount = 0;
            int phase;
            if (string.IsNullOrEmpty(pending.sessionId))
            {
                phase = 0; path = "/api/v1/sessions";
                body = JsonUtility.ToJson(new SessionCreate { trainee_id = pending.traineeId, client_request_id = pending.attemptId });
            }
            else
            {
                path = ApiClient.SessionPath(pending.sessionId);
                if (!pending.started) { phase = 1; path += "/start"; body = "{}"; }
                else if (pending.events.Count > 0)
                {
                    phase = 2; path += "/events";
                    eventCount = Math.Min(1000, pending.events.Count);
                    body = JsonUtility.ToJson(new EventBatch { events = pending.events.GetRange(0, eventCount) });
                }
                else if (pending.samples.Count > 0 && (pending.wantsComplete || Time.realtimeSinceStartupAsDouble >= pending.sampleUploadAt))
                {
                    phase = 3; path += "/samples";
                    sampleCount = Math.Min(1000, pending.samples.Count);
                    body = JsonUtility.ToJson(new SampleBatch { samples = pending.samples.GetRange(0, sampleCount) });
                }
                else if (pending.wantsComplete && !pending.completed)
                {
                    phase = 4; path += "/complete";
                    body = JsonUtility.ToJson(new CompleteRequest { elapsed_s = pending.elapsedSeconds });
                }
                else if (pending.completed) { phase = 5; path += "/cost"; method = "GET"; body = null; }
                else yield break;
            }
            Status = "Uploading practice data";
            ApiResult result = null;
            yield return api.Send(method, path, body, response => result = response);
            if (!result.Success)
            {
                if (result.Retryable)
                {
                    pending.retryAt = Time.realtimeSinceStartupAsDouble + Math.Min(30, Math.Pow(2, Math.Min(++pending.failures, 5))) + UnityEngine.Random.value;
                    Status = "Connection unavailable: retrying buffered data";
                }
                else { pending.blocked = "Upload rejected (HTTP " + result.Status + "). Check server/configuration, then Retry upload."; Status = pending.blocked; dirty = true; }
                yield break;
            }
            try
            {
                switch (phase)
                {
                    case 0:
                        var session = JsonUtility.FromJson<SessionResponse>(result.Body);
                        if (session == null || string.IsNullOrWhiteSpace(session.id)) throw new FormatException("Missing session ID");
                        pending.sessionId = session.id; break;
                    case 1: pending.started = true; break;
                    case 2: pending.events.RemoveRange(0, eventCount); break;
                    case 3: pending.samples.RemoveRange(0, sampleCount); pending.sampleUploadAt = Time.realtimeSinceStartupAsDouble + 1; break;
                    case 4:
                        pending.metrics = JsonUtility.FromJson<Metrics>(result.Body);
                        if (pending.metrics == null) throw new FormatException("Missing metrics");
                        pending.completed = true;
                        ResultsAvailable?.Invoke(pending); break;
                    case 5:
                        pending.cost = JsonUtility.FromJson<CostBreakdown>(result.Body);
                        if (pending.cost == null || pending.cost.lines == null) throw new FormatException("Missing itemized cost");
                        pending.finished = true;
                        ResultsAvailable?.Invoke(pending); break;
                }
                pending.failures = 0; pending.retryAt = 0; dirty = true;
                Status = pending.finished ? "Upload complete" : "Practice data synchronized";
                Save(); // Persist server ID and acknowledged prefixes before the next operation.
            }
            catch (Exception exception)
            {
                pending.blocked = "Invalid server response: " + exception.GetType().Name;
                Status = pending.blocked; dirty = true;
            }
        }

        private void LateUpdate()
        {
            if (dirty && Time.realtimeSinceStartupAsDouble >= nextSave)
            { Save(); nextSave = Time.realtimeSinceStartupAsDouble + 1; }
        }
        public void Save()
        {
            if (!dirty || string.IsNullOrEmpty(queuePath)) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(queuePath));
                File.WriteAllText(queuePath + ".tmp", JsonUtility.ToJson(snapshot));
                if (File.Exists(queuePath + ".bak")) File.Delete(queuePath + ".bak");
                if (File.Exists(queuePath)) File.Move(queuePath, queuePath + ".bak");
                File.Move(queuePath + ".tmp", queuePath);
                dirty = false;
            }
            catch (Exception exception) { Status = "Queue is memory-only: " + exception.GetType().Name; }
        }
        private void Load()
        {
            foreach (string candidate in new[] { queuePath + ".tmp", queuePath, queuePath + ".bak" })
            {
                if (!File.Exists(candidate)) continue;
                try
                {
                    if (new FileInfo(candidate).Length > 64 * 1024 * 1024) throw new FormatException("Queue exceeds disk limit");
                    QueueSnapshot loaded = JsonUtility.FromJson<QueueSnapshot>(File.ReadAllText(candidate));
                    if (loaded == null || loaded.attempts == null || loaded.attempts.Count > Math.Max(1, maxAttempts)) throw new FormatException("Invalid queue");
                    foreach (PendingAttempt pending in loaded.attempts)
                    {
                        if (pending == null || string.IsNullOrEmpty(pending.attemptId) || pending.events == null || pending.samples == null
                            || pending.events.Count > Math.Max(1, maxEventsPerAttempt) || pending.samples.Count > Math.Max(1, maxSamplesPerAttempt)) throw new FormatException("Invalid attempt");
                        // An interrupted live attempt cannot be resumed from a new physical board.
                        if (!pending.wantsComplete) pending.cancelled = true;
                    }
                    snapshot = loaded;
                    return;
                }
                catch (Exception exception) { Status = "Could not restore queue: " + exception.GetType().Name; }
            }
        }
        private void OnApplicationPause(bool paused) { if (paused) Save(); }
        private void OnApplicationQuit() => Save();
        private void OnDestroy() => Save();
    }
}
