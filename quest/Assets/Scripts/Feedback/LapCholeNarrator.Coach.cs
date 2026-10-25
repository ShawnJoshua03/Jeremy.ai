using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;

namespace Jeremy
{
    // The coach side of the narrator:
    //   - mode flag: Guide (proactive coaching) or OnDemand (quiet; speaks for safety, and when asked);
    //   - push-to-talk: left-hand pinch-and-hold on the headset, T on desktop. The question goes to
    //     /coach/ask with the live sim state and the spoken answer cuts in;
    //   - live guidance: watches whether the tool is moving toward or away from the mark / target and says so,
    //     after giving the trainee a few seconds, with cooldowns so it guides rather than nags;
    //   - the voice bank: fixed lines pre-synthesized in the Cartesia voice (Resources/Voice/Bank), so they play
    //     instantly, cost nothing, and never fall back to a different voice.
    public sealed partial class LapCholeNarrator
    {
        public enum CoachMode { Guide, OnDemand }
        [Tooltip("Guide: proactive coaching. OnDemand: quiet except safety callouts; ask (left pinch-and-hold / T) for help.")]
        public CoachMode mode = CoachMode.Guide;
        public bool OnDemand => mode == CoachMode.OnDemand;
        public bool Listening => talking || inSpeech;
        readonly List<string> recentLines = new List<string>();
        CoachAudioPlayer coachAudio;

        public void SetMode(CoachMode m, bool announce = true)
        {
            mode = m;
            try { PlayerPrefs.SetInt("jeremy.coachMode", (int)m); } catch (Exception) { }
            Log("mode " + m);
            if (announce) Enqueue(m == CoachMode.Guide ? Lines.ModeGuide : Lines.ModeOnDemand, Pri.Urgent, "mode", 8f);
        }

        void LoadMode()
        {
            try { if (PlayerPrefs.HasKey("jeremy.coachMode")) mode = (CoachMode)PlayerPrefs.GetInt("jeremy.coachMode"); } catch (Exception) { }
        }

        // ---------------- fixed lines (all digit-free so they can live in the voice bank) ----------------
        static class Lines
        {
            public const string ModeGuide = "Coaching on. I'll guide you through each move.";
            public const string ModeOnDemand = "On-demand coaching. I'll stay quiet unless it's about safety. Pinch and hold your left hand and ask me anything.";
            public const string ModeOnDemandDesk = "On-demand coaching. I'll stay quiet unless it's about safety. Hold T and ask me anything.";
            public const string AskHint = "You can ask me anything. Pinch and hold your left hand, ask, then let go.";
            public const string AskHintDesk = "You can ask me anything. Hold T, ask, then let go.";
            public const string Listening = "Listening. Let go when you're done.";
            public const string NotHeard = "I didn't catch that. Hold the pinch a little longer while you ask.";
            public const string NoMic = "No microphone found, so I can't hear questions on this device.";
            public const string Thinking = "One moment.";
            public const string LinedUp = "That's it. Lined up. Push in, slow and steady.";
            public const string Closer = "Good, closer.";
            public const string KeepComing = "That's it. Keep coming.";
            public const string PushMore = "A little more pressure. You'll feel the fascia give.";
            public const string OneMoreLayer = "Through the fascia. One more layer.";
            public const string EaseBack = "Ease back a little. You're past the target depth.";
            public const string HoldStill = "Good depth. Hold still.";
            public const string DriftingInside = "You're drifting off. Come back toward the yellow glow.";
            public const string CloserInside = "Closer.";
            public const string RightThere = "Right there. Close the jaws.";
            public const string RightThereCut = "Right there. Close the blades.";
            public const string CbdWarning = "Careful. That's the common bile duct under your jaws. Come up onto the cystic duct.";
            public const string SweepMore = "Keep sweeping. Short strokes across the yellow area.";
        }

        static string HandleFix(string lean) => lean == "feet" ? "bring the handle back toward the head"
            : lean == "head" ? "bring the handle back toward the feet"
            : lean == "right" ? "bring the handle toward the patient's left"
            : lean == "left" ? "bring the handle toward the patient's right" : "line the shaft up with the guide";

