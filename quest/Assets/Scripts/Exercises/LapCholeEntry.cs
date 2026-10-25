using System;
using System.Collections.Generic;
using UnityEngine;

namespace Jeremy
{
    // Abdominal entry with real hand-held tools (headset, and the editor's simulated-hand verification):
    // the Veress needle and the four trocars are modelled tools held at the pinch. Entry is physical: the tip must
    // cross the skin at the marked site with the shaft near the planned axis; once through, the tool pivots at the
    // skin (it cannot slide sideways through the wall), depth is measured along the shaft, and fascia / peritoneum
    // give a "pop". The tool is placed when it is held still inside the target depth window.
    // Everything is board-local (x toward the feet, y up, +z the patient's right).
    public sealed class LapCholeEntry
    {
        public enum ToolKind { Veress, Trocar12, Trocar10, Trocar5 }

        public sealed class Spec
        {
            public ToolKind tool;
            public float length;        // grip (pinch) to tip, metres
            public float siteTolMm;     // entry must be this close to the mark
            public float angleTolDeg;   // shaft within this of the planned axis
            public float popA, popB;    // depths (mm) of the fascia and peritoneum pops; popB < 0 = one pop
            public float minDepth, maxDepth, dangerDepth; // placed when held in [min, max]; beyond danger = too deep
            public string popAText, popBText, name;
        }

        public struct Outcome
        {
            public Vector3 entry, outward;
            public float mm, angle, depth;
            public string tilt;
        }

        public static Spec SpecFor(string step)
        {
            switch (step)
            {
                case "veress":
                    return new Spec { tool = ToolKind.Veress, name = "Veress needle", length = 0.13f, siteTolMm = 15f, angleTolDeg = 35f,
                        popA = 9f, popB = 18f, minDepth = 22f, maxDepth = 38f, dangerDepth = 45f,
                        popAText = "First pop: through the fascia.", popBText = "Second pop: peritoneum. The safety stylet springs forward. You're in." };
                case "port_umbilical":
                    return new Spec { tool = ToolKind.Trocar12, name = "12 mm balloon trocar", length = 0.15f, siteTolMm = 10f, angleTolDeg = 30f,
                        popA = 14f, popB = 26f, minDepth = 32f, maxDepth = 50f, dangerDepth = 60f,
                        popAText = "Fascia giving.", popBText = "Through the peritoneum. Stop pushing." };
                case "port_epigastric":
                    return new Spec { tool = ToolKind.Trocar10, name = "10 mm trocar", length = 0.15f, siteTolMm = 15f, angleTolDeg = 32f,
                        popA = 12f, popB = 24f, minDepth = 30f, maxDepth = 48f, dangerDepth = 58f,
                        popAText = "Fascia giving.", popBText = "Through. The tip is in the abdomen." };
                default:
                    return new Spec { tool = ToolKind.Trocar5, name = "5 mm trocar", length = 0.14f, siteTolMm = 15f, angleTolDeg = 35f,
                        popA = 10f, popB = 20f, minDepth = 26f, maxDepth = 44f, dangerDepth = 54f,
                        popAText = "Fascia giving.", popBText = "Through. The tip is in the abdomen." };
            }
        }

        public event Action<string> Said;
        public event Action<int> Popped; // 0 skin, 1 fascia, 2 peritoneum
        public string Readout { get; private set; } = "";
        public bool Inserted => inserted;
        public bool Active => spec != null;
        public Spec Current => spec;
        public float Depth => depth;
        public int Attempts { get; private set; }
        public Vector3 Site => site;
        public Vector3 Ideal => ideal;
        // Live measurements for the coach (valid while the tool is outside the body).
        public bool Hovering { get; private set; }
        public bool Blocked => blocked;
        public float HoverMm { get; private set; } = float.NaN;
        public float HoverAngle { get; private set; }
        public float TipHeightMm { get; private set; }
        public string TowardMark { get; private set; } = "";
        public string Lean { get; private set; } = "none";
        public bool Lined { get; private set; }
        public bool PoppedFascia => popA;
        public bool PoppedPeritoneum => popB;
        public float SiteTolMm => spec != null ? spec.siteTolMm : 0f;
        public float AngleTolDeg => spec != null ? spec.angleTolDeg : 0f;

