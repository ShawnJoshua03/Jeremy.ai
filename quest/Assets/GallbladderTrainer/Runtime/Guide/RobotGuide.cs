using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using GallbladderTrainer.Backend;

namespace GallbladderTrainer.Guide
{
    /// <summary>
    /// Jeremy, the hovering robot guide. A small holographic head built from primitives: gentle bob, eased look-at
    /// toward the trainee, eyes tinted by narrator severity, mouth and eye glow driven by voice loudness, and a
    /// holographic subtitle panel that fades in and out.
    /// Polls POST narrator/line every 8 s and right after each decision, plays narrator/line/audio for new lines
    /// (text only on 503), and mirrors each line to the trainer's feedback panel via TrainingEvents.SendExternalFeedback.
    /// All wording comes from the backend; the robot never computes a score.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class RobotGuide : MonoBehaviour
    {
        [SerializeField] internal JeremyBackendHook hook;
        [SerializeField, Range(4f, 20f)] float pollSeconds = 8f;
        [SerializeField] float decisionPollTimeout = 2f;
        [SerializeField] bool mirrorToFeedbackPanel = true;

        /// <summary>The API crew beside the robot (no-op until Runtime/Crew is merged).</summary>
        public CrewBridge Crew { get { return crew; } }
        public bool IsSpeaking { get { return voice != null && (voice.isPlaying || audioBusy); } }
        public string Subtitle { get { return subtitle != null ? subtitle.text : ""; } }

        AudioSource voice;
        CrewBridge crew;
        AudioClip ownedClip;
        Transform head, leftEye, rightEye, mouth, ringA, ringB;
        Material shellMat, visorMat, glowMat, ringMat;
        HoloUi.Panel panel;
        Text subtitle, header;
        Camera cam;
        Vector3 basePos;
        Color eyeColor = HoloUi.Cyan, eyeTarget = HoloUi.Cyan;
        float amplitude, subtitleAlpha, subtitleUntil, nextPoll, nextBlink, blinkT = 1f, decisionPollAt = -1f;
        bool polling, audioBusy, narrationOn = true, decisionPending, announcedAll;
        string severity = "";
        readonly float[] samples = new float[256];
        static readonly Vector3 EyeScale = new Vector3(0.024f, 0.024f, 0.01f);
        static readonly Vector3 MouthScale = new Vector3(0.05f, 0.005f, 0.008f);

        // ------------------------------------------------------------------ lifecycle

        void Awake()
        {
            voice = GetComponent<AudioSource>();
            voice.playOnAwake = false;
            voice.loop = false;
            voice.spatialBlend = 0.6f;
            voice.minDistance = 0.7f;
            voice.maxDistance = 6f;
            if (hook == null) hook = GetComponentInParent<JeremyBackendHook>();
            if (hook == null) hook = FindAnyObjectByType<JeremyBackendHook>();
            BuildHead();
            BuildSubtitle();
            crew = CrewBridge.Create(transform.parent != null ? transform.parent : transform, transform.localPosition + new Vector3(0.30f, -0.02f, 0f));
            basePos = transform.localPosition;
            nextBlink = Time.unscaledTime + 3f;
        }

        void OnEnable()
        {
            if (hook == null) return;
            hook.DecisionRecorded += OnDecision;
            hook.EventsFlushed += OnFlushed;
            hook.SessionReady += OnSessionReady;
            hook.StateChanged += OnStateChanged;
        }

        void OnDisable()
        {
            if (hook != null)
            {
                hook.DecisionRecorded -= OnDecision;
                hook.EventsFlushed -= OnFlushed;
                hook.SessionReady -= OnSessionReady;
                hook.StateChanged -= OnStateChanged;
            }
            StopVoice();
            StopAllCoroutines();
            polling = false; audioBusy = false;
        }

        void Start()
        {
            Say("Hi, I'm Jeremy. Explore the anatomy, then switch to Assess when you're ready. I'll check in as you go.", "", 7f);
        }

        void OnDestroy()
        {
            HoloUi.Kill(shellMat); HoloUi.Kill(visorMat); HoloUi.Kill(glowMat); HoloUi.Kill(ringMat);
            if (ownedClip != null) Destroy(ownedClip);
        }

        void OnSessionReady(string sid) { nextPoll = Time.unscaledTime + 2f; }

        void OnStateChanged(BackendState s)
        {
            if (s == BackendState.LocalOnly && hook.SessionId == null)
                Say("I can't reach the coaching server, so this is local practice. Your answers still get the built-in feedback.", "slow", 7f);
        }

        void OnDecision(DecisionEvent e)
        {
            decisionPending = true;
            decisionPollAt = Time.unscaledTime + decisionPollTimeout; // poll even if the flush callback never comes
            if (!announcedAll && hook.AllAnswered)
            {
                announcedAll = true;
                StartCoroutine(AnnounceAllAnsweredLater());
            }
        }

        IEnumerator AnnounceAllAnsweredLater()
        {
            yield return new WaitForSecondsRealtime(9f);
            while (IsSpeaking) yield return null;
            if (hook.State != BackendState.Completing && hook.State != BackendState.Completed)
                Say("Every exercise has an answer. Press Finish & review when you're ready for your session review.", "on_track", 8f);
        }

        void OnFlushed()
        {
            if (decisionPending) decisionPollAt = Mathf.Min(decisionPollAt, Time.unscaledTime + 0.15f);
        }

        /// <summary>Stops narrator polling (e.g. while the end-of-session report is shown).</summary>
        public void SetNarration(bool on) { narrationOn = on; }

        void Update()
        {
            float dt = Time.unscaledDeltaTime, t = Time.unscaledTime;
            if (cam == null) cam = Camera.main;

            // calm hover + eased look-at (face is local -Z, so forward points away from the viewer)
            transform.localPosition = basePos + new Vector3(0f, Mathf.Sin(t * 1.4f) * 0.010f, 0f);
            if (cam != null)
            {
                Vector3 away = transform.position - cam.transform.position;
                if (away.sqrMagnitude > 1e-4f)
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(away, Vector3.up), HoloUi.Damp(3f, dt));
            }
            head.localRotation = Quaternion.Euler(Mathf.Sin(t * 0.9f) * 3f, Mathf.Sin(t * 0.6f) * 4f, 0f);
            ringA.localRotation = Quaternion.Euler(90f, 0f, t * 25f);
            ringB.localRotation = Quaternion.Euler(90f, 0f, -t * 15f);

            AnimateVoice(dt);
            AnimateEyes(dt, t);
            AnimateSubtitle(dt, t);
            if (voice.isPlaying || amplitude > 0.01f) crew.Speak("cartesia", voice.isPlaying ? amplitude : 0f);

            if (!narrationOn || polling || IsSpeaking || hook == null || hook.SessionId == null) return;
            if (hook.State != BackendState.Active && hook.State != BackendState.Retrying) return;
            bool decisionDue = decisionPending && t >= decisionPollAt;
            if (decisionDue || t >= nextPoll) StartCoroutine(Poll(decisionDue));
        }