        static readonly string[] Towards = { "toward the head", "toward the feet", "toward the patient's right", "toward the patient's left" };
        static readonly string[] Leans = { "feet", "head", "right", "left", "none" };
        static string OtherWay(string toward) => "Other way. The mark is " + toward + ".";
        static string MarkIs(string toward, bool close) => (close ? "Almost. Just a touch " : "The mark is ") + toward + ".";
        static string RightSpot(string lean) => "Right spot. Now " + HandleFix(lean) + ".";
        static string Wrong(string contact, string expected) => "That's the " + Name(contact) + ". You want the " + Name(expected) + ".";
        static string Name(string s) => s == "cbd" ? "common bile duct" : s == "fundus" ? "gallbladder fundus" : (s ?? "").Replace('_', ' ');

        // ---------------- live guidance ----------------
        float nextGuide, lastGuide = -100f, lastPraise = -100f, lastSafety = -100f, contactSince = -1f, depthStallSince = -1f;
        string lastContact;
        bool rightThereSaid, pushSaid, layerSaid, linedSaid, holdSaid;
        readonly List<KeyValuePair<float, float>> samples = new List<KeyValuePair<float, float>>();
        int guideStep = -1;

        void Sample(float v)
        {
            float now = Time.unscaledTime;
            samples.Add(new KeyValuePair<float, float>(now, v));
            while (samples.Count > 0 && now - samples[0].Key > 3f) samples.RemoveAt(0);
        }

        /// <summary>Change in the sampled value over the last `window` seconds (positive = growing).</summary>
        float Change(float window)
        {
            if (samples.Count < 2) return 0f;
            float now = Time.unscaledTime, latest = samples[samples.Count - 1].Value;
            foreach (var s in samples) if (now - s.Key <= window) return latest - s.Value;
            return 0f;
        }

        /// <summary>Say a guidance line unless the coach is mid-sentence or spoke within the cooldown. True if it was said.</summary>
        bool Guide(string line, string key, float cooldown = 5f)
        {
            float now = Time.unscaledTime;
            if (now - lastGuide < cooldown || busy) return false;
            lastGuide = now;
            Enqueue(line, Pri.Info, key, 3f);
            return true;
        }

        void LiveGuide(StepStat cur)
        {
            float now = Time.unscaledTime;
            if (now < nextGuide || proc.Autopiloting) return;
            nextGuide = now + 0.35f;
            if (guideStep != proc.StepIndex)
            {
                guideStep = proc.StepIndex; samples.Clear(); lastContact = null; contactSince = -1f;
                rightThereSaid = pushSaid = layerSaid = linedSaid = holdSaid = false; depthStallSince = -1f;
            }
            float inStep = now - cur.start;
            string id = proc.StepId;

            // Safety first, in every mode: stop a clip or cut before it lands on the common bile duct.
            bool sharp = id.StartsWith("clip") || id.StartsWith("divide") || id == "ductotomy" || id == "cholangiogram";
            if (sharp && proc.ContactStructure == "cbd" && now - lastSafety > 6f)
            {
                lastSafety = now;
                Enqueue(Lines.CbdWarning, Pri.Urgent, "safety", 4f);
            }
            if (OnDemand) return;

            LapCholeEntry e = proc.Entry;
            if (proc.UsingHands && e != null && e.Active)
            {
                if (e.Hovering && !float.IsNaN(e.HoverMm))
                {
                    Sample(e.HoverMm);
                    if (inStep < 3f || float.IsNaN(e.TipHeightMm) || e.TipHeightMm > 140f) return; // hand not down at the patient yet
                    if (e.Lined) { if (!linedSaid) linedSaid = Guide(Lines.LinedUp, "guide", 2f); return; }
                    if (e.HoverMm > e.SiteTolMm + 10f) linedSaid = false;
                    float change = Change(1.5f);
                    if (e.HoverMm > e.SiteTolMm)
                    {
                        if (change > 8f && e.HoverMm > 20f) Guide(OtherWay(e.TowardMark), "guide");
                        else if (change < -15f && now - lastPraise > 7f) { if (Guide(e.HoverMm < 40f ? Lines.Closer : Lines.KeepComing, "praise", 2f)) lastPraise = now; }
                        else if (Mathf.Abs(change) < 4f && now - lastGuide > 7f) Guide(MarkIs(e.TowardMark, e.HoverMm < 30f), "guide", 7f);
                    }
                    else if (e.HoverAngle > e.AngleTolDeg && now - lastGuide > 5f) Guide(RightSpot(e.Lean), "guide");
                }
                else if (e.Inserted)
                {
                    float d = e.Depth;
                    Sample(d);
                    float moving = Mathf.Abs(Change(2f));
                    if (!e.PoppedFascia && moving < 2f) { if (depthStallSince < 0f) depthStallSince = now; if (now - depthStallSince > 3f && !pushSaid) pushSaid = Guide(Lines.PushMore, "guide", 2f); }
                    else depthStallSince = -1f;
                    if (e.PoppedFascia && !e.PoppedPeritoneum && !layerSaid && moving < 2f && now - lastGuide > 3f) layerSaid = Guide(Lines.OneMoreLayer, "guide", 2f);
                    if (e.Current != null && d > e.Current.maxDepth && d <= e.Current.dangerDepth) Guide(Lines.EaseBack, "guide", 4f);
                    if (e.Current != null && e.PoppedPeritoneum && d >= e.Current.minDepth && d <= e.Current.maxDepth && !holdSaid) holdSaid = Guide(Lines.HoldStill, "guide", 1f);
                }
                return;
            }

            if (proc.StepIndex < 6 || inStep < 3f) return;
            string expected = id == "retract" ? "fundus" : id == "clip_artery" || id == "divide_artery" ? "cystic_artery"
                : id == "dissect" ? null : "cystic_duct";
            if (expected == null)
            {
                if (proc.Veil > 0.2f && now - cur.lastProgress > 6f) Guide(Lines.SweepMore, "guide", 9f);
                return;
            }
            string contact = proc.ContactStructure;
            if (contact != lastContact) { lastContact = contact; contactSince = now; rightThereSaid = false; }
            if (contact != null && contact != "veil" && contact != expected && contact != "cbd") { Guide(Wrong(contact, expected), "guide"); return; }
            if (contact == expected)
            {
                if (!rightThereSaid && now - contactSince > 1.8f) rightThereSaid = Guide(id.StartsWith("divide") || id == "ductotomy" ? Lines.RightThereCut : Lines.RightThere, "guide", 2f);
                return;
            }
            if (!proc.ToolTipBoard.HasValue) return;
            float dist = Vector3.Distance(proc.ToolTipBoard.Value, proc.TargetBoard) * 1000f;
            Sample(dist);
            float trend = Change(1.5f);
            if (trend > 6f && dist > 12f) Guide(Lines.DriftingInside, "guide", 6f);
            else if (trend < -10f && now - lastPraise > 7f) { if (Guide(Lines.CloserInside, "praise", 2f)) lastPraise = now; }
        }

