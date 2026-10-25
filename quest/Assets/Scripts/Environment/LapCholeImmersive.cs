using UnityEngine;

namespace Jeremy
{
    // Headset only: the lap chole scene is a passthrough overlay. The patient and table are built around a fixed
    // board origin while the real room remains visible, with the laparoscope image on a world-space monitor.
    // Budget (Quest 3): ~100k triangles in the eye view, one shadowed directional light, no image effects.
    public sealed class LapCholeImmersive : MonoBehaviour
    {
        public const float NavelHeight = 0.97f; // metres above the floor
        public LapCholeDressing Dressing { get; private set; }
        public LaparoscopeMonitor Monitor { get; private set; }
        private PegTransferRig rig;
        private Camera eye;
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
                Dressing.ShowProps(false, false);
                Monitor.enabled = true;
                Monitor.Scope.enabled = true;
                SetPassthrough(true);
            }
            else
            {
                if (Dressing != null) Dressing.gameObject.SetActive(false);
                if (Monitor != null) { Monitor.Scope.enabled = false; Monitor.enabled = false; }
                SetPassthrough(true);
            }
        }

        /// <summary>Board (navel) half a metre in front of the user at a fixed height above the floor-level XR origin.
        /// The board is not parented to the headset, so the trainee can walk around the patient.</summary>
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
            // OVRManager is configured for FloorLevel tracking. Convert that real-world floor plane into the
            // board-local frame so the table base stays on the physical floor after the board is placed.
            float floorY = board.InverseTransformPoint(new Vector3(board.position.x, 0f, board.position.z)).y;
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
            Dressing.ShowStage(0);
            // The old camera-demo props are not part of the interactive procedure. Leaving them enabled creates
            // duplicate graspers/clip appliers inside the patient before the trainee inserts anything.
            Dressing.ShowProps(false, false);
        }

        private void SetPassthrough(bool on)
        {
            if (!on) return;
            foreach (OVRPassthroughLayer layer in FindObjectsByType<OVRPassthroughLayer>(FindObjectsSortMode.None))
                layer.enabled = true;
            if (OVRManager.instance != null) OVRManager.instance.isInsightPassthroughEnabled = true;
            eye = rig.head != null ? rig.head.GetComponent<Camera>() : Camera.main;
            if (eye != null)
            {
                eye.clearFlags = CameraClearFlags.SolidColor;
                eye.backgroundColor = new Color(0f, 0f, 0f, 0f);
            }
        }

        private void OnDestroy() { SetPassthrough(true); }
    }
}
