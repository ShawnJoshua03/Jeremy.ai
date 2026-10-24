using System.Collections.Generic;
using UnityEngine;

namespace Jeremy
{
    // Headset only: the lap chole scene as an immersive (non-passthrough) environment. When the rig switches to
    // lap_chole on device, passthrough is switched off, the camera background made opaque, and the same scene the
    // desktop preview shows (room, table, patient, drape, ports, anatomy) is built around the board, with the board
    // (= the navel) at a comfortable standing height in front of the user and the laparoscope image on a large
    // world-space monitor across the table at eye height. Leaving lap_chole restores passthrough.
    // Budget (Quest 3): ~100k triangles in the eye view, one shadowed directional light, no image effects.
    public sealed class LapCholeImmersive : MonoBehaviour
    {
        public const float NavelHeight = 0.97f; // metres above the floor
        public LapCholeDressing Dressing { get; private set; }
        public LaparoscopeMonitor Monitor { get; private set; }
        private PegTransferRig rig;
        private Camera eye;
        private CameraClearFlags savedClear;
        private Color savedBackground;
        private int savedMask;
        private readonly List<Behaviour> passthrough = new List<Behaviour>();
        private readonly Dictionary<Camera, int> exteriorCameraMasks = new Dictionary<Camera, int>();
        private bool active, placedWithTracking;

        // At launch the head pose is not tracked yet (it reads the origin), so place the table again once it is,
        // and whenever the user recenters (long-press the Meta button).
        private void Update()
        {
            if (!active || rig == null || rig.head == null) return;
            if (!placedWithTracking && rig.head.position.y > 0.8f) { placedWithTracking = true; Recenter(rig.head); }
        }

        private void OnEnable() { if (OVRManager.display != null) OVRManager.display.RecenteredPose += OnRecentered; }
        private void OnDisable() { if (OVRManager.display != null) OVRManager.display.RecenteredPose -= OnRecentered; }
        private void OnRecentered() { if (active && rig != null) Recenter(rig.head); }

        public static LapCholeImmersive Ensure(PegTransferRig rig)
        {
            var immersive = rig.GetComponent<LapCholeImmersive>();
            if (immersive == null) { immersive = rig.gameObject.AddComponent<LapCholeImmersive>(); immersive.rig = rig; }
            return immersive;
        }

        public void SetActive(bool on)
        {
            if (on == active) return;
            active = on;
            if (on)
            {
                Recenter(rig.head);
                if (Dressing == null) Build();
                Dressing.gameObject.SetActive(true);
                Monitor.enabled = true;
                Monitor.Scope.enabled = true;
                SetPassthrough(false);
            }
            else
            {
                if (Dressing != null) Dressing.gameObject.SetActive(false);
                if (Monitor != null) { Monitor.Scope.enabled = false; Monitor.enabled = false; }
                SetPassthrough(true);
            }
        }

        /// <summary>Board (navel) half a metre in front of the user, at standing working height; the user is on the
        /// patient's left side, the patient's head to the user's left.</summary>
        public void Recenter(Transform head)
        {
            if (head == null || rig == null || rig.board == null) return;
            Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            // Floor-level tracking puts the head at ~1.6 m; eye-level tracking puts it near 0.
            // The rig tracks from the floor, so the navel always sits at standing working height above it.
            float y = NavelHeight;
            Vector3 at = head.position + forward * 0.45f;
            rig.board.Place(new Vector3(at.x, y, at.z), Quaternion.LookRotation(forward));
        }

        private void Build()
        {
            Transform board = rig.board.transform;
            float floorY = -board.position.y; // floor-level tracking: the real floor is world y = 0
            Dressing = LapCholeDressing.Create(board, board, floorY);
            Monitor = gameObject.AddComponent<LaparoscopeMonitor>();
            Monitor.desktopLayout = false;
            Monitor.Build(Dressing.transform);
            LapCholeAnatomy a = Dressing.Anatomy;
            Monitor.Aim(a.ScopePosition, a.ScopeTarget, a.ScopeFieldOfView, true, "LAPAROSCOPE 30°  |  CAM 1",
                LaparoscopeMonitor.CavityLook.VideoHaze.fog, a.ScopeUp, LaparoscopeMonitor.CavityLook.VideoHaze);
            // Large monitor across the table (beyond the patient's right side), at eye height, facing the user.
            float eyeAbove = rig.head != null ? Mathf.Clamp(rig.head.position.y - board.position.y, 0.45f, 0.8f) : 0.62f;
            Monitor.BuildWorldScreen(Dressing.transform, new Vector3(-0.08f, eyeAbove, 0.95f), Quaternion.identity, new Vector2(1.1f, 0.62f));
            Dressing.ShowStage(3);
            Dressing.ShowProps(true, false);
        }

        private void SetPassthrough(bool on)
        {
            if (eye == null)
            {
                eye = rig.head != null ? rig.head.GetComponent<Camera>() : Camera.main;
                if (eye != null) { savedClear = eye.clearFlags; savedBackground = eye.backgroundColor; savedMask = eye.cullingMask; }
            }
            if (!on)
            {
                passthrough.Clear();
                foreach (OVRPassthroughLayer layer in FindObjectsByType<OVRPassthroughLayer>(FindObjectsSortMode.None))
                    if (layer.enabled) { layer.enabled = false; passthrough.Add(layer); }
                if (OVRManager.instance != null) OVRManager.instance.isInsightPassthroughEnabled = false;
                // Internal anatomy must never leak through the patient in the room view. Unity can create more than
                // one eye/spectator camera, so mask every exterior camera rather than only Camera.main.
                exteriorCameraMasks.Clear();
                foreach (Camera camera in FindObjectsByType<Camera>(FindObjectsSortMode.None))
                {
                    if (Monitor != null && (camera == Monitor.Scope || camera == Monitor.PointerCamera)) continue;
                    exteriorCameraMasks[camera] = camera.cullingMask;
                    camera.cullingMask &= ~(1 << LapCholeAnatomy.Layer);
                }
                if (eye != null)
                {
                    eye.clearFlags = CameraClearFlags.SolidColor;
                    eye.backgroundColor = new Color(0.16f, 0.22f, 0.26f, 1f);
                }
            }
            else
            {
                foreach (Behaviour b in passthrough) if (b != null) b.enabled = true;
                if (passthrough.Count > 0 && OVRManager.instance != null) OVRManager.instance.isInsightPassthroughEnabled = true;
                passthrough.Clear();
                foreach (KeyValuePair<Camera, int> pair in exteriorCameraMasks) if (pair.Key != null) pair.Key.cullingMask = pair.Value;
                exteriorCameraMasks.Clear();
                if (eye != null) { eye.clearFlags = savedClear; eye.backgroundColor = savedBackground; eye.cullingMask = savedMask; }
            }
        }

        private void OnDestroy() { if (active) SetPassthrough(true); }
    }
}