        /// <summary>The coach's own read of this moment: what the trainee should hear if they ask "am I doing this right?".</summary>
        public string LiveStatus()
        {
            if (proc == null || !proc.Active) return "";
            string id = proc.StepId;
            LapCholeEntry e = proc.Entry;
            if (rig.Exercise.State == ExerciseState.Completed) return "The procedure is complete. " + ScoreSentence();
            if (rig.Exercise.State != ExerciseState.Running) return "We haven't started yet.";
            if (proc.UsingHands && e != null && e.Active && !proc.ToolInHand)
                return "First pick up the " + proc.TrayToolName + ". It's glowing on the instrument tray on your right. Pinch it.";
            if (proc.UsingHands && e != null && e.Active)
            {
                if (e.Blocked) return "The skin stopped you because the entry was off the mark or at the wrong angle. Lift the tip out, re-aim, and go again.";
                if (e.Hovering && !float.IsNaN(e.HoverMm))
                {
                    if (e.HoverMm > e.SiteTolMm) return "Not yet. The tip is " + e.HoverMm.ToString("F0") + " mm from the mark; move it " + e.TowardMark + ".";
                    if (e.HoverAngle > e.AngleTolDeg) return "Position is good, " + e.HoverMm.ToString("F0") + " mm from the mark. The shaft is " + e.HoverAngle.ToString("F0") + " degrees off the planned axis; " + HandleFix(e.Lean) + ".";
                    return "Yes. You're on the mark and lined up with the guide. Push straight in, slow and steady.";
                }
                if (e.Inserted)
                {
                    LapCholeEntry.Spec s = e.Current;
                    float d = e.Depth;
                    if (s != null && d > s.dangerDepth) return "Too deep at " + d.ToString("F0") + " mm. Pull back now.";
                    if (!e.PoppedFascia) return "You're through the skin, " + d.ToString("F0") + " mm in. Keep pushing gently until the fascia gives.";
                    if (!e.PoppedPeritoneum) return "Through the fascia at " + d.ToString("F0") + " mm. One more layer, the peritoneum.";
                    if (s != null && d > s.maxDepth) return "You're in, but " + d.ToString("F0") + " mm is a bit deep. Ease back a little.";
                    return "Yes, you're in at " + d.ToString("F0") + " mm. Hold still and it will seat.";
                }
            }
            switch (id)
            {
                case "insufflate":
                    return "Pressure is " + proc.Pressure.ToString("F0") + ". " + (proc.Pressure < 12f ? "Keep holding, and let go between twelve and fifteen." : proc.Pressure <= 15f ? "Let go now, you're in range." : "That's too high. Let go.");
                case "port_umbilical":
                    if (proc.UmbilicalSeated) return "The trocar is seated. The balloon is at " + Mathf.RoundToInt(proc.Balloon * 100f) + " percent; keep holding.";
                    break;
                case "dissect":
                    return "About " + Mathf.RoundToInt(proc.Veil * 100f) + " percent of the peritoneum is left. Keep making short sweeps across the yellow area.";
                case "cholangiogram":
                    if (proc.CatheterIn) return "The catheter is in. Contrast is at " + Mathf.RoundToInt(proc.Contrast * 100f) + " percent; keep holding.";
                    break;
            }
            if (proc.StepIndex >= 6)
            {
                if (proc.UsingHands && proc.ExchangePending) return "First pull the " + proc.EquippedToolLabel + " back to the trocar so the next instrument can go in.";
                if (proc.UsingHands && !proc.ShaftAligned) return "Re-centre the handle over the port. The instrument pivots at the trocar.";
                string contact = proc.ContactStructure;
                string expected = id == "retract" ? "fundus" : id.Contains("artery") ? "cystic_artery" : "cystic_duct";
                if (contact == "cbd") return "Stop. Your jaws are on the common bile duct. Come up onto the cystic duct.";
                if (contact != null && contact != "veil" && contact != expected) return "Your jaws are on the " + Name(contact) + ". You want the " + Name(expected) + ".";
                if (contact == expected) return "Yes. You're on the " + Name(contact) + ". Close the jaws.";
                if (proc.ToolTipBoard.HasValue)
                {
                    float d = Vector3.Distance(proc.ToolTipBoard.Value, proc.TargetBoard) * 1000f;
                    return "The tip is about " + d.ToString("F0") + " mm from the target. Move toward the yellow glow.";
                }
            }
            return proc.StepHow;
        }