        // ------------------------------------------------------------------ narrator

        IEnumerator Poll(bool afterDecision)
        {
            polling = true;
            if (afterDecision) decisionPending = false;
            nextPoll = Time.unscaledTime + pollSeconds;
            string sid = hook.SessionId;
            ApiResponse r = null;
            yield return hook.Api.Send("POST", JeremyApi.SessionPath(sid) + "/narrator/line", "{}", x => r = x);
            if (r != null && r.Ok)
            {
                object line = Json.Parse(r.Body);
                string text = Json.Str(line, "text");
                string sev = Json.Str(line, "severity", "");
                bool repeat = Json.Bool(line, "repeat") ?? false;
                string provider = Json.Str(Json.Obj(line, "provider"), "text");
                if (provider != null)
                {
                    crew.SetState("claude", provider == "claude" ? "used" : "fallback");
                    if (provider == "claude") crew.Pulse("claude", "wrote the line");
                }
                SetSeverity(sev);
                if (!repeat && !string.IsNullOrEmpty(text))
                {
                    Say(text, sev, 0f);
                    if (mirrorToFeedbackPanel) Mirror(text, provider, afterDecision ? Json.Str(line, "step") : null);
                    yield return SpeakFrom(JeremyApi.SessionPath(sid) + "/narrator/line/audio", null);
                }
            }
            else if (r != null && r.Code == 409)
            {
                narrationOn = false; // backend does not narrate this exercise type; stay quiet instead of retrying
                Debug.LogWarning("[Jeremy] Narrator not available for this session: " + r.Body);
            }
            nextPoll = Mathf.Max(nextPoll, Time.unscaledTime + pollSeconds * 0.5f);
            polling = false;
        }