        readonly Transform parent;
        readonly Func<float, float, float> skin;
        readonly LapCholeKit kit;
        readonly Dictionary<ToolKind, Transform> models = new Dictionary<ToolKind, Transform>();
        readonly Material markerInk, guideOff, guideOn, stylet;
        Transform model, guide, marker, styletFlag;
        Renderer guideRenderer;
        Spec spec;
        Vector3 site, ideal;
        bool inserted, blocked, popA, popB, warnedDeep, warnedLever;
        Vector3 entry;
        float depth, settle, lastDepth, blockedAt;
        bool armed; // the tip has been outside the body since the last block; entry needs it

        public LapCholeEntry(Transform parent, LapCholeKit kit, Func<float, float, float> skin)
        {
            this.parent = parent; this.kit = kit; this.skin = skin;
            markerInk = SurgicalField.Standard(new Color(0.28f, 0.07f, 0.42f), 0.3f); // surgical skin marker
            guideOff = OrKit.Fade(new Color(0.55f, 0.85f, 1f, 0.28f), 0.2f);
            guideOn = OrKit.Fade(new Color(0.3f, 1f, 0.55f, 0.42f), 0.2f);
            guideOn.EnableKeyword("_EMISSION"); guideOn.SetColor("_EmissionColor", new Color(0.2f, 0.9f, 0.4f) * 0.6f);
            stylet = SurgicalField.Standard(new Color(0.9f, 0.15f, 0.1f), 0.5f);
            BuildMarker();
            BuildGuide();
        }

        public void Destroy()
        {
            foreach (Material m in new[] { markerInk, guideOff, guideOn, stylet }) if (m != null) UnityEngine.Object.Destroy(m);
        }

        // ---------------- lifecycle ----------------
        /// <summary>Start a new entry: the tool appears in the hand, the mark and the planned axis on the patient.</summary>
        public void Begin(Spec s, Vector3 siteOnSkin, Vector3 plannedOutward)
        {
            spec = s; site = siteOnSkin; ideal = plannedOutward.normalized;
            Hovering = false; HoverMm = float.NaN; Lined = false; TowardMark = ""; Lean = "none";
            inserted = blocked = popA = popB = warnedDeep = warnedLever = false;
            depth = settle = lastDepth = 0f; Attempts = 0; armed = false;
            foreach (Transform m in models.Values) m.gameObject.SetActive(false);
            model = Model(s.tool);
            model.gameObject.SetActive(false); // shown once the hand is tracked
            if (styletFlag != null) styletFlag.localPosition = new Vector3(0f, 0f, -0.03f);
            marker.gameObject.SetActive(true);
            marker.localPosition = site + Vector3.up * 0.0006f;
            marker.localRotation = Quaternion.FromToRotation(Vector3.up, ideal) * Quaternion.Euler(0f, 35f, 0f);
            marker.localScale = Vector3.one * (s.tool == ToolKind.Trocar5 ? 0.8f : 1f);
            guide.localPosition = site;
            guide.localRotation = Quaternion.FromToRotation(Vector3.up, ideal);
            guide.gameObject.SetActive(true);
            Readout = "";
        }

        /// <summary>Stop the entry. keepTool leaves the tool where it is (the Veress stays in during insufflation).</summary>
        public void End(bool keepTool = false)
        {
            spec = null;
            if (!keepTool && model != null) model.gameObject.SetActive(false);
            if (!keepTool) model = null;
            marker.gameObject.SetActive(false);
            guide.gameObject.SetActive(false);
            Readout = "";
        }

        public void HideAll()
        {
            End();
            foreach (Transform m in models.Values) m.gameObject.SetActive(false);
        }

        /// <summary>Desktop preview: show the tool hovering on the mouse point along the planned axis.</summary>
        public void PoseHover(Vector3 tipOnSkin, bool visible)
        {
            if (spec == null || model == null) return;
            model.gameObject.SetActive(visible);
            if (!visible) return;
            Vector3 dir = -ideal;
            PoseModel(tipOnSkin - dir * (spec.length + 0.004f), dir);
        }

