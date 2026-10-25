using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Jeremy
{
    // The lap chole interface: one step card (step and progress, what/how, live metric pills, last result,
    // the coach's bubble with mic and mode state), an instrument card with a live port map (headset), and a
    // small metric card floating over the patient during entry (headset). Desktop shows the step card as a
    // screen overlay. Replaces the plain text panels.
    public sealed class LapCholePanel : MonoBehaviour
    {
        const float W = 440f, H = 660f;
        PegTransferRig rig;
        LapCholeProcedure proc;
        LapCholeNarrator narrator;
        LaparoscopeMonitor monitor;
        bool built;

        Text difficultyLabel, stepLabel, title, what, how, feedback, coach, coachHint, next, modeText, toolName, safety;
        Image modeBadge, coachCard, listenDot, micTrack, micBar, micMark;
        readonly List<Image> segments = new List<Image>();
        readonly Pill[] pills = new Pill[3];
        readonly Pill[] floatPills = new Pill[3];
        RectTransform floatCard;
        Canvas floatCanvas;
        RectTransform portMap;
        readonly Dictionary<string, Image> portDots = new Dictionary<string, Image>();
        float nextRefresh;

        sealed class Pill { public Image bg, bar; public Text label, value; public RectTransform root; }
        struct Metric { public string label, value; public Color color; public bool on; }

        public bool Built => built;

        void Update()
        {
            if (rig == null) rig = GetComponent<PegTransferRig>();
            if (proc == null) proc = GetComponent<LapCholeProcedure>();
            if (narrator == null) narrator = GetComponent<LapCholeNarrator>();
            if (rig == null || proc == null) return;
            bool briefing = LapCholeIntro.Blocking(rig);
            bool show = rig.LapChole && proc.Active && !briefing;
            if (hudCanvas == null) foreach (Canvas c in rig.GetComponentsInChildren<Canvas>(true)) if (c.name == "Practice HUD") hudCanvas = c;
            if (hudCanvas == null && rig.desktopPreview) { GameObject d = GameObject.Find("Desktop HUD"); if (d != null) hudCanvas = d.GetComponent<Canvas>(); }
            // The practice HUD (start/pause/upload/coach buttons) is a debug panel: hidden in the lap chole.
            if (hudCanvas != null && rig.LapChole && hudCanvas.gameObject.activeSelf) hudCanvas.gameObject.SetActive(false);
            if (!built)
            {
                if (!show) return;
                if (!rig.desktopPreview && (monitor == null || monitor.WorldScreen == null)) { monitor = FindFirstObjectByType<LaparoscopeMonitor>(); if (monitor == null || monitor.WorldScreen == null) return; }
                Build();
            }
            foreach (Canvas c in canvases) if (c != null && c.gameObject.activeSelf != show) c.gameObject.SetActive(show);
            if (show && !rig.desktopPreview) FaceViewer();
            if (!show || Time.unscaledTime < nextRefresh) { FloatCard(); return; }
            nextRefresh = Time.unscaledTime + 0.08f;
            Refresh();
            FloatCard();
        }

        readonly List<Canvas> canvases = new List<Canvas>();
        Canvas hudCanvas;

        // World-space UI reads correctly only from its front: turn each card (and the HUD) to face the trainee, yaw only.
        Transform stepCanvas, toolCanvas;
        Vector3 placedFrom; bool placed;

        // Cards float beside the trainee, not in the room geometry: step card front-left, instrument card front-right,
        // slightly above eye height, out to the sides of the working area. Placed once and never moved.
        void PlaceAroundHead()
        {
            Vector3 head = rig.head.position;
            Vector3 fwd = Vector3.ProjectOnPlane(rig.head.forward, Vector3.up);
            if (fwd.sqrMagnitude < 0.01f) return;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            if (stepCanvas != null) stepCanvas.position = head + fwd * 0.75f - right * 0.85f + Vector3.up * 0.08f;
            if (toolCanvas != null) toolCanvas.position = head + fwd * 0.75f + right * 0.9f + Vector3.up * 0.04f;
            placedFrom = head; placed = true;
        }

        void FaceViewer()
        {
            if (rig.head == null) return;
            // Placed once, then pinned in the room: cards that jump to wherever the trainee looks get in the way.
            if (!placed && rig.head.position.y > 0.8f) PlaceAroundHead();
            if (hudCanvas == null) foreach (Canvas c in rig.GetComponentsInChildren<Canvas>(true)) if (c.name == "Practice HUD") hudCanvas = c;
            foreach (Canvas c in canvases) Face(c != null ? c.transform : null);
            if (hudCanvas != null && hudCanvas.renderMode == RenderMode.WorldSpace) Face(hudCanvas.transform);
        }

        void Face(Transform t)
        {
            if (t == null || !t.gameObject.activeInHierarchy) return;
            Vector3 away = t.position - rig.head.position; away.y = 0f;
            if (away.sqrMagnitude > 1e-4f) t.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
        }

        // ---------------- build ----------------
        void Build()
        {
            built = true;
            RectTransform card;
            if (rig.desktopPreview)
            {
                var screen = new GameObject("Lap chole panel (screen)", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                Canvas canvas = screen.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 5;
                var scaler = screen.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1600, 900); scaler.matchWidthOrHeight = 1f;
                canvases.Add(canvas);
                card = UiKit.Rect(screen.transform, "Step card", new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, Vector2.zero);
                card.pivot = new Vector2(0, 1); card.anchoredPosition = new Vector2(14, -40); card.sizeDelta = new Vector2(W, H);
                card.localScale = Vector3.one * 0.82f;
            }
            else
            {
                Transform parent = monitor.WorldScreen.transform.parent;
                card = WorldCanvas(parent, "Step card (VR)", new Vector3(-0.95f, 0.36f, 0f), new Vector2(W, H));
                stepCanvas = card.parent;
                Canvas stepC = stepCanvas.GetComponent<Canvas>();
                stepCanvas.gameObject.AddComponent<GraphicRaycaster>();
                stepCanvas.gameObject.AddComponent<HandRayPointer>().Setup(stepC, new[] { rig.leftHand, rig.rightHand }, new[] { rig.leftController, rig.rightController });
                RectTransform tool = WorldCanvas(parent, "Instrument card (VR)", new Vector3(0.72f, 0.36f, 0f), new Vector2(360f, 430f));
                toolCanvas = tool.parent;
                Canvas toolC = toolCanvas.GetComponent<Canvas>();
                toolCanvas.gameObject.AddComponent<GraphicRaycaster>();
                toolCanvas.gameObject.AddComponent<HandRayPointer>().Setup(toolC, new[] { rig.leftHand, rig.rightHand }, new[] { rig.leftController, rig.rightController });
                BuildToolCard(tool);
                floatCard = WorldCanvas(parent, "Entry metrics (VR)", Vector3.zero, new Vector2(420f, 92f));
                floatCanvas = floatCard.GetComponentInParent<Canvas>();
                canvases.Remove(floatCanvas); // shown only while entering; FloatCard() owns it
                floatCard.parent.SetParent(rig.board.transform, false);
                floatCard.parent.localScale = Vector3.one * 0.0009f;
                for (int i = 0; i < 3; i++) floatPills[i] = MakePill(floatCard, 8 + i * 136, 8, 128, 76);
                UiKit.Panel(floatCard, UiKit.Glass, 16);
            }
            BuildStepCard(card);
            foreach (Canvas c in canvases) c.gameObject.SetActive(false);
        }

        RectTransform WorldCanvas(Transform parent, string name, Vector3 local, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            go.transform.localScale = Vector3.one * 0.00105f;
            Canvas canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            if (rig.head != null) canvas.worldCamera = rig.head.GetComponent<Camera>();
            var rt = (RectTransform)go.transform; rt.pivot = new Vector2(0, 1); rt.sizeDelta = size;
            canvases.Add(canvas);
            RectTransform card = UiKit.Rect(go.transform, "Card", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            return card;
        }

        void BuildStepCard(RectTransform card)
        {
            UiKit.Panel(card, UiKit.Glass, 22);
            if (!rig.desktopPreview) UiKit.GrabRail(card, stepCanvas, W, H);
            RectTransform shell = card;
            card = UiKit.Rect(shell, "Step content", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            card.offsetMax = new Vector2(0, -50);
            Image difficulty = UiKit.PanelAt(shell, "Difficulty", 16, 8, W - 32, 36, new Color(0.1f, 0.35f, 0.4f), 12);
            difficulty.raycastTarget = true;
            var toggle = difficulty.gameObject.AddComponent<Button>();
            toggle.targetGraphic = difficulty;
            toggle.onClick.AddListener(LapDifficulty.Toggle);
            difficultyLabel = UiKit.Label(difficulty.transform, "Label", 6, 0, W - 44, 36, 15, UiKit.Ink, FontStyle.Bold, TextAnchor.MiddleCenter);
            UiKit.StyleButton(toggle, new Color(0.1f, 0.35f, 0.4f), 12);
            UiKit.PanelAt(card, "Accent", 0, 18, 5, 64, UiKit.Accent, 2);
            stepLabel = UiKit.Label(card, "Step", 24, 20, 220, 22, 15, UiKit.Accent, FontStyle.Bold);
            modeBadge = UiKit.PanelAt(card, "Mode", W - 148, 16, 124, 28, UiKit.Idle, 14);
            listenDot = UiKit.PanelAt(modeBadge.transform, "Dot", 10, 9, 10, 10, UiKit.Bad, 5);
            modeText = UiKit.Label(modeBadge.transform, "Mode text", 0, 4, 124, 22, 13, UiKit.Ink, FontStyle.Bold, TextAnchor.MiddleCenter);
            title = UiKit.Label(card, "Title", 24, 46, W - 48, 36, 27, UiKit.Ink, FontStyle.Bold);
            float segW = (W - 48 - 14 * 4) / 15f;
            for (int i = 0; i < 15; i++) segments.Add(UiKit.PanelAt(card, "Segment " + i, 24 + i * (segW + 4), 90, segW, 7, UiKit.Idle, 3));
            UiKit.Label(card, "What label", 24, 114, 100, 18, 12, UiKit.Muted, FontStyle.Bold).text = "WHAT";
            what = UiKit.Label(card, "What", 24, 132, W - 48, 46, 16, UiKit.Ink);
            UiKit.Label(card, "How label", 24, 184, 100, 18, 12, UiKit.Muted, FontStyle.Bold).text = "HOW";
            how = UiKit.Label(card, "How", 24, 202, W - 48, 92, 16, UiKit.Ink);
            float pw = (W - 48 - 16) / 3f;
            for (int i = 0; i < 3; i++) pills[i] = MakePill(card, 24 + i * (pw + 8), 302, pw, 70);
            feedback = UiKit.Label(card, "Result", 24, 382, W - 48, 42, 15, new Color(0.6f, 0.92f, 0.75f));
            coachCard = UiKit.PanelAt(card, "Coach", 16, 430, W - 32, 136, UiKit.Card, 16);
            UiKit.Label(coachCard.transform, "Coach label", 16, 12, 120, 18, 12, UiKit.Accent, FontStyle.Bold).text = "COACH";
            coachHint = UiKit.Label(coachCard.transform, "Ask hint", 120, 12, W - 32 - 136, 18, 12, UiKit.Muted, FontStyle.Normal, TextAnchor.UpperRight);
            coach = UiKit.Label(coachCard.transform, "Coach text", 16, 34, W - 64, 82, 17, UiKit.Ink);
            micTrack = UiKit.PanelAt(coachCard.transform, "Mic track", 16, 120, W - 64, 6, UiKit.Idle, 3);
            micBar = UiKit.PanelAt(coachCard.transform, "Mic level", 16, 120, 0, 6, UiKit.Accent, 3);
            micMark = UiKit.PanelAt(coachCard.transform, "Mic threshold", 16, 117, 2, 12, UiKit.Muted, 1);
            next = UiKit.Label(card, "Next", 24, 578, W - 170, 22, 14, UiKit.Muted);
            // Restart applies any pending difficulty selection; pinch-click on the card (pinch-click with the hand ray on the headset).
            Image restart = UiKit.PanelAt(card, "Restart button", W - 132, 566, 108, 34, new Color(0.17f, 0.24f, 0.31f), 12);
            restart.raycastTarget = true;
            var button = restart.gameObject.AddComponent<Button>();
            button.targetGraphic = restart;
            button.onClick.AddListener(Restart);
            UiKit.Label(restart.transform, "Label", 0, 0, 108, 34, 15, UiKit.Ink, FontStyle.Bold, TextAnchor.MiddleCenter).text = "Restart";
            UiKit.StyleButton(button, new Color(0.17f, 0.24f, 0.31f), 12);
        }

        void Restart() => StartCoroutine(RestartRoutine());
        System.Collections.IEnumerator RestartRoutine()
        {
            rig.Exercise.ResetExercise();
            yield return null; yield return null;
            rig.Exercise.Begin();
        }

        void BuildToolCard(RectTransform card)
        {
            UiKit.Panel(card, UiKit.Glass, 22);
            if (!rig.desktopPreview) UiKit.GrabRail(card, toolCanvas, 360f, 430f);
            UiKit.Label(card, "Tool label", 22, 20, 200, 18, 12, UiKit.Accent, FontStyle.Bold).text = "INSTRUMENT";
            toolName = UiKit.Label(card, "Tool", 22, 40, 320, 32, 24, UiKit.Ink, FontStyle.Bold);
            UiKit.Label(card, "Ports label", 22, 84, 200, 18, 12, UiKit.Muted, FontStyle.Bold).text = "PORT MAP  ·  head at top";
            Image map = UiKit.PanelAt(card, "Abdomen", 22, 106, 316, 210, UiKit.Card, 60);
            portMap = map.rectTransform;
            foreach (string id in new[] { "veress", "umbilical", "epigastric", "rsc_medial", "rsc_lateral" })
                portDots[id] = UiKit.PanelAt(portMap, id, 0, 0, 18, 18, UiKit.Idle, 9);
            UiKit.Label(card, "Safety label", 22, 330, 200, 18, 12, UiKit.Muted, FontStyle.Bold).text = "TECHNIQUE";
            safety = UiKit.Label(card, "Safety", 22, 350, 316, 70, 16, UiKit.Ink);
        }

        Pill MakePill(RectTransform parent, float x, float y, float w, float h)
        {
            var p = new Pill();
            p.bg = UiKit.PanelAt(parent, "Pill", x, y, w, h, UiKit.Card, 12);
            p.root = p.bg.rectTransform;
            p.bar = UiKit.PanelAt(p.root, "Bar", 0, h - 5, w, 5, UiKit.Idle, 2);
            p.label = UiKit.Label(p.root, "Label", 12, 9, w - 20, 16, 11, UiKit.Muted, FontStyle.Bold);
            p.value = UiKit.Label(p.root, "Value", 12, 27, w - 20, 34, 22, UiKit.Ink, FontStyle.Bold);
            return p;
        }

        // ---------------- refresh ----------------
        void Refresh()
        {
            int i = proc.StepIndex, n = LapCholeProcedure.StepCount;
            bool done = rig.Exercise.State == ExerciseState.Completed;
            bool ready = rig.Exercise.State == ExerciseState.Ready;
            difficultyLabel.text = "Difficulty: " + LapDifficulty.CardLabel(ready);
            stepLabel.text = done ? "COMPLETE" : "STEP " + (i + 1) + " / " + n;
            title.text = done ? "Procedure complete" : LapCholeProcedure.StepTitleAt(i);
            for (int k = 0; k < segments.Count; k++)
            {
                Color c = done || k < i ? UiKit.Good : k == i ? UiKit.Warn : UiKit.Idle;
                if (k == i && !done) c = Color.Lerp(UiKit.Warn, Color.white, 0.25f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4f)));
                segments[k].color = c;
            }
            what.text = done ? "All fifteen steps done. " + LapDifficulty.Summary : proc.StepWhat;
            how.text = done ? "Review the debrief and the score on the HUD." : (ready ? (rig.desktopPreview ? "Press ENTER to begin, then: " : "Press Start on the HUD, then: ") : "") + proc.StepHow;
            List<Metric> metrics = Metrics();
            for (int k = 0; k < 3; k++) SetPill(pills[k], k < metrics.Count ? metrics[k] : default);
            string fb = proc.Feedback;
            bool issue = fb.Contains("error") || fb.StartsWith("Move the actual") || fb.StartsWith("Straighten") || fb.StartsWith("Too deep")
                || fb.StartsWith("Don't lever") || fb.Contains("withdrawn") || fb.Contains("off plan") || fb.Contains("not on the");
            feedback.text = fb.Length == 0 ? "" : (issue ? "!  " : "✓  ") + fb;
            feedback.color = issue ? UiKit.Warn : new Color(0.6f, 0.92f, 0.75f);
            string cap = narrator != null ? narrator.Caption : "";
            bool listening = narrator != null && narrator.Listening;
            coach.text = cap.Length > 0 ? cap : "<color=#8A9AA8>" + (narrator != null && narrator.OnDemand ? "Quiet mode. Ask whenever you want help." : "I'll guide you as you go.") + "</color>";
            coachHint.text = rig.desktopPreview ? "Hold T to ask  ·  M mode" : narrator != null && narrator.handsFree ? "Just ask out loud" : "Empty left hand: pinch to ask";
            modeText.text = listening ? "   HEARING YOU" : narrator != null && narrator.OnDemand ? "ON DEMAND" : "GUIDE";
            modeBadge.color = listening ? new Color(0.35f, 0.08f, 0.1f) : narrator != null && narrator.OnDemand ? new Color(0.24f, 0.2f, 0.36f) : new Color(0.08f, 0.3f, 0.3f);
            listenDot.gameObject.SetActive(listening);
            if (listening) listenDot.color = Color.Lerp(UiKit.Bad, Color.white, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 8f));
            coachCard.color = listening ? new Color(0.2f, 0.1f, 0.12f) : UiKit.Card;
            bool mic = narrator != null && narrator.MicOn;
            micTrack.gameObject.SetActive(mic); micBar.gameObject.SetActive(mic); micMark.gameObject.SetActive(mic);
            if (mic)
            {
                float full = W - 64;
                micBar.rectTransform.sizeDelta = new Vector2(Mathf.Max(4f, full * narrator.MicLevel), 6);
                micBar.color = narrator.MicMuted ? UiKit.Idle : narrator.MicLevel >= narrator.MicThreshold ? UiKit.Good : UiKit.Accent;
                micMark.rectTransform.anchoredPosition = new Vector2(16 + full * narrator.MicThreshold, -117);
                if (cap.Length == 0 && narrator.MicMuted) coachHint.text = "Talk any time, I'll stop and listen";
            }
            next.text = done ? "" : "NEXT  ·  " + (i + 1 < n ? LapCholeProcedure.StepTitleAt(i + 1) : "Finish");
            if (toolName != null)
            {
                toolName.text = ToolName(proc.StepId);
                string cue = proc.AlignmentCue;
                safety.text = cue.Length > 0 ? cue : i < 6 ? "Tip on the purple cross · match the guide line · push until it pops · hold still"
                    : "The instrument pivots at its trocar. Move the handle, not the tip.";
                safety.color = cue.StartsWith("HAND") ? UiKit.Bad : cue.StartsWith("Re-centre") || cue.StartsWith("Withdraw") ? UiKit.Warn : UiKit.Ink;
                RefreshPortMap();
            }
        }

        void SetPill(Pill p, Metric m)
        {
            p.root.gameObject.SetActive(m.on);
            if (!m.on) return;
            p.label.text = m.label; p.value.text = m.value; p.value.color = m.color; p.bar.color = m.color;
        }

        static string ToolName(string step)
        {
            switch (step)
            {
                case "veress": case "insufflate": return "Veress needle";
                case "port_umbilical": return "12 mm balloon trocar";
                case "port_epigastric": return "10 mm trocar";
                case "port_rsc_medial": case "port_rsc_lateral": return "5 mm trocar";
                case "retract": return "Grasper";
                case "dissect": return "Maryland dissector";
                case "cholangiogram": return "Cholangiogram catheter";
                default: return step.StartsWith("clip") ? "Clip applier" : "Scissors";
            }
        }

        List<Metric> Metrics()
        {
            var list = new List<Metric>();
            string id = proc.StepId;
            LapCholeEntry e = proc.Entry;
            if (proc.UsingHands && e != null && e.Active)
            {
                if (e.Inserted && e.Current != null)
                {
                    float d = e.Depth;
                    LapCholeEntry.Spec s = e.Current;
                    Color dc = d > s.dangerDepth ? UiKit.Bad : d >= s.minDepth && d <= s.maxDepth ? UiKit.Good : UiKit.Warn;
                    list.Add(new Metric { on = true, label = "DEPTH  " + s.minDepth.ToString("F0") + "–" + s.maxDepth.ToString("F0"), value = d.ToString("F0") + " mm", color = dc });
                    list.Add(new Metric { on = true, label = "LAYERS", value = e.PoppedPeritoneum ? "In" : e.PoppedFascia ? "Fascia" : "Skin", color = e.PoppedPeritoneum ? UiKit.Good : UiKit.Warn });
                }
                else if (!float.IsNaN(e.HoverMm))
                {
                    list.Add(new Metric { on = true, label = "OFFSET", value = e.HoverMm > 99f ? "far" : e.HoverMm.ToString("F0") + " mm", color = UiKit.Severity(e.HoverMm, e.SiteTolMm, e.SiteTolMm * 2.5f) });
                    list.Add(new Metric { on = true, label = "ANGLE", value = e.HoverAngle.ToString("F0") + "°", color = UiKit.Severity(e.HoverAngle, e.AngleTolDeg, e.AngleTolDeg * 1.8f) });
                    if (e.Blocked) list.Add(new Metric { on = true, label = "ENTRY", value = "Blocked", color = UiKit.Bad });
                    else list.Add(new Metric { on = true, label = "GUIDE", value = e.Lined ? "Lined up" : "Align", color = e.Lined ? UiKit.Good : UiKit.Warn });
                }
                return list;
            }
            switch (id)
            {
                case "insufflate":
                    list.Add(new Metric { on = true, label = "PRESSURE  12–15", value = PressureGauge(proc.Pressure) + "  " + proc.Pressure.ToString("F1") + " mmHg", color = proc.Pressure > 15f ? UiKit.Bad : proc.Pressure >= 12f ? UiKit.Good : UiKit.Warn });
                    break;
                case "port_umbilical":
                    if (proc.UmbilicalSeated) list.Add(new Metric { on = true, label = "BALLOON", value = Mathf.RoundToInt(proc.Balloon * 100f) + "%", color = proc.Balloon >= 1f ? UiKit.Good : UiKit.Warn });
                    break;
                case "dissect":
                    list.Add(new Metric { on = true, label = "PERITONEUM LEFT", value = Mathf.RoundToInt(proc.Veil * 100f) + "%", color = proc.Veil < 0.1f ? UiKit.Good : UiKit.Warn });
                    break;
                case "cholangiogram":
                    if (proc.CatheterIn) list.Add(new Metric { on = true, label = "CONTRAST", value = Mathf.RoundToInt(proc.Contrast * 100f) + "%", color = UiKit.Accent });
                    break;
            }
            if (proc.StepIndex >= 6 && id != "dissect")
            {
                if (proc.ToolTipBoard.HasValue)
                {
                    float d = Vector3.Distance(proc.ToolTipBoard.Value, proc.TargetBoard) * 1000f;
                    list.Add(new Metric { on = true, label = "TO TARGET", value = d.ToString("F0") + " mm", color = UiKit.Severity(d, 8f, 25f) });
                }
                string contact = proc.ContactStructure;
                string expected = id == "retract" ? "fundus" : id.Contains("artery") ? "cystic_artery" : "cystic_duct";
                if (contact != null && contact != "veil")
                    list.Add(new Metric { on = true, label = "JAWS ON", value = contact == "cbd" ? "CBD" : contact == "fundus" ? "Fundus" : contact == "cystic_duct" ? "Cystic duct" : "Artery",
                        color = contact == expected ? UiKit.Good : contact == "cbd" ? UiKit.Bad : UiKit.Warn });
            }
            return list;
        }

        void RefreshPortMap()
        {
            // Board-local x runs head (-) to feet (+), z is the patient's right (+). Head at the top, patient's right on the left.
            Vector2 size = portMap.sizeDelta;
            int current = proc.StepIndex;
            foreach (var kv in portDots)
            {
                int stepOf = kv.Key == "veress" ? 0 : kv.Key == "umbilical" ? 2 : kv.Key == "epigastric" ? 3 : kv.Key == "rsc_medial" ? 4 : 5;
                Vector3 p = proc.PortPositionForMap(kv.Key);
                float u = 1f - Mathf.InverseLerp(-0.18f, 0.18f, p.z), v = Mathf.InverseLerp(-0.22f, 0.08f, p.x);
                var rt = kv.Value.rectTransform;
                rt.anchoredPosition = new Vector2(u * (size.x - 18f), -v * (size.y - 18f));
                bool placed = current > stepOf || rig.Exercise.State == ExerciseState.Completed;
                bool now = current == stepOf || (kv.Key == "veress" && current == 1);
                kv.Value.color = placed ? UiKit.Good : now ? Color.Lerp(UiKit.Warn, Color.white, 0.4f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f))) : UiKit.Idle;
                rt.sizeDelta = Vector2.one * (kv.Key == "veress" ? 12f : now ? 22f : 18f);
                kv.Value.gameObject.SetActive(!(kv.Key == "veress" && current >= 2));
            }
        }

        // Metric card over the patient while the hand is entering (headset).
        void FloatCard()
        {
            if (floatCard == null) return;
            LapCholeEntry e = proc.Entry;
            bool on = rig.LapChole && proc.Active && proc.UsingHands && e != null && e.Active && (e.Inserted || !float.IsNaN(e.HoverMm));
            Transform root = floatCard.parent;
            if (root.gameObject.activeSelf != on) root.gameObject.SetActive(on);
            if (!on) return;
            List<Metric> m = Metrics();
            for (int k = 0; k < 3; k++) SetPill(floatPills[k], k < m.Count ? m[k] : default);
            root.localPosition = e.Site + Vector3.up * 0.32f;
            if (rig.head != null)
            {
                Vector3 look = root.position - rig.head.position;
                if (look.sqrMagnitude > 1e-4f) root.rotation = Quaternion.LookRotation(look, Vector3.up);
            }
        }
    
        // 0-20 mmHg, one slot per mmHg: # = filled, = marks the 12-15 target band, - = empty.
        static string PressureGauge(float mmHg)
        {
            var sb = new System.Text.StringBuilder("[");
            for (int i = 0; i < 20; i++) sb.Append(i < mmHg ? '#' : i >= 12 && i < 15 ? '=' : '-');
            return sb.Append(']').ToString();
        }
}
}
