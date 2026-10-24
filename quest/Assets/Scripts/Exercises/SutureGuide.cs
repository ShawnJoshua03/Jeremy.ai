using System.Collections.Generic;
using UnityEngine;

namespace Jeremy
{
    // Wound-closure UX on top of SutureDetector (added by it at runtime): open-surgery instrument models,
    // the curved needle in the driver's jaws and the suture trailing from it (through the tissue during a
    // stitch, wrapped round the jaws while tying), an aim ring on the skin under each tip, the knot guide,
    // step-by-step instructions, per-stitch popups and the final summary, plus a focused field light.
    // Visual only: events and scoring stay in SutureDetector / EventRecorder.
    [DefaultExecutionOrder(70)]
    public sealed class SutureGuide : MonoBehaviour
    {
        public const float NeedleRadius = 0.011f, NeedleWire = 0.00042f, GraspBehindPoint = 115f;
        private PegTransferRig rig;
        private SutureDetector detector;
        private WoundPad pad;
        private DesktopPreview preview;
        private WoundInstrumentModel driverModel, forcepsModel;
        private Transform needle, aimRing, forcepsRing, knotGuide;
        private Renderer aimRenderer, forcepsRenderer, knotRenderer;
        private LineRenderer thread;
        private bool woundActive, ready;
        private readonly List<Renderer> hidden = new List<Renderer>();
        private readonly int[] childCount = { -1, -1 };
        private Vector3 needleCentre, tailRest;
        private Quaternion needleBasis = Quaternion.identity;
        private float needleAngle, pendingProgress;
        private bool needleInit;
        private Light fieldLight;
        private ReflectionProbe probe;
        private bool cameraMoved;
        private Vector3 savedCameraPosition;
        private Quaternion savedCameraRotation;

        private struct Popup { public string text; public Color color; public Vector3 local; public float t0, life; public int size; }
        private readonly List<Popup> popups = new List<Popup>();
        private string flash;
        private Color flashColor;
        private float flashUntil;

        private Transform Board => rig.board.transform;
        private static float Lift => Mathf.Sqrt(NeedleRadius * NeedleRadius - WoundPad.BiteOffset * WoundPad.BiteOffset);
        private static float EntryAngle => 180f + Mathf.Asin(Lift / NeedleRadius) * Mathf.Rad2Deg;
        private static float ExitAngle => 360f - Mathf.Asin(Lift / NeedleRadius) * Mathf.Rad2Deg;

        private void Start()
        {
            rig = GetComponent<PegTransferRig>();
            detector = GetComponent<SutureDetector>();
            preview = GetComponent<DesktopPreview>();
            if (rig == null || detector == null || rig.board == null || rig.Instruments == null) { enabled = false; return; }
            pad = rig.Pad;
            detector.tieKnots = true;
            detector.Feedback += OnFeedback;
            rig.Exercise.Reset += OnReset;
            Transform board = Board;
            needle = WoundArt.MeshObject(board, "Suture needle", WoundArt.NeedleMesh(NeedleRadius, NeedleWire), WoundArt.Needle).transform;
            thread = WoundArt.Thread(board, "Suture (on the needle)", 0.00035f);
            thread.useWorldSpace = true;
            Mesh annulus = WoundArt.Annulus(40, 0.7f);
            aimRenderer = WoundArt.MeshObject(board, "Needle aim ring", annulus, WoundArt.Ring, false); aimRing = aimRenderer.transform;
            forcepsRenderer = WoundArt.MeshObject(board, "Forceps aim ring", annulus, WoundArt.Ring, false); forcepsRing = forcepsRenderer.transform;
            knotRenderer = WoundArt.MeshObject(board, "Knot guide", WoundArt.Annulus(64, 0.93f), WoundArt.Ring, false); knotGuide = knotRenderer.transform;
            driverModel = WoundInstrumentModel.NeedleDriver(rig.Instruments[1].tip);
            forcepsModel = WoundInstrumentModel.Forceps(rig.Instruments[0].tip);
            ready = true;
            Apply(false);
        }
        private void OnDestroy()
        {
            if (detector != null) detector.Feedback -= OnFeedback;
            if (rig != null && rig.Exercise != null) rig.Exercise.Reset -= OnReset;
        }
        private void OnReset() { popups.Clear(); flash = null; needleInit = false; }