        /// <summary>One frame of the hand-held tool. grip and forward are board-local (forward points from grip to tip).
        /// Returns true once, when the tool is placed; outcome holds the measurements.</summary>
        public bool Step(Vector3 grip, Vector3 forward, bool tracked, float dt, out Outcome outcome)
        {
            outcome = default;
            if (spec == null) return false;
            if (!tracked) { if (model != null && !inserted) model.gameObject.SetActive(false); Readout = "Hand not tracked"; return false; }
            model.gameObject.SetActive(true);
            Vector3 dir = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.down;
            float L = spec.length;
            if (!inserted)
            {
                Vector3 tip = grip + dir * L;
                float sy = skin(tip.x, tip.z);
                bool below = !float.IsNaN(sy) && tip.y < sy;
                float hoverMm = Vector2.Distance(new Vector2(tip.x, tip.z), new Vector2(site.x, site.z)) * 1000f;
                float hoverAngle = Vector3.Angle(-dir, ideal);
                bool lined = hoverMm <= spec.siteTolMm && hoverAngle <= spec.angleTolDeg;
                Hovering = true; HoverMm = hoverMm; HoverAngle = hoverAngle; Lined = lined; Lean = Tilt(-dir);
                TipHeightMm = float.IsNaN(sy) ? float.NaN : (tip.y - sy) * 1000f;
                TowardMark = Direction(site - tip);
                guideRenderer.sharedMaterial = lined ? guideOn : guideOff;
                // Entry needs the tip to actually cross the skin from outside. A tip first seen inside the body
                // (tracking picked the hand up there, or it jumped) is held at the surface until it is lifted out.
                if (!below && !float.IsNaN(sy)) armed = true;
                if (below && !armed && !blocked)
                {
                    PoseModel(grip, dir, CrossSkin(grip, dir, L, out _));
                    Readout = "Lift the tip out above the skin, then enter at the mark";
                    return false;
                }
                if (blocked)
                {
                    // The skin stops a tool that came in at the wrong place or angle: the tip rests on the surface.
                    if (!below) blocked = false;
                    else
                    {
                        PoseModel(grip, dir, CrossSkin(grip, dir, L, out _));
                        Readout = "Blocked: lift off and re-aim";
                        return false;
                    }
                }
                if (below)
                {
                    Vector3 e = CrossSkin(grip, dir, L, out bool found);
                    if (!found) e = new Vector3(tip.x, sy, tip.z);
                    float mm = Vector2.Distance(new Vector2(e.x, e.z), new Vector2(site.x, site.z)) * 1000f;
                    float angle = Vector3.Angle(-dir, ideal);
                    if (mm > spec.siteTolMm)
                    {
                        blocked = true; armed = false; Attempts++; blockedAt = Time.unscaledTime;
                        Said?.Invoke("Move the actual tool tip onto the entry mark (within " + spec.siteTolMm.ToString("F0") + " mm). That entry was " + mm.ToString("F0") + " mm away, " + Direction(e - site) + ".");
                        PoseModel(grip, dir, e);
                        return false;
                    }
                    if (angle > spec.angleTolDeg)
                    {
                        blocked = true; armed = false; Attempts++; blockedAt = Time.unscaledTime;
                        Said?.Invoke("Straighten the shaft before entering (within " + spec.angleTolDeg.ToString("F0") + "° of the guide). You were " + angle.ToString("F0") + "° off, leaning " + Tilt(-dir) + ".");
                        PoseModel(grip, dir, e);
                        return false;
                    }
                    inserted = true; entry = e; popA = popB = false; warnedDeep = warnedLever = false;
                    Popped?.Invoke(0);
                }
                else
                {
                    PoseModel(grip, dir);
                    Readout = (hoverMm < 60f ? hoverMm.ToString("F0") + " mm from the mark" : "Bring the tip to the marked site")
                        + " · " + hoverAngle.ToString("F0") + "° from the planned axis";
                    return false;
                }
            }

            // Through the skin: the tool pivots at the entry point.
            Hovering = false;
            Vector3 toEntry = entry - grip;
            float outside = toEntry.magnitude;
            Vector3 shaft = outside > 1e-4f ? toEntry / outside : dir;
            depth = (L - outside) * 1000f;
            if (depth <= 0.5f)
            {
                inserted = false; settle = 0f;
                if (popB) Said?.Invoke(spec.name + " withdrawn. Re-enter at the mark.");
                PoseModel(grip, dir);
                return false;
            }
            float tilt = Vector3.Angle(-shaft, ideal);
            PoseModel(grip, shaft);
            if (!popA && depth >= spec.popA) { popA = true; Popped?.Invoke(1); Said?.Invoke(spec.popAText); }
            if (spec.popB > 0f && !popB && depth >= spec.popB) { popB = true; Popped?.Invoke(2); Said?.Invoke(spec.popBText); if (styletFlag != null) styletFlag.localPosition = new Vector3(0f, 0f, -0.024f); }
            if (depth > spec.dangerDepth && !warnedDeep) { warnedDeep = true; Attempts++; Said?.Invoke("Too deep: " + depth.ToString("F0") + " mm. Pull back. Bowel and the aorta sit just under the wall."); }
            if (depth < spec.maxDepth) warnedDeep = false;
            if (tilt > spec.angleTolDeg + 12f && !warnedLever) { warnedLever = true; Attempts++; Said?.Invoke("Don't lever the " + (spec.tool == ToolKind.Veress ? "needle" : "trocar") + ". Bring the handle back over the entry."); }
            if (tilt <= spec.angleTolDeg) warnedLever = false;

            float speed = Mathf.Abs(depth - lastDepth) / Mathf.Max(dt, 1e-3f);
            lastDepth = depth;
            bool inWindow = depth >= spec.minDepth && depth <= spec.maxDepth && tilt <= spec.angleTolDeg + 5f && (spec.popB < 0f || popB);
            settle = inWindow && speed < 30f ? settle + dt : 0f;
            Readout = "Depth " + depth.ToString("F0") + " mm (target " + spec.minDepth.ToString("F0") + "-" + spec.maxDepth.ToString("F0") + ") · "
                + tilt.ToString("F0") + "° · " + (depth > spec.dangerDepth ? "TOO DEEP" : inWindow ? "hold steady" : depth < spec.minDepth ? "push" : "back off");
            if (settle < 0.45f) return false;

            float entryMm = Vector2.Distance(new Vector2(entry.x, entry.z), new Vector2(site.x, site.z)) * 1000f;
            outcome = new Outcome { entry = entry, outward = -shaft, mm = entryMm, angle = tilt, depth = depth, tilt = Tilt(-shaft) };
            return true;
        }