        // ---------------- push-to-talk ----------------
        bool talking, tapMode, speechHeard;
        float heldSince = -1f, talkStart, lastVoice;

        /// <summary>HUD "Ask coach" button: start listening (ends by itself when you stop talking), or stop and send.</summary>
        public void ToggleTalk()
        {
            if (HandsFreeActive) { forceNext = true; ShowCaption("Listening. Ask your question.", 6f); Log("button: next utterance forced"); return; }
            if (talking) { StopTalkAndAsk(); return; }
            tapMode = true; speechHeard = false; lastVoice = Time.unscaledTime;
            StartTalk();
        }

        // Tap mode: end the question after about a second of quiet once speech was heard (or after 8 s).
        void TapAutoStop()
        {
            if (!talking || !tapMode || micClip == null) return;
            float now = Time.unscaledTime;
            int pos = Microphone.GetPosition(null);
            int window = 16000 / 5;
            if (pos > window)
            {
                var buf = new float[window];
                micClip.GetData(buf, pos - window);
                float sum = 0f; foreach (float v in buf) sum += v * v;
                float rms = Mathf.Sqrt(sum / window);
                if (rms > 0.02f) { speechHeard = true; lastVoice = now; }
            }
            if ((speechHeard && now - lastVoice > 1.1f) || now - talkStart > 8f || (!speechHeard && now - talkStart > 5f)) StopTalkAndAsk();
        }
        AudioClip micClip;
        bool micAsked;

        void UpdateTalk()
        {
            if (coachAudio == null) coachAudio = GetComponent<CoachAudioPlayer>();
            Keyboard keys = Keyboard.current;
            if (rig.desktopPreview && keys != null && keys.mKey.wasPressedThisFrame)
                SetMode(OnDemand ? CoachMode.Guide : CoachMode.OnDemand);
            bool held = proc.simLeftPinch || (rig.desktopPreview ? keys != null && keys.tKey.isPressed : LeftPinch());
            float now = Time.unscaledTime;
            if (talking && tapMode) { TapAutoStop(); return; }
            if (HandsFreeActive)
            {
                // The mic is always on: a pinch just makes sure the next sentence is answered.
                if (held && !forceNext) { forceNext = true; ShowCaption("Listening. Ask your question.", 6f); Log("pinch: next utterance forced"); }
                UpdateHandsFree();
                return;
            }
            if (held)
            {
                if (heldSince < 0f) heldSince = now;
                if (!talking && now - heldSince > (rig.desktopPreview ? 0.05f : 0.25f)) { tapMode = false; StartTalk(); }
                if (talking && now - talkStart > 10f) StopTalkAndAsk();
            }
            else
            {
                heldSince = -1f;
                if (talking) StopTalkAndAsk();
            }
        }