        private void LateUpdate()
        {
            if (!ready) return;
            bool wound = detector.enabled && rig.WoundClosure;
            if (wound != woundActive) Apply(wound);
            if (!wound) return;
            HideOriginals(0); HideOriginals(1);
            float dt = Time.deltaTime;
            InstrumentController driver = rig.Instruments[1], forceps = rig.Instruments[0];
            UpdateNeedle(driver, dt);
            forcepsModel.root.SetPositionAndRotation(forceps.tip.position, forceps.tip.rotation);
            forcepsModel.SetClosed(forceps.Closed, dt);
            driverModel.SetClosed(true, dt);
            UpdateThread(driver, dt);
            UpdateRings(driver, forceps);
        }

        // ---------- mode switch ----------
        private void Apply(bool wound)
        {
            woundActive = wound;
            needle.gameObject.SetActive(wound);
            thread.gameObject.SetActive(wound);
            driverModel.root.gameObject.SetActive(wound);
            forcepsModel.root.gameObject.SetActive(wound);
            if (!wound) { aimRing.gameObject.SetActive(false); forcepsRing.gameObject.SetActive(false); knotGuide.gameObject.SetActive(false); }
            if (!wound) { foreach (Renderer r in hidden) if (r != null) r.enabled = true; hidden.Clear(); childCount[0] = childCount[1] = -1; }
            needleInit = false;
            SetEnvironment(wound);
        }
        // The shared tip carries the peg-transfer jaws and the preview's instrument dressings; in wound closure the
        // models here replace them visually (the tip pose, jaws state and detectors are untouched).
        private void HideOriginals(int slot)
        {
            Transform tip = rig.Instruments[slot].tip;
            if (tip.childCount == childCount[slot]) return;
            childCount[slot] = tip.childCount;
            Transform model = slot == 1 ? driverModel.root : forcepsModel.root;
            foreach (Renderer r in tip.GetComponentsInChildren<Renderer>(true))
            {
                if (r.transform.IsChildOf(model) || !r.enabled) continue;
                r.enabled = false;
                hidden.Add(r);
            }
        }
        private void SetEnvironment(bool wound)
        {
            if (wound && fieldLight == null)
            {
                // A focused surgical light on the field: bright, slightly warm, soft shadows that read on thread scale.
                var lamp = new GameObject("Wound field light");
                lamp.transform.SetParent(Board, false);
                lamp.transform.localPosition = new Vector3(-0.22f, 0.85f, -0.30f);
                lamp.transform.LookAt(Board.TransformPoint(new Vector3(0, WoundPad.PadTop, 0)));
                fieldLight = lamp.AddComponent<Light>();
                fieldLight.type = LightType.Spot; fieldLight.spotAngle = 30f; fieldLight.innerSpotAngle = 12f; fieldLight.range = 2.5f;
                fieldLight.intensity = 2.4f; fieldLight.color = new Color(1f, 0.97f, 0.92f);
                fieldLight.shadows = LightShadows.Soft; fieldLight.shadowStrength = 0.85f;
                fieldLight.shadowResolution = UnityEngine.Rendering.LightShadowResolution.VeryHigh;
                fieldLight.shadowBias = 0.004f; fieldLight.shadowNormalBias = 0.05f; fieldLight.shadowNearPlane = 0.2f;
                fieldLight.renderMode = LightRenderMode.ForcePixel;
                // Steel and the needle need something to reflect: a probe of the OR around the field.
                var probeObject = new GameObject("Wound field reflections");
                probeObject.transform.SetParent(Board, false);
                probeObject.transform.localPosition = new Vector3(0, 0.25f, 0);
                probe = probeObject.AddComponent<ReflectionProbe>();
                probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime;
                probe.refreshMode = UnityEngine.Rendering.ReflectionProbeRefreshMode.ViaScripting;
                probe.timeSlicingMode = UnityEngine.Rendering.ReflectionProbeTimeSlicingMode.NoTimeSlicing;
                probe.size = new Vector3(2.5f, 2f, 2.5f); probe.resolution = 128; probe.importance = 2; probe.intensity = 1.1f;
                probe.boxProjection = false;
            }
            if (fieldLight != null) fieldLight.enabled = wound;
            if (probe != null) { probe.enabled = wound; if (wound) probe.RenderProbe(); }
            Camera view = preview != null ? preview.View : null;
            if (view == null) return;
            if (wound && !cameraMoved)
            {
                // Surgeon's-eye close-up so 4 mm on the skin is a mouse-sized target.
                savedCameraPosition = view.transform.position; savedCameraRotation = view.transform.rotation;
                Vector3 eye = Board.TransformPoint(new Vector3(-0.015f, 0.215f, -0.185f));
                view.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(Board.TransformPoint(new Vector3(0, WoundPad.PadTop, -0.004f)) - eye, Board.up));
                cameraMoved = true;
            }
            else if (!wound && cameraMoved)
            {
                view.transform.SetPositionAndRotation(savedCameraPosition, savedCameraRotation);
                cameraMoved = false;
            }
        }