        void Mirror(string text, string provider, string exerciseId)
        {
            string label = "Jeremy coach (" + (provider == "claude" ? "Claude" : provider == null ? "backend" : "template") + ")";
            string body = text;
            LocalDecision d;
            if (exerciseId != null && hook.Decisions.TryGetValue(exerciseId, out d))
            {
                // Keep the authored per-exercise explanation visible next to the coach line.
                Exercise ex = HoloUi.FindExercise(exerciseId);
                string word = d.Result == ResultStatus.Match ? "<color=#7DDC9C><b>[OK] Matches the prepared exercise</b></color>"
                            : d.Result == ResultStatus.Mismatch ? "<color=#FF9A76><b>[X] Does not match the prepared exercise</b></color>"
                            : "<color=#F2B84B><b>[?] Instructor review requested</b></color>";
                string authored = ex == null ? "" : d.Result == ResultStatus.Match ? ex.FeedbackMatch : d.Result == ResultStatus.Mismatch ? ex.FeedbackMismatch : "";
                body = word + "\n" + (authored.Length > 0 ? authored + "\n\n" : "") + "<b>Jeremy:</b> " + text;
            }
            TrainingEvents.SendExternalFeedback(new ExternalFeedback { Text = body, SourceLabel = label });
        }

        /// <summary>POSTs path, plays the returned audio/wav. 503 or bad audio leaves the subtitle as the fallback.</summary>
        public IEnumerator SpeakFrom(string path, Action<bool> done)
        {
            audioBusy = true;
            ApiResponse r = null;
            yield return hook.Api.Send("POST", path, "{}", "audio/wav", x => r = x);
            audioBusy = false;
            bool ok = false;
            if (r != null && r.Ok)
            {
                AudioClip clip = WavClip.Create(r.Data, "jeremy-voice");
                if (clip != null)
                {
                    StopVoice();
                    ownedClip = clip; voice.clip = clip; voice.Play();
                    subtitleUntil = Time.unscaledTime + clip.length + 2.5f;
                    ok = true;
                    string speech = r.Header("X-Speech-Provider");
                    if (speech == "cartesia")
                    {
                        crew.SetState("cartesia", "used");
                        crew.Pulse("cartesia", "spoke the line");
                    }
                }
            }
            else if (r != null && r.Code == 503) crew.SetState("cartesia", "unavailable");
            if (done != null) done(ok);
        }

        /// <summary>Shows a subtitle. holdSeconds 0 = auto from length.</summary>
        public void Say(string text, string sev, float holdSeconds)
        {
            if (string.IsNullOrEmpty(text)) return;
            subtitle.text = text;
            SetSeverity(sev);
            float auto = Mathf.Clamp(text.Length * 0.065f, 4f, 16f);
            subtitleUntil = Time.unscaledTime + (holdSeconds > 0f ? holdSeconds : auto);
        }

        void SetSeverity(string sev)
        {
            severity = sev ?? "";
            eyeTarget = HoloUi.SeverityColor(severity);
            string s = HoloUi.SeverityLabel(severity);
            header.text = s.Length > 0 ? "JEREMY  ·  " + s.ToUpperInvariant() : "JEREMY";
            header.color = Color.Lerp(eyeTarget, Color.white, 0.25f);
        }

        void StopVoice()
        {
            if (voice != null) { voice.Stop(); voice.clip = null; }
            if (ownedClip != null) { Destroy(ownedClip); ownedClip = null; }
        }

        // ------------------------------------------------------------------ animation

        void AnimateVoice(float dt)
        {
            float energy = 0f;
            if (voice.isPlaying)
            {
                voice.GetOutputData(samples, 0);
                for (int i = 0; i < samples.Length; i++) energy += samples[i] * samples[i];
            }
            float target = Mathf.Clamp01(Mathf.Sqrt(energy / samples.Length) * 10f);
            amplitude = Mathf.Lerp(amplitude, target, HoloUi.Damp(18f, dt));
            mouth.localScale = new Vector3(MouthScale.x * (1f - amplitude * 0.2f), MouthScale.y + amplitude * 0.026f, MouthScale.z);
        }

        void AnimateEyes(float dt, float t)
        {
            eyeColor = Color.Lerp(eyeColor, eyeTarget, HoloUi.Damp(3f, dt));
            Color glow = Color.Lerp(eyeColor, Color.white, amplitude * 0.45f);
            HoloUi.SetColor(glowMat, glow);
            HoloUi.SetColor(ringMat, new Color(eyeColor.r, eyeColor.g, eyeColor.b, 0.35f + amplitude * 0.4f));
            if (t >= nextBlink) { blinkT = 0f; nextBlink = t + UnityEngine.Random.Range(3f, 6.5f); }
            blinkT = Mathf.Min(1f, blinkT + dt * 7f);
            float open = blinkT < 1f ? Mathf.Abs(Mathf.Cos(blinkT * Mathf.PI)) : 1f;
            Vector3 eye = EyeScale * (1f + amplitude * 0.25f);
            eye.y *= Mathf.Max(0.12f, open);
            leftEye.localScale = rightEye.localScale = eye;
        }

