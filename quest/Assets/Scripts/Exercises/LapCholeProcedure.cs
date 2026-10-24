using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Jeremy
{
    // The guided port placement (docs/PORT_PLACEMENT.md): the four ports of a laparoscopic cholecystectomy, and nothing after.
    // Works in the desktop preview (mouse) and on the headset (right controller or pinch). Every step shows where you are,
    // what to do, how, and what's next. The abdomen starts insufflated, so there is no Veress or CO2 step.
    // Uploads only `port_placed` and two `landmark` events, the kinds the backend accepts for port_placement.
    public sealed class LapCholeProcedure : MonoBehaviour
    {
        struct StepInfo { public string id, title, what, mouse, vr; }
        static readonly StepInfo[] Steps =
        {
            new StepInfo { id = "port_umbilical", title = "Umbilical port", what = "Place the umbilical balloon trocar, inflate its balloon, seal, insert the scope.",
                mouse = "Seat the trocar at the navel, then hold SPACE to inflate its retention balloon.", vr = "Seat the trocar at the navel while keeping its cannula aligned. Squeeze once to place it, then hold to inflate the balloon." },
            new StepInfo { id = "port_epigastric", title = "Epigastric port", what = "Place the 10 mm trocar just below the breastbone, aimed at the right upper quadrant.",
                mouse = "Place the trocar below the breastbone, aimed toward the right upper quadrant.", vr = "Touch skin below the breastbone with the trocar tip, aim toward the right upper quadrant, then squeeze only when the shaft guide is centred." },
            new StepInfo { id = "port_rsc_medial", title = "Right subcostal port (medial)", what = "Place a 5 mm trocar below the right ribs, in line with the middle of the collarbone.",
                mouse = "Place the trocar below the right ribs on the mid-clavicular line.", vr = "Use the trocar: contact skin below the right ribs, centre the cannula on the guide, then squeeze without sweeping through the body." },
            new StepInfo { id = "port_rsc_lateral", title = "Right subcostal port (lateral)", what = "Place a 5 mm trocar further out on the right flank.",
                mouse = "Place the trocar on the right flank while preserving instrument spacing.", vr = "Use the trocar at the lateral guide. Keep clear of the medial port, contact skin, align the shaft, then squeeze." },
        };

        public PegTransferRig rig;
        public int StepIndex { get; private set; }
        public string Feedback { get; private set; } = "";
        public bool Active => rig != null && dressing != null;

        LapCholeDressing dressing;
        LapCholeAnatomy anatomy;
        LaparoscopeMonitor monitor;
        DesktopPreview preview;
        LapCholeKit kit;
        Transform props;
        Transform targetRing, handStopRing;
        float balloon, feedbackUntil;
        bool placedUmbilical, prevAct, wasRunning, vrPanelBuilt;
        bool handCollisionLatched, controllerHaptic;
        float hapticUntil;
        string landmarkAttempt;
        TextMesh vrPanel, diagramPanel;
        GUIStyle panel, title, small;

        // ---------------- lifecycle ----------------
        void Update()
        {
            if (controllerHaptic && Time.unscaledTime >= hapticUntil)
            {
                OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
                controllerHaptic = false;
            }
            if (rig == null) rig = GetComponent<PegTransferRig>();
            if (rig == null) return;
            if (dressing == null) { dressing = FindFirstObjectByType<LapCholeDressing>(); if (dressing == null) return; anatomy = dressing.Anatomy; }
            if (monitor == null) monitor = FindFirstObjectByType<LaparoscopeMonitor>();
            if (preview == null) preview = GetComponent<DesktopPreview>();
            if (kit == null) BuildProps();
            bool running = rig.Exercise.State == ExerciseState.Running;
            if (running && !wasRunning && rig.Exercise.ElapsedSeconds < 0.5f) ResetProcedure();
            wasRunning = running;
            UpdateTarget();
            UpdateVrPanel();
            Speak(running);
            if (!running || autopilot)
            {
                SetHandCollision(null, false, 0f, Vector3.zero);
                return;
            }
            Read(out Vector3 point, out bool hasPoint, out bool act, out bool hold);
            bool pressed = act && !prevAct;
            prevAct = act;
            Drive(point, hasPoint, pressed, hold);
        }

        // Spoken guidance: one pre-recorded clip per step (Assets/Art/Resources/Voice), played when the step begins.
        AudioSource voice;
        int spokenStep = -1;
        bool spokeComplete;
        void Speak(bool running)
        {
            if (voice == null)
            {
                voice = gameObject.AddComponent<AudioSource>();
                voice.playOnAwake = false; voice.spatialBlend = 0f; voice.volume = 1f;
            }
            if (rig.Exercise.State == ExerciseState.Completed)
            {
                if (!spokeComplete) { spokeComplete = true; Play("complete"); }
                return;
            }
            if (!running) return;
            if (spokenStep == StepIndex) return;
            spokenStep = StepIndex; spokeComplete = false;
            Play(Steps[StepIndex].id);
        }
        void Play(string id)
        {
            AudioClip clip = Resources.Load<AudioClip>("Voice/lapchole_" + id);
            if (clip == null) return;
            voice.Stop(); voice.clip = clip; voice.Play();
        }

        void ResetProcedure()
        {
            spokenStep = -1; spokeComplete = false;
            StepIndex = 0; balloon = 0;
            placedUmbilical = false;
            if (props != null) Destroy(props.gameObject);
            if (kit != null) kit.Destroy();
            kit = null; BuildProps();
            dressing.ShowStage(0);       // no ports placed yet
            dressing.SetInsufflation(1f); // the abdomen starts insufflated
            SetScopeMain(false);
            Feedback = "";
            SetHandCollision(null, false, 0f, Vector3.zero);
            RecordLandmarks();
        }

        // The two reference points the backend needs to judge the layout (docs/PORT_PLACEMENT.md): Calot's triangle
        // (the "target" the working ports triangulate on) and a point on the midline (only its z is used). Sent once per attempt.
        void RecordLandmarks()
        {
            if (rig.Exercise.State != ExerciseState.Running || landmarkAttempt == rig.Exercise.AttemptId) return;
            landmarkAttempt = rig.Exercise.AttemptId;
            rig.Recorder.RecordLap("landmark", "target", null, ToBoard(LapCholeAnatomy.CalotCenter), null, null);
            rig.Recorder.RecordLap("landmark", "midline", null, SkinPoint(Vector2.zero), null, null);
        }

        void BuildProps()
        {
            kit = new LapCholeKit();
            props = new GameObject("Port placement props").transform;
            props.SetParent(rig.board.transform, false);
            var ring = new GameObject("Entry alignment guide", typeof(MeshFilter), typeof(MeshRenderer));
            ring.GetComponent<MeshFilter>().sharedMesh = kit.Ring;
            ring.GetComponent<MeshRenderer>().sharedMaterial = kit.glowSkin;
            targetRing = ring.transform; targetRing.SetParent(props, false);
            var stop = new GameObject("Hand collision boundary", typeof(MeshFilter), typeof(MeshRenderer));
            stop.GetComponent<MeshFilter>().sharedMesh = kit.Ring;
            stop.GetComponent<MeshRenderer>().sharedMaterial = kit.warning;
            handStopRing = stop.transform; handStopRing.SetParent(props, false);
            handStopRing.gameObject.SetActive(false);
        }

        // ---------------- geometry helpers ----------------
        Transform Board => rig.board.transform;
        Vector3 ToBoard(Vector3 anatomyLocal) => Board.InverseTransformPoint(anatomy.transform.TransformPoint(anatomyLocal));

        Vector3 SkinPoint(Vector2 site)
        {
            float y = dressing.Skin(site.x, site.y);
            return new Vector3(site.x, float.IsNaN(y) ? 0f : y, site.y);
        }

        Vector2 PortSite(string id) { foreach (LapCholeDressing.Port p in dressing.Ports) if (p.id == id) return p.site; return Vector2.zero; }

        // ---------------- input ----------------
        void Read(out Vector3 point, out bool hasPoint, out bool act, out bool hold)
        {
            point = Vector3.zero; hasPoint = false; act = false; hold = false;
            if (rig.desktopPreview)
            {
                Mouse mouse = Mouse.current; Keyboard keys = Keyboard.current;
                if (mouse == null) return;
                act = mouse.leftButton.isPressed;
                hold = act || (keys != null && keys.spaceKey.isPressed);
                hasPoint = PickSkin(mouse.position.ReadValue(), out point);
                return;
            }
            InstrumentController right = rig.Instruments != null && rig.Instruments.Length > 1 ? rig.Instruments[1] : null;
            if (right == null || right.tip == null || !right.TrackingValid)
            {
                SetHandCollision(right, false, 0f, Vector3.zero);
                return;
            }
            act = right.Closed; hold = act;
            Vector3 tip = Board.InverseTransformPoint(right.tip.position);
            Vector3 rawGrip = Board.InverseTransformPoint(right.GripPosition);
            Vector3 grip = rawGrip;
            float gripSkin = dressing.Skin(grip.x, grip.z);
            const float handClearance = 0.03f;
            float penetration = float.IsNaN(gripSkin) ? 0f : gripSkin + handClearance - grip.y;
            bool handBlocked = penetration > 0f;
            Vector3 contact = new Vector3(grip.x, float.IsNaN(gripSkin) ? grip.y : gripSkin, grip.z);
            if (handBlocked)
            {
                grip.y += penetration;
                // Stop the virtual grip at the patient. The working end follows the stopped grip, so pushing
                // the tracked hand through the abdomen cannot place a port.
                tip += grip - rawGrip;
            }
            SetHandCollision(right, handBlocked, penetration, contact);
            float skin = dressing.Skin(tip.x, tip.z);
            float depth = float.IsNaN(skin) ? float.PositiveInfinity : skin - tip.y;
            hasPoint = !handBlocked && !float.IsNaN(skin) && depth >= -0.01f && depth <= 0.03f;
            point = new Vector3(tip.x, float.IsNaN(skin) ? tip.y : skin, tip.z);
        }

        // Shows the red boundary and buzzes the controller when the hand is pushed into the abdominal wall.
        // Not uploaded: the backend's port_placement exercise has no hand-collision event.
        void SetHandCollision(InstrumentController instrument, bool blocked, float penetration, Vector3 contact)
        {
            if (handStopRing != null)
            {
                handStopRing.gameObject.SetActive(blocked);
                if (blocked)
                {
                    handStopRing.localPosition = contact + Vector3.up * 0.002f;
                    handStopRing.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    handStopRing.localScale = Vector3.one * (0.026f + Mathf.Min(0.018f, penetration));
                }
            }
            if (blocked && !handCollisionLatched && instrument != null && !instrument.UsingHandTracking)
            {
                OVRInput.SetControllerVibration(0.8f, 0.35f, OVRInput.Controller.RTouch);
                controllerHaptic = true;
                hapticUntil = Time.unscaledTime + 0.11f;
            }
            handCollisionLatched = blocked;
        }

        bool PickSkin(Vector2 screen, out Vector3 point)
        {
            point = Vector3.zero;
            Camera cam = preview != null ? preview.View : Camera.main;
            if (cam == null) return false;
            Ray ray = cam.ScreenPointToRay(screen);
            Vector3 o = Board.InverseTransformPoint(ray.origin), d = Board.InverseTransformDirection(ray.direction).normalized;
            for (float t = 0.05f; t < 4f; t += 0.004f)
            {
                Vector3 p = o + d * t;
                float y = dressing.Skin(p.x, p.z);
                if (!float.IsNaN(y) && p.y <= y) { point = new Vector3(p.x, y, p.z); return true; }
            }
            return false;
        }

        // ---------------- the procedure ----------------
        void Drive(Vector3 point, bool hasPoint, bool pressed, bool hold)
        {
            switch (Steps[StepIndex].id)
            {
                case "port_umbilical":
                    if (!placedUmbilical) { if (pressed && hasPoint && SkinPlacementReady("umbilical", Vector2.zero, point)) { placedUmbilical = true; ShowTrocar("umbilical"); Say("Trocar seated. Keep it stable and inflate the retention balloon."); } }
                    else
                    {
                        if (hold) balloon = Mathf.Min(1f, balloon + 0.8f * Time.deltaTime);
                        if (balloon >= 1f) PlacePort("umbilical", Vector2.zero, SkinPoint(Vector2.zero));
                    }
                    break;
                case "port_epigastric": if (pressed && hasPoint) PlacePort("epigastric", PortSite("epigastric"), point); break;
                case "port_rsc_medial": if (pressed && hasPoint) PlacePort("rsc_medial", PortSite("rsc_medial"), point); break;
                case "port_rsc_lateral": if (pressed && hasPoint) PlacePort("rsc_lateral", PortSite("rsc_lateral"), point); break;
            }
        }

        float CurrentToolAngle(string id)
        {
            if (rig.desktopPreview || rig.Instruments == null || rig.Instruments.Length < 2 || rig.Instruments[1].tip == null) return 0f;
            Vector3 axis = dressing.PortAxis(id);
            Vector3 tool = -Board.InverseTransformDirection(rig.Instruments[1].tip.forward);
            return Vector3.Angle(axis, tool);
        }

        bool SkinPlacementReady(string id, Vector2 site, Vector3 hit)
        {
            float mm = Vector2.Distance(new Vector2(hit.x, hit.z), site) * 1000f;
            float angle = CurrentToolAngle(id);
            if (mm <= 14f && angle <= 38f) return true;
            Say(mm > 14f ? "Move the actual tool tip onto the entry mark (within 14 mm)." : "Straighten the shaft before entering (within 38° of the guide)." );
            return false;
        }

        void PlacePort(string id, Vector2 site, Vector3 hit)
        {
            Vector3 ideal = SkinPoint(site);
            float angle = CurrentToolAngle(id);
            if (!SkinPlacementReady(id, site, hit)) return;
            rig.Recorder.RecordLap("port_placed", id, "right", hit, ideal, angle);
            ShowTrocar(id);
            float mm = Vector2.Distance(new Vector2(hit.x, hit.z), site) * 1000f;
            string verdict = mm <= 7f ? "precise" : "acceptable";
            if (id == "umbilical") SetScopeMain(false);
            Complete("Port placed " + mm.ToString("F0") + " mm from the ideal site" + (angle > 0f ? ", " + angle.ToString("F0") + "° off" : "") + " · " + verdict);
        }

        void ShowTrocar(string id)
        {
            GameObject t = dressing.TrocarObject(id);
            if (t != null) t.SetActive(true);
        }

        void Complete(string feedback)
        {
            Say(feedback);
            StepIndex++;
            if (StepIndex >= Steps.Length) { StepIndex = Steps.Length - 1; rig.Exercise.Complete(); }
        }

        void Say(string text) { Feedback = text; feedbackUntil = Time.unscaledTime + 6f; }

        void SetScopeMain(bool scope) { if (monitor != null) monitor.SetMainView(scope); }

        // ---------------- target ----------------
        Vector3 TargetPoint()
        {
            switch (Steps[StepIndex].id)
            {
                case "port_epigastric": return SkinPoint(PortSite("epigastric"));
                case "port_rsc_medial": return SkinPoint(PortSite("rsc_medial"));
                case "port_rsc_lateral": return SkinPoint(PortSite("rsc_lateral"));
                default: return SkinPoint(Vector2.zero);
            }
        }

        void UpdateTarget()
        {
            if (targetRing == null) return;
            bool running = rig.Exercise.State == ExerciseState.Running || rig.Exercise.State == ExerciseState.Ready;
            float pulse = 1f + 0.25f * Mathf.Sin(Time.unscaledTime * 6f);
            targetRing.gameObject.SetActive(running);
            targetRing.localPosition = TargetPoint() + Vector3.up * 0.002f;
            targetRing.localRotation = Quaternion.Euler(90f, 0f, 0f);
            targetRing.localScale = Vector3.one * 0.012f * pulse;
        }

        // ---------------- guidance UI ----------------
        void OnGUI()
        {
            if (!Active || !rig.desktopPreview) return;
            if (panel == null)
            {
                panel = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 13, wordWrap = true, padding = new RectOffset(12, 12, 10, 10) };
                title = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold, wordWrap = true };
                small = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true, richText = true };
            }
            float w = 360f, x = 12f, y = 44f;
            GUI.Box(new Rect(x, y, w, 330f), "", panel);
            StepInfo s = Steps[StepIndex];
            bool done = rig.Exercise.State == ExerciseState.Completed;
            GUI.Label(new Rect(x + 12, y + 8, w - 24, 26), done ? "All ports placed" : "Port " + (StepIndex + 1) + " of " + Steps.Length + " · " + s.title, title);
            float yy = y + 38;
            if (!done)
            {
                GUI.Label(new Rect(x + 12, yy, w - 24, 40), "<b>What:</b> " + s.what, small); yy += 40;
                GUI.Label(new Rect(x + 12, yy, w - 24, 46), "<b>How:</b> " + (rig.Exercise.State == ExerciseState.Ready ? "Press ENTER to begin, then: " : "") + s.mouse, small); yy += 48;
                string next = StepIndex + 1 < Steps.Length ? Steps[StepIndex + 1].title : "Finish";
                GUI.Label(new Rect(x + 12, yy, w - 24, 20), "<b>Next:</b> " + next, small); yy += 22;
                if (s.id == "port_umbilical" && placedUmbilical) { GUI.Label(new Rect(x + 12, yy, w - 24, 20), "<b>Balloon:</b> " + (balloon * 100f).ToString("F0") + "%", small); yy += 22; }
            }
            if (Feedback.Length > 0 && Time.unscaledTime < feedbackUntil + 30f)
            { GUI.Label(new Rect(x + 12, yy, w - 24, 40), "<color=#7fe3b2><b>✓</b> " + Feedback + "</color>", small); yy += 42; }
            yy += 4;
            for (int i = 0; i < Steps.Length; i++)
            {
                string mark = i < StepIndex || done ? "<color=#7fe3b2>✓</color>" : i == StepIndex ? "<color=#ffd166>▶</color>" : "<color=#8a93a0>·</color>";
                GUI.Label(new Rect(x + 12, yy, w - 24, 18), mark + "  " + (i + 1) + ". " + Steps[i].title, small);
                yy += 17;
            }
        }

        void UpdateVrPanel()
        {
            if (rig.desktopPreview || monitor == null || monitor.WorldScreen == null) return;
            if (!vrPanelBuilt)
            {
                vrPanelBuilt = true;
                var go = new GameObject("Step guidance (VR)");
                go.transform.SetParent(monitor.WorldScreen.transform.parent, false);
                go.transform.localPosition = new Vector3(-0.95f, 0.25f, 0f);
                vrPanel = go.AddComponent<TextMesh>();
                vrPanel.fontSize = 48; vrPanel.characterSize = 0.006f; vrPanel.anchor = TextAnchor.UpperLeft;
                vrPanel.color = Color.white;
                go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

                var diagram = new GameObject("Operation diagram (VR)");
                diagram.transform.SetParent(monitor.WorldScreen.transform.parent, false);
                diagram.transform.localPosition = new Vector3(0.72f, 0.25f, 0f);
                diagram.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                diagramPanel = diagram.AddComponent<TextMesh>();
                diagramPanel.fontSize = 44; diagramPanel.characterSize = 0.0052f; diagramPanel.anchor = TextAnchor.UpperLeft;
                diagramPanel.color = new Color(0.72f, 0.94f, 1f);
            }
            if (vrPanel == null) return;
            StepInfo s = Steps[StepIndex];
            bool done = rig.Exercise.State == ExerciseState.Completed;
            string next = StepIndex + 1 < Steps.Length ? Steps[StepIndex + 1].title : "Finish";
            vrPanel.text = done ? "All ports placed" :
                "Port " + (StepIndex + 1) + " of " + Steps.Length + " · " + s.title + "\n\nWhat: " + Wrap(s.what) + "\n\nHow: " +
                (rig.Exercise.State == ExerciseState.Ready ? "Press Start on the HUD, then: " : "") + Wrap(s.vr) + "\n\nNext: " + next +
                (Feedback.Length > 0 ? "\n\n✓ " + Wrap(Feedback) : "");
            if (diagramPanel != null)
                diagramPanel.text = "CURRENT TOOL  TROCAR\n\nBODY MAP\n       xiphoid\n          +  epigastric\n     + medial     + lateral\n          O  navel\n\nContact skin • centre shaft • squeeze";
        }

        static string Wrap(string text, int width = 46)
        {
            var sb = new System.Text.StringBuilder(); int col = 0;
            foreach (string word in text.Split(' ')) { if (col + word.Length > width) { sb.Append('\n'); col = 0; } sb.Append(word).Append(' '); col += word.Length + 1; }
            return sb.ToString().TrimEnd();
        }

        // ---------------- autopilot (desktop demo and verification) ----------------
        bool autopilot;
        /// <summary>Places the four ports at their ideal sites. <paramref name="unsafeRun"/> is accepted for the
        /// terminal bridge's `autopilot unsafe` command and has no effect now that only port placement remains.</summary>
        public IEnumerator Autoplay(bool unsafeRun)
        {
            autopilot = true;
            if (rig.Exercise.State != ExerciseState.Running) { rig.Exercise.ResetExercise(); yield return null; rig.Exercise.Begin(); }
            yield return null;
            ResetProcedure();
            yield return new WaitForSeconds(0.5f);
            while (rig.Exercise.State == ExerciseState.Running && StepIndex < Steps.Length)
            {
                string id = Steps[StepIndex].id;
                int before = StepIndex;
                Vector3 p = TargetPoint();
                if (id == "port_umbilical")
                {
                    Drive(p, true, true, false); yield return null;
                    while (StepIndex == before) { Drive(p, true, false, true); yield return null; }
                }
                else Drive(p, true, true, false);
                yield return new WaitForSeconds(0.9f);
                if (StepIndex == before && rig.Exercise.State == ExerciseState.Running) { Say("Autopilot stuck at " + id); break; }
            }
            autopilot = false;
        }
    }
}
