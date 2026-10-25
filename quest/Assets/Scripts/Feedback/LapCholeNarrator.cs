using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Jeremy
{
    // Dynamic spoken coaching for the lap chole sim, voiced by Cartesia through the backend's /coach/audio route.
    // It watches how the trainee is doing (time on the step, repeated corrections, port accuracy, pressure,
    // dissection progress, errors, hand collisions, clean streaks) and speaks short lines:
    //   - on each new step: a recap of the last step plus the next instruction, more detailed when struggling,
    //     terse when fluent;
    //   - escalating hints when progress stalls, built from the live tool state;
    //   - live cues (pressure window, dissection progress) and urgent callouts on major errors;
    //   - a debrief when the procedure completes.
    // Lines are template text computed here (no LLM, like the port coach): instant and deterministic, and the
    // same string is shown as the caption and spoken. Voice tries the sim's backend first, then
    // voiceFallbackOrigin. If no voice is available, step intros fall back to the recorded clips.
    public sealed partial class LapCholeNarrator : MonoBehaviour
    {
        [Tooltip("HTTPS backend with Cartesia configured, used when the sim's own backend has no voice. Empty disables.")]
        public string voiceFallbackOrigin = "https://jeremy-api.onrender.com";
        public bool narrate = true;
        [Tooltip("Seconds without progress before the first hint; later hints escalate.")]
        public float firstHintAfter = 12f;
        [Tooltip("Cache synthesized lines on disk so repeated lines play instantly and cost nothing.")]
        public bool diskCache = true;

        public string Caption => Time.unscaledTime < captionUntil ? caption : "";
        public string Status { get; private set; } = "idle";
        public string VoiceRoute { get; private set; } = "none";

        PegTransferRig rig;
        LapCholeProcedure proc;
        ApiClient api;
        AudioSource source;

        // ---------------- performance model ----------------
        sealed class StepStat
        {
            public string id;
            public float start, end = -1f, lastProgress;
            public int corrections, major, hintLevel, ductAtStart, arteryAtStart;
            public float? mm, angle, pressure;
            public string result = "";
            public bool cueA, cueB, cueC;
            public readonly Dictionary<string, int> repeats = new Dictionary<string, int>();
            public readonly Dictionary<string, float> lastSaidAt = new Dictionary<string, float>();
        }
        readonly List<StepStat> stats = new List<StepStat>();
        StepStat Cur => stats.Count > 0 ? stats[stats.Count - 1] : null;
        int lastStep = -1, cleanStreak, bestStreak, handCollisions;
        ExerciseState lastState = ExerciseState.Idle;
        float runStart, lastHandCue = -100f, lastAlignCue = -100f, misalignedSince = -1f, exchangeSince = -1f;
        float lastPressure, lastBalloon, lastVeil = 1f, lastContrast;
        bool debriefed;
        readonly List<string> pendingSaid = new List<string>();
        System.Random rng = new System.Random();

        static readonly Dictionary<string, string> Terse = new Dictionary<string, string>
        {
            { "veress", "Veress needle at the navel." },
            { "insufflate", "Insufflate to twelve to fifteen." },
            { "port_umbilical", "Umbilical trocar, then inflate the balloon." },
            { "port_epigastric", "Epigastric port, just under the breastbone." },
            { "port_rsc_medial", "Medial subcostal port, below the right ribs." },
            { "port_rsc_lateral", "Lateral port, out on the right flank." },
            { "retract", "Grasp the fundus and lift it over the liver." },
            { "dissect", "Open Calot's triangle to the critical view." },
            { "clip_duct_distal", "One clip on the cystic duct, gallbladder side." },
            { "ductotomy", "Small ductotomy just below that clip." },
            { "cholangiogram", "Catheter into the duct, then contrast." },
            { "clip_duct_proximal", "Two clips on the bile duct side." },
            { "divide_duct", "Divide the duct in the protected gap." },
            { "clip_artery", "Three clips on the artery: two hepatic side, one gallbladder side." },
            { "divide_artery", "divide the artery between the clips. Last step." },
        };
        // Rough expected seconds per step, used only to decide pace remarks and detail level.
        static float Expected(string id) => id == "dissect" ? 35f : id == "clip_artery" || id == "clip_duct_proximal" ? 25f
            : id == "insufflate" ? 12f : id == "cholangiogram" ? 18f : 16f;
        static bool IsPort(string id) => id == "veress" || id.StartsWith("port_");

        // ---------------- lifecycle ----------------
        void Awake()
        {
            rig = GetComponent<PegTransferRig>();
            LoadMode();
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false; source.spatialBlend = 0f; source.volume = 1f;
        }

        void OnDestroy()
        {
            if (proc != null) proc.Said -= OnSaid;
            if (rig != null && rig.Recorder != null) rig.Recorder.Recorded -= OnRecorded;
            foreach (AudioClip c in memory.Values) if (c != null) Destroy(c);
        }

        void LateUpdate()
        {
            if (rig == null) rig = GetComponent<PegTransferRig>();
            if (rig == null || rig.Exercise == null) return;
            if (proc == null)
            {
                proc = GetComponent<LapCholeProcedure>();
                if (proc == null) return;
                proc.Said += OnSaid;
                rig.Recorder.Recorded += OnRecorded;
                api = GetComponent<ApiClient>();
            }
            proc.ExternalVoice = narrate && rig.LapChole;
            if (!narrate || !rig.LapChole || !proc.Active) { pendingSaid.Clear(); return; }
            if (!rig.desktopPreview) RequestMic();
            UpdateTalk();

            ExerciseState state = rig.Exercise.State;
            if (state == ExerciseState.Running && lastState != ExerciseState.Running && rig.Exercise.ElapsedSeconds < 1.0) BeginRun();
            if (state == ExerciseState.Ready && lastState != ExerciseState.Ready) { ClearQueue(); stats.Clear(); lastStep = -1; }
            lastState = state;

            if (state == ExerciseState.Running || state == ExerciseState.Completed) Track(state);
            pendingSaid.Clear();
            Pump();
        }

        void BeginRun()
        {
            ClearQueue();
            stats.Clear();
            cleanStreak = bestStreak = handCollisions = 0;
            debriefed = false;
            runStart = Time.unscaledTime;
            lastStep = 0;
            lastPressure = lastBalloon = lastContrast = 0f; lastVeil = 1f;
            StartStep(0);
            if (OnDemand)
            {
                Enqueue(rig.desktopPreview ? Lines.ModeOnDemandDesk : Lines.ModeOnDemand, Pri.Step, "intro", 14f);
                return;
            }
            Enqueue(Opening(rig.desktopPreview), Pri.Step, "intro", 14f);
            Enqueue(IntroFor(0, 0, rig.desktopPreview), Pri.Step, "step:veress", 30f, "veress", append: true);
            Enqueue(rig.desktopPreview ? Lines.AskHintDesk : Lines.AskHint, Pri.Step, "askhint", 40f, append: true);
            PrefetchCues(LapCholeProcedure.StepIdAt(0));
        }

        void StartStep(int index)
        {
            float now = Time.unscaledTime;
            stats.Add(new StepStat { id = LapCholeProcedure.StepIdAt(index), start = now, lastProgress = now, ductAtStart = proc.DuctClips, arteryAtStart = proc.ArteryClips });
            misalignedSince = exchangeSince = -1f;
        }

        // ---------------- tracking ----------------
        void Track(ExerciseState state)
        {
            StepStat cur = Cur;
            if (cur == null) { StartStep(proc.StepIndex); cur = Cur; lastStep = proc.StepIndex; }
            bool advanced = proc.StepIndex != lastStep;
            bool finished = state == ExerciseState.Completed && !debriefed;
            // Lines said this frame: the last one before an advance is the step result, the rest are corrections.
            for (int i = 0; i < pendingSaid.Count; i++)
            {
                bool result = (advanced || finished) && i == pendingSaid.Count - 1;
                if (result)
                {
                    cur.result = pendingSaid[i];
                    if (cur.result.IndexOf("major error", StringComparison.OrdinalIgnoreCase) >= 0) cur.major++;
                }
                else OnCorrection(cur, pendingSaid[i]);
            }
            if (advanced || finished)
            {
                cur.end = Time.unscaledTime;
                if (cur.major == 0 && cur.corrections <= 1) { cleanStreak++; bestStreak = Mathf.Max(bestStreak, cleanStreak); }
                else cleanStreak = 0;
            }
            if (finished) { debriefed = true; Enqueue(Recap(cur) + " " + Debrief(), Pri.Step, "debrief", 30f, "complete"); return; }
            if (state != ExerciseState.Running) return;
            if (advanced)
            {
                StepStat done = cur;
                lastStep = proc.StepIndex;
                StartStep(proc.StepIndex);
                if (OnDemand) { ShowCaption(LapCholeProcedure.StepTitleAt(proc.StepIndex), 5f); return; }
                // Recap (one-off, has numbers) and the instruction (fixed text, comes from the voice bank) as two lines.
                Enqueue(Recap(done), Pri.Step, "recap", 10f);
                Enqueue(Intro(), Pri.Step, "step:" + proc.StepId, 14f, proc.StepId, append: true);
                PrefetchCues(proc.StepId);
                return;
            }
            LiveCues(cur);
            LiveGuide(cur);
            if (!OnDemand) Hints(cur);
        }

        void OnSaid(string text) { if (narrate) pendingSaid.Add(text); }

        void OnRecorded(RecordedEvent e)
        {
            StepStat cur = Cur;
            if (cur == null || !narrate) return;
            switch (e.type)
            {
                case "port_placed":
                    if (!e.noSource && !e.noTarget)
                    {
                        Vector2 a = new Vector2(e.sourceLocal.x, e.sourceLocal.z), b = new Vector2(e.targetLocal.x, e.targetLocal.z);
                        cur.mm = Vector2.Distance(a, b) * 1000f;
                    }
                    if (e.hasValue) cur.angle = e.value;
                    break;
                case "insufflation": if (e.hasValue) cur.pressure = e.value; break;
                case "hand_collision":
                    handCollisions++;
                    if (Time.unscaledTime - lastHandCue > 12f)
                    {
                        lastHandCue = Time.unscaledTime;
                        Enqueue(handCollisions == 1
                            ? "Hand stop. You're pressing into the abdominal wall. Back off and let the trocar be the pivot."
                            : Pick("Ease your hand off the patient.", "Hand off the abdomen. Work through the port."), Pri.Urgent, "hand", 5f);
                    }
                    break;
            }
        }

        void Progress(StepStat cur) { cur.lastProgress = Time.unscaledTime; cur.hintLevel = 0; }

        void OnCorrection(StepStat cur, string text)
        {
            if (text.StartsWith("Autopilot")) return;
            float now = Time.unscaledTime;
            if (text.Contains("Major error"))
            {
                cur.major++; cleanStreak = 0;
                Enqueue(MajorErrorLine(text), Pri.Urgent, "major", 10f);
                Progress(cur);
                return;
            }
            string key = text.StartsWith("Move the actual tool tip") ? "tip" : text.StartsWith("Straighten the shaft") ? "angle"
                : text.StartsWith("Clip at") ? "clip" : text.StartsWith("Trocar seated") ? "seated"
                : text.StartsWith("Catheter in") ? "catheter" : text.StartsWith("Too deep") ? "deep"
                : text.StartsWith("Don't lever") ? "lever" : text.Contains("pop") || text.StartsWith("Through") || text.StartsWith("Fascia") ? "pop"
                : text.Contains("withdrawn") ? "withdrawn" : "other";
            cur.repeats.TryGetValue(key, out int n); n++; cur.repeats[key] = n;
            bool isCorrection = key == "tip" || key == "angle" || key == "deep" || key == "lever";
            if (key == "deep") { cur.corrections++; Enqueue(text, Pri.Urgent, "deep", 4f); return; }
            if (key == "pop") { Progress(cur); Enqueue(text, Pri.Info, "pop", 3f); return; }
            if (isCorrection) cur.corrections++;
            else Progress(cur);
            if (cur.lastSaidAt.TryGetValue(key, out float at) && now - at < (isCorrection ? 5f : 1.5f)) return;
            string line = null;
            switch (key)
            {
                case "tip":
                    string where = text.Contains(", toward") ? " You went in" + text.Substring(text.IndexOf(", toward") + 1).TrimEnd('.') + "." : "";
                    line = n == 1 ? "Off the mark." + where + " Lift out, put the tip on the purple cross, then go in."
                        : n == 2 ? "Still off the mark." + where + " Touch the tip to the centre of the cross before you push."
                        : n % 2 == 1 ? Pick("Take your time. Settle the tip on the cross first, then push.",
                                            "Small movements. Tip on the cross, then straight in.") : null;
                    break;
                case "angle":
                    string lean = text.Contains("leaning ") ? text.Substring(text.IndexOf("leaning ") + 8).TrimEnd('.') : "";
                    string fix = lean == "feet" ? " Bring the handle back toward the head." : lean == "head" ? " Bring the handle back toward the feet."
                        : lean == "right" ? " Bring the handle toward the patient's left." : lean == "left" ? " Bring the handle toward the patient's right." : "";
                    line = n == 1 ? "Wrong angle for this port." + fix + " Match the faint guide line before you push."
                        : n == 2 ? "Still angled." + fix + " The guide turns green when you're lined up."
                        : n % 2 == 1 ? "Line up first, then push. Wait for the guide to go green." : null;
                    break;
                case "lever": line = "Don't lever it. The tool pivots at the skin; keep the handle over the entry point."; break;
                case "withdrawn": line = text; break;
                case "seated": line = "Trocar's in. Now hold to inflate the retention balloon."; break;
                case "catheter": line = "Catheter's in the duct. Hold to inject, and watch the ducts fill."; break;
                case "clip":
                    if (text.Contains("oblique")) line = "That clip went on at an angle. Come across the duct square, jaws at right angles to it.";
                    else if (text.Contains("mm off plan") && OffPlan(text) >= 5) line = "About " + OffPlan(text) + " millimetres off the planned spot. Aim for the yellow marker.";
                    else line = ClipProgress();
                    break;
                default: line = text; break;
            }
            if (line == null) return;
            cur.lastSaidAt[key] = now;
            Enqueue(line, Pri.Info, key, 6f);
        }

        static int OffPlan(string text)
        {
            int at = text.IndexOf(" mm off plan");
            int start = text.LastIndexOf(", ", at) + 2;
            return at > start && int.TryParse(text.Substring(start, at - start), out int v) ? v : 0;
        }

        int DuctPlaced => Cur != null ? proc.DuctClips - Cur.ductAtStart : 0;
        int ArteryPlaced => Cur != null ? proc.ArteryClips - Cur.arteryAtStart : 0;

        string ClipProgress()
        {
            switch (proc.StepId)
            {
                case "clip_duct_proximal":
                    return DuctPlaced != 1 ? null : Pick("One on. One more, closer to the common bile duct.", "Good clip. Place the second just below it.");
                case "clip_artery":
                    int left = 3 - ArteryPlaced;
                    return left == 2 ? "First artery clip on. One more on the hepatic side." : left == 1 ? "Two down. Last clip goes on the gallbladder side." : null;
                default: return null;
            }
        }

        string MajorErrorLine(string text)
        {
            if (text.StartsWith("Clip on the common bile duct"))
                return "Stop. That clip is on the common bile duct. Major error. The cystic duct is the thinner branch running up to the gallbladder. Confirm it before every squeeze.";
            if (text.StartsWith("That cut is not on the cystic duct"))
                return "That cut missed the cystic duct. Major error. Put the blades just below the distal clip, on the duct only.";
            if (text.StartsWith("That cut is on the"))
            {
                string structure = text.Substring("That cut is on the ".Length).Split('.')[0];
                if (structure == "cbd") structure = "common bile duct";
                string expected = proc.StepId == "divide_artery" ? "cystic artery" : "cystic duct";
                return "Wrong structure. That was the " + structure + ". Major error. Divide only the " + expected + ", between the clips.";
            }
            return "Major error. " + text.Replace("Major error.", "").Trim() + " Slow down and confirm the anatomy before you squeeze.";
        }

        void LiveCues(StepStat cur)
        {
            string id = proc.StepId;
            if (proc.Pressure != lastPressure || proc.Balloon != lastBalloon || proc.Contrast != lastContrast || proc.Veil < lastVeil - 0.005f)
            {
                Progress(cur);
                lastPressure = proc.Pressure; lastBalloon = proc.Balloon; lastContrast = proc.Contrast; lastVeil = proc.Veil;
            }
            if (id == "insufflate")
            {
                if (!cur.cueA && proc.Pressure >= 6.5f && !OnDemand) { cur.cueA = true; Enqueue("Rising. Let go at twelve.", Pri.Urgent, "cue", 2f); }
                if (!cur.cueB && proc.Pressure > 15.2f) { cur.cueB = true; Enqueue("Too high. Let go now.", Pri.Urgent, "cue", 2.5f); }
            }
            else if (OnDemand) return;
            else if (id == "dissect")
            {
                if (!cur.cueA && proc.Veil < 0.55f) { cur.cueA = true; Enqueue("Halfway. Keep the sweeps short and lateral.", Pri.Info, "cue", 4f); }
                if (!cur.cueB && proc.Veil < 0.2f) { cur.cueB = true; Enqueue("Almost there. The duct and artery are nearly clear.", Pri.Info, "cue", 4f); }
            }
            else if (id == "port_umbilical" && proc.UmbilicalSeated && !cur.cueA && proc.Balloon > 0.5f)
            { cur.cueA = true; Enqueue("Balloon's halfway. Keep holding.", Pri.Info, "cue", 3f); }
            else if (id == "cholangiogram" && proc.CatheterIn && !cur.cueA && proc.Contrast > 0.45f)
            { cur.cueA = true; Enqueue("Contrast is filling the common bile duct. No leak so far.", Pri.Info, "cue", 4f); }

            // Instrument handling (headset): an exchange that stalls, or a shaft forced across the body wall.
            if (rig.desktopPreview || proc.Autopiloting || proc.StepIndex < 6) return;
            float now = Time.unscaledTime;
            if (proc.ExchangePending) { if (exchangeSince < 0) exchangeSince = now; } else exchangeSince = -1f;
            if (!proc.ShaftAligned && !proc.ExchangePending) { if (misalignedSince < 0) misalignedSince = now; } else misalignedSince = -1f;
            if (now - lastAlignCue < 15f) return;
            if (exchangeSince > 0 && now - exchangeSince > 5f)
            { lastAlignCue = now; Enqueue("Pull the " + proc.EquippedToolLabel + " back to the trocar first. Then the next instrument goes in.", Pri.Info, "align", 6f); }
            else if (misalignedSince > 0 && now - misalignedSince > 4f && !proc.AlignmentCue.StartsWith("HAND"))
            { lastAlignCue = now; Enqueue("Re-centre the handle over the port. The shaft pivots at the trocar; it can't pass through the body wall.", Pri.Info, "align", 6f); }
        }

        void Hints(StepStat cur)
        {
            if (proc.Autopiloting) return;
            // Idle counts from the later of real progress and the end of the coach's last line.
            float idle = Time.unscaledTime - Mathf.Max(cur.lastProgress, speechEnded);
            float[] at = { firstHintAfter, firstHintAfter + 16f, firstHintAfter + 38f };
            int due = 0;
            for (int i = 0; i < at.Length; i++) if (idle >= at[i]) due = i + 1;
            if (due > 3) due = 3;
            if (idle >= at[2] + 35f * (cur.hintLevel - 2) && cur.hintLevel >= 3) due = cur.hintLevel + 1;
            if (due <= cur.hintLevel) return;
            if (busy) return; // never stack a hint on top of other speech; try again next frame
            cur.hintLevel = due;
            Enqueue(Hint(Mathf.Min(due, 3)), Pri.Hint, "hint", 8f);
        }

        // ---------------- composition ----------------
        string Recap(StepStat s)
        {
            var parts = new List<string>();
            float took = s.end - s.start;
            bool unsafeCut = (s.id == "divide_duct" || s.id == "divide_artery") && s.result.Contains("Not enough clips");
            if (unsafeCut) parts.Add("That cut wasn't protected by enough clips. Major error.");
            else if (s.major > 0) parts.Add("Let's reset after that error.");
            else if (IsPort(s.id) && s.mm.HasValue)
            {
                int mm = Mathf.RoundToInt(s.mm.Value);
                string where = mm <= 1 ? Pick("Dead centre.", "Right on the mark.")
                    : mm <= 4 ? Pick("Spot on", "Right on the mark", "Excellent") + ", " + Mm(mm) + " off."
                    : mm <= 8 ? "Good, " + Mm(mm) + " off."
                    : Mm(mm) + " off. Aim tighter on the next one.";
                if (s.angle.HasValue && s.angle.Value > 20f) where += " Watch the angle, " + Mathf.RoundToInt(s.angle.Value) + " degrees.";
                parts.Add(where);
            }
            else if (s.id == "insufflate" && s.pressure.HasValue)
            {
                float p = s.pressure.Value; string v = p.ToString("F0");
                parts.Add(p >= 12f && p <= 15f ? v + " millimetres of mercury, right in range."
                    : p > 15f ? v + ", a bit high. Release sooner next time." : v + ", a little low. Hold a moment longer next time.");
            }
            else if (s.id == "dissect") parts.Add("Critical view of safety. Duct and artery clearly seen.");
            else if (s.id == "divide_duct" || s.id == "divide_artery") parts.Add("Clean division, clips secure.");
            else if (s.id == "cholangiogram") parts.Add("Clear cholangiogram, no filling defects.");

            if (s.major == 0)
            {
                if (s.corrections >= 3) parts.Add("You got there. That one took a few tries.");
                else if (s.corrections == 0 && took < Expected(s.id) * 0.6f && parts.Count < 2) parts.Add(Pick("Nice and efficient.", "Smooth."));
                if (cleanStreak == 3 || cleanStreak == 6 || cleanStreak == 10) parts.Add(Num(cleanStreak) + " clean steps in a row.");
            }
            if (parts.Count == 0) parts.Add(Pick("Done.", "Good.", "Got it."));
            return string.Join(" ", parts.GetRange(0, Mathf.Min(2, parts.Count)));
        }

        /// <summary>Opening line for a run.</summary>
        static string Opening(bool desktop) => desktop
            ? "Let's begin. Fifteen steps, in the order of a real lap chole. I'll coach you as you go."
            : "Let's begin. Fifteen steps, in the order of a real lap chole. Move the instruments like real tools; I'll coach you as you go.";

        /// <summary>Step instruction. level 0 = full how-to, 1 = what, 2 = terse.</summary>
        static string IntroFor(int index, int level, bool desktop)
        {
            string id = LapCholeProcedure.StepIdAt(index);
            string prefix = id == "retract" ? "We're inside now. Work from the laparoscope image. " : "";
            string n = Word(index + 1);
            n = char.ToUpperInvariant(n[0]) + n.Substring(1);
            if (level == 0) return prefix + "Step " + n + ". " + LapCholeProcedure.StepWhatAt(index) + " " + LapCholeProcedure.StepHowAt(index, desktop);
            if (level == 2 && Terse.TryGetValue(id, out string terse)) return prefix + "Next, " + terse;
            return prefix + "Step " + n + ", " + LapCholeProcedure.StepTitleAt(index) + ". " + LapCholeProcedure.StepWhatAt(index);
        }

        string Intro()
        {
            string id = proc.StepId;
            int n = proc.StepIndex + 1;
            bool struggling = false, fluent = cleanStreak >= 2;
            for (int i = Mathf.Max(0, stats.Count - 3); i < stats.Count - 1; i++)
            {
                StepStat s = stats[i];
                if (s.major > 0 || s.corrections >= 3 || (s.end > 0 && s.end - s.start > Expected(s.id) * 2f)) struggling = true;
                if (s.end > 0 && s.end - s.start > Expected(s.id)) fluent = false;
            }
            return IntroFor(proc.StepIndex, struggling ? 0 : fluent && Terse.ContainsKey(id) ? 2 : 1, rig.desktopPreview);
        }

        string Hint(int level)
        {
            string id = proc.StepId;
            bool desk = rig.desktopPreview;
            if (!desk && proc.AlignmentCue.StartsWith("HAND"))
                return "Your hand is in the patient. Withdraw it and steer through the trocar.";
            if (!desk && proc.ExchangePending)
                return "Withdraw the " + proc.EquippedToolLabel + " to the trocar to change instruments.";
            if (!desk && proc.StepIndex >= 6 && !proc.ShaftAligned)
                return "Re-centre the handle over the port. The instrument pivots at the trocar.";
            string specific = null;
            switch (id)
            {
                case "veress": specific = desk ? "Put the needle on the navel ring and click." : "Needle tip on the purple cross at the navel, shaft upright along the guide, then push. You'll feel two pops."; break;
                case "insufflate":
                    specific = proc.Pressure <= 0.1f ? (desk ? "Hold space to start the CO2." : "Hold the trigger to start the CO2.")
                        : "You're at " + proc.Pressure.ToString("F0") + ". " + (proc.Pressure < 12f ? "Keep holding, and let go between twelve and fifteen." : "Let go now.");
                    break;
                case "port_umbilical":
                    specific = proc.UmbilicalSeated ? "Hold to inflate the balloon. It's at " + Mathf.RoundToInt(proc.Balloon * 100f) + " percent."
                        : desk ? "Click on the navel to seat the trocar." : "Go in through the needle puncture, straight down, and push until it pops through.";
                    break;
                case "port_epigastric": specific = desk ? "Find the ring just below the breastbone and click." : "The cross is just below the breastbone. Tip on it, match the guide's angle toward the gallbladder, push through."; break;
                case "port_rsc_medial": specific = desk ? "The ring is under the right ribs." : "The cross is under the right ribs, in line with the middle of the collarbone. Follow the guide's angle in."; break;
                case "port_rsc_lateral": specific = desk ? "Go further out on the right flank, to the outer ring." : "Furthest cross, out on the right flank. Keep clear of the medial port and follow the guide."; break;
                case "retract": specific = "Find the round fundus at the top of the gallbladder. Close the grasper on it."; break;
                case "dissect":
                    specific = "About " + Mathf.RoundToInt(proc.Veil * 100f) + " percent of the peritoneum is left. "
                        + (desk ? "Hold the button and sweep across the yellow area." : "Hold the trigger and make short sweeps across the yellow area.");
                    break;
                case "clip_duct_distal": specific = "Target the cystic duct close to the gallbladder, where the marker glows. Not the blue common bile duct."; break;
                case "ductotomy": specific = "Cut just below the clip you placed. Duct only."; break;
                case "cholangiogram":
                    specific = proc.CatheterIn ? "Hold to inject. Contrast is at " + Mathf.RoundToInt(proc.Contrast * 100f) + " percent." : "Aim the catheter tip into the small cut on the duct.";
                    break;
                case "clip_duct_proximal": specific = (2 - Mathf.Clamp(DuctPlaced, 0, 2)) + " clips to go, on the duct below the cut, toward the common bile duct."; break;
                case "divide_duct": specific = "Cut in the gap between the lower clips and the ductotomy."; break;
                case "clip_artery": specific = (3 - Mathf.Clamp(ArteryPlaced, 0, 3)) + " clips to go on the salmon-coloured artery."; break;
                case "divide_artery": specific = "Cut the artery between the two lower clips and the top one."; break;
            }
            if (level <= 1) return specific;
            if (level == 2) return "Here's how. " + proc.StepHow;
            return Pick("No rush. ", "Take a breath. ") + "Follow the glowing target. " + specific;
        }

        string Debrief()
        {
            float total = Time.unscaledTime - runStart;
            int minutes = Mathf.FloorToInt(total / 60f), seconds = Mathf.RoundToInt(total % 60f);
            int major = rig.Recorder.LapMajor, minor = rig.Recorder.LapMinor;
            var sb = new StringBuilder("Procedure complete in ");
            sb.Append(minutes > 0 ? minutes + (minutes == 1 ? " minute " : " minutes ") : "").Append(seconds).Append(" seconds. ");
            sb.Append(ScoreSentence()).Append(' ');
            StepStat best = null, worst = null;
            foreach (StepStat s in stats)
            {
                if (s.end < 0) continue;
                float ratio = (s.end - s.start) / Expected(s.id);
                if (s.major == 0 && s.corrections == 0 && (best == null || ratio < (best.end - best.start) / Expected(best.id))) best = s;
                int badness = s.major * 10 + s.corrections;
                if (badness > 0 && (worst == null || badness > worst.major * 10 + worst.corrections)) worst = s;
            }
            if (best != null) sb.Append("Smoothest step: ").Append(Title(best.id)).Append(". ");
            if (worst != null) sb.Append("Next time, focus on ").Append(Title(worst.id)).Append(worst.major > 0 ? ": confirm the structure before every squeeze." : ": settle the tip before you commit.");
            else if (bestStreak >= 10) sb.Append("Clean from start to finish.");
            return sb.ToString().Trim();
        }

        static readonly Dictionary<string, string> FindingNames = new Dictionary<string, string>
        {
            { "injury", "an injury" }, { "hand_collision", "hand collisions with the abdominal wall" }, { "port_offset", "a port off target" },
            { "insufflation_out_of_range", "insufflation pressure out of range" }, { "cbd_clip", "a clip on the common bile duct" },
            { "clip_crossing", "a clip across the structure" }, { "ductotomy_wrong_structure", "a ductotomy on the wrong structure" },
            { "unsafe_ductotomy", "a ductotomy without a clip above it" }, { "ductotomy_before_cvs", "a ductotomy before the critical view" },
            { "cholangiogram_not_in_duct", "a catheter outside the duct" }, { "cholangiogram_leak", "a contrast leak" },
            { "cbd_cut", "a cut on the common bile duct" }, { "clip_too_close", "a cut too close to a clip" },
            { "unsafe_division", "a division without enough clips" }, { "division_before_cvs", "a division before the critical view" },
        };

        /// <summary>Score from the sim's findings (EventRecorder.LapFindings mirrors backend scoring.lap_chole_findings).</summary>
        string ScoreSentence()
        {
            List<KeyValuePair<string, bool>> findings = rig.Recorder.LapFindings();
            var major = new List<string>(); var minor = new List<string>();
            foreach (var f in findings)
            {
                string name = FindingNames.TryGetValue(f.Key, out string n) ? n : f.Key.Replace('_', ' ');
                List<string> list = f.Value ? major : minor;
                if (!list.Contains(name)) list.Add(name);
            }
            int majors = 0, minors = 0; foreach (var f in findings) { if (f.Value) majors++; else minors++; }
            string rating = majors > 0 ? "Not yet safe" : minors == 0 ? "Proficient" : minors <= 2 ? "Competent" : "Competent, with work to do";
            var sb = new StringBuilder("Score: ").Append(rating).Append(". ");
            if (majors == 0 && minors == 0) sb.Append("No errors.");
            else
            {
                if (majors > 0) sb.Append(majors == 1 ? "One major error: " : majors + " major errors: ").Append(string.Join(", ", major)).Append(". ");
                if (minors > 0) sb.Append(minors == 1 ? "One minor: " : minors + " minor: ").Append(string.Join(", ", minor)).Append('.');
            }
            return sb.ToString().Trim();
        }

        static string Title(string id)
        {
            for (int i = 0; i < LapCholeProcedure.StepCount; i++) if (LapCholeProcedure.StepIdAt(i) == id) return LapCholeProcedure.StepTitleAt(i);
            return id;
        }
        static string Mm(int mm) => mm == 1 ? "one millimetre" : mm + " millimetres";
        static string Num(int n) => n == 3 ? "Three" : n == 6 ? "Six" : n == 10 ? "Ten" : n.ToString();
        string Pick(params string[] options) => options[rng.Next(options.Length)];

        // Lines that are the same every run: fetch at step start so the cue plays the instant it is due.
        void PrefetchCues(string id)
        {
            if (id == "insufflate") { Prefetch("Rising. Let go at twelve."); Prefetch("Too high. Let go now."); }
            else if (id == "dissect") { Prefetch("Halfway. Keep the sweeps short and lateral."); Prefetch("Almost there. The duct and artery are nearly clear."); }
        }

        // ---------------- speech queue ----------------
        enum Pri { Hint = 0, Info = 1, Step = 2, Urgent = 3 }
        sealed class Line
        {
            public string text, spoken, key, recorded; public bool prefetch, cached; public Pri pri; public float made, maxAge;
            public AudioClip clip; public bool failed, done;
        }
        readonly List<Line> queue = new List<Line>();
        Line playing;
        string caption = "";
        float captionUntil, speechEnded;
        bool busy => playing != null || queue.Count > 0;

        void Enqueue(string text, Pri pri, string key, float maxAge, string recordedFallback = null, bool append = false)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            text = text.Trim();
            if (rig != null && !rig.desktopPreview) text = HandWords(text);
            // On demand: only safety (urgent), step-level lines it chose to say, and answers are spoken.
            if (OnDemand && pri <= Pri.Info) { ShowCaption(text, 5f); Log("caption only (on demand) \"" + text + "\""); return; }
            if (pri == Pri.Hint && busy) return;
            Remember(text);
            if (pri >= Pri.Step && !append) queue.RemoveAll(l => l.pri < Pri.Urgent || (pri == Pri.Step && l.pri == Pri.Step));
            if (pri == Pri.Urgent) queue.RemoveAll(l => l.pri <= Pri.Info);
            queue.RemoveAll(l => l.key == key && l.pri == pri);
            if (queue.Count >= 5) queue.RemoveAt(queue.Count - 1);
            var line = new Line { text = text, spoken = Speakable(text), pri = pri, key = key, made = Time.unscaledTime, maxAge = maxAge, recorded = recordedFallback };
            queue.Add(line);
            queue.Sort((a, b) => a.pri != b.pri ? b.pri.CompareTo(a.pri) : a.made.CompareTo(b.made));
            Log("queue " + pri + " \"" + text + "\"");
            StartCoroutine(Load(line));
        }

        void ShowCaption(string text, float seconds) { caption = text; captionUntil = Time.unscaledTime + seconds; }

        /// <summary>Play an answer now: it interrupts whatever the coach was saying.</summary>
        void PlayNow(string text, AudioClip clip, string heard)
        {
            ClearQueue();
            proc.StopRecordedClip();
            Remember(text);
            if (clip == null) { ShowCaption(text, 8f); return; }
            var line = new Line { text = text, spoken = text, pri = Pri.Urgent, key = "answer", made = Time.unscaledTime, maxAge = 30f, clip = clip };
            queue.Insert(0, line);
        }

        void ClearQueue()
        {
            queue.Clear();
            if (source != null) source.Stop();
            playing = null;
            caption = ""; captionUntil = 0f;
        }

        void Pump()
        {
            float now = Time.unscaledTime;
            if (playing != null) speechEnded = now;
            if (playing != null && !source.isPlaying) playing = null;
            queue.RemoveAll(l => now - l.made > l.maxAge + (l.pri >= Pri.Step ? 6f : 0f) && l != playing);
            if (queue.Count == 0 || talking) return;
            if (coachAudio != null && coachAudio.IsSpeaking && playing == null) return; // the end-of-run coach is talking
            Line head = queue[0];
            // An urgent line cuts in on anything less urgent.
            if (playing != null && !(head.pri == Pri.Urgent && playing.pri < Pri.Urgent && (head.clip != null || head.failed))) return;
            if (head.clip == null && !head.failed) return; // still synthesizing; keep order
            queue.RemoveAt(0);
            source.Stop();
            proc.StopRecordedClip();
            caption = head.text;
            if (head.clip != null)
            {
                source.clip = head.clip;
                source.Play();
                playing = head;
                captionUntil = now + head.clip.length + 3f;
                string via = head.cached ? "cache" : VoiceRoute;
                Status = "speaking (" + via + ")";
                Log("play " + head.pri + " via " + via + " " + head.clip.length.ToString("F1") + "s \"" + head.text + "\"");
            }
            else
            {
                captionUntil = now + 6f;
                if (!string.IsNullOrEmpty(head.recorded)) { proc.PlayRecordedClip(head.recorded); Log("fallback recorded clip " + head.recorded); }
                else Log("text only \"" + head.text + "\"");
                Status = "voice unavailable; captions";
            }
        }

        // ---------------- Cartesia via backend ----------------
        readonly Dictionary<string, AudioClip> memory = new Dictionary<string, AudioClip>();
        readonly Dictionary<string, bool> inflight = new Dictionary<string, bool>();
        string fallbackSession;
        bool creatingSession;
        float primaryDownUntil, fallbackDownUntil, backoffUntil;
        readonly Queue<float> sent = new Queue<float>();
        const int BudgetPerMinute = 11; // the backend allows 12 /coach/audio calls per session per minute

        void Prefetch(string text)
        {
            string spoken = Speakable(text);
            if (!memory.ContainsKey(spoken)) StartCoroutine(Load(new Line { text = text, spoken = spoken, pri = Pri.Hint, made = Time.unscaledTime, maxAge = 60f, prefetch = true }));
        }

        // The headset build is hands only: the pinch is the trigger.
        static string HandWords(string text) => text
            .Replace("hold the trigger", "hold the pinch").Replace("Hold the trigger", "Hold the pinch")
            .Replace("the trigger", "your pinch").Replace("squeeze", "pinch").Replace("Squeeze", "Pinch");

        // Caption text stays as written; the transcript spells out units so the voice reads them naturally.
        static string Speakable(string text)
        {
            string t = System.Text.RegularExpressions.Regex.Replace(text, @"(\d+)-(\d+)", "$1 to $2");
            t = t.Replace("mmHg", "millimetres of mercury").Replace("CO2", "C O two").Replace("Hem-o-lok", "hem-o-lock");
            t = System.Text.RegularExpressions.Regex.Replace(t, @"(\d+) mm\b", "$1 millimetre");
            t = System.Text.RegularExpressions.Regex.Replace(t, @"(\d+) cm\b", "$1 centimetres");
            t = t.Replace("°", " degrees");
            t = System.Text.RegularExpressions.Regex.Replace(t, @"\d+(\.\d+)?", m => SpokenNumber(m.Value));
            return t;
        }

        static string SpokenNumber(string digits)
        {
            if (!double.TryParse(digits, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v)) return digits;
            int n = (int)Math.Round(v);
            if (n < 0 || n > 999) return digits;
            string[] ones = { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen" };
            string[] tens = { "", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety" };
            string Under100(int x) => x < 20 ? ones[x] : tens[x / 10] + (x % 10 > 0 ? "-" + ones[x % 10] : "");
            return n < 100 ? Under100(n) : ones[n / 100] + " hundred" + (n % 100 > 0 ? " " + Under100(n % 100) : "");
        }

        IEnumerator Load(Line line)
        {
            string text = line.spoken ?? line.text;
            AudioClip banked = BankClip(text);
            if (banked != null) { line.clip = banked; line.cached = true; yield break; }
            if (memory.TryGetValue(text, out AudioClip hit) && hit != null) { line.clip = hit; line.cached = true; yield break; }
            string file = CachePath(text);
            if (file != null && File.Exists(file))
            {
                AudioClip disk = null;
                try { disk = WavToClip(File.ReadAllBytes(file), "narration"); } catch (IOException) { }
                if (disk != null) { Remember(text, disk); line.clip = disk; line.cached = true; yield break; }
            }
            // Synthesize only when this line is next up: superseded lines then cost no provider call.
            while (!line.prefetch && queue.Contains(line) && queue[0] != line) yield return null;
            if (!line.prefetch && !queue.Contains(line)) yield break;
            // Same text already on its way (prefetch): wait for it.
            while (inflight.ContainsKey(text)) { yield return null; if (memory.TryGetValue(text, out hit) && hit != null) { line.clip = hit; yield break; } }
            if (memory.TryGetValue(text, out hit) && hit != null) { line.clip = hit; yield break; }
            float now = Time.unscaledTime;
            while (sent.Count > 0 && now - sent.Peek() > 60f) sent.Dequeue();
            // Keep budget for step intros and urgent lines when the minute is nearly used up.
            // Urgent safety lines may use the whole minute; step lines keep one call back for them.
            int reserve = line.pri == Pri.Urgent ? 0 : line.pri == Pri.Step ? 1 : 4;
            if (now < backoffUntil || sent.Count >= BudgetPerMinute - reserve) { line.failed = true; Log("skip voice (budget) \"" + line.text + "\""); yield break; }
            inflight[text] = true;
            sent.Enqueue(now);
            byte[] wav = null;
            // 1) The sim's own backend, for the attempt it is already uploading.
            PendingAttempt attempt = rig.Uploads != null ? rig.Uploads.LatestStarted() : null;
            if (api != null && attempt != null && now >= primaryDownUntil
                && api.TryUrl(ApiClient.SessionPath(attempt.sessionId) + "/coach/audio", out string primaryUrl))
            {
                yield return Post(primaryUrl, text, true, 8, r => wav = r);
                if (wav != null) VoiceRoute = "sim backend"; else primaryDownUntil = Time.unscaledTime + 120f;
            }
            // 2) The deployed backend with Cartesia configured.
            if (wav == null && !string.IsNullOrWhiteSpace(voiceFallbackOrigin) && Time.unscaledTime >= fallbackDownUntil)
            {
                for (int attemptNo = 0; attemptNo < 2 && wav == null; attemptNo++)
                {
                    if (fallbackSession == null) yield return EnsureFallbackSession();
                    if (fallbackSession == null) break;
                    long status = 0;
                    yield return Post(Origin() + "/api/v1/sessions/" + Uri.EscapeDataString(fallbackSession) + "/coach/audio", text, false, 30, r => wav = r, s => status = s);
                    if (wav != null) { VoiceRoute = "Cartesia via " + new Uri(Origin()).Host; break; }
                    if (status == 404) { fallbackSession = null; continue; } // the server's database was reset
                    if (status == 429) backoffUntil = Time.unscaledTime + 20f;
                    else if (status == 0 || status >= 500) fallbackDownUntil = Time.unscaledTime + 30f;
                    break;
                }
            }
            inflight.Remove(text);
            AudioClip clip = wav != null ? WavToClip(wav, "narration") : null;
            if (clip == null) { line.failed = true; Log("voice failed \"" + line.text + "\""); yield break; }
            Remember(text, clip);
            if (file != null) { try { Directory.CreateDirectory(Path.GetDirectoryName(file)); File.WriteAllBytes(file, wav); } catch (IOException) { } }
            line.clip = clip;
        }

        string Origin() => voiceFallbackOrigin.Trim().TrimEnd('/');

        IEnumerator EnsureFallbackSession()
        {
            if (creatingSession) { while (creatingSession) yield return null; yield break; }
            if (!Uri.TryCreate(Origin(), UriKind.Absolute, out Uri origin) || origin.Scheme != "https") yield break;
            creatingSession = true;
            string body = "{\"trainee_id\":\"voice-narrator\",\"exercise\":\"lap_chole\",\"client_request_id\":\"narrator-" + Guid.NewGuid().ToString("N").Substring(0, 24) + "\"}";
            using (var request = new UnityWebRequest(Origin() + "/api/v1/sessions", "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = 40; // a sleeping free-tier server takes a while to wake
                request.redirectLimit = 0;
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success)
                {
                    var created = JsonUtility.FromJson<CreatedSession>(request.downloadHandler.text);
                    if (created != null && !string.IsNullOrEmpty(created.id)) { fallbackSession = created.id; Log("voice session " + created.id + " on " + origin.Host); }
                }
                else { fallbackDownUntil = Time.unscaledTime + 30f; Log("voice session failed: " + request.responseCode + " " + request.error); }
            }
            creatingSession = false;
        }
        [Serializable] sealed class CreatedSession { public string id; }
        [Serializable] sealed class SpeechBody { public string text; }

        IEnumerator Post(string url, string text, bool withAuth, int timeout, Action<byte[]> done, Action<long> status = null)
        {
            using (var request = new UnityWebRequest(url, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(new SpeechBody { text = text })));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                if (withAuth && api != null) api.ApplyAuth(request);
                request.timeout = timeout;
                request.redirectLimit = 0;
                float t0 = Time.unscaledTime;
                yield return request.SendWebRequest();
                status?.Invoke(request.responseCode);
                if (request.result == UnityWebRequest.Result.Success && request.downloadHandler.data != null)
                {
                    Log("synth " + ((Time.unscaledTime - t0) * 1000f).ToString("F0") + " ms " + request.downloadHandler.data.Length + " bytes");
                    done(request.downloadHandler.data);
                }
                else { Log("synth failed " + request.responseCode + " " + request.error + " (" + new Uri(url).Host + ")"); done(null); }
            }
        }

        void Remember(string text, AudioClip clip)
        {
            if (memory.Count > 60)
            {
                // Drop arbitrary entries that are not about to play; clips on disk come back cheaply.
                var keys = new List<string>(memory.Keys);
                foreach (string k in keys.GetRange(0, 20))
                    if (playing == null || playing.clip != memory[k]) { if (queue.TrueForAll(l => l.clip != memory[k])) Destroy(memory[k]); memory.Remove(k); }
            }
            memory[text] = clip;
        }

        string CachePath(string text)
        {
            // Only reusable lines go to disk; lines with numbers are one-offs.
            if (!diskCache) return null;
            foreach (char c in text) if (char.IsDigit(c)) return null;
            using (var sha = System.Security.Cryptography.SHA1.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes("cartesia-v1|" + text));
                var sb = new StringBuilder(); for (int i = 0; i < 10; i++) sb.Append(h[i].ToString("x2"));
                return Path.Combine(Application.temporaryCachePath, "narration", sb + ".wav");
            }
        }

        /// <summary>PCM16 WAV bytes to an AudioClip; tolerates extra chunks (LIST) before the data chunk.</summary>
        public static AudioClip WavToClip(byte[] wav, string name)
        {
            if (wav == null || wav.Length < 44 || Encoding.ASCII.GetString(wav, 0, 4) != "RIFF" || Encoding.ASCII.GetString(wav, 8, 4) != "WAVE") return null;
            int pos = 12, channels = 0, rate = 0, bits = 0, dataAt = -1, dataLen = 0;
            while (pos + 8 <= wav.Length)
            {
                string id = Encoding.ASCII.GetString(wav, pos, 4);
                int len = BitConverter.ToInt32(wav, pos + 4);
                if (id == "fmt " && pos + 24 <= wav.Length)
                {
                    channels = BitConverter.ToInt16(wav, pos + 10); rate = BitConverter.ToInt32(wav, pos + 12); bits = BitConverter.ToInt16(wav, pos + 22);
                }
                else if (id == "data")
                {
                    dataAt = pos + 8;
                    dataLen = len < 0 || dataAt + len > wav.Length ? wav.Length - dataAt : len;
                    break;
                }
                if (len < 0) return null;
                pos += 8 + len + (len & 1);
            }
            if (dataAt < 0 || bits != 16 || channels < 1 || rate <= 0) return null;
            int samples = dataLen / 2 / channels * channels;
            if (samples == 0) return null;
            var data = new float[samples];
            for (int i = 0; i < samples; i++) data[i] = BitConverter.ToInt16(wav, dataAt + i * 2) / 32768f;
            AudioClip clip = AudioClip.Create(name, samples / channels, channels, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        // ---------------- captions and log ----------------
        GUIStyle captionStyle;
        void OnGUI()
        {
            if (rig == null || !rig.desktopPreview || !rig.LapChole) return;
            LapCholePanel ui = GetComponent<LapCholePanel>();
            if (ui != null && ui.Built) return; // the panel's coach bubble shows the caption
            string text = Caption;
            if (text.Length == 0) return;
            if (captionStyle == null)
                captionStyle = new GUIStyle(GUI.skin.box) { fontSize = 16, wordWrap = true, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(14, 14, 10, 10), richText = true };
            float w = Mathf.Min(760f, Screen.width - 420f);
            float h = captionStyle.CalcHeight(new GUIContent(text), w) + 4f;
            GUI.Box(new Rect((Screen.width - w) * 0.5f + 180f, Screen.height - h - 24f, w, h), "<color=#ffd166><b>Coach</b></color>  " + text, captionStyle);
        }

        static string logPath;
        static void Log(string message)
        {
            Debug.Log("[Narrator] " + message);
            try
            {
                if (logPath == null) { Directory.CreateDirectory(PreviewAutopilot.BridgeFolder); logPath = Path.Combine(PreviewAutopilot.BridgeFolder, "narration.log"); }
                File.AppendAllText(logPath, DateTime.Now.ToString("HH:mm:ss.fff") + " " + message + "\n");
            }
            catch (IOException) { }
        }
    }
}
