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
                mouse = "Guide the needle onto the navel and click only when the shaft is centred.", vr = "Hold the Veress needle like a pencil. Centre its tip on the navel, keep the shaft nearly perpendicular to the abdominal wall, then squeeze." },
            new StepInfo { id = "insufflate", title = "Insufflation", what = "Insufflate the abdomen with CO2 to 12-15 mmHg.",
                mouse = "Hold SPACE to add CO2; release between 12 and 15 mmHg.", vr = "Keep the needle steady and hold the trigger to add CO2. Release at 12-15 mmHg; do not chase the marker." },
            new StepInfo { id = "port_umbilical", title = "Umbilical port", what = "Place the umbilical balloon trocar, inflate its balloon, seal, insert the scope.",
                mouse = "Seat the trocar at the navel, then hold SPACE to inflate its retention balloon.", vr = "Seat the trocar through the same umbilical entry while keeping its cannula aligned. Squeeze once to place it, then hold to inflate the balloon." },
            new StepInfo { id = "port_epigastric", title = "Epigastric port", what = "Place the 10 mm trocar just below the breastbone, aimed at the right upper quadrant.",
                mouse = "Place the trocar below the breastbone, aimed toward the right upper quadrant.", vr = "Touch skin below the breastbone with the trocar tip, aim toward the right upper quadrant, then squeeze only when the shaft guide is centred." },
            new StepInfo { id = "port_rsc_medial", title = "Right subcostal port (medial)", what = "Place a 5 mm trocar below the right ribs, in line with the middle of the collarbone.",
                mouse = "Place the trocar below the right ribs on the mid-clavicular line.", vr = "Use the trocar: contact skin below the right ribs, centre the cannula on the guide, then squeeze without sweeping through the body." },
            new StepInfo { id = "port_rsc_lateral", title = "Right subcostal port (lateral)", what = "Place a 5 mm trocar further out on the right flank.",
                mouse = "Place the trocar on the right flank while preserving instrument spacing.", vr = "Use the trocar at the lateral guide. Keep clear of the medial port, contact skin, align the shaft, then squeeze." },
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
                mouse = "Click the two glowing bands on the cystic duct below the cut.", vr = "Clip each glowing band below the cut with the trigger." },
            new StepInfo { id = "divide_duct", title = "Divide the cystic duct", what = "Cut the cystic duct between the proximal clips and the ductotomy.",
                mouse = "Click the glowing gap between the clips.", vr = "Bring the scissors to the glowing gap and pull the trigger." },
            new StepInfo { id = "clip_artery", title = "Clip the cystic artery", what = "Place three clips on the cystic artery: two proximal, one distal.",
                mouse = "Click the three glowing bands on the salmon cystic artery.", vr = "Clip each glowing band on the artery with the trigger." },
            new StepInfo { id = "divide_artery", title = "Divide the cystic artery", what = "Cut the cystic artery between the clips.",
                mouse = "Click the glowing gap on the artery.", vr = "Bring the scissors to the glowing gap and pull the trigger." },
        };

        public PegTransferRig rig;
        public int StepIndex { get; private set; }
        public float Pressure { get; private set; }
        public string Feedback { get; private set; } = "";
        public bool Active => rig != null && rig.ExerciseId == LapCholeAnatomy.ExerciseId && dressing != null;

        LapCholeDressing dressing;
        LapCholeAnatomy anatomy;
        LaparoscopeMonitor monitor;
        DesktopPreview preview;
        LapCholeKit kit;
        Transform props;
        LapTool grasper, dissector, clipper, scissors, catheter;
        Transform targetRing, targetGlow;
        readonly List<float> ductClips = new List<float>(), arteryClips = new List<float>();
        float ductotomyAt = -1f, ductCutAt = -1f, veil = 1f, balloon, contrast, feedbackUntil, prevPressureHold;
        bool placedUmbilical, catheterIn, fundusHeld, prevAct, wasRunning, vrPanelBuilt, shaftAligned = true, proxyToolsVisible = true;
        Vector3 lastDissect;
        Vector3? toolTip;
        float toolOutside = 0.26f;
        string alignmentCue = "";
        Texture2D chole;
        TextMesh vrPanel, diagramPanel;
        GUIStyle panel, title, small;

        // ---------------- lifecycle ----------------
        void Update()
        {
            if (rig == null) rig = GetComponent<PegTransferRig>();
            if (rig == null) return;
            if (rig.ExerciseId != LapCholeAnatomy.ExerciseId) { SetProxyToolsVisible(true); return; }
            if (dressing == null) { dressing = FindFirstObjectByType<LapCholeDressing>(); if (dressing == null) return; anatomy = dressing.Anatomy; }
            if (monitor == null) monitor = FindFirstObjectByType<LaparoscopeMonitor>();
            if (preview == null) preview = GetComponent<DesktopPreview>();
            if (kit == null) BuildProps();
            SetProxyToolsVisible(StepIndex < 6);
            bool running = rig.Exercise.State == ExerciseState.Running;
            if (running && !wasRunning && rig.Exercise.ElapsedSeconds < 0.5f) ResetProcedure();
            wasRunning = running;
            UpdateTarget();
            UpdateVrPanel();
            Speak(running);
            if (!running || autopilot) return;
            Read(out Vector3 point, out string structure, out bool hasPoint, out bool act, out bool hold);
            bool pressed = act && !prevAct;
            prevAct = act;
            Drive(point, structure, hasPoint, pressed, hold);
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
            StepIndex = 0; Pressure = 0; balloon = 0; contrast = 0; veil = 1f; ductotomyAt = ductCutAt = -1f;
            placedUmbilical = catheterIn = fundusHeld = false;
            ductClips.Clear(); arteryClips.Clear();
            if (props != null) Destroy(props.gameObject);
            if (kit != null) kit.Destroy();
            kit = null; BuildProps();
            dressing.ShowStage(0);
            dressing.SetInsufflation(0f);
            if (anatomy != null) anatomy.SetPeritoneumCover(1f);
            SetScopeMain(false);
            Feedback = ""; toolTip = null; shaftAligned = true; alignmentCue = "";
        }

        void BuildProps()
        {
            kit = new LapCholeKit();
            props = new GameObject("Lap chole procedure props").transform;
            props.SetParent(rig.board.transform, false);
            grasper = new LapTool(props, LapToolKind.Grasper, kit);
            dissector = new LapTool(props, LapToolKind.Dissector, kit);
            clipper = new LapTool(props, LapToolKind.ClipApplier, kit);
            scissors = new LapTool(props, LapToolKind.Scissors, kit);
            catheter = new LapTool(props, LapToolKind.Catheter, kit);
            foreach (LapTool t in Tools()) t.SetVisible(false);
            var ring = new GameObject("Target ring", typeof(MeshFilter), typeof(MeshRenderer));
            ring.GetComponent<MeshFilter>().sharedMesh = kit.Ring;
            ring.GetComponent<MeshRenderer>().sharedMaterial = kit.glowSkin;
            targetRing = ring.transform; targetRing.SetParent(props, false);
            GameObject glow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(glow.GetComponent<Collider>());
            glow.GetComponent<Renderer>().sharedMaterial = kit.glowInside;
            targetGlow = glow.transform; targetGlow.SetParent(props, false);
            if (anatomy != null) LapCholeAnatomy.SetLayer(targetGlow, LapCholeAnatomy.Layer);
        }

        IEnumerable<LapTool> Tools() { yield return grasper; yield return dissector; yield return clipper; yield return scissors; yield return catheter; }

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
            if (rig.desktopPreview)
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
            if (right == null || right.tip == null || !right.TrackingValid) return;
            act = right.Closed; hold = act;
            Vector3 tip = Board.InverseTransformPoint(right.tip.position);
            if (!inside)
            {
                float skin = dressing.Skin(tip.x, tip.z);
                hasPoint = !float.IsNaN(skin) && tip.y < skin + 0.04f;
                point = new Vector3(tip.x, float.IsNaN(skin) ? tip.y : skin, tip.z);
                return;
            }
            // The trocar is a real fulcrum. The visible instrument and its interaction point both stay on the line
            // from the user's hand through that trocar; reaching through the abdominal wall anywhere else is rejected.
            string portId = StepIndex == 6 ? "rsc_lateral" : "epigastric";
            Vector3 port = SkinPoint(PortSite(portId));
            Vector3 handle = tip - port;
            toolOutside = Mathf.Clamp(handle.magnitude, 0.12f, 0.42f);
            Vector3 outward = dressing.PortAxis(portId).normalized;
            float shaftAngle = Vector3.Angle(outward, handle.normalized);
            shaftAligned = handle.magnitude > 0.09f && shaftAngle <= 52f;
            alignmentCue = shaftAligned ? "Shaft centred through " + PortLabel(portId) : "Re-centre the handle over the " + PortLabel(portId) + " — the shaft cannot pass through the body";
            Vector3 dir = handle.sqrMagnitude > 1e-6f ? -handle.normalized : -outward;
            Vector3 inner = port + dir * Mathf.Clamp(0.31f - handle.magnitude, 0.018f, 0.19f);
            toolTip = inner;
            float contactRadius = Steps[StepIndex].id == "retract" ? 0.014f : Steps[StepIndex].id == "dissect" ? 0.012f : 0.009f;
            hasPoint = shaftAligned && NearestStructure(inner, out point, out structure, contactRadius);
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
                        if (balloon >= 1f) PlacePort("umbilical", Vector2.zero, SkinPoint(Vector2.zero), -1);
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

        bool Clip(string structure, Vector3 point)
        {
            float mm = AlongMm(structure, point);
            rig.Recorder.RecordLap("clip_applied", structure, "right", point, null, mm, "ok");
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
            else Say("Clip at " + mm.ToString("F0") + " mm on the " + structure.Replace('_', ' ') + ".");
            return true;
        }

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

        void Say(string text) { Feedback = text; feedbackUntil = Time.unscaledTime + 6f; }

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
            targetRing.gameObject.SetActive(running && !inside);
            targetGlow.gameObject.SetActive(running && inside && (!toolTip.HasValue || Vector3.Distance(toolTip.Value, p) > 0.006f));
            targetRing.localPosition = p + Vector3.up * 0.002f;
            targetRing.localRotation = Quaternion.Euler(90f, 0f, 0f);
            targetRing.localScale = Vector3.one * 0.012f * pulse;
            targetGlow.localPosition = p;
            targetGlow.localScale = Vector3.one * (Steps[StepIndex].id == "dissect" ? 0.014f : 0.0035f) * pulse;
            // The active tool follows the target region inside, entering from its port.
            foreach (LapTool t in Tools()) t.SetVisible(false);
            if (!inside || anatomy == null) return;
            LapTool tool = ToolFor(Steps[StepIndex].id);
            Vector3 port = SkinPoint(PortSite(Steps[StepIndex].id == "retract" ? "rsc_lateral" : "epigastric"));
            Vector3 tip = toolTip ?? (p + (port - p).normalized * 0.012f);
            tool.SetVisible(true);
            tool.Pose(Board, port, tip, prevAct, toolOutside);
            if (fundusHeld && Steps[StepIndex].id != "retract" && anatomy.Fundus != null)
            {
                grasper.SetVisible(true);
                grasper.Pose(Board, SkinPoint(PortSite("rsc_lateral")), Board.InverseTransformPoint(anatomy.Fundus.position), true);
            }
        }

        LapTool ToolFor(string step) =>
            step == "retract" ? grasper : step == "dissect" ? dissector : step == "cholangiogram" ? catheter
            : step.StartsWith("clip") ? clipper : scissors;

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
            vrPanel.text = done ? "Procedure complete" :
                "Step " + (StepIndex + 1) + " of " + Steps.Length + " · " + s.title + "\n\nWhat: " + Wrap(s.what) + "\n\nHow: " +
                (rig.Exercise.State == ExerciseState.Ready ? "Press Start on the HUD, then: " : "") + Wrap(s.vr) + "\n\nNext: " + next +
                (s.id == "insufflate" ? "\nPressure: " + Pressure.ToString("F1") + " mmHg" : "") + (Feedback.Length > 0 ? "\n\n✓ " + Wrap(Feedback) : "");
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
