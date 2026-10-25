using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using GallbladderTrainer.Backend;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace GallbladderTrainer.Guide
{
    /// <summary>
    /// Session bar ("Finish &amp; review") and the end-of-session holographic review.
    /// On finish: flush + POST /complete, GET /report (retries 409), animate the review in (score ring, one card per
    /// exercise, movement stats, strengths / focus next, mistake-cost count-up), hand the providers to the crew,
    /// and have the robot read the summary via POST /report/audio. "Replay" opens ReplayView.
    /// Every number shown comes from the backend; offline the panel says it is not scored instead of guessing.
    /// Desktop keys: F finish, Esc close.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SessionReportPanel : MonoBehaviour
    {
        [SerializeField] internal JeremyBackendHook hook;
        [SerializeField] internal RobotGuide guide;
        [SerializeField] internal ReplayView replay;
        [SerializeField] internal Transform workstationRoot;
        [SerializeField] Vector3 barPosition = new Vector3(-0.80f, 1.80f, -0.235f);
        [SerializeField] Vector3 barEuler = new Vector3(0f, -25f, 0f);
        [SerializeField] Vector3 reportPosition = new Vector3(0f, 1.45f, -0.38f);
        [SerializeField] int reportFetchAttempts = 6;

        const float W = 1400f, H = 900f, Scale = 0.0009f;

        // bar
        HoloUi.Panel bar;
        Text barStatus, finishLabel;
        Button finishButton;
        Image barDot;
        // report
        Transform reportRoot;
        HoloUi.Panel report;
        RectTransform cardsRoot, costRoot, listsRoot;
        Image ring, ringBack;
        Text scoreText, gradeText, headerText, sourceText, movementText, summaryText, listsText, costTotalText, costNote, disclaimerText;
        Button replayButton, hearButton;
        CrewBridge crew;

        enum Phase { Running, Finishing, Report, Replay, Closed }
        Phase phase = Phase.Running;
        object reportJson;
        bool localOnly;
        float reportShownAt;
        readonly List<RectTransform> animated = new List<RectTransform>();
        readonly List<CanvasGroup> animatedGroups = new List<CanvasGroup>();
        readonly StringBuilder sb = new StringBuilder(512);
        float targetScore, ringStart = 99f;

        // ------------------------------------------------------------------ lifecycle

        void Awake()
        {
            if (hook == null) hook = GetComponentInParent<JeremyBackendHook>() ?? FindAnyObjectByType<JeremyBackendHook>();
            if (guide == null) guide = FindAnyObjectByType<RobotGuide>();
            if (replay == null) replay = GetComponent<ReplayView>();
            if (workstationRoot == null)
            {
                var ctrl = GetComponentInParent<TrainingController>();
                workstationRoot = ctrl != null ? ctrl.transform : transform;
            }
            BuildBar();
            BuildReport();
            HoloUi.Fade(report.Group, 0f);
            reportRoot.gameObject.SetActive(false);
        }

        void Update()
        {
            HoloUi.AssignCamera(bar.Canvas);
            HoloUi.AssignCamera(report.Canvas);
            UpdateBar();
            AnimateReport();
            ReadKeys();
        }

        void ReadKeys()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard k = Keyboard.current;
            if (k == null) return;
            if (k.fKey.wasPressedThisFrame) OnFinishPressed();
            if (k.escapeKey.wasPressedThisFrame && phase == Phase.Report) CloseReport();
#endif
        }

        // ------------------------------------------------------------------ session bar

        void UpdateBar()
        {
            if (hook == null) return;
            long s = hook.ElapsedMs / 1000;
            int answered = 0;
            foreach (var ex in ExerciseLibrary.All) if (hook.Decisions.ContainsKey(ex.Id)) answered++;
            sb.Length = 0;
            sb.Append(hook.StatusText).Append('\n')
              .Append((s / 60).ToString("00")).Append(':').Append((s % 60).ToString("00"))
              .Append("   ·   ").Append(answered).Append(" of ").Append(ExerciseLibrary.All.Length).Append(" answered");
            if (hook.State == BackendState.Retrying && hook.PendingCount > 0) sb.Append("   ·   ").Append(hook.PendingCount).Append(" queued");
            barStatus.text = sb.ToString();

            Color dot;
            switch (hook.State)
            {
                case BackendState.Active: case BackendState.Completed: dot = HoloUi.Green; break;
                case BackendState.Connecting: case BackendState.Completing: dot = HoloUi.Cyan; break;
                case BackendState.Retrying: dot = HoloUi.Amber; break;
                default: dot = HoloUi.TextDim; break;
            }
            float breathe = 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 2.2f);
            barDot.color = new Color(dot.r, dot.g, dot.b, breathe);

            bool ready = phase == Phase.Running && hook.AllAnswered;
            finishButton.interactable = phase == Phase.Running || phase == Phase.Closed;
            finishLabel.text = phase == Phase.Finishing ? "Scoring…" : phase == Phase.Closed ? "Show review" : "Finish & review";
            // a slow glow invites (never forces) finishing once every exercise has an answer
            float glow = ready ? 0.25f + 0.25f * Mathf.Sin(Time.unscaledTime * 2.5f) : 0.12f;
            bar.GlowImage.color = new Color(HoloUi.Cyan.r, HoloUi.Cyan.g, HoloUi.Cyan.b, glow);
        }

        void OnFinishPressed()
        {
            if (phase == Phase.Closed) { ShowReport(); return; }
            if (phase != Phase.Running) return;
            StartCoroutine(FinishRoutine());
        }

        IEnumerator FinishRoutine()
        {
            phase = Phase.Finishing;
            if (guide != null) { guide.SetNarration(false); guide.Say("Wrapping up. I'm scoring your session now.", "", 5f); }
            bool ok = false;
            yield return hook.Complete((done, r) => ok = done);

            reportJson = null;
            if (ok)
            {
                for (int i = 0; i < reportFetchAttempts && reportJson == null; i++)
                {
                    ApiResponse r = null;
                    yield return hook.Api.Send("GET", JeremyApi.SessionPath(hook.SessionId) + "/report", null, x => r = x);
                    if (r.Ok) reportJson = Json.Parse(r.Body);
                    else if (r.Code == 409 || r.Retryable) yield return new WaitForSecondsRealtime(1f + i);
                    else break;
                }
            }
            localOnly = reportJson == null;
            Populate();
            ShowReport();

            yield return new WaitForSecondsRealtime(1.2f);
            if (!localOnly) StartCoroutine(PlayProviderEvents());
            string summary = localOnly ? null : Json.Str(reportJson, "summary");
            if (guide != null && !string.IsNullOrEmpty(summary))
            {
                guide.Say(summary, Grade() == "needs_practice" ? "slow" : "on_track", 0f);
                yield return guide.SpeakFrom(JeremyApi.SessionPath(hook.SessionId) + "/report/audio", null);
            }
            else if (guide != null && localOnly)
                guide.Say("I couldn't reach the server, so there's no score this time. Your answers and the movement replay are still here.", "slow", 9f);
        }

        // ------------------------------------------------------------------ report content

        string Grade() { return Json.Str(reportJson, "grade", ""); }

        void Populate()
        {
            ClearChildren(cardsRoot);
            ClearChildren(listsRoot);
            animated.Clear(); animatedGroups.Clear();

            if (localOnly)
            {
                headerText.text = "SESSION REVIEW  ·  Laparoscopic cholecystectomy: critical view of safety";
                sourceText.text = "Not scored: the backend was unreachable. Scores come only from the backend.";
                targetScore = 0f;
                scoreText.text = "–";
                gradeText.text = "not scored";
                gradeText.color = HoloUi.TextDim;
                movementText.text = MovementLocal();
                summaryText.text = "Local summary. Your decisions are listed with the trainer's built-in result. Reconnect to get a scored review, coaching and costs.";
                listsText.text = "";
                BuildLocalCards();
                costTotalText.text = "—";
                costNote.text = "Mistake costs are computed by the backend; unavailable offline.";
                disclaimerText.text = ExerciseLibrary.Disclaimer;
                hearButton.interactable = false;
                return;
            }

            object r = reportJson;
            headerText.text = "SESSION REVIEW  ·  Laparoscopic cholecystectomy: critical view of safety";
            string source = Json.Str(r, "source", "");
            sourceText.text = "Score: deterministic backend rules.  Words: " + (source == "gemini" ? "Gemini" : "built-in template") + ".";
            targetScore = Mathf.Clamp((float)Json.Num(r, "score"), 0f, 100f);
            string grade = Grade();
            gradeText.text = grade == "excellent" ? "Excellent" : grade == "competent" ? "Competent" : grade == "needs_practice" ? "Needs practice" : HoloUi.Humanize(grade);
            gradeText.color = grade == "excellent" ? HoloUi.Green : grade == "competent" ? HoloUi.Cyan : HoloUi.Amber;
            ring.color = gradeText.color;

            var mv = Json.Obj(r, "movement");
            movementText.text = mv == null ? "No movement data." :
                "<b>MOVEMENT</b>\nHand path  " + F(Json.Num(mv, "hand_path_m"), "0.00") + " m\nHead path  " + F(Json.Num(mv, "head_path_m"), "0.00") +
                " m\nSmoothness  " + F(Json.Num(mv, "smoothness") * 100, "0") + "%\nIdle  " + F(Json.Num(mv, "idle_s"), "0") + " s\nSamples  " + F(Json.Num(mv, "samples"), "0");

            summaryText.text = Json.Str(r, "summary", "");
            sb.Length = 0;
            AppendList("<color=#7DDC9C><b>STRENGTHS</b></color>", Json.Arr(r, "strengths"));
            AppendList("<color=#F2B84B><b>FOCUS NEXT</b></color>", Json.Arr(r, "focus_next"));
            listsText.text = sb.ToString();

            var exs = Json.Arr(r, "exercises") ?? new List<object>();
            float h = Mathf.Min(156f, (560f - 10f * Mathf.Max(0, exs.Count - 1)) / Mathf.Max(1, exs.Count));
            for (int i = 0; i < exs.Count; i++) BuildCard(exs[i], i, h);

            var cost = Json.Obj(r, "mistake_cost");
            disclaimerText.text = Json.Str(r, "disclaimer", "Illustrative, not billing data.");
            costNote.text = "Detailed itemized report: paid via Coinbase x402 (0.01 USDC testnet) on the instructor dashboard.";
            costTotalText.text = "$0.00";
            hearButton.interactable = true;
            pendingCost = cost;
        }

        void AppendList(string title, List<object> items)
        {
            if (items == null || items.Count == 0) return;
            if (sb.Length > 0) sb.Append("\n\n");
            sb.Append(title);
            foreach (object o in items) sb.Append("\n•  ").Append(o as string ?? Convert.ToString(o, CultureInfo.InvariantCulture));
        }

        void BuildCard(object e, int i, float h)
        {
            string id = Json.Str(e, "exercise_id", "exercise");
            Exercise local = HoloUi.FindExercise(id);
            string title = Json.Str(e, "title") ?? (local != null ? local.ReviewName : HoloUi.Humanize(id));
            bool? correct = Json.Bool(e, "correct");
            bool unclear = Json.Bool(e, "unclear") ?? false;
            Color c; string chip;
            if (unclear) { c = HoloUi.Amber; chip = "UNCLEAR · INSTRUCTOR ASKED"; }
            else if (!correct.HasValue) { c = HoloUi.TextDim; chip = "NOT ASSESSED"; }
            else if (correct.Value) { c = HoloUi.Green; chip = "CORRECT"; }
            else { c = HoloUi.Red; chip = "INCORRECT"; }

            RectTransform card = MakeCard(cardsRoot, i, h, c);
            HoloUi.Label(card, "Title", title, 24, HoloUi.TextMain, TextAnchor.UpperLeft, 18, 10, 380, 30, true);
            HoloUi.Label(card, "Chip", chip, 18, c, TextAnchor.UpperRight, 380, 14, 160, 26, true);
            string ttd = Json.Has(e, "time_to_decision_s") && Json.Num(e, "time_to_decision_s", -1) >= 0 ? F(Json.Num(e, "time_to_decision_s"), "0") + " s" : "–";
            HoloUi.Label(card, "Answer", "You: " + HoloUi.DecisionText(Json.Str(e, "decision")) + "\nExpected: " + HoloUi.DecisionText(Json.Str(e, "expected")) + "   ·   " + ttd,
                         18, HoloUi.TextBody, TextAnchor.UpperLeft, 18, 42, 524, 48);
            string review = Json.Str(e, "review", ""), tip = Json.Str(e, "tip", "");
            HoloUi.Label(card, "Review", review + (tip.Length > 0 ? "\n<i><color=#8FB3C8>Tip: " + tip + "</color></i>" : ""),
                         17, HoloUi.TextBody, TextAnchor.UpperLeft, 18, 90, 524, h - 96);
        }

        void BuildLocalCards()
        {
            int n = ExerciseLibrary.All.Length;
            float h = Mathf.Min(156f, (560f - 10f * Mathf.Max(0, n - 1)) / Mathf.Max(1, n));
            for (int i = 0; i < n; i++)
            {
                Exercise ex = ExerciseLibrary.All[i];
                LocalDecision d;
                bool has = hook.Decisions.TryGetValue(ex.Id, out d);
                Color c = !has ? HoloUi.TextDim : d.Result == ResultStatus.Match ? HoloUi.Green : d.Result == ResultStatus.Mismatch ? HoloUi.Red : HoloUi.Amber;
                string chip = !has ? "NO ANSWER" : d.Result == ResultStatus.Match ? "MATCH (LOCAL)" : d.Result == ResultStatus.Mismatch ? "MISMATCH (LOCAL)" : "UNCLEAR (LOCAL)";
                RectTransform card = MakeCard(cardsRoot, i, h, c);
                HoloUi.Label(card, "Title", ex.ReviewName, 24, HoloUi.TextMain, TextAnchor.UpperLeft, 18, 10, 380, 30, true);
                HoloUi.Label(card, "Chip", chip, 18, c, TextAnchor.UpperRight, 360, 14, 180, 26, true);
                string expected = ex.Kind == ExerciseKind.Identify ? "identify:" + ex.TargetStructureId : ex.ExpectedDecisionId;
                HoloUi.Label(card, "Answer", "You: " + (has ? HoloUi.DecisionText(d.Detail) : "no answer") + "\nExpected: " + HoloUi.DecisionText(expected) +
                             (has ? "   ·   " + d.TimeToDecisionS.ToString("0", CultureInfo.InvariantCulture) + " s" : ""),
                             18, HoloUi.TextBody, TextAnchor.UpperLeft, 18, 42, 524, 48);
                string fb = !has ? "" : d.Result == ResultStatus.Match ? ex.FeedbackMatch : d.Result == ResultStatus.Mismatch ? ex.FeedbackMismatch : "";
                HoloUi.Label(card, "Review", fb, 17, HoloUi.TextBody, TextAnchor.UpperLeft, 18, 90, 524, h - 96);
            }
        }

        RectTransform MakeCard(RectTransform parent, int i, float h, Color accent)
        {
            RectTransform card = HoloUi.Rect(parent, "Card " + i);
            HoloUi.TopLeft(card, 0, i * (h + 10f), 560, h);
            var g = card.gameObject.AddComponent<CanvasGroup>();
            g.alpha = 0f;
            Image bg = HoloUi.Box(card, "Bg", HoloUi.GlassDeep); HoloUi.Stretch(bg.rectTransform, 0f);
            HoloUi.Box(card, "Accent", accent, 0, 0, 5, h);
            animated.Add(card); animatedGroups.Add(g);
            return card;
        }

        string MovementLocal()
        {
            var rec = hook.GetComponent<MovementRecorder>();
            return "<b>MOVEMENT</b>\nRecorded on this device: " + (rec != null ? rec.SampleCount : 0) + " samples.\nPath metrics need the backend.";
        }

        static string F(double v, string fmt) { return v.ToString(fmt, CultureInfo.InvariantCulture); }

        // ------------------------------------------------------------------ show / animate

        Dictionary<string, object> pendingCost;

        void ShowReport()
        {
            phase = Phase.Report;
            reportRoot.gameObject.SetActive(true);
            reportShownAt = Time.unscaledTime;
            ringStart = reportShownAt + 0.35f;
            if (crew == null && guide != null) crew = guide.Crew;
            if (crew != null) crew.SetCounterVisible(true);
            if (pendingCost != null) StartCoroutine(PlayCost(pendingCost));
        }

        void CloseReport()
        {
            phase = Phase.Closed;
            HoloUi.Fade(report.Group, 0f);
            reportRoot.gameObject.SetActive(false);
            if (crew != null) crew.SetCounterVisible(false);
        }

        void AnimateReport()
        {
            if (phase != Phase.Report) return;
            float t = Time.unscaledTime - reportShownAt;
            float a = HoloUi.OutCubic(t / 0.6f);
            report.Group.alpha = a;
            report.Group.interactable = report.Group.blocksRaycasts = a > 0.9f;
            reportRoot.localScale = Vector3.one * (0.94f + 0.06f * a);
            reportRoot.localPosition = reportPosition + new Vector3(0f, (1f - a) * -0.03f, 0f);

            // score ring fills and the number counts with it
            float rt = HoloUi.OutCubic((Time.unscaledTime - ringStart) / 1.4f);
            if (!localOnly)
            {
                ring.fillAmount = rt * targetScore / 100f;
                scoreText.text = Mathf.RoundToInt(rt * targetScore).ToString(CultureInfo.InvariantCulture);
            }
            else ring.fillAmount = 0f;

            // exercise cards slide up one after another
            for (int i = 0; i < animated.Count; i++)
            {
                float k = HoloUi.OutCubic((t - 0.5f - i * 0.15f) / 0.5f);
                animatedGroups[i].alpha = k;
                float h = animated[i].sizeDelta.y;
                animated[i].anchoredPosition = new Vector2((1f - k) * 24f, -(i * (h + 10f)));
            }
        }

        IEnumerator PlayCost(Dictionary<string, object> cost)
        {
            pendingCost = null;
            yield return new WaitForSecondsRealtime(1.6f);
            var lines = new List<CrewBridge.CostLine>();
            var arr = Json.Arr(cost, "lines");
            if (arr != null)
                foreach (object o in arr)
                    lines.Add(new CrewBridge.CostLine
                    {
                        Label = Json.Str(o, "label", "line"), Usd = (decimal)Json.Num(o, "usd"),
                        Assumed = Json.Bool(o, "assumed") ?? false, Source = Json.Str(o, "source", "")
                    });
            decimal total = (decimal)Json.Num(cost, "total_usd");
            string disclaimer = disclaimerText.text;

            if (crew != null && !crew.HasCounter && crew.Available) crew.AttachCounter(reportRoot, new Vector3(0.46f, -0.16f, -0.02f));
            if (crew != null && crew.PlayMoney(lines, total, disclaimer, null, false))
            {
                costTotalText.text = "";
                yield break;
            }
            // Fallback when Runtime/Crew is not merged: plain eased ticker with lines dropping in.
            double shown = 0, running = 0;
            ClearChildren(costRoot);
            for (int i = 0; i < lines.Count && i < 5; i++)
            {
                var l = lines[i];
                Text row = HoloUi.Label(costRoot, "Line " + i, l.Label + (l.Assumed ? "  <color=#F2B84B>(assumed)</color>" : "  <color=#7DDC9C>(sourced)</color>") + "   " + HoloUi.Money((double)l.Usd),
                                        17, HoloUi.TextBody, TextAnchor.UpperLeft, 0, i * 26, 320, 26);
                double from = running;
                running += (double)l.Usd;
                float t0 = Time.unscaledTime;
                while (Time.unscaledTime - t0 < 0.55f)
                {
                    float k = HoloUi.OutCubic((Time.unscaledTime - t0) / 0.55f);
                    row.color = new Color(1, 1, 1, k);
                    row.rectTransform.anchoredPosition = new Vector2(0, -(i * 26) + (1f - k) * 14f);
                    shown = from + (running - from) * k;
                    costTotalText.text = HoloUi.Money(shown);
                    yield return null;
                }
            }
            if (lines.Count > 5) HoloUi.Label(costRoot, "More", "+" + (lines.Count - 5) + " more on the dashboard", 16, HoloUi.TextDim, TextAnchor.UpperLeft, 0, 5 * 26, 320, 24);
            costTotalText.text = HoloUi.Money((double)total);
        }

        IEnumerator PlayProviderEvents()
        {
            if (crew == null && guide != null) crew = guide.Crew;
            if (crew == null) yield break;
            var providers = Json.Obj(reportJson, "providers");
            if (providers != null)
            {
                var map = new Dictionary<string, string>();
                foreach (var kv in providers) map[kv.Key] = kv.Value as string ?? "off";
                crew.SetFromReport(map);
            }
            // Re-enact who did what during the session, in order, compressed to a calm few seconds.
            var evs = Json.Arr(reportJson, "provider_events");
            if (evs == null) yield break;
            evs.Sort((a, b) => Json.Num(a, "t_ms").CompareTo(Json.Num(b, "t_ms")));
            int n = Mathf.Min(evs.Count, 14);
            for (int i = 0; i < n; i++)
            {
                crew.Pulse(Json.Str(evs[i], "provider"), Json.Str(evs[i], "action", ""));
                yield return new WaitForSecondsRealtime(0.45f);
            }
        }

        // ------------------------------------------------------------------ buttons

        void OnReplay()
        {
            if (replay == null) return;
            StartCoroutine(OpenReplay());
        }

        IEnumerator OpenReplay()
        {
            ReplayData data = null;
            if (!localOnly && hook.SessionId != null)
            {
                ApiResponse r = null;
                yield return hook.Api.Send("GET", JeremyApi.SessionPath(hook.SessionId) + "/replay", null, x => r = x);
                if (r.Ok) data = ReplayData.FromJson(r.Body);
            }
            if (data == null || data.Tracks.Count == 0) data = hook.Replay.Data; // device copy (offline or backend without /replay)
            phase = Phase.Replay;
            reportRoot.gameObject.SetActive(false);
            if (crew != null) crew.SetCounterVisible(false);
            replay.Show(data, workstationRoot, () => { reportRoot.gameObject.SetActive(true); phase = Phase.Report; reportShownAt = Time.unscaledTime - 0.6f; if (crew != null) crew.SetCounterVisible(true); });
        }

        void OnHear()
        {
            if (guide == null || localOnly || hook.SessionId == null || guide.IsSpeaking) return;
            guide.Say(Json.Str(reportJson, "summary", ""), "on_track", 0f);
            StartCoroutine(guide.SpeakFrom(JeremyApi.SessionPath(hook.SessionId) + "/report/audio", null));
        }

        // ------------------------------------------------------------------ construction

        void BuildBar()
        {
            bar = HoloUi.MakePanel(workstationRoot, "Jeremy SessionBar", 620, 110, 0.001f, true);
            bar.Root.localPosition = barPosition;
            bar.Root.localRotation = Quaternion.Euler(barEuler);
            barDot = HoloUi.Box(bar.Root, "Dot", HoloUi.Green, 22, 24, 16, 16);
            barDot.sprite = HoloUi.Disc();
            barStatus = HoloUi.Label(bar.Root, "Status", "", 19, HoloUi.TextBody, TextAnchor.UpperLeft, 48, 16, 330, 80);
            finishButton = HoloUi.Button(bar.Root, "Finish", "Finish & review", 22, 392, 20, 206, 70, OnFinishPressed, out finishLabel);
        }

        void BuildReport()
        {
            reportRoot = new GameObject("Jeremy SessionReport").transform;
            reportRoot.SetParent(workstationRoot, false);
            reportRoot.localPosition = reportPosition;
            report = HoloUi.MakePanel(reportRoot, "Report", W, H, Scale, true);
            report.Canvas.sortingOrder = 20;
            report.Background.color = new Color(0.02f, 0.06f, 0.09f, 0.97f);
            Transform t = report.Root;

            HoloUi.Box(t, "HeaderBar", new Color(HoloUi.Cyan.r, HoloUi.Cyan.g, HoloUi.Cyan.b, 0.10f), 3, 3, W - 6, 74);
            headerText = HoloUi.Label(t, "Header", "SESSION REVIEW", 28, HoloUi.TextMain, TextAnchor.UpperLeft, 40, 16, 1000, 36, true);
            sourceText = HoloUi.Label(t, "Source", "", 18, HoloUi.TextDim, TextAnchor.UpperLeft, 40, 50, 1100, 24);
            Text closeLabel;
            HoloUi.Button(t, "Close", "Close", 20, W - 150, 18, 110, 46, CloseReport, out closeLabel);

            // left: score ring + grade + movement
            ringBack = HoloUi.Box(t, "RingBack", new Color(1f, 1f, 1f, 0.08f), 70, 110, 280, 280);
            ringBack.sprite = HoloUi.Ring();
            ring = HoloUi.Box(t, "Ring", HoloUi.Cyan, 70, 110, 280, 280);
            ring.sprite = HoloUi.Ring();
            ring.type = Image.Type.Filled;
            ring.fillMethod = Image.FillMethod.Radial360;
            ring.fillOrigin = (int)Image.Origin360.Top;
            ring.fillClockwise = true;
            ring.fillAmount = 0f;
            scoreText = HoloUi.Label(t, "Score", "0", 84, HoloUi.TextMain, TextAnchor.MiddleCenter, 70, 175, 280, 110, true);
            HoloUi.Label(t, "ScoreCaption", "SCORE / 100", 16, HoloUi.TextDim, TextAnchor.MiddleCenter, 70, 285, 280, 24);
            gradeText = HoloUi.Label(t, "Grade", "", 32, HoloUi.Cyan, TextAnchor.MiddleCenter, 40, 410, 340, 44, true);
            movementText = HoloUi.Label(t, "Movement", "", 19, HoloUi.TextBody, TextAnchor.UpperLeft, 60, 480, 320, 220);

            // middle: exercise cards
            HoloUi.Label(t, "CardsHeader", "<b>EXERCISES</b>", 18, HoloUi.TextDim, TextAnchor.UpperLeft, 420, 94, 560, 24);
            cardsRoot = HoloUi.Rect(t, "Cards");
            HoloUi.TopLeft(cardsRoot, 420, 124, 560, 580);

            // right: summary, lists, cost
            summaryText = HoloUi.Label(t, "Summary", "", 19, HoloUi.TextMain, TextAnchor.UpperLeft, 1010, 94, 350, 170);
            summaryText.resizeTextForBestFit = true; summaryText.resizeTextMinSize = 14; summaryText.resizeTextMaxSize = 19;
            listsText = HoloUi.Label(t, "Lists", "", 17, HoloUi.TextBody, TextAnchor.UpperLeft, 1010, 270, 350, 230);
            listsText.resizeTextForBestFit = true; listsText.resizeTextMinSize = 13; listsText.resizeTextMaxSize = 17;
            listsRoot = HoloUi.Rect(t, "ListsExtra");
            HoloUi.Box(t, "CostBox", HoloUi.GlassDeep, 1000, 510, 370, 270);
            HoloUi.Label(t, "CostHeader", "<b>COST OF MISTAKES</b>  (illustrative)", 17, HoloUi.TextDim, TextAnchor.UpperLeft, 1016, 520, 340, 24);
            costTotalText = HoloUi.Label(t, "CostTotal", "$0.00", 46, HoloUi.Amber, TextAnchor.UpperLeft, 1016, 546, 340, 56, true);
            costRoot = HoloUi.Rect(t, "CostLines");
            HoloUi.TopLeft(costRoot, 1016, 606, 340, 150);
            costNote = HoloUi.Label(t, "CostNote", "", 15, HoloUi.TextDim, TextAnchor.UpperLeft, 1010, 786, 360, 40);
            disclaimerText = HoloUi.Label(t, "Disclaimer", "Illustrative, not billing data.", 15, HoloUi.Amber, TextAnchor.UpperLeft, 1010, 826, 360, 40);

            // bottom buttons
            Text l;
            replayButton = HoloUi.Button(t, "Replay", "Replay movements", 22, 420, 806, 270, 64, OnReplay, out l);
            hearButton = HoloUi.Button(t, "Hear", "Hear summary", 22, 710, 806, 270, 64, OnHear, out l);
            HoloUi.Label(t, "Footer", ExerciseLibrary.Disclaimer, 15, HoloUi.TextDim, TextAnchor.UpperLeft, 60, 820, 340, 50);
        }

        static void ClearChildren(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) HoloUi.Kill(t.GetChild(i).gameObject);
        }
    }
}