        float nextHandLog;
        /// <summary>Left index-thumb pinch, read straight from the tracked hand (no confidence gate: a relaxed left hand
        /// often reports low confidence). Also logs the hand state every few seconds for headset diagnosis.</summary>
        bool LeftPinch()
        {
            OVRHand hand = rig.leftHand;
            InstrumentController left = rig.Instruments != null && rig.Instruments.Length > 0 ? rig.Instruments[0] : null;
            bool tracked = hand != null && hand.IsTracked;
            float strength = tracked ? hand.GetFingerPinchStrength(OVRHand.HandFinger.Index) : 0f;
            bool pinching = tracked && (strength > 0.75f || hand.GetFingerIsPinching(OVRHand.HandFinger.Index));
            if (left != null && left.TrackingValid && left.Closed) pinching = true;
            if (Time.unscaledTime > nextHandLog)
            {
                nextHandLog = Time.unscaledTime + 3f;
                Log("left hand: " + (hand == null ? "none" : "tracked=" + tracked + " conf=" + (tracked ? hand.HandConfidence.ToString() : "-") + " pinch=" + strength.ToString("F2"))
                    + " instrument=" + (left == null ? "none" : left.TrackingValid + "/" + left.Closed) + " mic=" + (Microphone.devices.Length > 0 ? Microphone.devices[0] : "none"));
            }
            if (pinching && !talking && heldSince > 0f) ShowCaption("Keep holding to ask...", 0.5f);
            return pinching;
        }

        void RequestMic()
        {
            if (micAsked) return;
            micAsked = true;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
#endif
        }

        void StartTalk()
        {
            talking = true; talkStart = Time.unscaledTime;
            ClearQueue();
            if (coachAudio != null) coachAudio.StopSpeaking();
            proc.StopRecordedClip();
            if (proc.simLeftPinch) { ShowCaption(Lines.Listening, 10f); return; } // simulated question; text comes from the bridge
            if (Microphone.devices == null || Microphone.devices.Length == 0) { talking = false; ShowCaption(Lines.NoMic, 5f); return; }
            micClip = Microphone.Start(null, false, 11, 16000);
            if (micClip == null) { talking = false; ShowCaption(Lines.NoMic, 5f); Log("microphone did not start"); return; }
            ShowCaption(tapMode ? "Listening. Ask your question." : Lines.Listening, 11f);
            Log("listening (" + (tapMode ? "button" : "pinch") + ", mic " + Microphone.devices[0] + ")");
        }

        void StopTalkAndAsk()
        {
            talking = false; tapMode = false;
            if (micClip == null) return;
            int frames = Microphone.GetPosition(null);
            Microphone.End(null);
            AudioClip clip = micClip; micClip = null;
            if (frames < 16000 * 0.4f) { ShowCaption(Lines.NotHeard, 4f); Destroy(clip); return; }
            var data = new float[frames * clip.channels];
            clip.GetData(data, 0);
            byte[] wav = ToWav(data, clip.channels, clip.frequency);
            Destroy(clip);
            Log("asking with " + (frames / 16000f).ToString("F1") + " s of audio");
            ShowCaption("Thinking...", 15f);
            StartCoroutine(Ask(null, wav));
        }

        /// <summary>Ask a typed question (bridge: "ask am I doing this right").</summary>
        public void AskText(string question) { if (!string.IsNullOrWhiteSpace(question)) StartCoroutine(Ask(question.Trim(), null)); }