        // ---------------- geometry ----------------
        Vector3 CrossSkin(Vector3 grip, Vector3 dir, float length, out bool found)
        {
            // First point along the shaft that is at or below the skin (binary search after a coarse march).
            found = false;
            float prev = 0f;
            for (float t = 0.005f; t <= length + 1e-4f; t += 0.005f)
            {
                Vector3 p = grip + dir * t;
                float y = skin(p.x, p.z);
                if (float.IsNaN(y) || p.y > y) { prev = t; continue; }
                float lo = prev, hi = t;
                for (int k = 0; k < 12; k++)
                {
                    float mid = (lo + hi) * 0.5f; Vector3 q = grip + dir * mid; float qy = skin(q.x, q.z);
                    if (!float.IsNaN(qy) && q.y <= qy) hi = mid; else lo = mid;
                }
                found = true;
                return grip + dir * hi;
            }
            return grip + dir * length;
        }

        void PoseModel(Vector3 grip, Vector3 dir, Vector3? stopTipAt = null)
        {
            if (model == null) return;
            Vector3 up = Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.95f ? Vector3.right : Vector3.up;
            Vector3 root = stopTipAt.HasValue ? stopTipAt.Value - dir * spec.length : grip;
            model.localPosition = root;
            model.localRotation = Quaternion.LookRotation(dir, up);
        }

        /// <summary>Which way the outer end of the tool leans relative to the planned axis (port coach vocabulary).</summary>
        string Tilt(Vector3 outward)
        {
            Vector3 d = outward.normalized - ideal;
            if (d.magnitude < 0.08f) return "none";
            return Mathf.Abs(d.x) >= Mathf.Abs(d.z) ? (d.x > 0f ? "feet" : "head") : (d.z > 0f ? "right" : "left");
        }