        void AnimateSubtitle(float dt, float t)
        {
            bool show = subtitle.text.Length > 0 && (t < subtitleUntil || voice.isPlaying);
            subtitleAlpha = Mathf.MoveTowards(subtitleAlpha, show ? 1f : 0f, dt * (show ? 3f : 1.2f));
            panel.Group.alpha = HoloUi.InOutSine(subtitleAlpha);
            panel.Root.localScale = Vector3.one * 0.0007f * (0.96f + 0.04f * HoloUi.OutCubic(subtitleAlpha));
            HoloUi.AssignCamera(panel.Canvas);
        }

        // ------------------------------------------------------------------ construction

        void BuildHead()
        {
            shellMat = HoloUi.Lit(new Color(0.80f, 0.88f, 0.94f), new Color(0.04f, 0.10f, 0.14f), 0.9f);
            visorMat = HoloUi.Lit(new Color(0.02f, 0.04f, 0.06f), Color.black, 0.95f);
            glowMat = HoloUi.Glowing(HoloUi.Cyan);
            ringMat = HoloUi.Translucent(new Color(0.24f, 0.86f, 1f, 0.4f));

            head = new GameObject("Head").transform;
            head.SetParent(transform, false);
            HoloUi.Primitive(head, "Shell", PrimitiveType.Sphere, Vector3.zero, new Vector3(0.16f, 0.13f, 0.12f), shellMat);
            HoloUi.Primitive(head, "Visor", PrimitiveType.Sphere, new Vector3(0f, 0.004f, -0.032f), new Vector3(0.13f, 0.085f, 0.06f), visorMat);
            leftEye = HoloUi.Primitive(head, "Left eye", PrimitiveType.Sphere, new Vector3(-0.03f, 0.016f, -0.061f), EyeScale, glowMat).transform;
            rightEye = HoloUi.Primitive(head, "Right eye", PrimitiveType.Sphere, new Vector3(0.03f, 0.016f, -0.061f), EyeScale, glowMat).transform;
            mouth = HoloUi.Primitive(head, "Mouth", PrimitiveType.Cube, new Vector3(0f, -0.02f, -0.06f), MouthScale, glowMat).transform;
            for (int s = -1; s <= 1; s += 2)
            {
                var ear = HoloUi.Primitive(head, "Ear", PrimitiveType.Cylinder, new Vector3(s * 0.08f, 0f, 0f), new Vector3(0.035f, 0.006f, 0.035f), shellMat);
                ear.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                var light = HoloUi.Primitive(head, "Ear light", PrimitiveType.Cylinder, new Vector3(s * 0.0835f, 0f, 0f), new Vector3(0.02f, 0.002f, 0.02f), glowMat);
                light.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }
            HoloUi.Primitive(head, "Antenna", PrimitiveType.Cylinder, new Vector3(0f, 0.08f, 0f), new Vector3(0.005f, 0.022f, 0.005f), shellMat);
            HoloUi.Primitive(head, "Antenna light", PrimitiveType.Sphere, new Vector3(0f, 0.105f, 0f), Vector3.one * 0.016f, glowMat);

            ringA = HoloUi.Circle(transform, "Hover ring", 0.07f, 0.003f, ringMat).transform;
            ringA.localPosition = new Vector3(0f, -0.085f, 0f);
            ringB = HoloUi.Circle(transform, "Hover ring outer", 0.095f, 0.0015f, ringMat).transform;
            ringB.localPosition = new Vector3(0f, -0.092f, 0f);
        }

        void BuildSubtitle()
        {
            panel = HoloUi.MakePanel(transform, "Subtitle", 640, 180, 0.0007f, false);
            panel.Root.localPosition = new Vector3(0f, -0.16f, 0f);
            header = HoloUi.Label(panel.Root, "Header", "JEREMY", 20, HoloUi.Cyan, TextAnchor.UpperLeft, 22, 12, 596, 26, true);
            subtitle = HoloUi.Label(panel.Root, "Text", "", 25, HoloUi.TextMain, TextAnchor.UpperLeft, 22, 42, 596, 128);
            subtitle.resizeTextForBestFit = true;
            subtitle.resizeTextMinSize = 16;
            subtitle.resizeTextMaxSize = 25;
            panel.Group.alpha = 0f;
        }
    }
}