        static byte[] ToWav(float[] samples, int channels, int rate)
        {
            int frames = samples.Length / Mathf.Max(1, channels);
            var pcm = new byte[frames * 2];
            for (int i = 0; i < frames; i++)
            {
                float v = 0f; for (int c = 0; c < channels; c++) v += samples[i * channels + c];
                short s = (short)Mathf.Clamp(v / channels * 32767f, -32768f, 32767f);
                pcm[i * 2] = (byte)(s & 0xff); pcm[i * 2 + 1] = (byte)((s >> 8) & 0xff);
            }
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + pcm.Length); w.Write(Encoding.ASCII.GetBytes("WAVE"));
                w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data")); w.Write(pcm.Length); w.Write(pcm);
                return ms.ToArray();
            }
        }

        [Serializable] sealed class AskContext { public string step, what, how, status, readout, last_feedback, errors, mode, device; public float step_s; public string[] recent; }
        [Serializable] sealed class AskRequest { public string text; public string audio_wav_b64; public AskContext context; public bool speak = true; public bool hands_free; }
        [Serializable] sealed class AskReply { public string transcript, reply, intent, source, audio_wav_b64; }

        AskContext Context()
        {
            StepStat cur = Cur;
            return new AskContext
            {
                step = "Step " + (proc.StepIndex + 1) + " of " + LapCholeProcedure.StepCount + " · " + LapCholeProcedure.StepTitleAt(proc.StepIndex),
                what = proc.StepWhat, how = rig.desktopPreview ? proc.StepHow : HandWords(proc.StepHow), status = LiveStatus(),
                readout = proc.EntryReadout, last_feedback = proc.Feedback,
                errors = rig.Recorder.LapMajor + " major, " + rig.Recorder.LapMinor + " minor",
                mode = OnDemand ? "on_demand" : "guide", device = rig.desktopPreview ? "desktop" : "headset",
                step_s = cur != null ? Time.unscaledTime - cur.start : 0f,
                recent = recentLines.GetRange(Mathf.Max(0, recentLines.Count - 4), Mathf.Min(4, recentLines.Count)).ToArray(),
            };
        }

        IEnumerator Ask(string text, byte[] wav, bool handsFreeFilter = false)
        {
            if (!handsFreeFilter) ShowCaption(text != null ? "You: " + text : Lines.Thinking, 12f);
            var request = new AskRequest { text = text ?? "", audio_wav_b64 = wav != null ? Convert.ToBase64String(wav) : "", context = Context(), speak = true, hands_free = handsFreeFilter };
            string json = JsonUtility.ToJson(request);
            float t0 = Time.unscaledTime;
            string body = null; long code = 0;
            foreach (string url in AskUrls())
            {
                if (url == null) { yield return EnsureFallbackSession(); continue; }
                using (var r = new UnityWebRequest(url, "POST"))
                {
                    r.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                    r.downloadHandler = new DownloadHandlerBuffer();
                    r.SetRequestHeader("Content-Type", "application/json");
                    if (api != null && url.StartsWith(api.baseUrl ?? "\u0000")) api.ApplyAuth(r);
                    r.timeout = 30; r.redirectLimit = 0;
                    yield return r.SendWebRequest();
                    code = r.responseCode;
                    if (r.result == UnityWebRequest.Result.Success) { body = r.downloadHandler.text; break; }
                    Log("ask failed " + code + " " + r.error + " (" + new Uri(url).Host + ")");
                    if (code == 422) { if (!handsFreeFilter) { ShowCaption(Lines.NotHeard, 5f); Enqueue(Lines.NotHeard, Pri.Urgent, "answer", 5f); } yield break; } // heard nothing usable
                }
            }
            if (body == null && handsFreeFilter) yield break; // background speech: stay quiet if the server is unreachable
            if (body == null)
            {
                // Offline: answer from the live read, without a voice.
                string offline = LiveStatus();
                ShowCaption(offline, 8f);
                Log("ask offline: " + offline);
                yield break;
            }
            AskReply reply = JsonUtility.FromJson<AskReply>(body);
            if (string.IsNullOrEmpty(reply.reply)) { Log("ignored (not addressed to the coach): \"" + reply.transcript + "\""); yield break; }
            Log("ask " + ((Time.unscaledTime - t0) * 1000f).ToString("F0") + " ms [" + reply.source + "/" + reply.intent + "] heard \"" + reply.transcript + "\" -> \"" + reply.reply + "\"");
            AudioClip clip = null;
            if (!string.IsNullOrEmpty(reply.audio_wav_b64))
            {
                try { clip = WavToClip(Convert.FromBase64String(reply.audio_wav_b64), "answer"); } catch (FormatException) { }
            }
            if (coachAudio != null) coachAudio.StopSpeaking();
            PlayNow(reply.reply, clip, "You: " + reply.transcript);
        }

        /// <summary>Ask URLs in order: the sim's backend (its live session), then the fallback; null = create the fallback session first.</summary>
        IEnumerable<string> AskUrls()
        {
            PendingAttempt attempt = rig.Uploads != null ? rig.Uploads.LatestStarted() : null;
            if (api != null && attempt != null && Time.unscaledTime >= primaryDownUntil && api.TryUrl(ApiClient.SessionPath(attempt.sessionId) + "/coach/ask", out string primary))
                yield return primary;
            if (string.IsNullOrWhiteSpace(voiceFallbackOrigin)) yield break;
            if (fallbackSession == null) yield return null;
            if (fallbackSession != null) yield return Origin() + "/api/v1/sessions/" + Uri.EscapeDataString(fallbackSession) + "/coach/ask";
        }

        void Remember(string line)
        {
            recentLines.Add(line);
            if (recentLines.Count > 8) recentLines.RemoveAt(0);
        }

        // ---------------- voice bank ----------------
        /// <summary>Stable key for a spoken line; the voice bank and the disk cache share it.</summary>
        public static string LineKey(string spoken)
        {
            using (var sha = System.Security.Cryptography.SHA1.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes("cartesia-v1|" + spoken));
                var sb = new StringBuilder(); for (int i = 0; i < 10; i++) sb.Append(h[i].ToString("x2"));
                return sb.ToString();
            }
        }

        static AudioClip BankClip(string spoken) => Resources.Load<AudioClip>("Voice/Bank/" + LineKey(spoken));

        static readonly string[] NumberWords = { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen" };
        static string Word(int n) => n >= 0 && n < NumberWords.Length ? NumberWords[n] : n.ToString();

        /// <summary>Every fixed line the coach can say, in the form it is spoken on this device type. For the bank.</summary>
        public static IEnumerable<string> FixedLines(bool desktop)
        {
            Func<string, string> dev = s => desktop ? s : HandWords(s);
            yield return dev(Opening(desktop));
            foreach (var f in typeof(Lines).GetFields()) yield return dev((string)f.GetValue(null));
            for (int i = 0; i < LapCholeProcedure.StepCount; i++)
                foreach (int level in new[] { 0, 1, 2 }) yield return dev(IntroFor(i, level, desktop));
            foreach (string t in Towards) { yield return OtherWay(t); yield return MarkIs(t, true); yield return MarkIs(t, false); }
            foreach (string l in Leans) yield return RightSpot(l);
            foreach (string a in new[] { "cystic_duct", "cystic_artery", "cbd", "fundus" })
                foreach (string b in new[] { "cystic_duct", "cystic_artery", "fundus" }) if (a != b) yield return Wrong(a, b);
            foreach (string s in StaticCoreLines()) yield return dev(s);
        }

        /// <summary>Fixed lines spoken from the core narrator and the entry tools (must match their text exactly).</summary>
        static IEnumerable<string> StaticCoreLines()
        {
            string[] core =
            {
                "Rising. Let go at twelve.", "Too high. Let go now.", "Halfway. Keep the sweeps short and lateral.",
                "Almost there. The duct and artery are nearly clear.", "Balloon's halfway. Keep holding.",
                "Contrast is filling the common bile duct. No leak so far.",
                "Hand stop. You're pressing into the abdominal wall. Back off and let the trocar be the pivot.",
                "Ease your hand off the patient.", "Hand off the abdomen. Work through the port.",
                "Trocar's in. Now hold to inflate the retention balloon.", "Catheter's in the duct. Hold to inject, and watch the ducts fill.",
                "One on. One more, closer to the common bile duct.", "Good clip. Place the second just below it.",
                "First artery clip on. One more on the hepatic side.", "Two down. Last clip goes on the gallbladder side.",
                "That clip went on at an angle. Come across the duct square, jaws at right angles to it.",
                "Stop. That clip is on the common bile duct. Major error. The cystic duct is the thinner branch running up to the gallbladder. Confirm it before every squeeze.",
                "That cut missed the cystic duct. Major error. Put the blades just below the distal clip, on the duct only.",
                "Wrong structure. That was the common bile duct. Major error. Divide only the cystic duct, between the clips.",
                "Wrong structure. That was the cystic artery. Major error. Divide only the cystic duct, between the clips.",
                "Wrong structure. That was the cystic duct. Major error. Divide only the cystic artery, between the clips.",
                "Wrong structure. That was the common bile duct. Major error. Divide only the cystic artery, between the clips.",
                "Don't lever it. The tool pivots at the skin; keep the handle over the entry point.",
                "Off the mark. Lift out, put the tip on the purple cross, then go in.",
                "Still off the mark. Touch the tip to the centre of the cross before you push.",
                "Take your time. Settle the tip on the cross first, then push.", "Small movements. Tip on the cross, then straight in.",
                "Wrong angle for this port. Match the faint guide line before you push.",
                "Still angled. The guide turns green when you're lined up.", "Line up first, then push. Wait for the guide to go green.",
                "Pull the clip applier back to the trocar first. Then the next instrument goes in.",
                "Pull the scissors back to the trocar first. Then the next instrument goes in.",
                "Pull the grasper back to the trocar first. Then the next instrument goes in.",
                "Pull the Maryland dissector back to the trocar first. Then the next instrument goes in.",
                "Pull the cholangiogram catheter back to the trocar first. Then the next instrument goes in.",
                "Re-centre the handle over the port. The shaft pivots at the trocar; it can't pass through the body wall.",
                "Re-centre the handle over the port. The instrument pivots at the trocar.",
                "Your hand is in the patient. Withdraw it and steer through the trocar.",
                "Find the round fundus at the top of the gallbladder. Close the grasper on it.",
                "Target the cystic duct close to the gallbladder, where the marker glows. Not the blue common bile duct.",
                "Cut just below the clip you placed. Duct only.", "Aim the catheter tip into the small cut on the duct.",
                "Cut in the gap between the lower clips and the ductotomy.", "Cut the artery between the two lower clips and the top one.",
                "Critical view of safety. Duct and artery clearly seen.", "Clean division, clips secure.", "Clear cholangiogram, no filling defects.",
                "Dead centre.", "Right on the mark.", "Nice and efficient.", "Smooth.", "Done.", "Good.", "Got it.",
                "You got there. That one took a few tries.", "Let's reset after that error.",
            };
            foreach (string s in core) yield return s;
            foreach (string step in new[] { "veress", "port_umbilical", "port_epigastric", "port_rsc_medial" })
            {
                LapCholeEntry.Spec spec = LapCholeEntry.SpecFor(step);
                yield return spec.popAText; yield return spec.popBText;
                yield return spec.name + " withdrawn. Re-enter at the mark.";
            }
            foreach (string t in Towards)
            {
                string where = " You went in " + t + ".";
                yield return "Off the mark." + where + " Lift out, put the tip on the purple cross, then go in.";
                yield return "Still off the mark." + where + " Touch the tip to the centre of the cross before you push.";
            }
            foreach (string l in Leans)
            {
                string fix = l == "feet" ? " Bring the handle back toward the head." : l == "head" ? " Bring the handle back toward the feet."
                    : l == "right" ? " Bring the handle toward the patient's left." : l == "left" ? " Bring the handle toward the patient's right." : "";
                yield return "Wrong angle for this port." + fix + " Match the faint guide line before you push.";
                yield return "Still angled." + fix + " The guide turns green when you're lined up.";
            }
        }

        /// <summary>Recorded per-step clips (used if a line cannot be voiced live) regenerated in the same voice.</summary>
        public static IEnumerable<KeyValuePair<string, string>> StepClipLines()
        {
            for (int i = 0; i < LapCholeProcedure.StepCount; i++)
                yield return new KeyValuePair<string, string>("lapchole_" + LapCholeProcedure.StepIdAt(i), HandWords(IntroFor(i, 0, false)));
            yield return new KeyValuePair<string, string>("lapchole_complete", "Procedure complete. Well done.");
        }

        /// <summary>Bridge "dumplines": write the bank list (key, spoken text) for the offline baker.</summary>
        public static void DumpLines(string path)
        {
            var seen = new HashSet<string>();
            var sb = new StringBuilder();
            foreach (bool desk in new[] { false, true })
                foreach (string line in FixedLines(desk))
                {
                    string spoken = Speakable(line);
                    if (string.IsNullOrWhiteSpace(spoken) || !seen.Add(spoken)) continue;
                    foreach (char c in spoken) if (char.IsDigit(c)) { spoken = null; break; }
                    if (spoken == null) continue;
                    sb.Append("bank\t").Append(LineKey(spoken)).Append('\t').Append(spoken).Append('\n');
                }
            foreach (var kv in StepClipLines()) sb.Append("clip\t").Append(kv.Key).Append('\t').Append(Speakable(kv.Value)).Append('\n');
            File.WriteAllText(path, sb.ToString());
        }
    }
}