        static string Direction(Vector3 offset)
        {
            // x toward the feet, +z the patient's right.
            return Mathf.Abs(offset.x) >= Mathf.Abs(offset.z)
                ? (offset.x > 0f ? "toward the feet" : "toward the head")
                : (offset.z > 0f ? "toward the patient's right" : "toward the patient's left");
        }

        // ---------------- models (root at the grip, +z toward the tip) ----------------
        Transform Model(ToolKind kind)
        {
            if (models.TryGetValue(kind, out Transform m) && m != null) return m;
            m = kind == ToolKind.Veress ? BuildVeress() : BuildTrocar(kind);
            models[kind] = m;
            return m;
        }

        static Quaternion AlongZ => Quaternion.Euler(90f, 0f, 0f); // lathe +y onto +z

        Transform BuildVeress()
        {
            var root = new GameObject("Hand tool: Veress needle").transform;
            root.SetParent(parent, false);
            float L = SpecFor("veress").length;
            // Hub in the fingers: grey-blue body with a luer stopcock, a clear indicator window with the red stylet flag.
            SurgicalField.Add(root, "Hub", SurgicalField.Lathe(new[] { new Vector2(0f, -0.045f), new Vector2(0.004f, -0.044f), new Vector2(0.0055f, -0.03f), new Vector2(0.0055f, -0.004f), new Vector2(0.0025f, 0f) }, 32, 10, 0.05f),
                SurgicalField.Standard(new Color(0.5f, 0.55f, 0.68f), 0.6f, 0.3f), Vector3.zero, AlongZ, Vector3.one);
            SurgicalField.Add(root, "Indicator window", SurgicalField.Lathe(new[] { new Vector2(0.0058f, -0.034f), new Vector2(0.006f, -0.02f) }, 24), kit.catheter, Vector3.zero, AlongZ, Vector3.one, false);
            styletFlag = new GameObject("Stylet flag").transform;
            styletFlag.SetParent(root, false);
            styletFlag.localPosition = new Vector3(0f, 0f, -0.03f);
            SurgicalField.Add(styletFlag, "Flag", SurgicalField.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.0035f, 0.0005f), new Vector2(0.0035f, 0.005f), new Vector2(0f, 0.0055f) }, 16), stylet, Vector3.zero, AlongZ, Vector3.one);
            SurgicalField.Add(root, "Stopcock", SurgicalField.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.0022f, 0.001f), new Vector2(0.0022f, 0.014f), new Vector2(0f, 0.015f) }, 16), kit.steel,
                new Vector3(0.004f, 0f, -0.038f), Quaternion.Euler(0f, 0f, -90f), Vector3.one);
            SurgicalField.Add(root, "Stopcock lever", SurgicalField.RoundedBox(new Vector3(0.0035f, 0.012f, 0.003f), 0.001f, 2), kit.white, new Vector3(0.016f, 0.004f, -0.038f), Quaternion.identity, Vector3.one);
            // 14 G needle with a bevel at the tip.
            SurgicalField.Add(root, "Needle", SurgicalField.Lathe(new[] { new Vector2(0.0011f, 0f), new Vector2(0.0011f, L - 0.004f), new Vector2(0.0006f, L - 0.0012f), new Vector2(0f, L) }, 16), kit.steel, Vector3.zero, AlongZ, Vector3.one);
            return root;
        }

        Transform BuildTrocar(ToolKind kind)
        {
            float diameter = kind == ToolKind.Trocar12 ? 0.012f : kind == ToolKind.Trocar10 ? 0.010f : 0.0055f;
            string id = kind == ToolKind.Trocar12 ? "port_umbilical" : kind == ToolKind.Trocar10 ? "port_epigastric" : "port_rsc_medial";
            float L = SpecFor(id).length;
            var root = new GameObject("Hand tool: " + diameter * 1000f + " mm trocar").transform;
            root.SetParent(parent, false);
            float r = diameter * 0.5f, capR = kind == ToolKind.Trocar5 ? 0.0095f : 0.014f;
            Material blue = SurgicalField.Standard(new Color(0.07f, 0.22f, 0.72f), 0.72f);
            Material clear = SurgicalField.ClearPlastic(new Color(0.85f, 0.9f, 0.95f, 0.45f), 0.92f);
            // Obturator cap in the palm, valve housing, then the cannula with 1 cm depth rings.
            SurgicalField.Add(root, "Obturator cap", SurgicalField.Lathe(new[] { new Vector2(0f, -0.03f), new Vector2(capR * 0.8f, -0.029f), new Vector2(capR, -0.022f), new Vector2(capR, -0.006f), new Vector2(capR * 0.8f, 0f) }, 48, 18, 0.06f),
                blue, Vector3.zero, AlongZ, Vector3.one);
            SurgicalField.Add(root, "Valve housing", SurgicalField.Lathe(new[] { new Vector2(capR * 0.8f, 0f), new Vector2(capR * 0.9f, 0.004f), new Vector2(capR * 0.85f, 0.018f), new Vector2(r * 1.25f, 0.024f) }, 40), kit.white, Vector3.zero, AlongZ, Vector3.one);
            SurgicalField.Add(root, "Stopcock", SurgicalField.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.0024f, 0.001f), new Vector2(0.0024f, 0.016f), new Vector2(0f, 0.017f) }, 16), kit.steel,
                new Vector3(capR * 0.8f, 0f, 0.01f), Quaternion.Euler(0f, 0f, -90f), Vector3.one);
            float tipLen = kind == ToolKind.Trocar5 ? 0.01f : 0.016f;
            SurgicalField.Add(root, "Cannula", SurgicalField.Lathe(new[] { new Vector2(r * 1.25f, 0.024f), new Vector2(r, 0.026f), new Vector2(r, L - tipLen) }, 32),
                kind == ToolKind.Trocar12 ? kit.steel : clear, Vector3.zero, AlongZ, Vector3.one, false);
            // Bladeless obturator tip.
            SurgicalField.Add(root, "Obturator tip", SurgicalField.Lathe(new[] { new Vector2(r * 0.95f, L - tipLen), new Vector2(r * 0.7f, L - tipLen * 0.45f), new Vector2(r * 0.25f, L - tipLen * 0.08f), new Vector2(0f, L) }, 32),
                kind == ToolKind.Trocar12 ? kit.grey : clear, Vector3.zero, AlongZ, Vector3.one);
            for (int cm = 1; cm <= 6; cm++)
            {
                float z = L - tipLen - (cm * 0.01f - tipLen);
                if (z < 0.03f) break;
                SurgicalField.Add(root, "Depth ring " + cm, OrKit.Torus(1f, 0.06f, 24, 4), kit.black, new Vector3(0f, 0f, L - cm * 0.01f), Quaternion.Euler(90f, 0f, 0f), Vector3.one * r * 1.04f, false);
            }
            if (kind == ToolKind.Trocar12)
                SurgicalField.Add(root, "Retention balloon (deflated)", SurgicalField.Lathe(new[] { new Vector2(r, L - 0.03f), new Vector2(r * 1.12f, L - 0.025f), new Vector2(r, L - 0.02f) }, 32), kit.balloon, Vector3.zero, AlongZ, Vector3.one, false);
            return root;
        }

        void BuildMarker()
        {
            // A purple skin-marker cross where the incision goes, as the surgeon draws it before the case.
            marker = new GameObject("Skin mark").transform;
            marker.SetParent(parent, false);
            foreach (float a in new[] { 0f, 90f })
                SurgicalField.Add(marker, "Ink stroke", SurgicalField.RoundedBox(new Vector3(0.014f, 0.0004f, 0.0016f), 0.0002f, 2), markerInk, Vector3.zero, Quaternion.Euler(0f, a, 0f), Vector3.one, false);
            marker.gameObject.SetActive(false);
        }

        void BuildGuide()
        {
            // Faint planned trajectory: where the shaft should be, from the skin outward. Turns green when lined up.
            guide = new GameObject("Planned axis").transform;
            guide.SetParent(parent, false);
            guideRenderer = SurgicalField.Add(guide, "Axis", SurgicalField.Lathe(new[] { new Vector2(0.0009f, 0.01f), new Vector2(0.0009f, 0.16f), new Vector2(0f, 0.161f) }, 12), guideOff, Vector3.zero, Quaternion.identity, Vector3.one, false);
            guide.gameObject.SetActive(false);
        }
    }
}
