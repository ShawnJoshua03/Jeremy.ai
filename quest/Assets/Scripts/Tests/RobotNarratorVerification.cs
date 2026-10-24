#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Jeremy.Tests
{
    // Loopback-only fake backend; no Gemini, Cartesia, keys, or external network access.
    public sealed class RobotNarratorVerification : MonoBehaviour
    {
        public Action<bool> Finished;
        private int assertions;
        private MockNarrator server;
        private GameObject root, robot;
        private RobotNarrator narrator;
        private void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            assertions++;
        }
        private IEnumerator Start()
        {
            Exception failure = null;
            IEnumerator checks = RunChecks();
            // Flatten nested enumerators so a timeout/assertion always fails the batch invocation.
            var stack = new System.Collections.Generic.Stack<IEnumerator>();
            stack.Push(checks);
            while (stack.Count > 0 && failure == null)
            {
                object current = null;
                try
                {
                    if (!stack.Peek().MoveNext()) { stack.Pop(); continue; }
                    current = stack.Peek().Current;
                    if (current is IEnumerator nested) { stack.Push(nested); continue; }
                }
                catch (Exception error) { failure = error; }
                yield return current;
            }
            if (robot != null) Destroy(robot);
            if (root != null) Destroy(root);
            server?.Dispose();
            if (failure != null) Debug.LogError("JEREMY NARRATOR VERIFICATION FAILED: " + failure);
            else Debug.Log("JEREMY NARRATOR VERIFICATION PASSED: " + assertions + " assertions");
            Finished?.Invoke(failure == null);
        }
        private IEnumerator Until(Func<bool> condition)
        {
            float deadline = Time.realtimeSinceStartup + 12f;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Check(condition(), "Timed out waiting for mock narrator: " + narrator.Status);
        }
        private bool Idle => typeof(RobotNarrator).GetField("polling", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(narrator) == null;
        private void PollNow() => typeof(RobotNarrator).GetField("nextPoll", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(narrator, 0f);

        private IEnumerator RunChecks()
        {
            server = new MockNarrator();
            root = new GameObject("Narrator verification dependencies");
            var api = root.AddComponent<ApiClient>();
            api.baseUrl = server.Origin; api.allowEditorHttp = true;
            api.apiKey = "mock-backend-key"; api.sessionToken = "mock-session-token";
            var exercise = root.AddComponent<ExerciseStateMachine>();
            var uploads = root.AddComponent<UploadQueue>(); // Never starts its uploader or touches queue storage.
            robot = new GameObject("Narrator verification robot");
            narrator = robot.AddComponent<RobotNarrator>();
            narrator.api = api; narrator.exercise = exercise; narrator.uploads = uploads;
            AudioSource voice = robot.GetComponent<AudioSource>();
            exercise.ResetExercise();
            uploads.BeginAttempt(exercise.AttemptId, "mock", PegTransferRig.PegTransferId);
            PendingAttempt attempt = uploads.Find(exercise.AttemptId);
            attempt.started = true; attempt.sessionId = "mock-session";
            exercise.Begin();
            yield return null; yield return null;
            Check(server.Lines == 0 && Idle, "Peg transfer must never request narration");
            attempt.exercise = PegTransferRig.LapCholeId;
            attempt.started = false;
            yield return null; yield return null;
            Check(server.Lines == 0 && Idle, "Wait for server start acknowledgement");
            attempt.started = true;
            yield return Until(() => server.Audio == 1 && Idle);
            Check(narrator.Subtitle == "Keep <b>both</b> jaws visible.", "503 preserves backend text verbatim");
            Check(!robot.GetComponentInChildren<Text>().supportRichText, "Backend subtitle is plain text");
            Check(voice.clip == null && !voice.isPlaying, "503 does not start audio");
            Check(server.ValidRequests, "POST paths, empty JSON bodies, and shared auth headers");
            Color eye = robot.transform.Find("Robot head and subtitles/Left eye").GetComponent<Renderer>().sharedMaterial.color;
            Check(eye.g > eye.r, "On-track eyes are green");
            foreach (Collider collider in robot.GetComponentsInChildren<Collider>()) Check(!collider.enabled, "Robot cannot collide with instruments");
            float next = (float)typeof(RobotNarrator).GetField("nextPoll", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(narrator);
            Check(next - Time.unscaledTime > 5.5f && next - Time.unscaledTime <= 10f, "Poll cadence stays within six to ten seconds");

            server.Line = "{\"text\":\"Repeated\",\"step\":\"veress\",\"severity\":\"slow\",\"repeat\":true}";
            PollNow();
            yield return Until(() => server.Lines == 2 && Idle);
            Check(server.Audio == 1 && narrator.Subtitle.StartsWith("Keep"), "Repeat suppresses speech and keeps previous subtitle");
            eye = robot.transform.Find("Robot head and subtitles/Left eye").GetComponent<Renderer>().sharedMaterial.color;
            Check(eye.r > eye.g && eye.g > eye.b, "Slow eyes are amber");

            server.Line = "{\"text\":\"Protect the duct.\",\"step\":\"dissect\",\"severity\":\"critical\",\"repeat\":false}";
            server.AudioStatus = 200;
            PollNow();
            yield return Until(() => server.Audio == 2 && Idle);
            Check(voice.clip != null && voice.clip.samples > 0, "WAV response decodes into owned audio clip");
            eye = robot.transform.Find("Robot head and subtitles/Left eye").GetComponent<Renderer>().sharedMaterial.color;
            Check(eye.r > eye.g * 3f, "Critical eyes are red");
            exercise.Pause();
            Check(voice.clip == null && narrator.Subtitle == "", "Pause immediately stops and clears old speech");
            yield return null;
            int calls = server.Lines;
            PollNow(); yield return null; yield return null;
            Check(server.Lines == calls, "Paused attempt does not poll");

            server.DelayLine = true;
            exercise.Resume();
            PollNow();
            yield return Until(() => server.Lines > calls);
            exercise.ResetExercise();
            server.ReleaseLine.TrySetResult(true);
            yield return Until(() => Idle);
            Check(narrator.Subtitle == "" && server.Audio == 2, "Late response after reset cannot speak in the new attempt");
            Check(!robot.transform.GetChild(0).gameObject.activeSelf, "Robot hides outside the lap chole attempt");

            server.DelayLine = false;
            uploads.BeginAttempt(exercise.AttemptId, "mock", PegTransferRig.LapCholeId);
            attempt = uploads.Find(exercise.AttemptId); attempt.started = true; attempt.sessionId = "mock-session";
            server.AudioStatus = 503;
            exercise.Begin();
            yield return Until(() => server.Audio == 3 && Idle);
            narrator.enabled = false;
            Check(narrator.Subtitle == "" && voice.clip == null, "Disable clears subtitles and audio");
            narrator.enabled = true;
            yield return Until(() => server.Audio == 4 && Idle);
            Check(narrator.Subtitle == "Protect the duct.", "Re-enable resumes polling with text fallback");
            exercise.Complete();
            calls = server.Lines;
            yield return null; PollNow(); yield return null; yield return null;
            Check(server.Lines == calls && narrator.Subtitle == "", "Completion stops narration");
            Check(server.ValidRequests, "All narrator requests use the contract and backend auth");
        }

        private sealed class MockNarrator : IDisposable
        {
            private readonly HttpListener listener = new HttpListener();
            public readonly string Origin;
            private int lines, audioCalls;
            public int Lines => Volatile.Read(ref lines);
            public int Audio => Volatile.Read(ref audioCalls);
            public volatile bool ValidRequests = true, DelayLine;
            public volatile int AudioStatus = 503;
            public volatile string Line = "{\"text\":\"Keep <b>both</b> jaws visible.\",\"step\":\"veress\",\"severity\":\"on_track\",\"repeat\":false}";
            public readonly TaskCompletionSource<bool> ReleaseLine = new TaskCompletionSource<bool>();
            public MockNarrator()
            {
                var probe = new TcpListener(IPAddress.Loopback, 0);
                probe.Start(); int port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
                Origin = "http://127.0.0.1:" + port;
                listener.Prefixes.Add(Origin + "/"); listener.Start();
                _ = Serve();
            }
            private async Task Serve()
            {
                try
                {
                    while (listener.IsListening)
                    {
                        HttpListenerContext context = await listener.GetContextAsync();
                        string path = context.Request.Url.AbsolutePath;
                        bool audio = path == "/api/v1/sessions/mock-session/narrator/line/audio";
                        using (var reader = new StreamReader(context.Request.InputStream))
                            ValidRequests &= await reader.ReadToEndAsync() == "{}";
                        ValidRequests &= context.Request.HttpMethod == "POST"
                            && context.Request.Headers["X-API-Key"] == "mock-backend-key"
                            && context.Request.Headers["Authorization"] == "Bearer mock-session-token"
                            && (audio || path == "/api/v1/sessions/mock-session/narrator/line");
                        if (audio) Interlocked.Increment(ref audioCalls); else Interlocked.Increment(ref lines);
                        if (!audio && DelayLine) await ReleaseLine.Task;
                        context.Response.StatusCode = audio ? AudioStatus : 200;
                        context.Response.ContentType = audio && AudioStatus == 200 ? "audio/wav" : "application/json";
                        byte[] body = audio && AudioStatus == 200 ? Wav() : Encoding.UTF8.GetBytes(audio ? "{\"detail\":\"Speech unavailable\"}" : Line);
                        context.Response.ContentLength64 = body.Length;
                        await context.Response.OutputStream.WriteAsync(body, 0, body.Length);
                        context.Response.Close();
                    }
                }
                catch (Exception) { if (listener.IsListening) ValidRequests = false; }
            }
            private static byte[] Wav()
            {
                const int rate = 16000, count = 16000;
                using (var stream = new MemoryStream())
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + count * 2);
                    writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
                    writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2);
                    writer.Write((short)2); writer.Write((short)16);
                    writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(count * 2);
                    for (int i = 0; i < count; i++) writer.Write((short)(Math.Sin(i * 2 * Math.PI * 220 / rate) * 4000));
                    return stream.ToArray();
                }
            }
            public void Dispose() { ReleaseLine.TrySetResult(true); listener.Close(); }
        }
    }
}
#endif
