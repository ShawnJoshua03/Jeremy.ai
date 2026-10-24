#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace Jeremy.Tests
{
    // Real Unity play-mode checks, without adding a test-framework package to the project.
    public sealed class VerificationRunner : MonoBehaviour
    {
        public Action<bool> Finished;
        private int assertions;
        private Exception failure;
        private void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            assertions++;
        }
        private IEnumerator Start()
        {
            // Wrap coroutine iteration so assertion failures produce a nonzero batch-mode exit.
            IEnumerator checks = RunChecks();
            while (true)
            {
                object current = null;
                bool more = false;
                try { more = checks.MoveNext(); if (more) current = checks.Current; }
                catch (Exception exception) { failure = exception; }
                if (failure != null || !more) break;
                yield return current;
            }
            if (failure != null) Debug.LogError("JEREMY VERIFICATION FAILED: " + failure);
            else Debug.Log("JEREMY VERIFICATION PASSED: " + assertions + " assertions");
            Finished?.Invoke(failure == null);
        }
        private IEnumerator RunChecks()
        {
            var root = new GameObject("Checks");
            var exercise = root.AddComponent<ExerciseStateMachine>();
            exercise.enabled = false; // Tests advance the same production clock explicitly.
            exercise.ResetExercise();
            string firstAttempt = exercise.AttemptId;
            Check(exercise.State == ExerciseState.Ready && !exercise.Resume(), "Ready transition");
            Check(exercise.Begin() && !exercise.Begin(), "Start is idempotent");
            exercise.Advance(2);
            Check(exercise.Pause(), "Pause running attempt");
            exercise.Advance(100);
            Check(exercise.ElapsedMs == 2000 && exercise.Resume(), "Paused time excluded");
            exercise.Advance(1);
            Check(exercise.Complete() && !exercise.Complete() && !exercise.Begin(), "Terminal state guards");
            exercise.ResetExercise();
            Check(exercise.AttemptId != firstAttempt && exercise.ElapsedMs == 0, "Reset creates fresh attempt");

            var board = new GameObject("Board").AddComponent<PegBoard>();
            board.exercise = exercise; board.Build();
            Check(board.Objects.Count == 6, "Six source objects");
            var leftTool = new GameObject("Left owner"); leftTool.SetActive(false);
            var rightTool = new GameObject("Right owner"); rightTool.SetActive(false);
            var leftOwner = leftTool.AddComponent<InstrumentController>();
            var rightOwner = rightTool.AddComponent<InstrumentController>();
            Check(board.Objects[0].TryGrab(leftOwner) && !board.Objects[0].TryGrab(rightOwner), "Two controllers cannot own one peg");
            board.Objects[0].ClearHolder();
            DestroyImmediate(leftTool); DestroyImmediate(rightTool);
            Check(board.Place(new Vector3(2, 1, 3), Quaternion.Euler(0, 67, 0)), "Manual positioning while ready");
            var recorder = root.AddComponent<EventRecorder>(); recorder.exercise = exercise;
            var detector = root.AddComponent<TransferDetector>(); detector.exercise = exercise; detector.board = board; detector.recorder = recorder;
            RecordedEvent captured = null;
            recorder.Recorded += value => captured = value;
            exercise.Begin();
            Check(!board.Place(Vector3.zero, Quaternion.identity), "Board cannot move during attempt");
            exercise.Advance(0.5);
            detector.Release(board.Objects[0], "left", board.transform.TransformPoint(board.Target(0)));
            Check(recorder.SuccessfulTransfers == 1 && board.Objects[0].Scored, "Matching target scores");
            Check(captured.instrument == "left" && captured.elapsedMs == 500 && captured.sequence == 1
                && Vector3.Distance(captured.positionLocal, board.Target(0)) < 0.00001f, "Board-local event and controller identity");
            string eventId = captured.eventId;
            detector.Release(board.Objects[0], "left", board.transform.TransformPoint(board.Target(0)));
            Check(recorder.SuccessfulTransfers == 1, "Duplicate release cannot score twice");
            detector.Release(board.Objects[1], "right", board.transform.TransformPoint(board.Target(2)));
            Check(recorder.InvalidReleases == 1 && !board.Objects[1].Scored, "Wrong target rejected");
            detector.Release(board.Objects[1], "right", board.transform.TransformPoint(new Vector3(1, 0, 0)));
            Check(recorder.Drops == 1, "Off-board release drops");
            detector.Release(board.Objects[1], "right", board.transform.TransformPoint(board.Target(1)), true);
            Check(recorder.Drops == 2 && !board.Objects[1].Scored, "Tracking loss at target never scores");
            recorder.RecordSample(Sample(0, 0, 0, true));
            recorder.RecordSample(Sample(1, 40, 1, true));
            recorder.RecordSample(Sample(2, 80, 0, false));
            recorder.RecordSample(Sample(3, 120, 8, true));
            recorder.RecordSample(Sample(4, 500, 9, true));
            Check(Mathf.Abs(recorder.LocalMetrics().total_distance_m - 1) < 0.001f, "Invalid samples and long gaps do not bridge motion");
            for (int i = 1; i < 6; i++) detector.Release(board.Objects[i], i % 2 == 0 ? "left" : "right", board.transform.TransformPoint(board.Target(i)));
            Check(exercise.State == ExerciseState.Completed && recorder.SuccessfulTransfers == 6, "Six unique transfers complete attempt");

            var commands = root.AddComponent<CommandPoller>(); commands.exercise = exercise; commands.board = board;
            string future = DateTimeOffset.UtcNow.AddMinutes(2).ToString("O");
            Check(commands.Apply(new InstructorCommand { type = "highlight_target", targetId = "unknown", expiresAt = future }).status == "rejected", "Unknown target rejected");
            Check(commands.Apply(new InstructorCommand { type = "request_retry", expiresAt = "2000-01-01T00:00:00Z" }).status == "rejected", "Expired command rejected");
            Check(commands.Apply(new InstructorCommand { type = "move_tool", expiresAt = future }).status == "rejected", "Transform command rejected");
            Check(commands.Apply(new InstructorCommand { type = "highlight_target", targetId = "target_2", expiresAt = future }).status == "applied", "Valid highlight applied");

            var api = root.AddComponent<ApiClient>();
            api.baseUrl = "http://example.com"; api.allowEditorHttp = true;
            Check(!api.IsConfigured, "Remote plaintext denied");
            api.baseUrl = "https://example.com";
            Check(api.IsConfigured, "HTTPS accepted");
            var queue = root.AddComponent<UploadQueue>(); queue.api = api; queue.maxSamplesPerAttempt = 1; queue.maxEventsPerAttempt = 1; queue.maxAttempts = 1;
            Check(queue.BeginAttempt("bounded", "test") && !queue.BeginAttempt("overflow", "test"), "Attempt queue bound");
            queue.EnqueueSample("bounded", Sample(0, 0, 0, true)); queue.EnqueueSample("bounded", Sample(1, 40, 1, true));
            Check(queue.Find("bounded").samples.Count == 1 && queue.Find("bounded").telemetryIncomplete, "Sample overflow explicitly withholds scoring");
            var record = new RecordedEvent { eventId = eventId, type = "transfer_success", instrument = "left", objectId = "peg_1" };
            queue.EnqueueEvent("bounded", record); queue.EnqueueEvent("bounded", record);
            Check(queue.Find("bounded").events.Count == 1 && queue.Find("bounded").blocked != null, "Event overflow bounded and visible");
            queue.RetryBlocked();
            Check(queue.Find("bounded").blocked != null, "Retry cannot conceal lost telemetry");
            queue.Find("bounded").finished = true;
            Check(!queue.BeginAttempt("overflow", "test"), "Resuming an unbuffered attempt cannot upload partial evidence");

            string url = Environment.GetEnvironmentVariable("JEREMY_VERIFY_URL");
            if (!string.IsNullOrEmpty(url))
            {
                api.baseUrl = "";
                var live = root.AddComponent<UploadQueue>(); live.api = api;
                string path = Path.Combine(Application.dataPath, "../verification-queue-" + Guid.NewGuid().ToString("N") + ".json");
                live.Initialize(path);
                live.BeginAttempt("round-trip", "unity-verification");
                live.EnqueueEvent("round-trip", new RecordedEvent { eventId = "unity-retry-event", type = "transfer_success", instrument = "left",
                    objectId = "peg_1", sourceLocal = Vector3.zero, targetLocal = new Vector3(0.2f, 0, 0), elapsedMs = 1000 });
                live.EnqueueEvent("round-trip", new RecordedEvent { eventId = "unity-retry-event", type = "transfer_success", instrument = "left",
                    objectId = "peg_1", sourceLocal = Vector3.zero, targetLocal = new Vector3(0.2f, 0, 0), elapsedMs = 1000 });
                live.EnqueueSample("round-trip", Sample(0, 0, 0, true));
                live.EnqueueSample("round-trip", Sample(1, 40, 0.2f, true));
                live.EndAttempt("round-trip", 1, false);
                yield return new WaitForSecondsRealtime(1.1f);
                Check(live.Find("round-trip").events.Count == 2 && File.Exists(path), "Offline events persisted before connectivity");
                // Reload from disk, exercising stable IDs and recovery after process interruption.
                DestroyImmediate(live);
                live = root.AddComponent<UploadQueue>(); live.api = api; live.Initialize(path);
                Check(live.Find("round-trip").events[0].event_id == "unity-retry-event", "Event identity survives disk recovery");
                api.baseUrl = "http://127.0.0.1:9";
                double retryDeadline = Time.realtimeSinceStartupAsDouble + 10;
                while (live.Find("round-trip").failures == 0 && Time.realtimeSinceStartupAsDouble < retryDeadline) yield return null;
                Check(live.Find("round-trip").failures > 0 && live.Find("round-trip").events.Count == 2, "Failed HTTP attempt retains retry data");
                api.baseUrl = url;
                double deadline = Time.realtimeSinceStartupAsDouble + 45;
                while (!live.Find("round-trip").finished && string.IsNullOrEmpty(live.Find("round-trip").blocked) && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
                PendingAttempt uploaded = live.Find("round-trip");
                Check(uploaded.finished, "Round trip complete: " + live.Status);
                Check(uploaded.metrics.successful_transfers == 1 && Math.Abs(uploaded.metrics.total_distance_m - 0.2) < 0.001, "Real backend metrics match telemetry");
                Check(uploaded.cost.lines.Length == 4 && uploaded.cost.total_usd > 0, "Real backend itemized cost returned");
                Check(uploaded.events.Count == 0 && uploaded.samples.Count == 0, "Acknowledged queue drained");
                Debug.Log("JEREMY BACKEND ROUND TRIP PASSED");
            }
            exercise.ResetExercise(); exercise.Begin(); exercise.Advance(4000);
            Check(exercise.State == ExerciseState.Completed && exercise.ElapsedSeconds == 3600, "Duration bound matches API");
            DestroyImmediate(board.gameObject);
            DestroyImmediate(root);
        }
        private static PoseSample Sample(int index, long time, float x, bool valid) => new PoseSample
        { instrument = "left", interaction = "idle", idx = index, t_ms = time, valid = valid, pos = new[] { x, 0f, 0f }, quat = new[] { 0f, 0f, 0f, 1f } };
    }
}
#endif
