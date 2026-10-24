using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace Jeremy
{
    [Serializable]
    public sealed class NarratorLine
    {
        public string text, step, severity;
        public bool repeat;
    }

    // Backend-only speech: no provider SDKs or provider credentials belong on the headset.
    [RequireComponent(typeof(AudioSource))]
    public sealed class RobotNarrator : MonoBehaviour
    {
        public ApiClient api;
        public UploadQueue uploads;
        public ExerciseStateMachine exercise;
        public Transform viewer;
        public GameObject guidanceObject;
        [Range(6f, 10f)] public float pollSeconds = 8f;
        public string Status { get; private set; } = "Waiting for laparoscopic cholecystectomy";
        public string Subtitle => subtitle != null ? subtitle.text : "";

        private AudioSource voice;
        private AudioClip ownedClip;
        private UnityWebRequest audioRequest;
        private IEnumerator lineRequest;
        private Coroutine polling;
        private GameObject visual;
        private Transform mouth, leftEye, rightEye;
        private Material shellMaterial, faceMaterial, eyeMaterial;
        private Text subtitle;
        private readonly float[] samples = new float[256];
        private readonly List<AudioSource> guidanceSources = new List<AudioSource>();
        private bool guidanceSpeaking;
        private ExerciseStateMachine subscribedExercise;
        private string session;
        private int generation;
        private float nextPoll, amplitude;
        private static readonly Vector3 EyeScale = new Vector3(0.026f, 0.026f, 0.012f);

        private void Awake()
        {
            voice = GetComponent<AudioSource>();
            voice.playOnAwake = false;
            voice.loop = false;
            voice.spatialBlend = 0.65f;
            voice.minDistance = 0.7f;
            voice.maxDistance = 5f;
            BuildHead();
            visual.SetActive(false);
        }

        private string CurrentSession()
        {
            if (api == null || !api.IsConfigured || uploads == null || exercise == null
                || exercise.State != ExerciseState.Running) return null;
            PendingAttempt attempt = uploads.Find(exercise.AttemptId);
            return attempt != null && attempt.exercise == PegTransferRig.LapCholeId && attempt.started
                && !attempt.cancelled && !attempt.finished && !attempt.completed
                && !string.IsNullOrEmpty(attempt.sessionId) ? attempt.sessionId : null;
        }

        private void Update()
        {
            if (subscribedExercise != exercise)
            {
                if (subscribedExercise != null) subscribedExercise.Changed -= OnExerciseState;
                subscribedExercise = exercise;
                if (subscribedExercise != null) subscribedExercise.Changed += OnExerciseState;
            }
            guidanceSpeaking = false;
            if (guidanceObject != null)
            {
                guidanceObject.GetComponents(guidanceSources);
                foreach (AudioSource source in guidanceSources) guidanceSpeaking |= source != voice && source.isPlaying;
            }
            if (guidanceSpeaking && (voice.isPlaying || audioRequest != null)) StopVoice();
            string current = CurrentSession();
            if (session != current)
            {
                generation++;
                session = current;
                StopVoice();
                subtitle.text = "";
                TintEyes("unknown");
                nextPoll = Time.unscaledTime;
            }
            // Also show the idle robot during offline lap_chole practice.
            PendingAttempt attempt = exercise != null && uploads != null ? uploads.Find(exercise.AttemptId) : null;
            visual.SetActive(attempt != null && attempt.exercise == PegTransferRig.LapCholeId);
            if (viewer != null)
            {
                Vector3 away = transform.position - viewer.position;
                if (away.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(away, Vector3.up);
            }
            if (session != null && polling == null && !voice.isPlaying && !guidanceSpeaking && Time.unscaledTime >= nextPoll)
                polling = StartCoroutine(Poll(session, generation));
            AnimateVoice();
        }

        private bool StillCurrent(string expected, int version) => isActiveAndEnabled
            && generation == version && CurrentSession() == expected && !guidanceSpeaking;

        private void OnExerciseState(ExerciseState state)
        {
            if (state == ExerciseState.Running) return;
            generation++;
            session = null;
            StopVoice();
            subtitle.text = "";
        }

        private IEnumerator Poll(string expected, int version)
        {
            // Always yield once so the coroutine handle is assigned even for immediate failures.
            yield return null;
            if (!StillCurrent(expected, version)) { polling = null; yield break; }
            nextPoll = Time.unscaledTime + Mathf.Clamp(pollSeconds, 6f, 10f);
            ApiResult response = null;
            // Own the helper's iterator so disabling this component also disposes its in-flight HTTP request.
            lineRequest = api.Send("POST", ApiClient.SessionPath(expected) + "/narrator/line", "{}", value => response = value);
            while (lineRequest != null && lineRequest.MoveNext()) yield return lineRequest.Current;
            (lineRequest as IDisposable)?.Dispose();
            lineRequest = null;
            if (StillCurrent(expected, version))
            {
                NarratorLine line = null;
                if (response != null && response.Success)
                {
                    try { line = JsonUtility.FromJson<NarratorLine>(response.Body); }
                    catch (Exception) { Status = "Invalid narrator response"; }
                }
                else Status = "Narrator unavailable; local procedure guidance remains available";
                if (line != null)
                {
                    TintEyes(line.severity);
                    if (!line.repeat && !string.IsNullOrWhiteSpace(line.text))
                    {
                        // Keep the exact backend text visible during loading, playback and any speech failure.
                        subtitle.text = line.text;
                        Status = "Loading narrator voice";
                        yield return FetchAudio(expected, version);
                    }
                }
            }
            // Slow requests back off too; do not immediately hammer a failing speech service.
            nextPoll = Mathf.Max(nextPoll, Time.unscaledTime + Mathf.Clamp(pollSeconds, 6f, 10f));
            polling = null;
        }

        private IEnumerator FetchAudio(string expected, int version)
        {
            Status = "Voice unavailable; read the subtitle";
            if (!StillCurrent(expected, version)
                || !api.TryUrl(ApiClient.SessionPath(expected) + "/narrator/line/audio", out string url)) yield break;
            using (var request = new UnityWebRequest(url, "POST"))
            {
                audioRequest = request;
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes("{}"));
                request.downloadHandler = new DownloadHandlerAudioClip(url, AudioType.WAV);
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Accept", "audio/wav");
                api.ApplyAuth(request);
                request.redirectLimit = 0;
                request.timeout = Mathf.Clamp(api.timeoutSeconds, 1, 120);
                yield return request.SendWebRequest();
                audioRequest = null;
                if (!StillCurrent(expected, version) || request.result != UnityWebRequest.Result.Success) yield break;
                AudioClip clip = null;
                try { clip = DownloadHandlerAudioClip.GetContent(request); }
                catch (Exception) { /* Invalid WAV has the same readable fallback as HTTP 503. */ }
                if (clip == null) yield break;
                StopVoice();
                ownedClip = clip;
                voice.clip = clip;
                voice.Play();
                Status = "Speaking";
            }
        }

        private void StopVoice()
        {
            if (audioRequest != null) audioRequest.Abort();
            if (voice != null) { voice.Stop(); voice.clip = null; }
            if (ownedClip != null) { Destroy(ownedClip); ownedClip = null; }
            amplitude = 0f;
        }

        private void AnimateVoice()
        {
            float energy = 0f;
            if (voice.isPlaying)
            {
                voice.GetOutputData(samples, 0);
                foreach (float sample in samples) energy += sample * sample;
            }
            float target = Mathf.Clamp01(Mathf.Sqrt(energy / samples.Length) * 12f);
            amplitude = Mathf.Lerp(amplitude, target, 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime));
            mouth.localScale = new Vector3(0.052f, 0.006f + amplitude * 0.03f, 0.01f);
            leftEye.localScale = rightEye.localScale = EyeScale * (1f + amplitude * 0.3f);
        }

        private void TintEyes(string severity)
        {
            Color color;
            switch (severity)
            {
                case "on_track": color = new Color(0.15f, 1f, 0.4f); break;
                case "slow": color = new Color(1f, 0.6f, 0.08f); break;
                case "off_track": case "critical": color = new Color(1f, 0.12f, 0.08f); break;
                default: color = new Color(0.3f, 0.75f, 1f); break;
            }
            eyeMaterial.color = color;
            if (eyeMaterial.HasProperty("_EmissionColor")) eyeMaterial.SetColor("_EmissionColor", color * 1.5f);
        }

        private void BuildHead()
        {
            visual = new GameObject("Robot head and subtitles");
            visual.transform.SetParent(transform, false);
            Shader shader = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null
                ? Shader.Find("Universal Render Pipeline/Lit") : Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Standard");
            shellMaterial = new Material(shader) { color = new Color(0.7f, 0.8f, 0.85f) };
            faceMaterial = new Material(shader) { color = new Color(0.025f, 0.045f, 0.065f) };
            eyeMaterial = new Material(shader);
            eyeMaterial.EnableKeyword("_EMISSION");
            TintEyes("unknown");
            Part("Shell", PrimitiveType.Sphere, Vector3.zero, new Vector3(0.18f, 0.14f, 0.12f), shellMaterial);
            Part("Face", PrimitiveType.Cube, new Vector3(0, 0, -0.055f), new Vector3(0.14f, 0.09f, 0.018f), faceMaterial);
            leftEye = Part("Left eye", PrimitiveType.Sphere, new Vector3(-0.035f, 0.018f, -0.071f), EyeScale, eyeMaterial);
            rightEye = Part("Right eye", PrimitiveType.Sphere, new Vector3(0.035f, 0.018f, -0.071f), EyeScale, eyeMaterial);
            mouth = Part("Mouth", PrimitiveType.Cube, new Vector3(0, -0.025f, -0.071f), new Vector3(0.052f, 0.006f, 0.01f), eyeMaterial);
            Part("Antenna", PrimitiveType.Cylinder, new Vector3(0, 0.09f, 0), new Vector3(0.006f, 0.025f, 0.006f), shellMaterial);
            Part("Antenna light", PrimitiveType.Sphere, new Vector3(0, 0.12f, 0), Vector3.one * 0.018f, eyeMaterial);
            var panel = new GameObject("Narrator subtitle", typeof(RectTransform), typeof(Canvas));
            panel.transform.SetParent(visual.transform, false);
            panel.transform.localPosition = new Vector3(0, -0.16f, -0.08f);
            panel.transform.localScale = Vector3.one * 0.0007f;
            panel.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var label = new GameObject("Text", typeof(RectTransform), typeof(Image));
            label.transform.SetParent(panel.transform, false);
            label.GetComponent<RectTransform>().sizeDelta = new Vector2(620, 200);
            label.GetComponent<Image>().color = new Color(0.015f, 0.025f, 0.04f, 0.92f);
            label.GetComponent<Image>().raycastTarget = false;
            var text = new GameObject("Subtitle", typeof(RectTransform), typeof(Text));
            text.transform.SetParent(label.transform, false);
            text.GetComponent<RectTransform>().sizeDelta = new Vector2(590, 180);
            subtitle = text.GetComponent<Text>();
            subtitle.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            subtitle.fontSize = 24;
            subtitle.resizeTextForBestFit = true;
            subtitle.resizeTextMinSize = 16;
            subtitle.resizeTextMaxSize = 24;
            subtitle.alignment = TextAnchor.MiddleCenter;
            subtitle.supportRichText = false;
            subtitle.raycastTarget = false;
            subtitle.color = Color.white;
        }

        private Transform Part(string partName, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = partName;
            part.transform.SetParent(visual.transform, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            part.GetComponent<Collider>().enabled = false;
            Destroy(part.GetComponent<Collider>());
            part.GetComponent<Renderer>().sharedMaterial = material;
            return part.transform;
        }

        private void OnDisable()
        {
            if (subscribedExercise != null) subscribedExercise.Changed -= OnExerciseState;
            subscribedExercise = null;
            generation++;
            StopVoice();
            StopAllCoroutines();
            (lineRequest as IDisposable)?.Dispose();
            lineRequest = null;
            polling = null;
            if (audioRequest != null) audioRequest.Dispose();
            audioRequest = null;
            session = null;
            if (subtitle != null) subtitle.text = "";
            if (visual != null) visual.SetActive(false);
        }

        private void OnDestroy()
        {
            Destroy(shellMaterial);
            Destroy(faceMaterial);
            Destroy(eyeMaterial);
        }
    }
}
