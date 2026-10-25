using System.Collections.Generic;
using UnityEngine;

namespace Jeremy
{
    // Hands-free questions: the microphone is always on (headset). Speech is detected by level against an adaptive
    // noise floor; each utterance (up to 8 s, ended by 0.8 s of quiet) goes to /coach/ask with hands_free = true,
    // and the server answers only what sounds addressed to the coach (questions, "help", "coach ...").
    // Listening pauses while the coach is talking, so it never answers its own voice.
    public sealed partial class LapCholeNarrator
    {
        [Tooltip("Always-on microphone: just talk to ask (headset). Pinch/button still force the next utterance through.")]
        public bool handsFree = true;
        public bool Hearing => inSpeech;
        /// <summary>Live microphone level 0..1 for the meter, and the level speech must reach (same scale).</summary>
        public float MicLevel { get; private set; }
        public float MicThreshold { get; private set; } = 0.3f;
        public bool MicMuted { get; private set; }
        public bool MicOn => liveOn;
        float peakRms, nextLevelLog;
        static float Meter(float rms) => Mathf.Clamp01(Mathf.Log10(1f + rms * 400f) / Mathf.Log10(1f + 0.25f * 400f));

        AudioClip liveMic;
        int liveRead, loudChunks, quietChunks;
        bool liveOn, inSpeech, asking, forceNext;
        float noiseFloor = 0.008f, muteUntil, nextMicTry;
        readonly List<float> segment = new List<float>(16000 * 8);
        readonly List<float> preroll = new List<float>(16000 / 2);
        const int Rate = 16000, Chunk = 320; // 20 ms

        bool HandsFreeActive => handsFree && !rig.desktopPreview;

        void StartLive()
        {
            if (Time.unscaledTime < nextMicTry) return;
            nextMicTry = Time.unscaledTime + 2f;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone)) { RequestMic(); return; }
#endif
            if (Microphone.devices == null || Microphone.devices.Length == 0) return;
            liveMic = Microphone.Start(null, true, 10, Rate);
            if (liveMic == null) { Log("hands-free: microphone did not start"); return; }
            liveOn = true; liveRead = 0;
            Log("hands-free: listening on " + Microphone.devices[0]);
        }

        void StopLive()
        {
            if (!liveOn) return;
            Microphone.End(null);
            liveOn = false; inSpeech = false; segment.Clear(); preroll.Clear();
            if (liveMic != null) { Destroy(liveMic); liveMic = null; }
        }

        void UpdateHandsFree()
        {
            if (!HandsFreeActive || talking) return;
            if (!liveOn) { StartLive(); if (!liveOn) return; }
            int pos = Microphone.GetPosition(null);
            if (pos < 0 || liveMic == null) return;
            int len = liveMic.samples;
            int n = (pos - liveRead + len) % len;
            if (n < Chunk) return;
            n -= n % Chunk;
            var buf = new float[n];
            if (liveRead + n <= len) liveMic.GetData(buf, liveRead);
            else
            {
                var a = new float[len - liveRead]; liveMic.GetData(a, liveRead);
                var b = new float[n - a.Length]; liveMic.GetData(b, 0);
                a.CopyTo(buf, 0); b.CopyTo(buf, a.Length);
            }
            liveRead = (liveRead + n) % len;

            float now = Time.unscaledTime;
            bool coachTalking = (playing != null && source.isPlaying) || (coachAudio != null && coachAudio.IsSpeaking);
            if (coachTalking) muteUntil = now + 0.4f; // and a short tail after it stops
            MicMuted = coachTalking || now < muteUntil || asking;
            float frameSum = 0f; for (int i = 0; i < n; i++) frameSum += buf[i] * buf[i];
            float frameRms = Mathf.Sqrt(frameSum / n);
            peakRms = Mathf.Max(peakRms, frameRms);
            MicLevel = Mathf.Lerp(MicLevel, Meter(frameRms), 0.5f);
            MicThreshold = Meter(Mathf.Max(0.006f, noiseFloor * 2.5f));
            if (now > nextLevelLog)
            {
                nextLevelLog = now + 3f;
                Log("mic: peak rms " + peakRms.ToString("F4") + " floor " + noiseFloor.ToString("F4") + " threshold " + Mathf.Max(0.006f, noiseFloor * 2.5f).ToString("F4") + (MicMuted ? " (paused: coach talking)" : ""));
                peakRms = 0f;
            }
            if (MicMuted)
            {
                if (inSpeech && !asking) { inSpeech = false; segment.Clear(); }
                preroll.Clear(); loudChunks = 0;
                return;
            }
            for (int c = 0; c < n; c += Chunk)
            {
                float sum = 0f;
                for (int i = 0; i < Chunk; i++) { float v = buf[c + i]; sum += v * v; }
                float rms = Mathf.Sqrt(sum / Chunk);
                float threshold = Mathf.Max(0.006f, noiseFloor * 2.5f);
                if (!inSpeech)
                {
                    for (int i = 0; i < Chunk; i++) preroll.Add(buf[c + i]);
                    if (preroll.Count > Rate * 4 / 10) preroll.RemoveRange(0, preroll.Count - Rate * 4 / 10);
                    if (rms > threshold) loudChunks++; else { loudChunks = 0; noiseFloor = Mathf.Lerp(noiseFloor, rms, 0.03f); }
                    if (loudChunks >= 5) { inSpeech = true; quietChunks = 0; segment.Clear(); segment.AddRange(preroll); preroll.Clear(); }
                }
                else
                {
                    for (int i = 0; i < Chunk; i++) segment.Add(buf[c + i]);
                    quietChunks = rms > threshold * 0.7f ? 0 : quietChunks + 1;
                    if (quietChunks >= 40 || segment.Count >= Rate * 8) EndUtterance();
                }
            }
        }

        void EndUtterance()
        {
            inSpeech = false;
            float seconds = segment.Count / (float)Rate;
            if (seconds < 0.6f) { segment.Clear(); return; }
            byte[] wav = ToWav(segment.ToArray(), 1, Rate);
            segment.Clear();
            bool filter = !forceNext;
            forceNext = false;
            Log("heard " + seconds.ToString("F1") + " s" + (filter ? "" : " (forced)"));
            asking = true;
            StartCoroutine(AskThen(wav, filter));
        }

        System.Collections.IEnumerator AskThen(byte[] wav, bool filter)
        {
            yield return Ask(null, wav, filter);
            asking = false;
        }
    }
}
