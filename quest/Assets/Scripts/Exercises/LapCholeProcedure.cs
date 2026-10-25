using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Jeremy
{
    // The guided laparoscopic cholecystectomy (docs/LAP_CHOLE.md): 15 steps in the order of the Teleflex video.
    // Works in the desktop preview (mouse) and on the headset (right controller or pinch; the instrument pivots
    // at its trocar like a real laparoscopic tool). Every step shows where you are, what to do, how, and what's next.
    public sealed class LapCholeProcedure : MonoBehaviour
    {
        struct StepInfo { public string id, title, what, mouse, vr; }
        static readonly StepInfo[] Steps =
        {
            new StepInfo { id = "veress", title = "Veress needle", what = "Create pneumoperitoneum access through the base of the navel.",
                mouse = "Guide the needle onto the navel and click only when the shaft is centred.", vr = "Hold the Veress needle like a pencil. Put the tip on the purple mark at the navel, keep the shaft upright along the faint guide, and push through: two pops, then hold still at 2-4 cm." },
            new StepInfo { id = "insufflate", title = "Insufflation", what = "Insufflate the abdomen with CO2 to 12-15 mmHg.",
                mouse = "Hold SPACE to add CO2; release between 12 and 15 mmHg.", vr = "The needle stays in. Pinch and hold to run CO2; let go between 12 and 15 mmHg." },
            new StepInfo { id = "port_umbilical", title = "Umbilical port", what = "Place the umbilical balloon trocar, inflate its balloon, seal, insert the scope.",
                mouse = "Seat the trocar at the navel, then hold SPACE to inflate its retention balloon.", vr = "Take the 12 mm balloon trocar. Enter through the Veress puncture, straight down, push until it pops through and hold still at 3-5 cm. Then pinch and hold to inflate the balloon." },
            new StepInfo { id = "port_epigastric", title = "Epigastric port", what = "Place the 10 mm trocar just below the breastbone, aimed at the right upper quadrant.",
                mouse = "Place the trocar below the breastbone, aimed toward the right upper quadrant.", vr = "Take the 10 mm trocar. Tip on the mark below the breastbone, line the shaft up with the guide toward the right upper quadrant, push until it pops through, then hold still." },
            new StepInfo { id = "port_rsc_medial", title = "Right subcostal port (medial)", what = "Place a 5 mm trocar below the right ribs, in line with the middle of the collarbone.",
                mouse = "Place the trocar below the right ribs on the mid-clavicular line.", vr = "Take a 5 mm trocar. Tip on the mark below the right ribs, follow the guide's angle, push through the wall until it pops, then hold still." },
            new StepInfo { id = "port_rsc_lateral", title = "Right subcostal port (lateral)", what = "Place a 5 mm trocar further out on the right flank.",
                mouse = "Place the trocar on the right flank while preserving instrument spacing.", vr = "Second 5 mm trocar, out on the right flank. Tip on the mark, follow the guide, push through until it pops, hold still." },
            new StepInfo { id = "retract", title = "Retract the gallbladder", what = "Grasp the gallbladder fundus and push it up over the liver.",
                mouse = "Guide the grasper jaws around the gallbladder fundus, then close them.", vr = "Work through the lateral trocar. Advance the grasper jaws around the gallbladder fundus, close them, and lift toward the patient's right shoulder." },
            new StepInfo { id = "dissect", title = "Dissect Calot's triangle", what = "Peel the peritoneum off the cystic duct and artery (critical view of safety).",
                mouse = "Open the peritoneum with short controlled sweeps over Calot's triangle.", vr = "Use the Maryland dissector through the epigastric trocar. Apply short lateral sweeps to the translucent peritoneum; avoid the blue common bile duct." },
            new StepInfo { id = "clip_duct_distal", title = "Distal clip, cystic duct", what = "Place one Hem-o-lok clip on the cystic duct near the gallbladder.",
                mouse = "Place the clip-applier jaws across the cystic duct near the gallbladder.", vr = "Advance the clip applier through the trocar, capture only the cystic duct between both jaws, verify the common bile duct is clear, then squeeze." },
            new StepInfo { id = "ductotomy", title = "Ductotomy", what = "Make a small cut in the cystic duct just below that clip.",
                mouse = "Place the scissor blades just below the distal clip and close them.", vr = "Advance scissors through the port, straddle only the cystic duct just below the clip, and squeeze once. Do not touch the common bile duct." },
            new StepInfo { id = "cholangiogram", title = "Cholangiogram", what = "Thread the catheter into the duct, inject contrast, review the bile ducts.",
                mouse = "Thread the catheter through the ductotomy, then hold SPACE to inject contrast.", vr = "Guide the catheter tip into the ductotomy along the duct axis. Squeeze to seat it, then hold while watching the cholangiogram fill." },
            new StepInfo { id = "clip_duct_proximal", title = "Proximal clips, cystic duct", what = "Place two clips on the cystic duct below the ductotomy (bile duct side).",
                mouse = "Capture the cystic duct at each of the two proximal positions and close the applier.", vr = "Place two separate clips on the cystic-duct side nearest the common bile duct. See both jaws around the duct before each squeeze." },
            new StepInfo { id = "divide_duct", title = "Divide the cystic duct", what = "Cut the cystic duct between the proximal clips and the ductotomy.",
                mouse = "Place the scissors in the protected gap and close the blades.", vr = "Put both scissor blades around the cystic duct in the protected gap between clips. Confirm the common bile duct is outside the blades, then squeeze." },
            new StepInfo { id = "clip_artery", title = "Clip the cystic artery", what = "Place three clips on the cystic artery: two proximal, one distal.",
                mouse = "Capture the salmon cystic artery at each planned clip position.", vr = "Use the clip applier on the salmon cystic artery only: two clips on the hepatic side and one on the gallbladder side. Verify both jaws before squeezing." },
            new StepInfo { id = "divide_artery", title = "Divide the cystic artery", what = "Cut the cystic artery between the clips.",
                mouse = "Place the scissors between the secured clips and close them.", vr = "Advance the scissors through the epigastric trocar, straddle the artery only in the protected gap, and squeeze once." },
        };

        public PegTransferRig rig;
        public int StepIndex { get; private set; }
        public float Pressure { get; private set; }
        public string Feedback { get; private set; } = "";
        public bool Active => rig != null && rig.ExerciseId == LapCholeAnatomy.ExerciseId && dressing != null;

        // ---- read-only state for the dynamic narrator (LapCholeNarrator); no effect on the procedure ----
        public static int StepCount => Steps.Length;
        public static string StepIdAt(int i) => Steps[Mathf.Clamp(i, 0, Steps.Length - 1)].id;
        public static string StepTitleAt(int i) => Steps[Mathf.Clamp(i, 0, Steps.Length - 1)].title;
        public string StepId => Steps[StepIndex].id;
        public string StepWhat => Steps[StepIndex].what;
        public string StepHow => rig != null && rig.desktopPreview && !simHands ? Steps[StepIndex].mouse : Steps[StepIndex].vr;
        public string AlignmentCue => alignmentCue;
        public bool ShaftAligned => shaftAligned;
        public bool ExchangePending => exchangePending;
        public string EquippedToolLabel => equippedTool != null ? ToolLabel(equippedTool) : "";
        public bool UmbilicalSeated => placedUmbilical;
        public bool CatheterIn => catheterIn;
        public float Balloon => balloon;
        public float Veil => veil;
        public float Contrast => contrast;
        public int DuctClips => ductClips.Count;
        public int ArteryClips => arteryClips.Count;
        public bool Autopiloting => autopilot;
        public static string StepWhatAt(int i) => Steps[Mathf.Clamp(i, 0, Steps.Length - 1)].what;
        public static string StepHowAt(int i, bool desktop) => desktop ? Steps[Mathf.Clamp(i, 0, Steps.Length - 1)].mouse : Steps[Mathf.Clamp(i, 0, Steps.Length - 1)].vr;
        /// <summary>Structure under the working jaws this frame (cystic_duct, cystic_artery, cbd, fundus, veil), or null.</summary>
        public string ContactStructure { get; private set; }
        public Vector3? ToolTipBoard => toolTip;
        public Vector3 TargetBoard => TargetPoint(out _);
        public LapCholeEntry Entry => entry;
        public bool UsingHands => !rig.desktopPreview || simHands;
        /// <summary>Simulated left-hand pinch (verification of push-to-talk without a headset).</summary>
        [System.NonSerialized] public bool simLeftPinch;
        /// <summary>Raised for every feedback line (corrections, progress, step results), before the step advances.</summary>
        public event System.Action<string> Said;
        /// <summary>Set by LapCholeNarrator: it speaks dynamically and plays the recorded step clips only as a fallback.</summary>
        public bool ExternalVoice { get; set; }
        LapCholeNarrator narrator;
        LapCholePanel uiPanel;
        bool PanelUp => uiPanel != null && uiPanel.Built;
        /// <summary>Port position for the panel's map: where it was actually placed, else the planned site.</summary>
        public Vector3 PortPositionForMap(string id) => id == "veress" ? (veressEntry ?? SkinPoint(Vector2.zero)) : PortPoint(id);

        // ---- hand-held entry (headset; and simulated hands for verification in the editor) ----
        LapCholeEntry entry;
        string entryFor;
        Vector3? veressEntry;
        LapCholeEntry.Outcome? umbilicalOutcome;
        readonly Dictionary<string, KeyValuePair<Vector3, Vector3>> placedPorts = new Dictionary<string, KeyValuePair<Vector3, Vector3>>();
        /// <summary>Verification: drive the headset input path from code (board-local grip, forward, pinch).</summary>
        [System.NonSerialized] public bool simHands;
        [System.NonSerialized] public Vector3 simGrip, simForward = Vector3.down;
        [System.NonSerialized] public bool simPinch, simTracked = true;
        public string EntryReadout => entry != null && entry.Active ? entry.Readout : "";
        public bool HandEntry => entry != null && entry.Active && (!rig.desktopPreview || simHands);
        // Instrument tray at the trainee's right: the entry tools lie on it; the next one glows; pinch it to pick it up.
        Transform tray, trayGlow;
        readonly Dictionary<string, Transform> trayTools = new Dictionary<string, Transform>();
        bool toolInHand, prevGrab;
        static readonly string[] TraySlots = { "veress", "port_umbilical", "port_epigastric", "port_rsc_medial", "port_rsc_lateral" };
        public bool ToolInHand => toolInHand || !UsingHands;
        public string TrayToolName => entry != null && entry.Current != null ? entry.Current.name : "tool";
        /// <summary>Board-local position of a tool's grip on the tray.</summary>
        public Vector3 TraySlot(string step)
        {
            int i = System.Array.IndexOf(TraySlots, step);
            return TrayOrigin + new Vector3(0.08f, 0.022f, -0.056f + 0.028f * Mathf.Max(0, i)); // rows across the tray
        }
        static readonly Vector3 TrayOrigin = new Vector3(0.2f, -0.06f, -0.3f); // toward the feet, on the trainee's side of the table
        AudioSource fx, hiss;
        AudioClip popClip, skinClip, hissClip;
        TextMesh readoutText;

        LapCholeDressing dressing;
        LapCholeAnatomy anatomy;
        LaparoscopeMonitor monitor;
        DesktopPreview preview;
        LapCholeKit kit;
        Transform props;
        LapTool grasper, assistantGrasper, dissector, clipper, scissors, catheter;
        LapTool equippedTool;
        string equippedPortId;
        bool exchangePending;
        Transform targetRing, targetGlow, handStopRing;
        readonly List<float> ductClips = new List<float>(), arteryClips = new List<float>();
        float ductotomyAt = -1f, ductCutAt = -1f, veil = 1f, balloon, contrast, feedbackUntil, prevPressureHold;
        bool placedUmbilical, catheterIn, fundusHeld, prevAct, wasRunning, vrPanelBuilt, shaftAligned = true, proxyToolsVisible = true;
        bool handCollisionLatched, controllerHaptic;
        Vector3 lastDissect;
        Vector3? toolTip;
        float toolOutside = 0.26f;
        Quaternion toolGripRotation = Quaternion.identity;
        float hapticUntil;
        string alignmentCue = "";
        Texture2D chole;
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
            if (uiPanel == null) { uiPanel = GetComponent<LapCholePanel>(); if (uiPanel == null) uiPanel = gameObject.AddComponent<LapCholePanel>(); }
            if (rig.ExerciseId != LapCholeAnatomy.ExerciseId) { SetProxyToolsVisible(true); return; }
            if (dressing == null) { dressing = FindFirstObjectByType<LapCholeDressing>(); if (dressing == null) return; anatomy = dressing.Anatomy; }
            if (monitor == null) monitor = FindFirstObjectByType<LaparoscopeMonitor>();
            if (preview == null) preview = GetComponent<DesktopPreview>();
            if (kit == null) BuildProps();
            SetProxyToolsVisible(false); // the procedure draws the real tools (entry tools, then the laparoscopic set)
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
            Read(out Vector3 point, out string structure, out bool hasPoint, out bool act, out bool hold);
            bool pressed = act && !prevAct;
            prevAct = act;
            ContactStructure = hasPoint ? structure : null;
            UpdateHiss(hold && Steps[StepIndex].id == "insufflate");
            UpdateTray();
            if (EntryStepNow())
            {
                EnsureEntry();
                if (rig.desktopPreview && !simHands) { entry.PoseHover(point, hasPoint); Drive(point, structure, hasPoint, pressed, hold); }
                else DriveEntry();
                UpdateReadout();
                return;
            }
            if (entry != null && entry.Active) entry.End();
            UpdateReadout();
            Drive(point, structure, hasPoint, pressed, hold);
        }

        // ---------------- instrument tray ----------------
        void BuildTray()
        {
            tray = new GameObject("Instrument tray").transform;
            tray.SetParent(props, false);
            tray.localPosition = TrayOrigin;
            Material steelTray = kit.steel, towel = SurgicalField.Standard(new Color(0.16f, 0.42f, 0.62f), 0.25f);
            SurgicalField.Add(tray, "Tray", SurgicalField.RoundedBox(new Vector3(0.36f, 0.012f, 0.16f), 0.006f, 3), steelTray, Vector3.zero, Quaternion.identity, Vector3.one);
            SurgicalField.Add(tray, "Towel", SurgicalField.RoundedBox(new Vector3(0.33f, 0.003f, 0.13f), 0.002f, 2), towel, new Vector3(0f, 0.0075f, 0f), Quaternion.identity, Vector3.one);
            SurgicalField.Add(tray, "Stand", SurgicalField.Lathe(new[] { new Vector2(0.008f, -0.9f), new Vector2(0.008f, -0.01f) }, 16), steelTray, Vector3.zero, Quaternion.identity, Vector3.one);
            foreach (string step in TraySlots)
            {
                LapCholeEntry.Spec spec = LapCholeEntry.SpecFor(step);
                Transform t = entry.BuildDisplay(spec.tool, props);
                t.name = "Tray tool " + step;
                Vector3 grip = TraySlot(step);
                // Lying flat on the towel, grip toward the trainee's right, tip toward the patient's head.
                t.localPosition = grip;
                t.localRotation = Quaternion.LookRotation(Vector3.left, Vector3.up);
                trayTools[step] = t;
            }
            var glow = new GameObject("Tray highlight", typeof(MeshFilter), typeof(MeshRenderer));
            glow.GetComponent<MeshFilter>().sharedMesh = kit.Ring;
            glow.GetComponent<MeshRenderer>().sharedMaterial = kit.guide;
            trayGlow = glow.transform; trayGlow.SetParent(props, false);
            trayGlow.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }

        void UpdateTray()
        {
            if (tray == null) return;
            bool show = UsingHands && Active && StepIndex < 6;
            tray.gameObject.SetActive(show);
            int current = System.Array.IndexOf(TraySlots, Steps[StepIndex].id);
            foreach (var kv in trayTools)
            {
                int i = System.Array.IndexOf(TraySlots, kv.Key);
                // Used tools are in the patient; the one in the hand is not on the tray.
                kv.Value.gameObject.SetActive(show && (i > current || (i == current && !toolInHand)));
            }
            bool waiting = show && current >= 0 && !toolInHand && EntryStepNow();
            trayGlow.gameObject.SetActive(waiting);
            if (waiting)
            {
                float pulse = 1f + 0.2f * Mathf.Sin(Time.unscaledTime * 5f);
                trayGlow.localPosition = TraySlot(Steps[StepIndex].id) + new Vector3(0f, -0.012f, 0f);
                trayGlow.localScale = Vector3.one * 0.022f * pulse;
            }
        }

        // ---------------- hand-held entry ----------------
        bool EntryStepNow()
        {
            string id = Steps[StepIndex].id;
            return id == "veress" || (id == "port_umbilical" && !placedUmbilical) || id == "port_epigastric" || id == "port_rsc_medial" || id == "port_rsc_lateral";
        }

        static string PortOf(string step) => step == "veress" ? "veress" : step.Substring("port_".Length);

        void EnsureEntry()
        {
            if (entry == null) return;
            string id = Steps[StepIndex].id;
            if (entryFor == id && entry.Active) return;
            entryFor = id;
            toolInHand = false;
            Vector3 site, axis;
            if (id == "veress") { site = SkinPoint(Vector2.zero); axis = Vector3.up; }
            else if (id == "port_umbilical")
            {
                Vector2 at = veressEntry.HasValue ? new Vector2(veressEntry.Value.x, veressEntry.Value.z) : Vector2.zero;
                site = SkinPoint(at); axis = Vector3.up;
            }
            else { string port = PortOf(id); site = SkinPoint(PortSite(port)); axis = dressing.PortAxis(port); }
            entry.Begin(LapCholeEntry.SpecFor(id), site, axis);
        }

        void DriveEntry()
        {
            bool tracked = HandPose(out Vector3 grip, out Vector3 tip, out _, out bool closed);
            bool grab = closed && !prevGrab; prevGrab = closed;
            if (!toolInHand)
            {
                string step = Steps[StepIndex].id;
                float reach = tracked ? Vector3.Distance(grip, TraySlot(step)) : float.PositiveInfinity;
                if (grab && reach < 0.09f)
                {
                    toolInHand = true;
                    OnPop(0);
                    Say("Picked up the " + entry.Current.name + ". Take it to the purple cross.");
                }
                else
                {
                    entry.Idle(reach < 0.25f ? "Pinch the glowing " + entry.Current.name + " to pick it up" : "Pick up the " + entry.Current.name + " from the tray on your right");
                    return;
                }
            }
            Vector3 forward = tip - grip;
            if (!entry.Step(grip, forward, tracked, Time.deltaTime, out LapCholeEntry.Outcome o)) return;
            string id = Steps[StepIndex].id;
            string port = PortOf(id);
            entry.End();
            if (id == "veress")
            {
                veressEntry = o.entry;
                dressing.ShowStage(1);
                if (dressing.VeressObject != null)
                {
                    // The dressing's needle has its tip 4 cm below its origin; seat it at the measured entry, axis and depth.
                    Transform v = dressing.VeressObject.transform;
                    v.position = Board.TransformPoint(o.entry + o.outward * (0.04f - o.depth / 1000f));
                    v.rotation = Board.rotation * Quaternion.FromToRotation(Vector3.up, o.outward);
                }
                CompletePort("veress", Vector2.zero, o);
                return;
            }
            ShowTrocar(port);
            SeatTrocar(port, o);
            if (id == "port_umbilical")
            {
                placedUmbilical = true; umbilicalOutcome = o;
                Say("Trocar seated at " + o.depth.ToString("F0") + " mm. Keep it stable and inflate the retention balloon.");
                return;
            }
            CompletePort(port, PortSite(port), o);
        }

        void SeatTrocar(string port, LapCholeEntry.Outcome o)
        {
            placedPorts[port] = new KeyValuePair<Vector3, Vector3>(o.entry, o.outward);
            GameObject t = dressing.TrocarObject(port);
            if (t == null) return;
            t.transform.position = Board.TransformPoint(o.entry);
            t.transform.rotation = Board.rotation * Quaternion.FromToRotation(Vector3.up, o.outward);
        }

        void CompletePort(string port, Vector2 site, LapCholeEntry.Outcome o)
        {
            string detail = "depth_mm=" + o.depth.ToString("F0") + (o.tilt != "none" ? ";tilt=" + o.tilt : "");
            rig.Recorder.RecordLap("port_placed", port, "right", o.entry, SkinPoint(site), o.angle, detail);
            string verdict = o.mm <= 7f && o.angle <= 10f ? "precise" : o.mm <= 12f && o.angle <= 18f ? "good" : "acceptable";
            Complete((port == "veress" ? "Needle in, " : "Port placed ") + o.mm.ToString("F0") + " mm from the ideal site, " + o.angle.ToString("F0") + "° off the planned axis, "
                + o.depth.ToString("F0") + " mm deep · " + verdict);
        }

        /// <summary>The right hand's pinch pose, board-local. False when the hand is not tracked.</summary>
        bool HandPose(out Vector3 grip, out Vector3 tip, out Quaternion gripRotation, out bool closed)
        {
            if (simHands)
            {
                Vector3 f = simForward.sqrMagnitude > 1e-6f ? simForward.normalized : Vector3.down;
                grip = simGrip; tip = simGrip + f * 0.12f; closed = simPinch;
                gripRotation = Board.rotation * Quaternion.LookRotation(f, Mathf.Abs(f.y) > 0.95f ? Vector3.right : Vector3.up);
                return simTracked;
            }
            InstrumentController right = rig.Instruments != null && rig.Instruments.Length > 1 ? rig.Instruments[1] : null;
            grip = tip = Vector3.zero; gripRotation = Quaternion.identity; closed = false;
            if (right == null || right.tip == null || !right.TrackingValid) return false;
            grip = Board.InverseTransformPoint(right.GripPosition);
            tip = Board.InverseTransformPoint(right.tip.position);
            gripRotation = right.GripRotation; closed = right.Closed;
            return true;
        }

        Vector3 PortPoint(string id) => placedPorts.TryGetValue(id, out var p) ? p.Key : SkinPoint(PortSite(id));
        Vector3 PortOut(string id) => placedPorts.TryGetValue(id, out var p) ? p.Value : dressing.PortAxis(id);

        void UpdateReadout()
        {
            if (PanelUp) { if (readoutText != null) readoutText.gameObject.SetActive(false); return; } // the panel's metric card replaces it
            if (rig.desktopPreview && !simHands) { if (readoutText != null) readoutText.gameObject.SetActive(false); return; }
            if (readoutText == null)
            {
                var go = new GameObject("Entry readout");
                go.transform.SetParent(props, false);
                readoutText = go.AddComponent<TextMesh>();
                readoutText.fontSize = 64; readoutText.characterSize = 0.0018f; readoutText.anchor = TextAnchor.LowerCenter;
                readoutText.color = new Color(0.85f, 1f, 0.9f);
            }
            string text = HandEntry ? entry.Readout : "";
            readoutText.gameObject.SetActive(text.Length > 0);
            if (text.Length == 0) return;
            readoutText.text = text.Replace(" · ", "\n");
            Vector3 at = TargetPoint(out _) + Vector3.up * 0.16f + Vector3.forward * -0.05f;
            readoutText.transform.localPosition = at;
            if (rig.head != null)
            {
                Vector3 look = readoutText.transform.position - rig.head.position;
                if (look.sqrMagnitude > 1e-4f) readoutText.transform.rotation = Quaternion.LookRotation(look, Vector3.up);
            }
        }

        // ---------------- sounds: tissue pops and the insufflator ----------------
        void EnsureFx()
        {
            if (fx != null) return;
            fx = gameObject.AddComponent<AudioSource>(); fx.playOnAwake = false; fx.spatialBlend = 0f; fx.volume = 0.9f;
            hiss = gameObject.AddComponent<AudioSource>(); hiss.playOnAwake = false; hiss.loop = true; hiss.spatialBlend = 0f; hiss.volume = 0f;
            popClip = Synth("pop", 0.09f, (t, r) => Mathf.Sin(2f * Mathf.PI * 95f * t) * Mathf.Exp(-t * 45f) * 0.9f + (float)(r.NextDouble() * 2 - 1) * Mathf.Exp(-t * 160f) * 0.6f);
            skinClip = Synth("skin", 0.05f, (t, r) => (float)(r.NextDouble() * 2 - 1) * Mathf.Exp(-t * 120f) * 0.35f);
            float last = 0f;
            hissClip = Synth("co2", 1.2f, (t, r) => { last = last * 0.55f + (float)(r.NextDouble() * 2 - 1) * 0.45f; return last * 0.5f; });
            hiss.clip = hissClip;
        }

        static AudioClip Synth(string name, float seconds, System.Func<float, System.Random, float> f)
        {
            const int rate = 44100;
            var r = new System.Random(7);
            int n = Mathf.CeilToInt(seconds * rate);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(f(i / (float)rate, r), -1f, 1f);
            AudioClip c = AudioClip.Create(name, n, 1, rate, false);
            c.SetData(data, 0);
            return c;
        }

        void OnPop(int layer)
        {
            EnsureFx();
            fx.PlayOneShot(layer == 0 ? skinClip : popClip, layer == 2 ? 1f : 0.75f);
            if (!rig.desktopPreview && rig.Instruments != null && rig.Instruments.Length > 1 && rig.Instruments[1] != null && !rig.Instruments[1].UsingHandTracking)
            {
                OVRInput.SetControllerVibration(0.6f, layer == 2 ? 0.9f : 0.5f, OVRInput.Controller.RTouch);
                controllerHaptic = true; hapticUntil = Time.unscaledTime + 0.08f;
            }
        }

        void UpdateHiss(bool on)
        {
            EnsureFx();
            float target = on && Pressure < 20f ? 0.22f : 0f;
            hiss.volume = Mathf.MoveTowards(hiss.volume, target, Time.deltaTime * 1.5f);
            if (hiss.volume > 0f && !hiss.isPlaying) hiss.Play();
            else if (hiss.volume <= 0f && hiss.isPlaying) hiss.Stop();
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
            if (ExternalVoice) return;
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
        /// <summary>Play the recorded clip for a step id (or "complete"); the narrator's fallback when voice is unavailable.</summary>
        public void PlayRecordedClip(string id)
        {
            if (voice == null)
            {
                voice = gameObject.AddComponent<AudioSource>();
                voice.playOnAwake = false; voice.spatialBlend = 0f; voice.volume = 1f;
            }
            Play(id);
        }
        public void StopRecordedClip() { if (voice != null) voice.Stop(); }
        void Play(string id)
        {
            AudioClip clip = Resources.Load<AudioClip>("Voice/lapchole_" + id);
            if (clip == null) return;
            voice.Stop(); voice.clip = clip; voice.Play();
        }

        void ResetProcedure()
        {
            spokenStep = -1; spokeComplete = false;
            StepIndex = 0; Pressure = 0; balloon = 0; contrast = 0; veil = 1f; ductotomyAt = ductCutAt = -1f;
            placedUmbilical = catheterIn = fundusHeld = false;
            ductClips.Clear(); arteryClips.Clear();
            placedPorts.Clear(); veressEntry = null; umbilicalOutcome = null; entryFor = null; toolInHand = false; trayTools.Clear(); tray = null;
            if (entry != null) entry.Destroy();
            entry = null; readoutText = null;
            if (props != null) Destroy(props.gameObject);
            if (kit != null) kit.Destroy();
            kit = null; BuildProps();
            dressing.ShowStage(0);
            dressing.SetInsufflation(0f);
            if (anatomy != null) anatomy.SetPeritoneumCover(1f);
            SetScopeMain(false);
            Feedback = ""; toolTip = null; shaftAligned = true; alignmentCue = "";
            equippedTool = null; equippedPortId = null; exchangePending = false;
            SetHandCollision(null, false, 0f, Vector3.zero);
        }

        void BuildProps()
        {
            kit = new LapCholeKit();
            props = new GameObject("Lap chole procedure props").transform;
            props.SetParent(rig.board.transform, false);
            grasper = new LapTool(props, LapToolKind.Grasper, kit);
            assistantGrasper = new LapTool(props, LapToolKind.Grasper, kit);
            dissector = new LapTool(props, LapToolKind.Dissector, kit);
            clipper = new LapTool(props, LapToolKind.ClipApplier, kit);
            scissors = new LapTool(props, LapToolKind.Scissors, kit);
            catheter = new LapTool(props, LapToolKind.Catheter, kit);
            foreach (LapTool t in Tools()) t.SetVisible(false);
            entry = new LapCholeEntry(props, kit, dressing.Skin);
            entry.Said += Say;
            entry.Popped += OnPop;
            BuildTray();
            var ring = new GameObject("Entry alignment guide", typeof(MeshFilter), typeof(MeshRenderer));
            ring.GetComponent<MeshFilter>().sharedMesh = kit.Ring;
            ring.GetComponent<MeshRenderer>().sharedMaterial = kit.glowSkin;
            targetRing = ring.transform; targetRing.SetParent(props, false);
            GameObject glow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(glow.GetComponent<Collider>());
            glow.GetComponent<Renderer>().sharedMaterial = kit.glowInside;
            targetGlow = glow.transform; targetGlow.SetParent(props, false);
            if (anatomy != null) LapCholeAnatomy.SetLayer(targetGlow, LapCholeAnatomy.Layer);
            var stop = new GameObject("Hand collision boundary", typeof(MeshFilter), typeof(MeshRenderer));
            stop.GetComponent<MeshFilter>().sharedMesh = kit.Ring;
            stop.GetComponent<MeshRenderer>().sharedMaterial = kit.warning;
            handStopRing = stop.transform; handStopRing.SetParent(props, false);
            handStopRing.gameObject.SetActive(false);
        }

        IEnumerable<LapTool> Tools() { yield return grasper; yield return assistantGrasper; yield return dissector; yield return clipper; yield return scissors; yield return catheter; }

        void SetProxyToolsVisible(bool visible)
        {
            if (proxyToolsVisible == visible || rig.Instruments == null) return;
            proxyToolsVisible = visible;
            foreach (InstrumentController instrument in rig.Instruments)
                if (instrument != null && instrument.tip != null)
                    foreach (Renderer renderer in instrument.tip.GetComponentsInChildren<Renderer>(true)) renderer.enabled = visible;
        }

        // ---------------- geometry helpers ----------------
        Transform Board => rig.board.transform;
        Vector3 ToBoard(Vector3 anatomyLocal) => Board.InverseTransformPoint(anatomy.transform.TransformPoint(anatomyLocal));
        Vector3[] Path(string id) => id == "cystic_duct" ? anatomy.CysticDuct : id == "cystic_artery" ? anatomy.CysticArtery : id == "cbd" ? anatomy.CommonBileDuct : null;
        float Length(Vector3[] p) { float l = 0; for (int i = 1; i < p.Length; i++) l += Vector3.Distance(p[i - 1], p[i]); return l; }
        Vector3 AlongBoard(string id, float metres) => ToBoard(LapCholeAnatomy.PointAlong(Path(id), metres));

        Vector3 SkinPoint(Vector2 site)
        {
            float y = dressing.Skin(site.x, site.y);
            return new Vector3(site.x, float.IsNaN(y) ? 0f : y, site.y);
        }

        Vector2 PortSite(string id) { foreach (LapCholeDressing.Port p in dressing.Ports) if (p.id == id) return p.site; return Vector2.zero; }

        // Planned positions along each structure (fractions of length from the proximal end).
        float DuctLen => Length(anatomy.CysticDuct);
        float ArtLen => Length(anatomy.CysticArtery);
        float PlanDistal => DuctLen * 0.82f;
        float PlanDuctotomy => DuctLen * 0.7f;
        float PlanProximal(int k) => DuctLen * (k == 0 ? 0.22f : 0.36f);
        float PlanDuctCut => DuctLen * 0.53f;
        float PlanArtery(int k) => ArtLen * (k == 0 ? 0.25f : k == 1 ? 0.4f : 0.78f);
        float PlanArteryCut => ArtLen * 0.6f;

        // ---------------- input ----------------
        void Read(out Vector3 point, out string structure, out bool hasPoint, out bool act, out bool hold)
        {
            point = Vector3.zero; structure = null; hasPoint = false; act = false; hold = false;
            toolTip = null; shaftAligned = true; alignmentCue = "";
            bool inside = StepIndex >= 6;
            if (rig.desktopPreview && !simHands)
            {
                Mouse mouse = Mouse.current; Keyboard keys = Keyboard.current;
                if (mouse == null) return;
                act = mouse.leftButton.isPressed;
                hold = act || (keys != null && keys.spaceKey.isPressed);
                Vector2 screen = mouse.position.ReadValue();
                if (inside) hasPoint = PickInside(screen, out point, out structure);
                else hasPoint = PickSkin(screen, out point);
                if (inside && hasPoint) toolTip = point;
                return;
            }
            InstrumentController right = rig.Instruments != null && rig.Instruments.Length > 1 ? rig.Instruments[1] : null;
            if (!HandPose(out Vector3 rawGrip, out Vector3 tip, out Quaternion handRotation, out bool closed))
            {
                SetHandCollision(right, false, 0f, Vector3.zero);
                return;
            }
            act = closed; hold = act;
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
                // the tracked hand through the abdomen cannot move or activate an instrument inside it.
                tip += grip - rawGrip;
            }
            SetHandCollision(right, handBlocked, penetration, contact);
            toolGripRotation = handRotation;
            if (!inside)
            {
                float skin = dressing.Skin(tip.x, tip.z);
                float depth = float.IsNaN(skin) ? float.PositiveInfinity : skin - tip.y;
                hasPoint = !handBlocked && !float.IsNaN(skin) && depth >= -0.01f && depth <= 0.03f;
                point = new Vector3(tip.x, float.IsNaN(skin) ? tip.y : skin, tip.z);
                return;
            }
            // The trocar is a real fulcrum. The visible instrument and its interaction point both stay on the line
            // from the user's hand through that trocar; reaching through the abdominal wall anywhere else is rejected.
            string portId = exchangePending && !string.IsNullOrEmpty(equippedPortId)
                ? equippedPortId : DesiredPort(Steps[StepIndex].id);
            Vector3 port = PortPoint(portId);
            Vector3 handle = grip - port;
            toolOutside = Mathf.Clamp(handle.magnitude, handClearance, 0.55f);
            Vector3 outward = PortOut(portId).normalized;
            float shaftAngle = Vector3.Angle(outward, handle.normalized);
            shaftAligned = !handBlocked && handle.magnitude > 0.09f && shaftAngle <= 52f;
            alignmentCue = handBlocked ? "HAND STOP: withdraw your hand from the abdominal wall"
                : shaftAligned ? "Shaft centred through " + PortLabel(portId)
                : "Re-centre the handle over the " + PortLabel(portId) + " — the shaft cannot pass through the body";
            Vector3 dir = handle.sqrMagnitude > 1e-6f ? -handle.normalized : -outward;
            Vector3 inner = port + dir * Mathf.Clamp(0.31f - handle.magnitude, 0.018f, 0.19f);
            toolTip = inner;
            if (exchangePending)
            {
                hasPoint = false;
                alignmentCue = shaftAligned
                    ? "Withdraw the " + ToolLabel(equippedTool) + " to the trocar before changing instruments"
                    : "Keep the " + ToolLabel(equippedTool) + " centred while withdrawing it";
                if (shaftAligned && Vector3.Distance(inner, port) <= 0.04f)
                {
                    equippedTool = ToolFor(Steps[StepIndex].id);
                    equippedPortId = DesiredPort(Steps[StepIndex].id);
                    exchangePending = false;
                    toolTip = null; // the replacement starts outside; it never materializes at the target
                    alignmentCue = "Instrument removed. Bring the " + ToolLabel(equippedTool) + " to the " + PortLabel(equippedPortId) + ".";
                }
                return;
            }
            float contactRadius = Steps[StepIndex].id == "retract" ? 0.014f : Steps[StepIndex].id == "dissect" ? 0.012f : 0.009f;
            hasPoint = shaftAligned && NearestStructure(inner, out point, out structure, contactRadius);
        }

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
            if (blocked && !handCollisionLatched)
            {
                rig.Recorder.RecordLap("hand_collision", "abdominal_wall", "right", contact, null,
                    penetration * 1000f, Steps[StepIndex].id);
                if (instrument != null && !instrument.UsingHandTracking && !simHands)
                {
                    OVRInput.SetControllerVibration(0.8f, 0.35f, OVRInput.Controller.RTouch);
                    controllerHaptic = true;
                    hapticUntil = Time.unscaledTime + 0.11f;
                }
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

        bool PickInside(Vector2 screen, out Vector3 point, out string structure)
        {
            point = Vector3.zero; structure = null;
            Camera cam = monitor != null && monitor.PointerCamera != null ? monitor.PointerCamera : monitor != null ? monitor.Scope : null;
            if (cam == null) return false;
            float best = 60f; // pixels
            void Try(string id, Vector3 boardPoint, ref Vector3 pt, ref string st)
            {
                Vector3 s = cam.WorldToScreenPoint(Board.TransformPoint(boardPoint));
                if (s.z <= 0) return;
                float dist = Vector2.Distance(screen, new Vector2(s.x, s.y));
                if (dist < best) { best = dist; pt = boardPoint; st = id; }
            }
            foreach (string id in new[] { "cystic_duct", "cystic_artery", "cbd" })
            {
                Vector3[] path = Path(id);
                float len = Length(path);
                for (float m = 0f; m <= len; m += 0.001f) Try(id, AlongBoard(id, m), ref point, ref structure);
            }
            if (anatomy.Fundus != null) Try("fundus", Board.InverseTransformPoint(anatomy.Fundus.position), ref point, ref structure);
            Try("veil", ToBoard(LapCholeAnatomy.CalotCenter), ref point, ref structure);
            return structure != null;
        }

        bool NearestStructure(Vector3 boardPoint, out Vector3 point, out string structure, float radius)
        {
            point = boardPoint; structure = null; float best = radius;
            foreach (string id in new[] { "cystic_duct", "cystic_artery", "cbd" })
            {
                Vector3[] path = Path(id);
                Vector3 local = anatomy.transform.InverseTransformPoint(Board.TransformPoint(boardPoint));
                float along = LapCholeAnatomy.DistanceAlong(path, local, out float off);
                if (off < best) { best = off; structure = id; point = AlongBoard(id, along); }
            }
            if (anatomy.Fundus != null && Vector3.Distance(boardPoint, Board.InverseTransformPoint(anatomy.Fundus.position)) < best) { structure = "fundus"; point = Board.InverseTransformPoint(anatomy.Fundus.position); }
            if (structure == null && Vector3.Distance(boardPoint, ToBoard(LapCholeAnatomy.CalotCenter)) < 0.04f) { structure = "veil"; point = boardPoint; }
            return structure != null;
        }

        float AlongMm(string structure, Vector3 boardPoint)
        {
            Vector3 local = anatomy.transform.InverseTransformPoint(Board.TransformPoint(boardPoint));
            return LapCholeAnatomy.DistanceAlong(Path(structure), local, out _) * 1000f;
        }

        // ---------------- the procedure ----------------
        void Drive(Vector3 point, string structure, bool hasPoint, bool pressed, bool hold)
        {
            string step = Steps[StepIndex].id;
            switch (step)
            {
                case "veress":
                    if (pressed && hasPoint) PlacePort("veress", Vector2.zero, point, 1);
                    break;
                case "insufflate":
                    if (hold) Pressure = Mathf.Min(20f, Pressure + 5f * Time.deltaTime);
                    dressing.SetInsufflation(Mathf.Clamp01(Pressure / 14f));
                    if (!hold && prevPressureHold > 0f && Pressure > 1f) FinishInsufflation();
                    prevPressureHold = hold ? 1f : 0f;
                    break;
                case "port_umbilical":
                    if (!placedUmbilical) { if (pressed && hasPoint && SkinPlacementReady("umbilical", Vector2.zero, point)) { placedUmbilical = true; ShowTrocar("umbilical"); Say("Trocar seated. Keep it stable and inflate the retention balloon."); } }
                    else
                    {
                        if (hold) balloon = Mathf.Min(1f, balloon + 0.8f * Time.deltaTime);
                        if (balloon >= 1f)
                        {
                            if (umbilicalOutcome.HasValue) { LapCholeEntry.Outcome o = umbilicalOutcome.Value; umbilicalOutcome = null; SetScopeMain(false); CompletePort("umbilical", Vector2.zero, o); }
                            else PlacePort("umbilical", Vector2.zero, SkinPoint(Vector2.zero), -1);
                        }
                    }
                    break;
                case "port_epigastric": if (pressed && hasPoint) PlacePort("epigastric", PortSite("epigastric"), point, -1); break;
                case "port_rsc_medial": if (pressed && hasPoint) PlacePort("rsc_medial", PortSite("rsc_medial"), point, -1); break;
                case "port_rsc_lateral": if (pressed && hasPoint) PlacePort("rsc_lateral", PortSite("rsc_lateral"), point, -1); break;
                case "retract":
                    if (pressed && hasPoint && structure == "fundus") { fundusHeld = true; Complete("Fundus held over the liver."); }
                    break;
                case "dissect":
                    if (hold && hasPoint && (structure == "veil" || structure == "cystic_duct" || structure == "cystic_artery"))
                    {
                        float moved = Vector3.Distance(point, lastDissect);
                        veil = Mathf.Max(0f, veil - Mathf.Clamp(moved, 0f, 0.01f) * 9f - Time.deltaTime * 0.08f);
                        anatomy.SetPeritoneumCover(veil);
                        if (veil <= 0.02f) { rig.Recorder.RecordLap("critical_view", null, "right", null, null, 100f); Complete("Critical view of safety: cystic duct and artery clearly exposed."); }
                    }
                    lastDissect = point;
                    break;
                case "clip_duct_distal":
                    if (pressed && hasPoint && structure != null && structure != "fundus" && structure != "veil")
                    { if (Clip(structure, point) && structure == "cystic_duct") Complete("Distal clip placed."); }
                    break;
                case "ductotomy":
                    if (pressed && hasPoint && structure != null && structure != "fundus" && structure != "veil")
                    {
                        float mm = AlongMm(structure, point);
                        rig.Recorder.RecordLap("ductotomy", structure, "right", point, null, mm);
                        Mark(point, kit.slit, 0.0028f);
                        if (structure == "cystic_duct") { ductotomyAt = mm; Complete("Ductotomy made at " + mm.ToString("F0") + " mm."); }
                        else Say("That cut is not on the cystic duct. Major error.");
                    }
                    break;
                case "cholangiogram":
                    if (!catheterIn) { if (pressed && hasPoint && structure == "cystic_duct") { catheterIn = true; Say("Catheter in the duct. Hold to inject contrast."); } }
                    else
                    {
                        if (hold) contrast = Mathf.Min(1f, contrast + 0.5f * Time.deltaTime);
                        if (contrast >= 1f)
                        {
                            rig.Recorder.RecordLap("cholangiogram", "cystic_duct", "left", null, null, 5f, "ok");
                            catheterIn = false;
                            Complete("Cholangiogram: contrast fills the common bile duct and hepatic ducts. Catheter removed.");
                        }
                    }
                    break;
                case "clip_duct_proximal":
                    if (pressed && hasPoint && structure != null && structure != "fundus" && structure != "veil")
                    {
                        Clip(structure, point);
                        int below = 0; foreach (float c in ductClips) if (ductotomyAt < 0 || c < ductotomyAt) below++;
                        if (below >= 2) Complete("Two proximal clips placed.");
                    }
                    break;
                case "divide_duct":
                    if (pressed && hasPoint && structure != null && structure != "fundus" && structure != "veil") Cut(structure, point, "cystic_duct", "Cystic duct divided.");
                    break;
                case "clip_artery":
                    if (pressed && hasPoint && structure != null && structure != "fundus" && structure != "veil")
                    { Clip(structure, point); if (arteryClips.Count >= 3) Complete("Artery clipped."); }
                    break;
                case "divide_artery":
                    if (pressed && hasPoint && structure != null && structure != "fundus" && structure != "veil") Cut(structure, point, "cystic_artery", "Cystic artery divided. Procedure complete.");
                    break;
            }
        }

        void FinishInsufflation()
        {
            rig.Recorder.RecordLap("insufflation", null, "right", null, null, Pressure);
            bool ok = Pressure >= 12f && Pressure <= 15f;
            Complete(Pressure.ToString("F1") + " mmHg" + (ok ? " · in range" : Pressure > 15f ? " · too high (minor error)" : " · too low (minor error)"));
        }

        float CurrentToolAngle(string id)
        {
            if (rig.desktopPreview || rig.Instruments == null || rig.Instruments.Length < 2 || rig.Instruments[1].tip == null) return 0f;
            Vector3 axis = id == "veress" ? Vector3.up : dressing.PortAxis(id == "umbilical" ? "umbilical" : id);
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

        void PlacePort(string id, Vector2 site, Vector3 hit, int stage)
        {
            Vector3 ideal = SkinPoint(site);
            float angle = CurrentToolAngle(id);
            if (!SkinPlacementReady(id, site, hit)) return;
            rig.Recorder.RecordLap("port_placed", id, "right", hit, ideal, angle);
            if (stage == 1) dressing.ShowStage(1);
            else if (id != "veress") ShowTrocar(id);
            float mm = Vector2.Distance(new Vector2(hit.x, hit.z), site) * 1000f;
            string verdict = mm <= 7f ? "precise" : "acceptable";
            if (id == "umbilical") { SetScopeMain(false); }
            Complete((id == "veress" ? "Needle in, " : "Port placed ") + mm.ToString("F0") + " mm from the ideal site" + (angle > 0f ? ", " + angle.ToString("F0") + "° off" : "") + " · " + verdict);
        }

        void ShowTrocar(string id)
        {
            if (dressing.VeressObject != null) dressing.VeressObject.SetActive(false);
            GameObject t = dressing.TrocarObject(id);
            if (t != null) t.SetActive(true);
            if (id == "umbilical") dressing.SetInsufflation(Mathf.Max(1f, Pressure / 14f));
        }

        // Planned clip position (mm along the structure) nearest to mm for the current step; NaN if none.
        float NearestPlanMm(string structure, float mm)
        {
            var plans = new List<float>();
            string step = Steps[StepIndex].id;
            if (structure == "cystic_duct" && step == "clip_duct_distal") plans.Add(PlanDistal);
            else if (structure == "cystic_duct" && step == "clip_duct_proximal") { plans.Add(PlanProximal(0)); plans.Add(PlanProximal(1)); }
            else if (structure == "cystic_artery" && step == "clip_artery") for (int k = 0; k < 3; k++) plans.Add(PlanArtery(k));
            float best = float.NaN;
            foreach (float m in plans) { float v = m * 1000f; if (float.IsNaN(best) || Mathf.Abs(v - mm) < Mathf.Abs(best - mm)) best = v; }
            return best;
        }

        /// <summary>Degrees away from crossing the structure at a right angle (0 = square).</summary>
        float ObliqueDeg(string structure, float mm, Vector3 point)
        {
            Vector3[] path = Path(structure);
            if (path == null) return 0f;
            Vector3 a = AlongBoard(structure, Mathf.Max(0f, mm / 1000f - 0.002f)), b = AlongBoard(structure, mm / 1000f + 0.002f);
            Vector3 tangent = b - a;
            Vector3 shaft = point - PortPoint(DesiredPort(Steps[StepIndex].id));
            if (tangent.sqrMagnitude < 1e-10f || shaft.sqrMagnitude < 1e-8f) return 0f;
            return Mathf.Abs(90f - Vector3.Angle(shaft, tangent));
        }

        bool Clip(string structure, Vector3 point)
        {
            float mm = AlongMm(structure, point);
            float plan = NearestPlanMm(structure, mm);
            float oblique = ObliqueDeg(structure, mm, point);
            string accuracy = "";
            if (!float.IsNaN(plan)) { float off = Mathf.Abs(mm - plan); accuracy = off <= 3f ? ", on plan" : ", " + off.ToString("F0") + " mm off plan"; }
            if (oblique > ObliqueLimit) accuracy += ", oblique " + oblique.ToString("F0") + "°";
            Debug.Log("[LapChole] clip " + structure + " at " + mm.ToString("F1") + " mm plan=" + plan.ToString("F1") + " oblique=" + oblique.ToString("F0"));
            rig.Recorder.RecordLap("clip_applied", structure, "right", point, null, mm, oblique > ObliqueLimit ? "crossing" : "ok");
            Transform clip = LapCholeKit.ClipMesh(props, kit, 0.009f);
            clip.localPosition = point;
            Vector3[] path = Path(structure);
            Vector3 a = AlongBoard(structure, Mathf.Max(0f, mm / 1000f - 0.002f)), b = AlongBoard(structure, mm / 1000f + 0.002f);
            Vector3 along = (b - a).sqrMagnitude > 1e-10f ? (b - a).normalized : Vector3.forward;
            clip.localRotation = Quaternion.LookRotation(along, Vector3.up) * Quaternion.Euler(0f, 0f, 90f);
            LapCholeAnatomy.SetLayer(clip, LapCholeAnatomy.Layer);
            if (structure == "cystic_duct") ductClips.Add(mm);
            else if (structure == "cystic_artery") arteryClips.Add(mm);
            if (structure == "cbd") Say("Clip on the common bile duct. Major error.");
            else Say("Clip at " + mm.ToString("F0") + " mm on the " + structure.Replace('_', ' ') + accuracy + ".");
            return true;
        }

        const float ObliqueLimit = 999f; // squareness is logged only: this anatomy model puts the duct near the epigastric shaft line

        void Cut(string structure, Vector3 point, string expected, string done)
        {
            float mm = AlongMm(structure, point);
            rig.Recorder.RecordLap("structure_cut", structure, "right", point, null, mm);
            Mark(point, kit.slit, 0.004f);
            if (structure != expected) { Say("That cut is on the " + structure.Replace('_', ' ') + ". Major error."); return; }
            List<float> clips = structure == "cystic_duct" ? ductClips : arteryClips;
            int below = 0, above = 0;
            foreach (float c in clips) { if (c < mm - 2f) below++; else if (c > mm + 2f) above++; }
            if (structure == "cystic_duct" && ductotomyAt > mm) above++;
            Complete(done + (below >= 2 && above >= 1 ? " Safe: two clips proximal, one distal." : " Not enough clips around the cut (major error)."));
        }

        void Mark(Vector3 point, Material material, float size)
        {
            GameObject m = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(m.GetComponent<Collider>());
            m.transform.SetParent(props, false);
            m.transform.localPosition = point;
            m.transform.localScale = new Vector3(size, size * 0.35f, size);
            m.GetComponent<Renderer>().sharedMaterial = material;
            LapCholeAnatomy.SetLayer(m.transform, LapCholeAnatomy.Layer);
        }

        void Complete(string feedback)
        {
            string id = Steps[StepIndex].id;
            rig.Recorder.RecordLap("step_completed", id, "right", null, null, (float)rig.Exercise.ElapsedSeconds);
            Say(feedback);
            StepIndex++;
            if (StepIndex == 6) SetScopeMain(true); // from here on the surgeon works from the laparoscope image
            if (StepIndex >= Steps.Length) { StepIndex = Steps.Length - 1; rig.Exercise.Complete(); }
        }

        void Say(string text) { Feedback = text; feedbackUntil = Time.unscaledTime + 6f; Said?.Invoke(text); }

        void SetScopeMain(bool scope) { if (monitor != null) monitor.SetMainView(scope); }

        // ---------------- targets and tools ----------------
        Vector3 TargetPoint(out bool inside)
        {
            inside = StepIndex >= 6;
            switch (Steps[StepIndex].id)
            {
                case "veress": case "insufflate": case "port_umbilical": return SkinPoint(Vector2.zero);
                case "port_epigastric": return SkinPoint(PortSite("epigastric"));
                case "port_rsc_medial": return SkinPoint(PortSite("rsc_medial"));
                case "port_rsc_lateral": return SkinPoint(PortSite("rsc_lateral"));
                case "retract": return anatomy.Fundus != null ? Board.InverseTransformPoint(anatomy.Fundus.position) : ToBoard(LapCholeAnatomy.CalotCenter);
                case "dissect": return ToBoard(LapCholeAnatomy.CalotCenter);
                case "clip_duct_distal": return AlongBoard("cystic_duct", PlanDistal);
                case "ductotomy": return AlongBoard("cystic_duct", PlanDuctotomy);
                case "cholangiogram": return AlongBoard("cystic_duct", ductotomyAt > 0 ? ductotomyAt / 1000f : PlanDuctotomy);
                case "clip_duct_proximal": return AlongBoard("cystic_duct", PlanProximal(ductClips.Count >= 2 ? 1 : Mathf.Max(0, ductClips.Count - 1)));
                case "divide_duct": return AlongBoard("cystic_duct", PlanDuctCut);
                case "clip_artery": return AlongBoard("cystic_artery", PlanArtery(Mathf.Min(2, arteryClips.Count)));
                default: return AlongBoard("cystic_artery", PlanArteryCut);
            }
        }

        void UpdateTarget()
        {
            if (targetRing == null) return;
            bool running = rig.Exercise.State == ExerciseState.Running || rig.Exercise.State == ExerciseState.Ready;
            Vector3 p = TargetPoint(out bool inside);
            float pulse = 1f + 0.25f * Mathf.Sin(Time.unscaledTime * 6f);
            targetRing.gameObject.SetActive(running && !inside && rig.desktopPreview && !simHands);
            targetGlow.gameObject.SetActive(running && inside && (!toolTip.HasValue || Vector3.Distance(toolTip.Value, p) > 0.006f));
            targetRing.localPosition = p + Vector3.up * 0.002f;
            targetRing.localRotation = Quaternion.Euler(90f, 0f, 0f);
            targetRing.localScale = Vector3.one * 0.012f * pulse;
            targetGlow.localPosition = p;
            targetGlow.localScale = Vector3.one * (Steps[StepIndex].id == "dissect" ? 0.014f : 0.0035f) * pulse;
            // The active tool follows the target region inside, entering from its port.
            foreach (LapTool t in Tools()) t.SetVisible(false);
            if (!inside || anatomy == null) return;
            LapTool desired = ToolFor(Steps[StepIndex].id);
            string desiredPort = DesiredPort(Steps[StepIndex].id);
            if (equippedTool == null) { equippedTool = desired; equippedPortId = desiredPort; }
            else if (((rig.desktopPreview && !simHands) || autopilot) && equippedTool != desired)
            { equippedTool = desired; equippedPortId = desiredPort; exchangePending = false; }
            else if (equippedTool != desired && !exchangePending) exchangePending = true;

            // On headset, no tracked grip means no instrument. In particular, never place a fallback model at
            // the anatomy target: it looks as though a clipper materialized inside the patient.
            if ((!rig.desktopPreview || simHands) && !toolTip.HasValue) return;
            LapTool tool = exchangePending ? equippedTool : desired;
            string portId = exchangePending ? equippedPortId : desiredPort;
            Vector3 port = PortPoint(portId);
            Vector3 tip = toolTip ?? (p + (port - p).normalized * 0.012f);
            tool.SetVisible(true);
            tool.Pose(Board, port, tip, prevAct, toolOutside, toolGripRotation);
            if (fundusHeld && Steps[StepIndex].id != "retract" && anatomy.Fundus != null)
            {
                assistantGrasper.SetVisible(true);
                assistantGrasper.Pose(Board, PortPoint("rsc_lateral"), Board.InverseTransformPoint(anatomy.Fundus.position), true);
            }
        }

        LapTool ToolFor(string step) =>
            step == "retract" ? grasper : step == "dissect" ? dissector : step == "cholangiogram" ? catheter
            : step.StartsWith("clip") ? clipper : scissors;

        static string DesiredPort(string step) => step == "retract" ? "rsc_lateral" : "epigastric";
        string ToolLabel(LapTool tool) => tool == grasper ? "grasper" : tool == dissector ? "Maryland dissector"
            : tool == clipper ? "clip applier" : tool == scissors ? "scissors" : "cholangiogram catheter";

        // ---------------- guidance UI ----------------
        void OnGUI()
        {
            if (!Active || !rig.desktopPreview) return;
            if (PanelUp) { if (Steps[StepIndex].id == "cholangiogram" && contrast > 0f) DrawCholangiogram(); return; }
            if (panel == null)
            {
                panel = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 13, wordWrap = true, padding = new RectOffset(12, 12, 10, 10) };
                title = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold, wordWrap = true };
                small = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true, richText = true };
            }
            float w = 360f, x = 12f, y = 44f;
            GUI.Box(new Rect(x, y, w, 520f), "", panel);
            StepInfo s = Steps[StepIndex];
            bool done = rig.Exercise.State == ExerciseState.Completed;
            GUI.Label(new Rect(x + 12, y + 8, w - 24, 26), done ? "Procedure complete" : "Step " + (StepIndex + 1) + " of " + Steps.Length + " · " + s.title, title);
            float yy = y + 38;
            if (!done)
            {
                GUI.Label(new Rect(x + 12, yy, w - 24, 40), "<b>What:</b> " + s.what, small); yy += 40;
                GUI.Label(new Rect(x + 12, yy, w - 24, 46), "<b>How:</b> " + (rig.Exercise.State == ExerciseState.Ready ? "Press ENTER to begin, then: " : "") + s.mouse, small); yy += 48;
                string next = StepIndex + 1 < Steps.Length ? Steps[StepIndex + 1].title : "Finish";
                GUI.Label(new Rect(x + 12, yy, w - 24, 20), "<b>Next:</b> " + next, small); yy += 22;
                if (EntryReadout.Length > 0) { GUI.Label(new Rect(x + 12, yy, w - 24, 36), "<b>Tool:</b> " + EntryReadout, small); yy += 36; }
                if (s.id == "insufflate") { GUI.Label(new Rect(x + 12, yy, w - 24, 20), "<b>Pressure:</b> " + Pressure.ToString("F1") + " mmHg (target 12-15)", small); yy += 22; }
                if (s.id == "port_umbilical" && placedUmbilical) { GUI.Label(new Rect(x + 12, yy, w - 24, 20), "<b>Balloon:</b> " + (balloon * 100f).ToString("F0") + "%", small); yy += 22; }
                if (s.id == "dissect") { GUI.Label(new Rect(x + 12, yy, w - 24, 20), "<b>Veil remaining:</b> " + (veil * 100f).ToString("F0") + "%", small); yy += 22; }
                if (s.id == "cholangiogram" && catheterIn) { GUI.Label(new Rect(x + 12, yy, w - 24, 20), "<b>Contrast:</b> " + (contrast * 100f).ToString("F0") + "%", small); yy += 22; }
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
            if (s.id == "cholangiogram" && contrast > 0f) DrawCholangiogram();
        }

        void DrawCholangiogram()
        {
            if (chole == null) chole = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            var tree = new List<Vector3>();
            for (int i = 0; i <= 40; i++) { float t = i / 40f; tree.Add(new Vector3(0.5f + 0.02f * Mathf.Sin(t * 6f), 0.15f + 0.6f * t, t)); }
            for (int i = 0; i <= 20; i++) { float t = i / 20f; tree.Add(new Vector3(0.5f - 0.22f * t, 0.75f + 0.12f * t, 0.6f + 0.4f * t)); tree.Add(new Vector3(0.5f + 0.2f * t, 0.75f + 0.15f * t, 0.6f + 0.4f * t)); }
            LapCholeKit.DrawCholangiogram(chole, tree, new Vector2(0.62f, 0.4f), contrast, false, false);
            GUI.DrawTexture(new Rect(Screen.width - 280, Screen.height - 300, 256, 256), chole);
        }

        void UpdateVrPanel()
        {
            if (rig.desktopPreview || monitor == null || monitor.WorldScreen == null) return;
            if (PanelUp)
            {
                if (vrPanel != null) vrPanel.gameObject.SetActive(false);
                if (diagramPanel != null) diagramPanel.gameObject.SetActive(false);
                return;
            }
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
            if (narrator == null) narrator = GetComponent<LapCholeNarrator>();
            string coach = narrator != null ? narrator.Caption : "";
            StepInfo s = Steps[StepIndex];
            bool done = rig.Exercise.State == ExerciseState.Completed;
            string next = StepIndex + 1 < Steps.Length ? Steps[StepIndex + 1].title : "Finish";
            vrPanel.text = done ? "Procedure complete" :
                "Step " + (StepIndex + 1) + " of " + Steps.Length + " · " + s.title + "\n\nWhat: " + Wrap(s.what) + "\n\nHow: " +
                (rig.Exercise.State == ExerciseState.Ready ? "Press Start on the HUD, then: " : "") + Wrap(s.vr) + "\n\nNext: " + next +
                (s.id == "insufflate" ? "\nPressure: " + Pressure.ToString("F1") + " mmHg" : "") + (Feedback.Length > 0 ? "\n\n✓ " + Wrap(Feedback) : "")
                + (EntryReadout.Length > 0 ? "\n\nTool: " + Wrap(EntryReadout) : "")
                + (coach.Length > 0 ? "\n\nCoach: " + Wrap(coach) : "");
            if (diagramPanel != null) diagramPanel.text = OperationDiagram(s.id);
        }

        static string PortLabel(string id) => id == "rsc_lateral" ? "lateral subcostal trocar" : "epigastric trocar";

        string OperationDiagram(string step)
        {
            string tool = step == "veress" || step == "insufflate" ? "VERESS NEEDLE" : step.StartsWith("port_") ? "TROCAR"
                : step == "retract" ? "GRASPER" : step == "dissect" ? "MARYLAND DISSECTOR" : step.StartsWith("clip") ? "CLIP APPLIER"
                : step == "cholangiogram" ? "CATHETER" : "SCISSORS";
            string map = StepIndex < 6
                ? "BODY MAP\n       xiphoid\n          +  epigastric\n     + medial     + lateral\n          O  navel"
                : "FULCRUM\nHAND ===== (TROCAR) ----- TIP\n             abdominal wall\n\nYellow = anatomy focus, not a button";
            string safety = StepIndex < 6 ? "Contact skin • centre shaft • squeeze" : (alignmentCue.Length > 0 ? alignmentCue : "Move the tool itself; keep the shaft in its trocar");
            return "CURRENT TOOL  " + tool + "\n\n" + map + "\n\n" + safety;
        }

        static string Wrap(string text, int width = 46)
        {
            var sb = new System.Text.StringBuilder(); int col = 0;
            foreach (string word in text.Split(' ')) { if (col + word.Length > width) { sb.Append('\n'); col = 0; } sb.Append(word).Append(' '); col += word.Length + 1; }
            return sb.ToString().TrimEnd();
        }

        // ---------------- simulated hands (editor verification of the headset path) ----------------
        // Drives the same input the tracked right hand gives (board-local pinch pose + pinch) through all 15 steps.
        // sloppy adds mistakes: an entry off the mark, a wrong trocar angle, a trocar pushed too deep, an off-plan clip.
        public IEnumerator HandDemo(bool sloppy)
        {
            simGrip = SkinPoint(Vector2.zero) + new Vector3(0.06f, 0.3f, -0.12f); simForward = Vector3.down;
            simHands = true; simPinch = false; simTracked = true;
            if (rig.Exercise.State != ExerciseState.Running) { rig.Exercise.ResetExercise(); yield return null; rig.Exercise.Begin(); }
            yield return null; yield return null;
            DemoLog("start sloppy=" + sloppy);
            simGrip = SkinPoint(Vector2.zero) + new Vector3(0.06f, 0.3f, -0.12f); simForward = Vector3.down;
            float deadline = Time.unscaledTime + 400f;
            while (rig.Exercise.State == ExerciseState.Running && Time.unscaledTime < deadline)
            {
                string id = Steps[StepIndex].id; int before = StepIndex;
                DemoLog("step " + (StepIndex + 1) + " " + id);
                if (id == "insufflate")
                {
                    simPinch = true;
                    for (float t = 0; Pressure < 13.4f && StepIndex == before && t < 10f; t += Time.deltaTime) yield return null;
                    simPinch = false;
                }
                else if (EntryStepNow())
                {
                    yield return null; yield return null;
                    LapCholeEntry.Spec spec = entry.Current;
                    Vector3 site = entry.Site, axis = entry.Ideal;
                    yield return DemoPickUp(id);
                    float mid = (spec.minDepth + spec.maxDepth) * 0.5f;
                    if (sloppy && id == "veress")
                    {
                        Vector3 off = SkinPoint(new Vector2(site.x - 0.03f, site.z));
                        yield return DemoInsert(spec, off, axis, 10f); // 30 mm toward the head: the skin should stop it
                        yield return DemoLift(spec);
                    }
                    if (sloppy && id == "port_epigastric")
                    {
                        yield return DemoInsert(spec, site, Quaternion.AngleAxis(40f, Vector3.forward) * axis, 10f); // 40 degrees off
                        yield return DemoLift(spec);
                    }
                    if (sloppy && id == "port_rsc_lateral")
                    {
                        Vector3 dir = -axis.normalized;
                        Vector3 Hover(float dxMm) => site + new Vector3(dxMm / 1000f, 0f, 0f) - dir * (spec.length + 0.03f);
                        yield return DemoMove(new Vector3(simGrip.x, simGrip.y + 0.08f, simGrip.z), simForward, 0.3f);
                        yield return DemoMove(Hover(45f), dir, 0.8f);
                        yield return new WaitForSeconds(3.2f);          // the coach gives the trainee a few seconds
                        yield return DemoMove(Hover(85f), dir, 2.2f);   // drifting the wrong way: expect "Other way"
                        yield return new WaitForSeconds(1.5f);
                        yield return DemoMove(Hover(25f), dir, 2.5f);   // coming back: expect "Good, closer"
                        yield return DemoMove(Hover(0f), dir, 1.5f);    // lined up: expect "Lined up, push in"
                        yield return new WaitForSeconds(2.5f);
                    }
                    if (sloppy && id == "port_rsc_medial")
                    {
                        yield return DemoInsert(spec, site, axis, spec.dangerDepth + 6f); // through the wall and on: too deep
                        yield return DemoPush(-(spec.dangerDepth + 6f - mid) / 1000f, spec);
                    }
                    else yield return DemoInsert(spec, site, axis, mid);
                    for (float t = 0; StepIndex == before && !placedUmbilical && t < 3f; t += Time.deltaTime) yield return null;
                    if (id == "port_umbilical" && placedUmbilical)
                    {
                        simPinch = true;
                        for (float t = 0; StepIndex == before && t < 6f; t += Time.deltaTime) yield return null;
                        simPinch = false;
                    }
                    yield return DemoMove(simGrip + Vector3.up * 0.1f, simForward, 0.3f);
                }
                else yield return DemoInside(id, sloppy);
                for (float t = 0; StepIndex == before && rig.Exercise.State == ExerciseState.Running && t < 3f; t += Time.deltaTime) yield return null;
                if (StepIndex == before && rig.Exercise.State == ExerciseState.Running) { DemoLog("STUCK at " + id + " | " + Feedback + " | " + EntryReadout); break; }
                DemoLog("done " + id + " | " + Feedback);
                yield return new WaitForSeconds(0.4f);
            }
            DemoLog("end state=" + rig.Exercise.State + " major=" + rig.Recorder.LapMajor + " minor=" + rig.Recorder.LapMinor);
            simHands = false; simPinch = false;
        }

        IEnumerator DemoPickUp(string step)
        {
            if (toolInHand) yield break;
            Vector3 slot = TraySlot(step);
            yield return DemoMove(new Vector3(simGrip.x, Mathf.Max(simGrip.y, slot.y + 0.2f), simGrip.z), simForward, 0.3f);
            yield return DemoMove(slot + Vector3.up * 0.05f, Vector3.down, 0.7f);
            yield return DemoMove(slot, Vector3.down, 0.3f);
            yield return DemoPulse();
            DemoLog("picked up " + step + " inHand=" + toolInHand);
            yield return DemoMove(slot + Vector3.up * 0.2f, Vector3.down, 0.4f);
        }

        IEnumerator DemoInsert(LapCholeEntry.Spec spec, Vector3 site, Vector3 outward, float depthMm)
        {
            Vector3 dir = -outward.normalized;
            Vector3 gripAbove = site - dir * (spec.length + 0.025f);
            // Lift clear of the patient before travelling to the next site, as a hand would.
            float clear = Mathf.Max(simGrip.y, gripAbove.y) + 0.08f;
            yield return DemoMove(new Vector3(simGrip.x, clear, simGrip.z), simForward, 0.35f);
            yield return DemoMove(new Vector3(gripAbove.x, clear, gripAbove.z), dir, 0.6f);
            yield return DemoMove(gripAbove, dir, 0.4f);
            yield return new WaitForSeconds(0.2f);
            yield return DemoPush(0.025f + depthMm / 1000f, spec);
            Vector3 t = simGrip + simForward.normalized * spec.length;
            DemoLog("pushed: tip y=" + t.y.ToString("F3") + " skin=" + dressing.Skin(t.x, t.z).ToString("F3") + " inserted=" + entry.Inserted + " depth=" + entry.Depth.ToString("F0") + " | " + entry.Readout);
            yield return new WaitForSeconds(0.9f);
        }

        // Move the grip along the current forward by metres (negative pulls back), at a steady hand speed.
        IEnumerator DemoPush(float metres, LapCholeEntry.Spec spec)
        {
            Vector3 start = simGrip, dir = simForward.normalized;
            float duration = Mathf.Abs(metres) / 0.03f;
            for (float t = 0; t < duration; t += Time.deltaTime) { simGrip = start + dir * metres * (t / duration); yield return null; }
            simGrip = start + dir * metres;
            yield return new WaitForSeconds(0.7f);
        }

        IEnumerator DemoLift(LapCholeEntry.Spec spec)
        {
            yield return DemoMove(simGrip - simForward.normalized * 0.05f, simForward, 0.4f);
            yield return new WaitForSeconds(1.2f);
        }

        IEnumerator DemoMove(Vector3 grip, Vector3 forward, float seconds)
        {
            Vector3 g0 = simGrip, f0 = simForward;
            for (float t = 0; t < seconds; t += Time.deltaTime)
            {
                float u = Mathf.SmoothStep(0f, 1f, t / seconds);
                simGrip = Vector3.Lerp(g0, grip, u); simForward = Vector3.Slerp(f0, forward, u);
                yield return null;
            }
            simGrip = grip; simForward = forward;
        }

        // Grip pose that puts the working tip at target through the trocar (the procedure's fulcrum model).
        void GripFor(string portId, Vector3 target, out Vector3 grip, out Vector3 forward)
        {
            Vector3 port = PortPoint(portId);
            Vector3 dir = (target - port).normalized;
            float handle = Mathf.Clamp(0.31f - Vector3.Distance(target, port), 0.12f, 0.29f);
            grip = port - dir * handle; forward = dir;
        }

        IEnumerator DemoReach(Vector3 target, float seconds = 0.6f)
        {
            if (exchangePending && !string.IsNullOrEmpty(equippedPortId))
            {
                Vector3 port = PortPoint(equippedPortId);
                Vector3 outward = (simGrip - port).normalized;
                yield return DemoMove(port + outward * 0.295f, -outward, 0.5f);
                for (float t = 0; exchangePending && t < 2f; t += Time.deltaTime) yield return null;
                if (exchangePending) DemoLog("exchange did not complete");
            }
            GripFor(DesiredPort(Steps[StepIndex].id), target, out Vector3 grip, out Vector3 forward);
            yield return DemoMove(grip, forward, seconds);
        }

        IEnumerator DemoPulse()
        {
            simPinch = true; yield return new WaitForSeconds(0.15f);
            simPinch = false; yield return new WaitForSeconds(0.35f);
        }

        IEnumerator DemoInside(string id, bool sloppy)
        {
            int before = StepIndex;
            switch (id)
            {
                case "dissect":
                    {
                        Vector3 c = TargetPoint(out _);
                        yield return DemoReach(c);
                        simPinch = true;
                        for (int k = 0; StepIndex == before && k < 2500; k++)
                        {
                            Vector3 wob = c + new Vector3(Mathf.Sin(k * 0.21f), 0f, Mathf.Cos(k * 0.27f)) * 0.007f;
                            GripFor(DesiredPort(id), wob, out simGrip, out simForward);
                            yield return null;
                        }
                        simPinch = false;
                        break;
                    }
                case "cholangiogram":
                    yield return DemoReach(TargetPoint(out _));
                    yield return DemoPulse();
                    simPinch = true;
                    for (float t = 0; StepIndex == before && t < 6f; t += Time.deltaTime) yield return null;
                    simPinch = false;
                    break;
                case "clip_duct_distal":
                    if (sloppy) { yield return DemoReach(AlongBoard("cystic_duct", PlanDistal - 0.007f)); yield return DemoPulse(); break; }
                    yield return DemoReach(TargetPoint(out _)); yield return DemoPulse();
                    break;
                case "clip_duct_proximal":
                    for (int k = 0; k < 2 && StepIndex == before; k++) { yield return DemoReach(AlongBoard("cystic_duct", PlanProximal(k)), 0.4f); yield return DemoPulse(); }
                    break;
                case "clip_artery":
                    if (sloppy)
                    {
                        yield return DemoReach(AlongBoard("cystic_artery", PlanArtery(0)), 0.4f);
                        yield return new WaitForSeconds(3.2f);
                        yield return DemoReach(AlongBoard("cbd", Length(anatomy.CommonBileDuct) * 0.5f), 0.6f);   // expect the bile duct warning
                        yield return new WaitForSeconds(2.5f);
                        yield return DemoReach(AlongBoard("cystic_duct", PlanProximal(0) * 0.6f), 0.6f);          // expect "that's the cystic duct"
                        yield return new WaitForSeconds(2.5f);
                        yield return DemoReach(AlongBoard("cystic_artery", PlanArtery(0)), 0.6f);                 // expect "right there"
                        yield return new WaitForSeconds(3.5f);
                    }
                    for (int k = 0; k < 3 && StepIndex == before; k++) { yield return DemoReach(AlongBoard("cystic_artery", PlanArtery(k)), 0.4f); yield return DemoPulse(); }
                    break;
                default:
                    yield return DemoReach(TargetPoint(out _));
                    yield return DemoPulse();
                    break;
            }
        }

        void DemoLog(string message)
        {
            Debug.Log("[HandDemo] " + message);
            try { System.IO.File.AppendAllText(System.IO.Path.Combine(PreviewAutopilot.BridgeFolder, "handdemo.log"), System.DateTime.Now.ToString("HH:mm:ss.fff") + " " + message + "\n"); }
            catch (System.IO.IOException) { }
        }

        // ---------------- autopilot (desktop demo and verification) ----------------
        bool autopilot;
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
                Vector3 p = TargetPoint(out bool inside);
                if (inside) { toolTip = p + (SkinPoint(PortSite("epigastric")) - p).normalized * 0.03f; for (float t = 0; t < 0.6f; t += Time.deltaTime) { toolTip = Vector3.Lerp(toolTip.Value, p, 0.08f); yield return null; } }
                switch (id)
                {
                    case "insufflate":
                        while (Pressure < 13.5f) { Drive(p, null, true, false, true); yield return null; }
                        Drive(p, null, true, false, false); break;
                    case "port_umbilical":
                        Drive(p, null, true, true, false); yield return null;
                        while (StepIndex == before) { Drive(p, null, true, false, true); yield return null; }
                        break;
                    case "retract": Drive(p, "fundus", true, true, false); break;
                    case "dissect":
                        for (int k = 0; StepIndex == before && k < 2000; k++)
                        { Vector3 wob = p + new Vector3(Mathf.Sin(k * 0.3f), 0f, Mathf.Cos(k * 0.37f)) * 0.008f; Drive(wob, "veil", true, false, true); yield return null; }
                        break;
                    case "cholangiogram":
                        Drive(p, "cystic_duct", true, true, false); yield return null;
                        while (StepIndex == before) { Drive(p, "cystic_duct", true, false, true); yield return null; }
                        break;
                    case "clip_duct_proximal":
                        for (int k = 0; k < 2 && StepIndex == before; k++)
                        {
                            string target = unsafeRun && k == 0 ? "cbd" : "cystic_duct";
                            Vector3 at = target == "cbd" ? AlongBoard("cbd", Length(anatomy.CommonBileDuct) * 0.5f) : AlongBoard("cystic_duct", PlanProximal(k));
                            Drive(at, target, true, true, false); yield return new WaitForSeconds(0.5f);
                        }
                        if (StepIndex == before) Drive(AlongBoard("cystic_duct", PlanProximal(1) * 0.8f), "cystic_duct", true, true, false);
                        break;
                    case "clip_artery":
                        for (int k = 0; k < 3; k++) { Drive(AlongBoard("cystic_artery", PlanArtery(k)), "cystic_artery", true, true, false); yield return new WaitForSeconds(0.5f); }
                        break;
                    default:
                        string st = id == "clip_duct_distal" || id == "ductotomy" || id == "divide_duct" ? "cystic_duct" : id == "divide_artery" ? "cystic_artery" : null;
                        Drive(p, st, true, true, false); break;
                }
                yield return new WaitForSeconds(0.9f);
                if (StepIndex == before && rig.Exercise.State == ExerciseState.Running) { Say("Autopilot stuck at " + id); break; }
            }
            toolTip = null;
            autopilot = false;
        }
    }
}