        // ---------- needle ----------
        private static Vector3 Dir(Quaternion basis, float degrees) => basis * new Vector3(Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad), 0);
        private void UpdateNeedle(InstrumentController driver, float dt)
        {
            Vector3 tip = Board.InverseTransformPoint(driver.tip.position);
            Vector3 centre; float angle; Quaternion basis = Quaternion.identity;
            int site = detector.PendingSite;
            if (site >= 0)
            {
                // In the tissue: the needle turns about its own centre from the entry mark to the exit mark as the
                // driver moves across; the point stays under the skin until it reaches the exit.
                Vector3 entry = detector.EntryHit, exit = WoundPad.Exit(site);
                Vector3 across = exit - entry; across.y = 0;
                if (across.sqrMagnitude < 1e-8f) across = Vector3.right;
                basis = Quaternion.LookRotation(Vector3.Cross(across.normalized, Vector3.up), Vector3.up);
                float half = Mathf.Min(across.magnitude / 2, NeedleRadius * 0.95f);
                float lift = Mathf.Sqrt(NeedleRadius * NeedleRadius - half * half), a = Mathf.Asin(lift / NeedleRadius) * Mathf.Rad2Deg;
                centre = (entry + exit) / 2; centre.y = WoundPad.PadTop + lift;
                float f = Mathf.Clamp01(Vector3.Dot(tip - entry, across.normalized) / (2 * half));
                pendingProgress = Mathf.MoveTowards(pendingProgress, Mathf.Max(0.04f, f), dt * 2.5f);
                angle = Mathf.Lerp(180f + a, 360f - a, pendingProgress);
            }
            else
            {
                pendingProgress = 0;
                angle = EntryAngle; // poised: point down at the skin, body arching up to the jaws
                centre = tip - Dir(basis, angle) * NeedleRadius;
            }
            float k = needleInit ? 1 - Mathf.Exp(-dt * 22f) : 1;
            needleInit = true;
            needleCentre = Vector3.Lerp(needleCentre, centre, k);
            needleAngle = Mathf.LerpAngle(needleAngle, angle, k);
            needleBasis = Quaternion.Slerp(needleBasis, basis, k);
            needle.localPosition = needleCentre;
            needle.localRotation = needleBasis * Quaternion.Euler(0, 0, needleAngle);
            // The driver's jaws hold the needle a little in front of the swage.
            Vector3 grasp = needleCentre + Dir(needleBasis, needleAngle - GraspBehindPoint) * NeedleRadius;
            driverModel.root.SetPositionAndRotation(Board.TransformPoint(grasp), driver.tip.rotation);
        }
        private Vector3 SwageLocal => needleCentre + Dir(needleBasis, needleAngle - 135f) * NeedleRadius;

        // ---------- thread ----------
        private float GroundY(Vector3 local) => WoundPad.OverPad(local) ? pad.SurfacePoint(local).y + 0.0003f : 0.0015f;
        private readonly List<Vector3> points = new List<Vector3>();
        private void UpdateThread(InstrumentController driver, float dt)
        {
            points.Clear();
            Vector3 swage = SwageLocal;
            if (detector.Phase == SuturePhase.Knot && detector.KnotSite >= 0)
            {
                int site = detector.KnotSite;
                Vector3 entry = detector.EntryHit, exit = detector.ExitHit;
                float p = detector.KnotProgress;
                Vector3 tail = entry + new Vector3(-0.024f + 0.012f * p, 0, -0.006f);
                AddOnSkin(tail, entry, 6);
                Vector3 under = Vector3.down * 0.0028f;
                points.Add(entry + under * 0.4f); points.Add(entry + under); points.Add(exit + under); points.Add(exit + under * 0.4f);
                Vector3 exitTop = exit; exitTop.y = GroundY(exit);
                points.Add(exitTop);
                // Instrument tie: loops thrown around the driver's jaws.
                Vector3 axis = Board.InverseTransformDirection(driver.tip.forward).normalized;
                Vector3 grasp = needleCentre + Dir(needleBasis, needleAngle - GraspBehindPoint) * NeedleRadius;
                Vector3 hub = grasp - axis * 0.005f;
                float turns = detector.KnotTurns;
                if (turns > 0.02f)
                {
                    Vector3 side = Vector3.Cross(axis, Vector3.up).normalized, upish = Vector3.Cross(side, axis).normalized;
                    int count = Mathf.Max(3, Mathf.CeilToInt(turns * 14));
                    for (int i = 0; i <= count; i++)
                    {
                        float t = i / (float)count, a = t * turns * Mathf.PI * 2;
                        points.Add(hub + axis * (0.0025f * t) + (side * Mathf.Cos(a) + upish * Mathf.Sin(a)) * 0.0019f);
                    }
                }
                else AddSag(exitTop, swage, 0.002f, 6);
                points.Add(swage);
                tailRest = swage + new Vector3(0.03f, 0, -0.05f);
            }
            else
            {
                // Suture trailing from the swage and lying on the drape behind the driver.
                Vector3 target = swage + new Vector3(0.035f, 0, -0.055f);
                target.y = GroundY(target);
                tailRest = Vector3.Lerp(tailRest, target, 1 - Mathf.Exp(-dt * 2.5f));
                if ((tailRest - target).sqrMagnitude > 0.04f) tailRest = target;
                Vector3 control = new Vector3(Mathf.Lerp(swage.x, tailRest.x, 0.25f), Mathf.Min(swage.y, tailRest.y + 0.004f), Mathf.Lerp(swage.z, tailRest.z, 0.25f));
                const int n = 16;
                for (int i = 0; i <= n; i++)
                {
                    float t = i / (float)n;
                    Vector3 q = (1 - t) * (1 - t) * swage + 2 * (1 - t) * t * control + t * t * tailRest;
                    q.y = Mathf.Max(q.y, GroundY(q));
                    points.Add(q);
                }
                Vector3 end = tailRest + new Vector3(0.018f, 0, -0.03f);
                end.y = GroundY(end);
                AddOnSkin(tailRest, end, 5);
            }
            thread.positionCount = points.Count;
            for (int i = 0; i < points.Count; i++) thread.SetPosition(i, Board.TransformPoint(points[i]));
        }
        private void AddOnSkin(Vector3 a, Vector3 b, int count)
        {
            for (int i = 0; i < count; i++)
            {
                Vector3 q = Vector3.Lerp(a, b, i / (float)count);
                q.y = GroundY(q);
                points.Add(q);
            }
        }
        private void AddSag(Vector3 a, Vector3 b, float drop, int count)
        {
            for (int i = 1; i < count; i++)
            {
                float t = i / (float)count;
                Vector3 q = Vector3.Lerp(a, b, t) + Vector3.down * drop * 4 * t * (1 - t);
                q.y = Mathf.Max(q.y, GroundY(q));
                points.Add(q);
            }
        }

        // ---------- aim rings and knot guide ----------
        private void UpdateRings(InstrumentController driver, InstrumentController forceps)
        {
            bool running = rig.Exercise.State == ExerciseState.Running;
            SuturePhase phase = detector.Phase;
            Vector3 tip = Board.InverseTransformPoint(driver.tip.position);
            bool aim = running && driver.TrackingValid && WoundPad.OverPad(tip) && (phase == SuturePhase.Entry || phase == SuturePhase.Exit);
            aimRing.gameObject.SetActive(aim);
            if (aim)
            {
                float distance = phase == SuturePhase.Exit ? Vector3.Distance(Flat(tip), WoundPad.Exit(detector.PendingSite)) : NearestEntry(tip);
                Color c = distance <= WoundPad.GoodBite ? new Color(0.3f, 1f, 0.45f, 0.95f)
                    : distance <= WoundPad.MaxBite ? new Color(1f, 0.72f, 0.2f, 0.95f) : new Color(1f, 1f, 1f, 0.7f);
                WoundArt.SetGlow(aimRenderer, c, c * 0.9f);
                aimRing.localPosition = pad.SurfacePoint(tip) + Vector3.up * 0.00018f;
                aimRing.localScale = Vector3.one * (driver.Closed ? 0.0011f : 0.0017f);
            }
            Vector3 ftip = Board.InverseTransformPoint(forceps.tip.position);
            bool faim = running && forceps.TrackingValid && WoundPad.OverPad(ftip) && (phase == SuturePhase.Entry || phase == SuturePhase.Exit)
                && Mathf.Abs(ftip.x) < 0.04f;
            forcepsRing.gameObject.SetActive(faim);
            if (faim)
            {
                Color c = detector.EdgeGrasped ? new Color(0.3f, 1f, 0.45f, 0.9f) : new Color(0.45f, 0.8f, 1f, 0.75f);
                WoundArt.SetGlow(forcepsRenderer, c, c * 0.8f);
                forcepsRing.localPosition = pad.SurfacePoint(ftip) + Vector3.up * 0.00018f;
                forcepsRing.localScale = Vector3.one * 0.0012f;
            }
            bool knot = running && phase == SuturePhase.Knot && detector.KnotSite >= 0;
            knotGuide.gameObject.SetActive(knot);
            if (knot)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 5f);
                Color c = Color.Lerp(new Color(0.45f, 0.8f, 1f, 0.55f), new Color(0.3f, 1f, 0.45f, 0.9f), detector.KnotProgress);
                WoundArt.SetGlow(knotRenderer, c, c * (0.5f + 0.5f * pulse));
                knotGuide.localPosition = pad.SurfacePoint(new Vector3(0, 0, WoundPad.SiteZ(detector.KnotSite))) + new Vector3(0, 0.0006f, 0);
                knotGuide.localScale = Vector3.one * 0.011f;
            }
        }
        private static Vector3 Flat(Vector3 local) => new Vector3(local.x, WoundPad.PadTop, local.z);
        private float NearestEntry(Vector3 tip)
        {
            float best = float.MaxValue;
            for (int i = 0; i < WoundPad.SiteCount; i++) if (!pad.IsPlaced(i)) best = Mathf.Min(best, Vector3.Distance(Flat(tip), WoundPad.Entry(i)));
            return best;
        }

        // ---------- feedback ----------
        public static Color ErrorColor(float mm) => mm < 0 ? new Color(1f, 0.35f, 0.3f) : mm <= WoundPad.GoodBite * 1000 ? new Color(0.35f, 0.95f, 0.45f)
            : mm <= WoundPad.MaxBite * 1000 ? new Color(1f, 0.75f, 0.25f) : new Color(1f, 0.35f, 0.3f);
        private void OnFeedback(SutureFeedback f)
        {
            switch (f.kind)
            {
                case SutureFeedback.Kind.Everted:
                    AddPopup("Nice eversion", new Color(0.5f, 0.85f, 1f), f.local, 1.8f, 16);
                    break;
                case SutureFeedback.Kind.EntryTaken:
                    AddPopup($"in  {f.errorMm:F1} mm", ErrorColor(f.errorMm), f.local + Vector3.left * 0.004f, 1.4f, 14);
                    break;
                case SutureFeedback.Kind.Placed:
                    AddPopup("through", new Color(0.85f, 0.9f, 1f), f.local + Vector3.right * 0.004f, 1.2f, 14);
                    break;
                case SutureFeedback.Kind.Tied:
                    AddPopup($"{f.errorMm:F1} mm", ErrorColor(f.errorMm), f.local, 2.6f, 22);
                    bool last = detector.Phase == SuturePhase.Done;
                    Flash(f.errorMm <= WoundPad.GoodBite * 1000
                        ? $"Good — {f.errorMm:F1} mm." + (last ? " Wound closed!" : " Next stitch.")
                        : $"Placed — {f.errorMm:F1} mm (aim for ≤ 4 mm)." + (last ? " Wound closed!" : " Next stitch."), ErrorColor(f.errorMm), 2.4f);
                    break;
                case SutureFeedback.Kind.Missed:
                    AddPopup(f.errorMm >= 0 ? $"missed  {f.errorMm:F1} mm" : "missed", ErrorColor(-1), f.local, 2.2f, 16);
                    Flash($"Missed — {f.reason}" + (f.errorMm >= 0 ? $" ({f.errorMm:F1} mm, max 8)" : "") + ". Try again at the glowing dot.", ErrorColor(-1), 2.6f);
                    break;
                case SutureFeedback.Kind.TooFar:
                    Flash($"{f.reason} — the ring turns green within 4 mm.", new Color(1f, 0.8f, 0.4f), 1.8f);
                    break;
                case SutureFeedback.Kind.Contact:
                    AddPopup("tissue contact", ErrorColor(-1), f.local, 1.8f, 15);
                    Flash("Tissue contact — keep the tips out of the cut edge.", ErrorColor(-1), 2f);
                    break;
            }
        }
        private void AddPopup(string text, Color color, Vector3 local, float life, int size) =>
            popups.Add(new Popup { text = text, color = color, local = local, t0 = Time.unscaledTime, life = life, size = size });
        private void Flash(string text, Color color, float seconds) { flash = text; flashColor = color; flashUntil = Time.unscaledTime + seconds; }

        /// <summary>Instruction for the current step: (title, detail). Shared with the HUD.</summary>
        public static void Describe(SutureDetector d, ExerciseStateMachine exercise, bool desktop, out string title, out string detail)
        {
            int n = Mathf.Clamp(d.CurrentSite + 1, 1, WoundPad.SiteCount);
            string click = desktop ? "click" : "squeeze the trigger";
            title = detail = "";
            if (exercise.State == ExerciseState.Ready || exercise.State == ExerciseState.Idle)
            {
                title = "Wound closure — 5 interrupted stitches";
                detail = desktop ? "Press Enter to start. Each stitch: bite in at the left dot, out at the right dot, then tie." : "Press Start. Each stitch: in at the left dot, out at the right dot, then tie.";
                return;
            }
            if (exercise.State == ExerciseState.Paused) { title = "Paused"; detail = desktop ? "Enter resumes." : "Resume to continue."; return; }
            if (exercise.State == ExerciseState.Completed || d.Phase == SuturePhase.Done) { title = "Wound closed"; detail = desktop ? "R resets for another attempt." : "Reset for another attempt."; return; }
            if (exercise.State != ExerciseState.Running) return;
            switch (d.Phase)
            {
                case SuturePhase.Entry:
                    if (!d.Everted && !d.EdgeGrasped)
                    {
                        title = $"Stitch {n} of 5 — lift the left edge with the forceps";
                        detail = desktop ? "Tab = forceps (left hand): click the left cut edge beside the glowing dot, then Tab back. Optional."
                                         : "Left forceps: grasp the left cut edge beside the glowing dot. Optional.";
                    }
                    else
                    {
                        title = $"Stitch {n} of 5 — drive the needle in at the glowing LEFT dot";
                        detail = (d.EdgeGrasped ? "Edge everted ✓  " : "") + (desktop ? "Needle driver (right hand, Tab): put the ring on the dot and click. Green ring = within 4 mm."
                                                                                   : "Needle driver: put the ring on the dot and squeeze. Green ring = within 4 mm.");
                    }
                    break;
                case SuturePhase.Exit:
                    title = $"Stitch {n} of 5 — rotate through and come out at the RIGHT dot";
                    detail = $"Move straight across the wound and {click} on the glowing right dot ({d.ExitSecondsLeft:F0} s left).";
                    break;
                case SuturePhase.Knot:
                    title = $"Stitch {n} of 5 — tie the knot: circle twice and pull";
                    detail = desktop ? "Hold the left mouse button and loop the driver around the stitch inside the blue ring (or just hold)."
                                     : "Hold the trigger and loop the driver around the stitch twice (or just hold).";
                    break;
            }
        }

        private GUIStyle titleStyle, detailStyle, popupStyle, panelStyle;
        private Texture2D panel, white;
        private void OnGUI()
        {
            if (!ready || !woundActive || preview == null || preview.View == null) return;
            Camera view = preview.View;
            if (panel == null)
            {
                panel = new Texture2D(1, 1); panel.SetPixel(0, 0, new Color(0.05f, 0.06f, 0.08f, 0.82f)); panel.Apply();
                white = Texture2D.whiteTexture;
                panelStyle = new GUIStyle { normal = { background = panel } };
                titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 19, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter, wordWrap = true, richText = true };
                detailStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.UpperCenter, wordWrap = true, richText = true };
                popupStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, richText = false };
            }
            float scale = Mathf.Clamp(Screen.height / 820f, 0.75f, 2f);
            Matrix4x4 saved = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            float width = Screen.width / scale;
            DrawPopups(view, scale);
            Describe(detector, rig.Exercise, true, out string title, out string detail);
            bool flashing = flash != null && Time.unscaledTime < flashUntil;
            float w = Mathf.Min(660, width - 24), x = (width - w) / 2, y = 10;
            bool knot = detector.Phase == SuturePhase.Knot && rig.Exercise.State == ExerciseState.Running;
            bool exit = detector.Phase == SuturePhase.Exit && rig.Exercise.State == ExerciseState.Running;
            float h = 84 + (knot || exit ? 14 : 0);
            GUI.Box(new Rect(x, y, w, h), GUIContent.none, panelStyle);
            titleStyle.normal.textColor = flashing ? flashColor : Color.white;
            GUI.Label(new Rect(x + 10, y + 6, w - 20, 26), flashing ? flash : title, titleStyle);
            detailStyle.normal.textColor = new Color(0.85f, 0.88f, 0.92f);
            GUI.Label(new Rect(x + 10, y + 32, w - 20, 36), flashing ? title : detail, detailStyle);
            // Stitch progress: one chip per site, coloured by its bite error.
            float chip = 16, gap = 6, cx = x + w / 2 - (WoundPad.SiteCount * (chip + gap) - gap) / 2;
            for (int i = 0; i < WoundPad.SiteCount; i++)
            {
                Color c = pad.IsTied(i) ? ErrorColor(detector.StitchErrorMm[i]) : i == detector.CurrentSite && rig.Exercise.State == ExerciseState.Running
                    ? new Color(0.45f, 0.8f, 1f) : new Color(0.35f, 0.37f, 0.42f);
                GUI.color = c;
                GUI.DrawTexture(new Rect(cx + i * (chip + gap), y + 66, chip, 8), white);
            }
            GUI.color = Color.white;
            if (knot || exit)
            {
                float fill = knot ? detector.KnotProgress : detector.ExitSecondsLeft / Mathf.Max(1, detector.exitTimeout);
                GUI.color = new Color(1, 1, 1, 0.15f); GUI.DrawTexture(new Rect(x + 40, y + 82, w - 80, 7), white);
                GUI.color = knot ? new Color(0.35f, 0.95f, 0.45f) : Color.Lerp(new Color(1f, 0.4f, 0.3f), new Color(0.45f, 0.8f, 1f), fill);
                GUI.DrawTexture(new Rect(x + 40, y + 82, (w - 80) * Mathf.Clamp01(fill), 7), white);
                GUI.color = Color.white;
            }
            if (rig.Exercise.State == ExerciseState.Completed) DrawSummary(width);
            GUI.matrix = saved;
        }
        private void DrawPopups(Camera view, float scale)
        {
            float now = Time.unscaledTime;
            for (int i = popups.Count - 1; i >= 0; i--)
            {
                Popup p = popups[i];
                float age = (now - p.t0) / p.life;
                if (age >= 1) { popups.RemoveAt(i); continue; }
                Vector3 screen = view.WorldToScreenPoint(Board.TransformPoint(p.local + Vector3.up * 0.004f));
                if (screen.z <= 0) continue;
                float sx = screen.x / scale, sy = (Screen.height - screen.y) / scale - 40 * Mathf.Sqrt(age);
                popupStyle.fontSize = p.size;
                float alpha = age < 0.75f ? 1 : 1 - (age - 0.75f) / 0.25f;
                var rect = new Rect(sx - 90, sy - 14, 180, 28);
                popupStyle.normal.textColor = new Color(0, 0, 0, 0.75f * alpha);
                GUI.Label(new Rect(rect.x + 1.5f, rect.y + 1.5f, rect.width, rect.height), p.text, popupStyle);
                popupStyle.normal.textColor = new Color(p.color.r, p.color.g, p.color.b, alpha);
                GUI.Label(rect, p.text, popupStyle);
            }
        }
        private void DrawSummary(float width)
        {
            EventRecorder recorder = rig.Recorder;
            Metrics m = recorder.LocalMetrics();
            float w = 380, h = 238, x = (width - w) / 2, y = 112;
            GUI.Box(new Rect(x, y, w, h), GUIContent.none, panelStyle);
            titleStyle.normal.textColor = new Color(0.4f, 0.95f, 0.5f);
            GUI.Label(new Rect(x, y + 10, w, 26), $"Wound closed — {recorder.StitchesPlaced}/{WoundPad.SiteCount} stitches", titleStyle);
            var line = new GUIStyle(detailStyle) { alignment = TextAnchor.MiddleLeft, fontSize = 15 };
            float row = y + 44;
            for (int i = 0; i < WoundPad.SiteCount; i++)
            {
                float e = detector.StitchErrorMm[i];
                line.normal.textColor = e >= 0 ? ErrorColor(e) : new Color(0.6f, 0.6f, 0.65f);
                string grade = e < 0 ? "not placed" : e <= WoundPad.GoodBite * 1000 ? "good" : "placed";
                GUI.Label(new Rect(x + 30, row + i * 20, w - 60, 20), $"Stitch {i + 1}:  {(e >= 0 ? e.ToString("F1") + " mm" : "—")}   {grade}", line);
            }
            line.normal.textColor = Color.white;
            row += WoundPad.SiteCount * 20 + 8;
            GUI.Label(new Rect(x + 30, row, w - 60, 20), "Mean bite error: " + (m.mean_bite_error_mm >= 0 ? m.mean_bite_error_mm.ToString("F1") + " mm" : "n/a")
                + "    Spacing SD: " + (m.spacing_sd_mm >= 0 ? m.spacing_sd_mm.ToString("F1") + " mm" : "n/a"), line);
            GUI.Label(new Rect(x + 30, row + 20, w - 60, 20), $"Missed bites: {recorder.StitchesMissed}    Tissue contacts: {recorder.TissueContacts}    Time: {rig.Exercise.ElapsedSeconds:F0} s", line);
            line.normal.textColor = new Color(0.7f, 0.72f, 0.78f);
            GUI.Label(new Rect(x + 30, row + 44, w - 60, 20), "Press R to reset. Server score in the HUD once uploaded.", line);
        }
    }
}
