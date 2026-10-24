using UnityEngine;
using UnityEngine.Rendering;

namespace Jeremy
{
    // Desktop preview only: the one preview scene, a laparoscopic cholecystectomy styled after the Teleflex
    // animation (docs/LAP_CHOLE_VISUAL_SPEC.md). Board-local frame, origin at the navel on the insufflated skin,
    // x toward the feet, y up, +z the patient's right. The headset path never creates this (it uses passthrough).
    //   LapChole  - minimal room, patient, drape, ports, anatomy, instrument props, camera presets
    //   Monitor   - the laparoscope: scope camera + tip light, scope-as-main-view with the room view as an inset (V)
    public sealed class OperatingRoom : MonoBehaviour
    {
        /// <summary>Trocar an interactive instrument shaft pivots in (side -1 left hand, +1 right hand).</summary>
        public static Vector3 InstrumentPort(int side) => current != null && current.LapChole != null
            ? current.LapChole.PortPosition(side < 0 ? "rsc_medial" : "epigastric")
            : new Vector3(0.12f * side, 0.15f, -0.12f);
        /// <summary>Open surgery (wound closure mechanics): shafts come from above the trainee's hands.</summary>
        public static Vector3 OpenPivot(int side) => new Vector3(0.14f * side, 0.32f, -0.30f);
        private static OperatingRoom current;

        public string ExerciseId { get; private set; } = LapCholeAnatomy.ExerciseId;
        public LaparoscopeMonitor Monitor { get; private set; }
        public LapCholeDressing LapChole { get; private set; }
        private Transform board;
        private ShadowQuality savedShadows;
        private ShadowResolution savedShadowResolution;
        private float savedShadowDistance;
        private int savedPixelLights, savedAntiAliasing;
        private bool savedQuality, aimed;
        private Transform R => transform;

        public static OperatingRoom Create(Transform boardTransform)
        {
            var room = new GameObject("Preview operating room").AddComponent<OperatingRoom>();
            room.Build(boardTransform);
            return room;
        }

        private void Build(Transform boardTransform)
        {
            current = this;
            board = boardTransform;
            transform.SetPositionAndRotation(board.position, board.rotation);
            float floorY = board.position.y > 0.5f ? -board.position.y : -1.15f;
            ConfigureRendering();
            LapChole = LapCholeDressing.Create(R, board, floorY);
            Monitor = gameObject.AddComponent<LaparoscopeMonitor>();
            Monitor.Build(R);
            SetExerciseId(LapCholeAnatomy.ExerciseId);
        }

        /// <summary>The preview scene is always the lap chole room. For lap_chole the rig's interactive tools are hidden
        /// (instrument props stand in until its mechanics exist); for the other exercise ids they stay visible.</summary>
        public void SetExerciseId(string id)
        {
            ExerciseId = id;
            SetInstrumentsVisible(id != LapCholeAnatomy.ExerciseId);
            SetRoomCameraSeesInterior(false);
            if (aimed) return;
            aimed = true;
            LapCholeAnatomy a = LapChole.Anatomy;
            Monitor.Aim(a.ScopePosition, a.ScopeTarget, a.ScopeFieldOfView, true, "LAPAROSCOPE 30°  |  CAM 1  |  CO2 12 mmHg",
                LaparoscopeMonitor.CavityLook.VideoHaze.fog, a.ScopeUp, LaparoscopeMonitor.CavityLook.VideoHaze);
            ApplyCameraPreset("inside");
        }

        private void SetInstrumentsVisible(bool visible)
        {
            foreach (InstrumentController tool in FindObjectsByType<InstrumentController>(FindObjectsSortMode.None))
                foreach (Renderer r in tool.GetComponentsInChildren<Renderer>(true)) r.enabled = visible;
        }

        // The organs are only ever seen through the scope: keep them off the room camera (layer LapCholeAnatomy.Layer).
        private void SetRoomCameraSeesInterior(bool sees)
        {
            Camera view = RoomCamera();
            if (view == null) return;
            int bit = 1 << LapCholeAnatomy.Layer;
            view.cullingMask = sees ? view.cullingMask | bit : view.cullingMask & ~bit;
        }

        private static Camera RoomCamera()
        {
            var preview = FindFirstObjectByType<DesktopPreview>();
            return preview != null && preview.View != null ? preview.View : Camera.main;
        }

        /// <summary>Snap the view to a camera preset (room, veress, umbilical, ports, inside, clip) for verification
        /// against the video; also sets the matching procedure stage and props. False for an unknown preset.</summary>
        public bool ApplyCameraPreset(string name)
        {
            if (!LapCholeDressing.Presets.TryGetValue(name, out LapCholeDressing.Preset p)) return false;
            LapChole.ShowProps(p.inside, p.clip);
            LapChole.ShowStage(p.stage);
            Monitor.SetMainView(p.scope);
            if (p.scope) return true;
            Camera view = RoomCamera();
            if (view == null) return true;
            Vector3 at = board.TransformPoint(p.position);
            view.transform.SetPositionAndRotation(at, Quaternion.LookRotation(board.TransformPoint(p.target) - at, board.up));
            view.fieldOfView = p.fov;
            return true;
        }

        /// <summary>Verification: room camera at board-local "x y z tx ty tz [fov]" (all ports placed, room view main).</summary>
        public bool ApplyFreeView(string args)
        {
            string[] a = args.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (a.Length < 6) return false;
            var f = new float[7];
            for (int i = 0; i < a.Length && i < 7; i++)
                if (!float.TryParse(a[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f[i])) return false;
            LapChole.ShowProps(false, false);
            LapChole.ShowStage(3);
            Monitor.SetMainView(false);
            Camera view = RoomCamera();
            if (view == null) return false;
            Vector3 at = board.TransformPoint(new Vector3(f[0], f[1], f[2]));
            view.transform.SetPositionAndRotation(at, Quaternion.LookRotation(board.TransformPoint(new Vector3(f[3], f[4], f[5])) - at, board.up));
            view.fieldOfView = a.Length > 6 ? f[6] : 45f;
            return true;
        }

        // Soft, cool studio light like the video; restored on exit so the Quest quality settings are untouched.
        private void ConfigureRendering()
        {
            savedShadows = QualitySettings.shadows; savedShadowResolution = QualitySettings.shadowResolution;
            savedShadowDistance = QualitySettings.shadowDistance; savedPixelLights = QualitySettings.pixelLightCount;
            savedAntiAliasing = QualitySettings.antiAliasing; savedQuality = true;
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowResolution = ShadowResolution.High;
            QualitySettings.shadowDistance = 4f;
            QualitySettings.pixelLightCount = 6;
            QualitySettings.antiAliasing = 4;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.42f, 0.5f, 0.56f);
            RenderSettings.ambientEquatorColor = new Color(0.3f, 0.37f, 0.42f);
            RenderSettings.ambientGroundColor = new Color(0.15f, 0.19f, 0.22f);
            RenderSettings.fog = false;
        }

        private void LateUpdate()
        {
            if (board != null) transform.SetPositionAndRotation(board.position, board.rotation);
        }

        private void OnDestroy()
        {
            if (current == this) current = null;
            if (!savedQuality) return;
            QualitySettings.shadows = savedShadows; QualitySettings.shadowResolution = savedShadowResolution;
            QualitySettings.shadowDistance = savedShadowDistance; QualitySettings.pixelLightCount = savedPixelLights;
            QualitySettings.antiAliasing = savedAntiAliasing;
        }
    }
}
