using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Jeremy
{
    // Desktop preview only: a procedural operating room built around the exercise board. Everything is in the
    // board-local frame (x right, y up, z away from the trainee). Laparoscopic exercise: the operative field
    // (PegBoard) lies inside the closed, insufflated abdomen; only trocars and instruments show from outside and the
    // laparoscope inside is the main view. Wound closure: the pad sits in a towelled window on the abdomen.
    // The patient lies along x with the head toward -x. The headset path never creates this (it uses passthrough).
    public sealed class OperatingRoom : MonoBehaviour
    {
        public const float TableTop = -0.26f;
        public const float DomeRx = 0.40f, DomeRz = 0.27f, DomeBase = -0.041f, DomeHeight = 0.27f;
        public static readonly Vector3 FieldTarget = new Vector3(0f, 0.01f, 0.03f);
        /// <summary>Where the laparoscope looks: between the specimens and the retrieval bag.</summary>
        public static readonly Vector3 ScopeTarget = new Vector3(-0.005f, 0f, 0.0f);
        public static float DomeY(float x, float z) =>
            DomeBase + DomeHeight * Mathf.Sqrt(Mathf.Max(0f, 1f - (x / DomeRx) * (x / DomeRx) - (z / DomeRz) * (z / DomeRz)));
        /// <summary>Trocar where an instrument shaft enters the insufflated abdomen (side -1 left, +1 right).</summary>
        public static Vector3 InstrumentPort(int side) => new Vector3(0.13f * side, DomeY(0.13f, -0.13f), -0.13f);
        public static Vector3 CameraPort => new Vector3(0f, DomeY(0f, -0.20f), -0.20f);
        /// <summary>Open surgery: shafts come from above the trainee's hands instead of through ports.</summary>
        public static Vector3 OpenPivot(int side) => new Vector3(0.14f * side, 0.32f, -0.30f);

        public bool WoundClosure { get; private set; }
        public LaparoscopeMonitor Monitor { get; private set; }
        private Transform board;
        private GameObject pegDressing, woundDressing;
        private Transform hourHand, minuteHand, secondHand;
        private LineRenderer ecg, pleth, capno;
        private TextMesh heartRateText, insufflatorText;
        private float floorY, ceilingY, nextVitals, heartRate = 72f;
        private Material steel, white, drape, skin;
        private Mesh torus;
        private ShadowQuality savedShadows;
        private ShadowResolution savedShadowResolution;
        private float savedShadowDistance;
        private int savedPixelLights, savedAntiAliasing;
        private bool savedQuality;
        private Transform R => transform;

        public static OperatingRoom Create(Transform boardTransform)
        {
            var room = new GameObject("Preview operating room").AddComponent<OperatingRoom>();
            room.Build(boardTransform);
            return room;
        }

        private void Build(Transform boardTransform)
        {
            board = boardTransform;
            transform.SetPositionAndRotation(board.position, board.rotation);
            floorY = board.position.y > 0.5f ? -board.position.y : -1.15f;
            ceilingY = floorY + 3f;
            steel = OrKit.Mat(new Color(0.78f, 0.8f, 0.83f), 0.8f, 0.85f);
            white = OrKit.Mat(new Color(0.86f, 0.88f, 0.89f), 0.5f);
            steel = OrKit.Pbr("Metal009", new Color(0.9f, 0.92f, 0.95f), new Vector2(1f, 1f), 0.6f) ?? steel;
            drape = OrKit.Pbr("Fabric030", new Color(0.3f, 0.66f, 0.8f), new Vector2(1f, 1f), 1.2f, 0.05f);
            if (drape == null) { drape = OrKit.Mat(new Color(0.17f, 0.43f, 0.53f), 0.12f); drape.mainTexture = OrKit.FabricTexture(); }
            skin = OrKit.Mat(new Color(0.86f, 0.66f, 0.55f), 0.32f);
            Texture2D skinNormal = SurgicalField.TissueTextures(SurgicalField.Skin, 256).normal;
            skin.SetTexture("_BumpMap", skinNormal); skin.SetFloat("_BumpScale", 0.5f); skin.EnableKeyword("_NORMALMAP");
            skin.mainTextureScale = new Vector2(4f, 4f);
            torus = OrKit.Torus(1f, 0.18f, 20, 8);
            ConfigureRendering();
            BuildRoom();
            BuildSurgicalLights();
            BuildTable();
            BuildPatient();
            BuildDrapes();
            BuildAnaesthesia();
            BuildIvPole();
            BuildInstrumentTables();
            BuildTower();
            pegDressing = Group(R, "Peg transfer dressing", Vector3.zero, Quaternion.identity).gameObject;
            woundDressing = Group(R, "Wound closure dressing", Vector3.zero, Quaternion.identity).gameObject;
            BuildPegDressing(pegDressing.transform);
            BuildWoundDressing(woundDressing.transform);
            Monitor = gameObject.AddComponent<LaparoscopeMonitor>();
            Monitor.Build(R, ceilingY);
            BuildProbe();
            SetExercise(false);
        }

        public void SetExercise(bool wound)
        {
            WoundClosure = wound;
            pegDressing.SetActive(!wound);
            woundDressing.SetActive(wound);
            if (insufflatorText != null) insufflatorText.text = wound ? "STANDBY" : "12 mmHg  4.1 L/min";
            if (wound)
                Monitor.Aim(new Vector3(0f, 0.55f, -0.10f), new Vector3(0f, WoundPad.PadTop, 0f), 30f, false,
                    "FIELD CAMERA  |  LIGHT HANDLE  |  OPEN", new Color(0.05f, 0.06f, 0.07f));
            else
            {
                Vector3 dir = (ScopeTarget - CameraPort).normalized;
                Monitor.Aim(CameraPort + dir * 0.05f, ScopeTarget, 60f, true,
                    "LAPAROSCOPE 30°  |  CAM 1  |  CO2 12 mmHg", new Color(0.06f, 0.015f, 0.01f));
            }
        }

        // ---------- rendering setup (restored on exit so the Quest quality settings are untouched) ----------
        private void ConfigureRendering()
        {
            savedShadows = QualitySettings.shadows; savedShadowResolution = QualitySettings.shadowResolution;
            savedShadowDistance = QualitySettings.shadowDistance; savedPixelLights = QualitySettings.pixelLightCount;
            savedAntiAliasing = QualitySettings.antiAliasing; savedQuality = true;
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowResolution = ShadowResolution.High;
            QualitySettings.shadowDistance = 6f;
            QualitySettings.pixelLightCount = 6;
            QualitySettings.antiAliasing = 4;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.56f, 0.62f, 0.66f);
            RenderSettings.ambientEquatorColor = new Color(0.42f, 0.48f, 0.5f);
            RenderSettings.ambientGroundColor = new Color(0.22f, 0.25f, 0.26f);
            RenderSettings.fog = false;
        }

        private void OnDestroy()
        {
            if (!savedQuality) return;
            QualitySettings.shadows = savedShadows; QualitySettings.shadowResolution = savedShadowResolution;
            QualitySettings.shadowDistance = savedShadowDistance; QualitySettings.pixelLightCount = savedPixelLights;
            QualitySettings.antiAliasing = savedAntiAliasing;
        }

        private void BuildProbe()
        {
            var probeObject = new GameObject("OR reflection probe");
            probeObject.transform.SetParent(R, false);
            probeObject.transform.localPosition = new Vector3(0f, floorY + 1.5f, 0.3f);
            var probe = probeObject.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            probe.resolution = 128;
            probe.size = new Vector3(5.6f, 3f, 5.4f);
            probe.boxProjection = true;
            probe.clearFlags = ReflectionProbeClearFlags.SolidColor;
            probe.backgroundColor = new Color(0.6f, 0.68f, 0.7f);
            StartCoroutine(RenderProbeSoon(probe));
        }

        private static IEnumerator RenderProbeSoon(ReflectionProbe probe)
        {
            yield return null;
            yield return null;
            if (probe != null) probe.RenderProbe();
        }

        // ---------- room shell ----------
        private void BuildRoom()
        {
            Material tile = OrKit.Pbr("Tiles107", new Color(0.86f, 0.9f, 0.9f), new Vector2(8f, 8f), 1f);
            if (tile == null) { tile = OrKit.Mat(Color.white, 0.55f); tile.mainTexture = OrKit.TileTexture(); tile.mainTextureScale = new Vector2(6f, 6f); }
            Material wall = OrKit.Mat(new Color(0.63f, 0.77f, 0.75f), 0.18f);
            Material kick = OrKit.Mat(new Color(0.32f, 0.46f, 0.47f), 0.3f);
            Material ceiling = OrKit.Mat(new Color(0.9f, 0.91f, 0.92f), 0.1f);
            Material panel = OrKit.Glow(new Color(0.93f, 0.96f, 1f), 1.1f);
            float x0 = -2.8f, x1 = 2.8f, z0 = -2.4f, z1 = 3.0f, cz = (z0 + z1) / 2, w = x1 - x0, d = z1 - z0, h = 3f, cy = floorY + h / 2;
            OrKit.Box(R, "Floor", new Vector3(0f, floorY - 0.01f, cz), new Vector3(w, 0.02f, d), tile);
            OrKit.Box(R, "Back wall", new Vector3(0f, cy, z1 + 0.05f), new Vector3(w + 0.2f, h, 0.1f), wall);
            OrKit.Box(R, "Front wall", new Vector3(0f, cy, z0 - 0.05f), new Vector3(w + 0.2f, h, 0.1f), wall);
            OrKit.Box(R, "Left wall", new Vector3(x0 - 0.05f, cy, cz), new Vector3(0.1f, h, d), wall);
            OrKit.Box(R, "Right wall", new Vector3(x1 + 0.05f, cy, cz), new Vector3(0.1f, h, d), wall);
            foreach (float y in new[] { floorY + 0.06f, floorY + 0.9f })
            {
                Material m = y < floorY + 0.5f ? kick : steel;
                Vector2 s = y < floorY + 0.5f ? new Vector2(0.12f, 0.02f) : new Vector2(0.05f, 0.035f);
                OrKit.Box(R, "Wall band", new Vector3(0f, y, z1 - s.y / 2), new Vector3(w, s.x, s.y), m);
                OrKit.Box(R, "Wall band", new Vector3(0f, y, z0 + s.y / 2), new Vector3(w, s.x, s.y), m);
                OrKit.Box(R, "Wall band", new Vector3(x0 + s.y / 2, y, cz), new Vector3(s.y, s.x, d), m);
                OrKit.Box(R, "Wall band", new Vector3(x1 - s.y / 2, y, cz), new Vector3(s.y, s.x, d), m);
            }
            OrKit.Box(R, "Ceiling", new Vector3(0f, ceilingY + 0.025f, cz), new Vector3(w, 0.05f, d), ceiling);
            foreach (float x in new[] { -1.7f, 1.7f })
                foreach (float z in new[] { -1.2f, 1.8f })
                    OrKit.Part(R, "Ceiling light", PrimitiveType.Cube, new Vector3(x, ceilingY - 0.006f, z), new Vector3(0.6f, 0.012f, 1.2f), panel, null, false);
            // Laminar-flow canopy over the table.
            OrKit.Box(R, "Laminar flow canopy", new Vector3(0f, ceilingY - 0.02f, 0.3f), new Vector3(1.8f, 0.04f, 2.2f), white);
            OrKit.Part(R, "Laminar flow diffuser", PrimitiveType.Cube, new Vector3(0f, ceilingY - 0.042f, 0.3f), new Vector3(1.6f, 0.006f, 2.0f),
                OrKit.Glow(new Color(0.8f, 0.86f, 0.9f), 0.45f), null, false);
            foreach (Vector3 p in new[] { new Vector3(-1.2f, ceilingY - 0.35f, 0.4f), new Vector3(1.3f, ceilingY - 0.35f, 0.6f), new Vector3(0f, ceilingY - 0.35f, -1.6f) })
            {
                var fill = new GameObject("Room fill light").AddComponent<Light>();
                fill.transform.SetParent(R, false); fill.transform.localPosition = p;
                fill.type = LightType.Point; fill.range = 4.5f; fill.intensity = 0.55f; fill.color = new Color(0.93f, 0.97f, 1f);
                fill.shadows = LightShadows.None; fill.renderMode = LightRenderMode.ForceVertex;
            }
            BuildWallDetails(z1, x0, x1);
        }

        private void BuildWallDetails(float backZ, float leftX, float rightX)
        {
            Material dark = OrKit.Mat(new Color(0.08f, 0.09f, 0.1f), 0.7f);
            Material glass = OrKit.Fade(new Color(0.75f, 0.88f, 0.92f, 0.25f), 0.9f);
            // Image viewer (PACS) with a chest film.
            var pacs = Group(R, "PACS display", new Vector3(-1.05f, floorY + 1.65f, backZ - 0.03f), Quaternion.identity);
            OrKit.Box(pacs, "Bezel", Vector3.zero, new Vector3(1.1f, 0.66f, 0.04f), dark);
            OrKit.Part(pacs, "Film", PrimitiveType.Quad, new Vector3(-0.26f, 0f, -0.021f), new Vector3(0.5f, 0.6f, 1f), OrKit.Unlit(OrKit.XrayTexture(), false), null, false);
            OrKit.Part(pacs, "Film 2", PrimitiveType.Quad, new Vector3(0.26f, 0f, -0.021f), new Vector3(0.5f, 0.6f, 1f), OrKit.Unlit(OrKit.XrayTexture(), false), null, false);
            // Supply cabinet with glass doors.
            var cabinet = Group(R, "Supply cabinet", new Vector3(1.25f, floorY + 1.55f, backZ - 0.18f), Quaternion.identity);
            OrKit.Box(cabinet, "Body", Vector3.zero, new Vector3(1.4f, 0.9f, 0.36f), steel);
            Material[] boxes = { OrKit.Mat(new Color(0.9f, 0.9f, 0.86f)), OrKit.Mat(new Color(0.35f, 0.55f, 0.8f)), OrKit.Mat(new Color(0.85f, 0.5f, 0.3f)), OrKit.Mat(new Color(0.55f, 0.75f, 0.5f)) };
            for (int shelf = 0; shelf < 3; shelf++)
            {
                float y = -0.33f + shelf * 0.28f;
                OrKit.Box(cabinet, "Shelf", new Vector3(0f, y - 0.06f, -0.02f), new Vector3(1.32f, 0.012f, 0.3f), white);
                for (int k = 0; k < 7; k++)
                    OrKit.Box(cabinet, "Supplies", new Vector3(-0.56f + k * 0.18f, y + 0.02f, -0.05f), new Vector3(0.14f, 0.15f - (k % 3) * 0.02f, 0.2f), boxes[(k + shelf) % boxes.Length]);
            }
            OrKit.Part(cabinet, "Glass doors", PrimitiveType.Cube, new Vector3(0f, 0f, -0.185f), new Vector3(1.36f, 0.86f, 0.008f), glass, null, false);
            // Wall clock.
            var clock = Group(R, "Wall clock", new Vector3(0.05f, floorY + 2.4f, backZ - 0.02f), Quaternion.identity);
            OrKit.Part(clock, "Rim", PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.36f, 0.015f, 0.36f), dark, Quaternion.Euler(90, 0, 0));
            OrKit.Part(clock, "Face", PrimitiveType.Cylinder, new Vector3(0, 0, -0.005f), new Vector3(0.33f, 0.012f, 0.33f), OrKit.Mat(new Color(0.96f, 0.96f, 0.95f), 0.3f), Quaternion.Euler(90, 0, 0));
            for (int k = 0; k < 12; k++)
            {
                float a = k * Mathf.PI / 6f;
                OrKit.Box(clock, "Tick", new Vector3(Mathf.Sin(a) * 0.14f, Mathf.Cos(a) * 0.14f, -0.019f), new Vector3(0.006f, 0.022f, 0.002f), dark, new Vector3(0, 0, -k * 30f));
            }
            hourHand = Hand(clock, 0.075f, 0.01f, dark, -0.021f);
            minuteHand = Hand(clock, 0.12f, 0.007f, dark, -0.023f);
            secondHand = Hand(clock, 0.13f, 0.003f, OrKit.Mat(new Color(0.85f, 0.15f, 0.12f)), -0.025f);
            // Sliding door with a window on the left wall, and a medical gas panel near the anaesthesia machine.
            var door = Group(R, "Door", new Vector3(leftX + 0.03f, floorY + 1.08f, -1.0f), Quaternion.identity);
            OrKit.Box(door, "Leaf", Vector3.zero, new Vector3(0.05f, 2.16f, 1.3f), steel);
            OrKit.Part(door, "Window", PrimitiveType.Cube, new Vector3(0.028f, 0.35f, 0f), new Vector3(0.01f, 0.4f, 0.5f), OrKit.Mat(new Color(0.25f, 0.33f, 0.38f), 0.9f), null, false);
            OrKit.Box(door, "Push plate", new Vector3(0.03f, 0f, -0.45f), new Vector3(0.01f, 0.3f, 0.12f), white);
            var gas = Group(R, "Gas outlets", new Vector3(leftX + 0.02f, floorY + 1.45f, 0.6f), Quaternion.identity);
            OrKit.Box(gas, "Panel", Vector3.zero, new Vector3(0.03f, 0.3f, 0.7f), white);
            Color[] gasColours = { new Color(0.1f, 0.6f, 0.25f), new Color(0.95f, 0.85f, 0.2f), new Color(0.2f, 0.35f, 0.8f), new Color(0.15f, 0.15f, 0.15f) };
            for (int k = 0; k < 4; k++)
                OrKit.Part(gas, "Outlet", PrimitiveType.Cylinder, new Vector3(0.025f, 0f, -0.24f + k * 0.16f), new Vector3(0.06f, 0.012f, 0.06f), OrKit.Mat(gasColours[k], 0.5f), Quaternion.Euler(0, 0, 90));
            // A wall cabinet on the right for balance.
            var side = Group(R, "Side cabinet", new Vector3(rightX - 0.25f, floorY + 0.45f, -0.6f), Quaternion.identity);
            OrKit.Box(side, "Body", Vector3.zero, new Vector3(0.5f, 0.9f, 1.4f), steel);
            for (int k = 0; k < 3; k++) OrKit.Box(side, "Handle", new Vector3(-0.255f, 0.3f - k * 0.3f, 0f), new Vector3(0.015f, 0.02f, 0.3f), dark);
        }

        private Transform Hand(Transform clock, float length, float width, Material material, float z)
        {
            var pivot = Group(clock, "Hand", new Vector3(0, 0, z), Quaternion.identity);
            OrKit.Box(pivot, "Blade", new Vector3(0f, length * 0.4f, 0f), new Vector3(width, length, 0.002f), material);
            return pivot;
        }

        // ---------- two surgical lights on spring arms, each with a real shadowing spotlight ----------
        private void BuildSurgicalLights()
        {
            Material arm = OrKit.Mat(new Color(0.9f, 0.92f, 0.93f), 0.55f);
            Material face = OrKit.Glow(new Color(0.85f, 0.9f, 0.95f), 0.85f);
            Material cell = OrKit.Glow(new Color(1f, 1f, 1f), 2.2f);
            Material handle = OrKit.Mat(new Color(0.3f, 0.5f, 0.68f), 0.4f);
            Vector3 top = new Vector3(0.05f, ceilingY, 0.25f), hub = new Vector3(0.05f, ceilingY - 0.42f, 0.25f);
            OrKit.Part(R, "Light mount cover", PrimitiveType.Cylinder, top + Vector3.down * 0.06f, new Vector3(0.3f, 0.02f, 0.3f), arm);
            OrKit.Rod(R, "Light mount column", top, hub, 0.035f, arm);
            OrKit.Sphere(R, "Light hub", hub, Vector3.one * 0.1f, arm);
            Lamp("Surgical light A", new Vector3(-0.38f, 0.95f, -0.02f), hub, arm, face, cell, handle);
            Lamp("Surgical light B", new Vector3(0.32f, 0.9f, 0.52f), hub, arm, face, cell, handle);
        }

        private void Lamp(string name, Vector3 position, Vector3 hub, Material arm, Material face, Material cell, Material handle)
        {
            Vector3 elbow = position + Vector3.up * 0.34f;
            OrKit.Rod(R, name + " arm", hub, elbow, 0.022f, arm);
            OrKit.Sphere(R, name + " joint", elbow, Vector3.one * 0.06f, arm);
            OrKit.Rod(R, name + " yoke", elbow, position + Vector3.up * 0.1f, 0.016f, arm);
            Transform head = Group(R, name, position, Quaternion.FromToRotation(Vector3.down, FieldTarget - position));
            OrKit.Sphere(head, "Housing", new Vector3(0f, 0.06f, 0f), new Vector3(0.62f, 0.15f, 0.62f), arm);
            OrKit.Part(head, "Rim", PrimitiveType.Cylinder, new Vector3(0f, -0.012f, 0f), new Vector3(0.64f, 0.012f, 0.64f), arm);
            OrKit.Part(head, "Diffuser", PrimitiveType.Cylinder, new Vector3(0f, -0.026f, 0f), new Vector3(0.58f, 0.004f, 0.58f), face, null, false);
            for (int k = 0; k < 12; k++)
            {
                float a = k * Mathf.PI / 6f + (k % 2) * 0.26f, r = k % 2 == 0 ? 0.2f : 0.11f;
                OrKit.Part(head, "LED cell", PrimitiveType.Cylinder, new Vector3(Mathf.Cos(a) * r, -0.031f, Mathf.Sin(a) * r), new Vector3(0.085f, 0.003f, 0.085f), cell, null, false);
            }
            OrKit.Rod(head, "Sterile handle", new Vector3(0f, -0.03f, 0f), new Vector3(0f, -0.14f, 0f), 0.018f, handle);
            var lightObject = new GameObject(name + " spot");
            lightObject.transform.SetParent(head, false);
            lightObject.transform.localPosition = new Vector3(0f, -0.15f, 0f);
            lightObject.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // forward = lamp down
            Light spot = lightObject.AddComponent<Light>();
            spot.type = LightType.Spot; spot.spotAngle = 48f; spot.innerSpotAngle = 26f; spot.range = 3.2f;
            spot.intensity = 1.25f; spot.color = new Color(0.96f, 0.98f, 1f);
            spot.shadows = LightShadows.Soft; spot.shadowStrength = 0.7f; spot.shadowResolution = LightShadowResolution.High;
            spot.shadowNearPlane = 0.1f; spot.renderMode = LightRenderMode.ForcePixel;
        }

        // ---------- OR table ----------
        private void BuildTable()
        {
            float T = TableTop;
            Material pad = OrKit.Mat(new Color(0.12f, 0.15f, 0.22f), 0.35f);
            Material body = OrKit.Mat(new Color(0.84f, 0.86f, 0.87f), 0.55f);
            OrKit.Box(R, "Table mattress", new Vector3(0.15f, T - 0.04f, 0f), new Vector3(2.0f, 0.08f, 0.6f), pad);
            OrKit.Box(R, "Table frame", new Vector3(0.15f, T - 0.105f, 0f), new Vector3(2.02f, 0.05f, 0.56f), steel);
            foreach (float z in new[] { -0.3f, 0.3f })
                OrKit.Rod(R, "Side rail", new Vector3(-0.82f, T - 0.1f, z), new Vector3(1.12f, T - 0.1f, z), 0.008f, steel);
            float columnTop = T - 0.13f, columnBottom = floorY + 0.09f;
            OrKit.Box(R, "Table column", new Vector3(0.1f, (columnTop + columnBottom) / 2, 0f), new Vector3(0.34f, columnTop - columnBottom, 0.24f), body);
            OrKit.Box(R, "Table base", new Vector3(0.1f, floorY + 0.045f, 0f), new Vector3(1.0f, 0.09f, 0.52f), body);
            OrKit.Box(R, "Arm board", new Vector3(-0.40f, T - 0.02f, 0.6f), new Vector3(0.16f, 0.035f, 0.62f), pad);
            OrKit.Rod(R, "Arm board strut", new Vector3(-0.40f, T - 0.04f, 0.3f), new Vector3(-0.40f, T - 0.04f, 0.88f), 0.01f, steel);
        }

        // ---------- patient: head on a pillow, one arm out on an arm board; the rest is under the drapes ----------
        private void BuildPatient()
        {
            float T = TableTop;
            Material cap = OrKit.Mat(new Color(0.36f, 0.56f, 0.76f), 0.08f);
            Material pillow = OrKit.Mat(new Color(0.9f, 0.93f, 0.95f), 0.1f);
            Material tape = OrKit.Mat(new Color(0.95f, 0.95f, 0.93f), 0.2f);
            OrKit.Sphere(R, "Pillow", new Vector3(-0.66f, T + 0.04f, 0f), new Vector3(0.32f, 0.09f, 0.46f), pillow);
            OrKit.Sphere(R, "Head", new Vector3(-0.645f, T + 0.165f, 0f), new Vector3(0.215f, 0.195f, 0.155f), skin);
            OrKit.Sphere(R, "Surgical cap", new Vector3(-0.69f, T + 0.155f, 0f), new Vector3(0.17f, 0.19f, 0.172f), cap);
            OrKit.Sphere(R, "Nose", new Vector3(-0.60f, T + 0.258f, 0f), new Vector3(0.04f, 0.03f, 0.026f), skin);
            foreach (float z in new[] { -0.03f, 0.03f })
                OrKit.Box(R, "Eye tape", new Vector3(-0.635f, T + 0.2555f, z), new Vector3(0.016f, 0.004f, 0.028f), tape);
            foreach (float z in new[] { -0.077f, 0.077f })
                OrKit.Sphere(R, "Ear", new Vector3(-0.655f, T + 0.165f, z), new Vector3(0.05f, 0.055f, 0.018f), skin);
            OrKit.Capsule(R, "Neck", new Vector3(-0.56f, T + 0.13f, 0f), new Vector3(-0.47f, T + 0.14f, 0f), 0.055f, skin);
            OrKit.Rod(R, "Endotracheal tube", new Vector3(-0.572f, T + 0.225f, 0f), new Vector3(-0.57f, T + 0.31f, 0f), 0.006f,
                OrKit.Mat(new Color(0.85f, 0.9f, 0.95f), 0.8f));
            OrKit.Capsule(R, "Arm", new Vector3(-0.43f, T + 0.075f, 0.24f), new Vector3(-0.41f, T + 0.05f, 0.79f), 0.043f, skin);
            OrKit.Capsule(R, "Arm drape", new Vector3(-0.43f, T + 0.08f, 0.2f), new Vector3(-0.42f, T + 0.07f, 0.5f), 0.052f, drape);
            OrKit.Sphere(R, "Hand", new Vector3(-0.41f, T + 0.04f, 0.85f), new Vector3(0.085f, 0.035f, 0.09f), skin);
            OrKit.Box(R, "IV dressing", new Vector3(-0.41f, T + 0.092f, 0.76f), new Vector3(0.05f, 0.006f, 0.045f), tape);
            OrKit.Part(R, "Pulse oximeter", PrimitiveType.Cube, new Vector3(-0.41f, T + 0.05f, 0.9f), new Vector3(0.025f, 0.025f, 0.035f),
                OrKit.Glow(new Color(0.9f, 0.12f, 0.1f), 0.9f));
        }

        // ---------- drapes: one sheet shaped over the body, with a window where the board sits ----------
        private void BuildDrapes()
        {
            float T = TableTop;
            var sheet = new GameObject("Surgical drape", typeof(MeshFilter), typeof(MeshRenderer));
            sheet.transform.SetParent(R, false);
            sheet.GetComponent<MeshFilter>().sharedMesh = DrapeMesh();
            sheet.GetComponent<MeshRenderer>().sharedMaterial = drape;
            // Prepped skin (iodine-tinted) showing inside the drape window, under the board.
            OrKit.Part(R, "Prepped skin", PrimitiveType.Cylinder, new Vector3(0f, DomeBase - 0.003f, 0f),
                new Vector3(DomeRx * 2.24f, 0.002f, DomeRz * 2.24f), OrKit.Mat(new Color(0.74f, 0.47f, 0.34f), 0.45f));
            // Anaesthesia screen at the patient's neck: drape over a bar.
            OrKit.Box(R, "Anaesthesia screen drape", new Vector3(-0.50f, T - 0.08f, 0f), new Vector3(0.012f, 0.84f, 1.36f), drape);
            OrKit.Rod(R, "Anaesthesia screen bar", new Vector3(-0.505f, T + 0.345f, -0.7f), new Vector3(-0.505f, T + 0.345f, 0.7f), 0.009f, steel);
            foreach (float z in new[] { -0.34f, 0.34f })
                OrKit.Rod(R, "Anaesthesia screen post", new Vector3(-0.512f, T - 0.12f, z), new Vector3(-0.512f, T + 0.345f, z), 0.009f, steel);
        }

        private static readonly float[] HeightKeys = { -0.55f, 0.16f, -0.40f, 0.185f, -0.25f, 0.2f, 0.2f, 0.2f, 0.42f, 0.16f, 0.7f, 0.13f, 0.98f, 0.1f, 1.2f, 0.09f };
        private static readonly float[] WidthKeys = { -0.55f, 0.25f, -0.4f, 0.26f, -0.3f, 0.28f, 0.3f, 0.28f, 0.45f, 0.22f, 0.8f, 0.18f, 1.0f, 0.15f, 1.2f, 0.15f };

        private static float Keyed(float x, float[] keys)
        {
            if (x <= keys[0]) return keys[1];
            for (int k = 2; k < keys.Length; k += 2)
                if (x <= keys[k]) return Mathf.Lerp(keys[k - 1], keys[k + 1], Mathf.SmoothStep(0f, 1f, (x - keys[k - 2]) / (keys[k] - keys[k - 2])));
            return keys[keys.Length - 1];
        }

        private static float SmoothMax(float a, float b, float k)
        {
            float h = Mathf.Clamp01(0.5f + 0.5f * (a - b) / k);
            return Mathf.Lerp(b, a, h) + k * h * (1f - h);
        }

        private static float Gauss(float x, float s) => Mathf.Exp(-x * x / (s * s));

        // Drape surface on the table: body profile, raised flat around the board, lying flat on the mattress elsewhere.
        private static float DrapeHeight(float x, float z)
        {
            float T = TableTop, W = Keyed(x, WidthKeys);
            float q = Mathf.Clamp01(1f - (z / W) * (z / W));
            float body = T + Keyed(x, HeightKeys) * Mathf.Pow(q, 0.45f);
            body -= 0.025f * Gauss(z, 0.04f) * Mathf.SmoothStep(0f, 1f, (x - 0.45f) / 0.15f); // between the legs
            body += 0.07f * Gauss(x - 1.04f, 0.035f) * (Gauss(z - 0.08f, 0.05f) + Gauss(z + 0.08f, 0.05f)); // feet
            float e = Mathf.Sqrt((x / DomeRx) * (x / DomeRx) + (z / DomeRz) * (z / DomeRz));
            float plateau = DomeBase - 0.003f - 0.17f * Mathf.SmoothStep(0f, 1f, (e - 1.08f) / 0.45f);
            float y = SmoothMax(body, plateau, 0.012f);
            y = SmoothMax(y, T + 0.008f, 0.01f);
            return y + 0.003f * Mathf.Sin(x * 41f + z * 13f) * Mathf.Sin(z * 31f - x * 9f) * Mathf.Clamp01((e - 1.1f) * 3f);
        }

        private static Mesh DrapeMesh()
        {
            const float u0 = -0.5f, xEnd = 1.15f, hang = 0.55f, tw = 0.31f;
            const int nu = 96, nv = 60;
            float u1 = xEnd + hang, v0 = -(tw + hang);
            var vertices = new Vector3[(nu + 1) * (nv + 1)];
            var uv = new Vector2[vertices.Length];
            for (int i = 0; i <= nu; i++)
                for (int j = 0; j <= nv; j++)
                {
                    float u = Mathf.Lerp(u0, u1, i / (float)nu), v = Mathf.Lerp(v0, -v0, j / (float)nv);
                    float xc = Mathf.Min(u, xEnd), zc = Mathf.Clamp(v, -tw, tw);
                    float ox = Mathf.Max(0f, u - xEnd), oz = Mathf.Max(0f, Mathf.Abs(v) - tw), s = Mathf.Sqrt(ox * ox + oz * oz);
                    // Past the table edge the sheet rolls over and hangs, with a few soft folds.
                    var p = new Vector3(xc, DrapeHeight(xc, zc) - s * s / (s + 0.03f), zc);
                    float flare = 0.035f * (1f - Mathf.Exp(-s / 0.03f)), fold = 0.014f * Mathf.Sin(u * 23f + v * 5f) * Mathf.Clamp01(s * 5f);
                    if (s > 0f) { p.x += flare * ox / s; p.z += Mathf.Sign(v) * flare * oz / s; }
                    if (oz > 0f) p.z += Mathf.Sign(v) * fold; else if (ox > 0f) p.x += fold;
                    vertices[i * (nv + 1) + j] = p;
                    uv[i * (nv + 1) + j] = new Vector2(u * 4f, v * 4f);
                }
            var triangles = new System.Collections.Generic.List<int>(nu * nv * 6);
            for (int i = 0; i < nu; i++)
                for (int j = 0; j < nv; j++)
                {
                    float uc = Mathf.Lerp(u0, u1, (i + 0.5f) / nu), vc = Mathf.Lerp(v0, -v0, (j + 0.5f) / nv);
                    float ex = uc / (DomeRx * 1.08f), ez = vc / (DomeRz * 1.08f);
                    if (ex * ex + ez * ez < 1f) continue; // surgical window around the board
                    int a0 = i * (nv + 1) + j, a1 = a0 + 1, b0 = a0 + nv + 1, b1 = b0 + 1;
                    triangles.Add(a0); triangles.Add(a1); triangles.Add(b0);
                    triangles.Add(b0); triangles.Add(a1); triangles.Add(b1);
                }
            var mesh = new Mesh { name = "Drape" };
            mesh.vertices = vertices; mesh.uv = uv; mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        // ---------- anaesthesia machine with a live vitals monitor ----------
        private void BuildAnaesthesia()
        {
            float T = TableTop;
            Vector3 position = new Vector3(-1.25f, floorY, 0.5f);
            Vector3 facing = new Vector3(-0.35f, floorY, -0.6f) - position;
            Transform g = Group(R, "Anaesthesia machine", position, Quaternion.LookRotation(-facing)); // front (-z) faces the table
            Material dark = OrKit.Mat(new Color(0.18f, 0.2f, 0.23f), 0.4f);
            Material panel = OrKit.Mat(new Color(0.93f, 0.94f, 0.95f), 0.45f);
            Material black = OrKit.Mat(new Color(0.02f, 0.02f, 0.025f), 0.85f);
            foreach (float x in new[] { -0.3f, 0.3f })
                foreach (float z in new[] { -0.24f, 0.24f })
                    OrKit.Part(g, "Caster", PrimitiveType.Cylinder, new Vector3(x, 0.04f, z), new Vector3(0.07f, 0.02f, 0.07f), dark, Quaternion.Euler(0, 0, 90));
            OrKit.Box(g, "Base", new Vector3(0f, 0.12f, 0f), new Vector3(0.78f, 0.08f, 0.62f), dark);
            OrKit.Box(g, "Cabinet", new Vector3(0f, 0.5f, 0f), new Vector3(0.75f, 0.68f, 0.58f), white);
            for (int k = 0; k < 3; k++)
            {
                OrKit.Box(g, "Drawer", new Vector3(0f, 0.28f + 0.215f * k, -0.295f), new Vector3(0.68f, 0.19f, 0.012f), panel);
                OrKit.Box(g, "Drawer handle", new Vector3(0f, 0.33f + 0.215f * k, -0.305f), new Vector3(0.25f, 0.018f, 0.02f), dark);
            }
            OrKit.Box(g, "Work surface", new Vector3(0f, 0.86f, 0f), new Vector3(0.84f, 0.035f, 0.66f), dark);
            OrKit.Box(g, "Upper body", new Vector3(0f, 1.19f, 0.08f), new Vector3(0.72f, 0.62f, 0.42f), white);
            OrKit.Box(g, "Flowmeter panel", new Vector3(-0.18f, 1.12f, -0.135f), new Vector3(0.24f, 0.22f, 0.01f), black);
            Color[] flows = { new Color(0.2f, 0.9f, 0.4f), new Color(0.95f, 0.95f, 0.9f), new Color(0.3f, 0.55f, 1f) };
            for (int k = 0; k < 3; k++)
                OrKit.Part(g, "Flow tube", PrimitiveType.Cube, new Vector3(-0.24f + k * 0.06f, 1.12f, -0.142f), new Vector3(0.012f, 0.16f, 0.004f), OrKit.Glow(flows[k], 0.6f), null, false);
            OrKit.Box(g, "Vaporizer sevoflurane", new Vector3(0.1f, 1.02f, -0.195f), new Vector3(0.1f, 0.17f, 0.13f), OrKit.Mat(new Color(0.95f, 0.78f, 0.2f), 0.5f));
            OrKit.Box(g, "Vaporizer desflurane", new Vector3(0.23f, 1.02f, -0.195f), new Vector3(0.1f, 0.17f, 0.13f), OrKit.Mat(new Color(0.2f, 0.45f, 0.85f), 0.5f));
            OrKit.Part(g, "Bellows housing", PrimitiveType.Cylinder, new Vector3(-0.44f, 1.03f, -0.05f), new Vector3(0.15f, 0.13f, 0.15f), OrKit.Fade(new Color(0.8f, 0.9f, 0.95f, 0.3f), 0.9f), null, false);
            OrKit.Part(g, "Bellows", PrimitiveType.Cylinder, new Vector3(-0.44f, 1.0f, -0.05f), new Vector3(0.11f, 0.09f, 0.11f), OrKit.Mat(new Color(0.35f, 0.55f, 0.8f), 0.3f));
            OrKit.Rod(g, "O2 cylinder", new Vector3(-0.2f, 0.2f, 0.36f), new Vector3(-0.2f, 0.85f, 0.36f), 0.055f, OrKit.Mat(new Color(0.15f, 0.55f, 0.3f), 0.5f));
            OrKit.Rod(g, "N2O cylinder", new Vector3(0.0f, 0.2f, 0.36f), new Vector3(0.0f, 0.85f, 0.36f), 0.055f, OrKit.Mat(new Color(0.2f, 0.3f, 0.7f), 0.5f));
            OrKit.Rod(g, "Monitor arm", new Vector3(0f, 1.5f, 0.08f), new Vector3(0f, 1.62f, -0.02f), 0.02f, steel);
            Transform screen = Group(g, "Vitals monitor", new Vector3(0f, 1.78f, -0.06f), Quaternion.identity);
            OrKit.Box(screen, "Bezel", Vector3.zero, new Vector3(0.52f, 0.36f, 0.05f), dark);
            OrKit.Part(screen, "Screen", PrimitiveType.Quad, new Vector3(0f, 0f, -0.026f), new Vector3(0.48f, 0.32f, 1f), black, null, false);
            Color green = new Color(0.3f, 1f, 0.45f), cyan = new Color(0.3f, 0.9f, 1f), yellow = new Color(1f, 0.9f, 0.3f);
            ecg = OrKit.Line(screen, "ECG", new Vector3[90], 0.003f, green);
            pleth = OrKit.Line(screen, "Pleth", new Vector3[90], 0.003f, cyan);
            capno = OrKit.Line(screen, "Capnogram", new Vector3[90], 0.003f, yellow);
            OrKit.Text(screen, "HR label", "HR", new Vector3(0.11f, 0.125f, -0.028f), 0.016f, green, TextAnchor.MiddleLeft);
            heartRateText = OrKit.Text(screen, "HR", "72", new Vector3(0.22f, 0.085f, -0.028f), 0.06f, green, TextAnchor.MiddleRight);
            OrKit.Text(screen, "SpO2 label", "SpO2", new Vector3(0.11f, 0.035f, -0.028f), 0.016f, cyan, TextAnchor.MiddleLeft);
            OrKit.Text(screen, "SpO2", "99", new Vector3(0.22f, -0.005f, -0.028f), 0.05f, cyan, TextAnchor.MiddleRight);
            OrKit.Text(screen, "EtCO2", "EtCO2 35", new Vector3(0.22f, -0.085f, -0.028f), 0.026f, yellow, TextAnchor.MiddleRight);
            OrKit.Text(screen, "NIBP", "NIBP 118/76 (90)   T 36.6", new Vector3(-0.225f, -0.135f, -0.028f), 0.02f, new Color(0.95f, 0.5f, 0.5f), TextAnchor.MiddleLeft);
            // Breathing circuit from the machine to the endotracheal tube.
            Material circuit = OrKit.Mat(new Color(0.55f, 0.72f, 0.88f), 0.6f);
            foreach (float offset in new[] { -0.014f, 0.014f })
            {
                Vector3 port = position + g.localRotation * new Vector3(0.34f + offset, 0.95f, -0.32f);
                Vector3 mouth = new Vector3(-0.57f, T + 0.31f, offset);
                Vector3 mid = new Vector3(-0.85f, T + 0.42f, 0.2f + offset);
                OrKit.Tube(R, "Breathing circuit", Join(OrKit.Sag(port, mid, 0.08f, 8), OrKit.Sag(mid, mouth, 0.03f, 6)), 0.011f, circuit);
            }
        }

        private static Vector3[] Join(Vector3[] a, Vector3[] b)
        {
            var all = new Vector3[a.Length + b.Length - 1];
            a.CopyTo(all, 0);
            for (int i = 1; i < b.Length; i++) all[a.Length + i - 1] = b[i];
            return all;
        }

        // ---------- IV pole with bag, pump and line to the hand ----------
        private void BuildIvPole()
        {
            float T = TableTop;
            Vector3 foot = new Vector3(-0.62f, floorY, 0.98f);
            Material dark = OrKit.Mat(new Color(0.15f, 0.16f, 0.18f), 0.4f);
            OrKit.Rod(R, "IV pole", foot + Vector3.up * 0.06f, foot + Vector3.up * 2.05f, 0.011f, steel);
            for (int k = 0; k < 5; k++)
            {
                float a = k * Mathf.PI * 0.4f;
                Vector3 tip = foot + new Vector3(Mathf.Cos(a) * 0.28f, 0.05f, Mathf.Sin(a) * 0.28f);
                OrKit.Rod(R, "IV pole leg", foot + Vector3.up * 0.07f, tip, 0.012f, steel);
                OrKit.Sphere(R, "Caster", tip + Vector3.down * 0.025f, Vector3.one * 0.045f, dark);
            }
            OrKit.Rod(R, "IV hooks", foot + new Vector3(-0.12f, 2.02f, 0f), foot + new Vector3(0.12f, 2.02f, 0f), 0.006f, steel);
            Vector3 bag = foot + new Vector3(0.1f, 1.84f, 0f);
            OrKit.Part(R, "IV bag", PrimitiveType.Sphere, bag, new Vector3(0.12f, 0.2f, 0.045f), OrKit.Fade(new Color(0.88f, 0.94f, 1f, 0.55f), 0.9f), null, false);
            OrKit.Box(R, "IV bag label", bag + new Vector3(0f, 0.01f, -0.02f), new Vector3(0.07f, 0.06f, 0.004f), white);
            Vector3 chamber = bag + Vector3.down * 0.15f;
            OrKit.Part(R, "Drip chamber", PrimitiveType.Cylinder, chamber, new Vector3(0.022f, 0.03f, 0.022f), OrKit.Fade(new Color(0.9f, 0.95f, 1f, 0.5f), 0.9f), null, false);
            Transform pump = Group(R, "Infusion pump", foot + new Vector3(0f, 1.22f, -0.07f), Quaternion.LookRotation(new Vector3(0.3f, 0f, 1f)));
            OrKit.Box(pump, "Body", Vector3.zero, new Vector3(0.13f, 0.17f, 0.1f), white);
            OrKit.Part(pump, "Display", PrimitiveType.Cube, new Vector3(0f, 0.035f, -0.051f), new Vector3(0.09f, 0.05f, 0.003f), OrKit.Glow(new Color(0.25f, 0.85f, 0.5f), 0.8f), null, false);
            Vector3 hand = new Vector3(-0.41f, T + 0.095f, 0.76f);
            OrKit.Tube(R, "IV line", Join(OrKit.Sag(chamber + Vector3.down * 0.03f, foot + new Vector3(0.06f, 1.22f, -0.13f), 0.05f, 6),
                OrKit.Sag(foot + new Vector3(0.06f, 1.22f, -0.13f), hand, 0.35f, 12)), 0.0025f, OrKit.Mat(new Color(0.9f, 0.94f, 0.97f), 0.8f));
        }

        // ---------- Mayo stand over the legs and an instrument back table ----------
        private void BuildInstrumentTables()
        {
            float T = TableTop, trayY = T + 0.38f;
            Material blade = OrKit.Mat(new Color(0.9f, 0.92f, 0.95f), 0.95f, 0.9f);
            Material gauze = OrKit.Mat(new Color(0.97f, 0.97f, 0.95f), 0.05f);
            OrKit.Rod(R, "Mayo post", new Vector3(0.62f, floorY + 0.05f, 0.48f), new Vector3(0.62f, trayY - 0.01f, 0.48f), 0.016f, steel);
            OrKit.Rod(R, "Mayo base", new Vector3(0.36f, floorY + 0.03f, 0.48f), new Vector3(0.88f, floorY + 0.03f, 0.48f), 0.014f, steel);
            OrKit.Rod(R, "Mayo arm", new Vector3(0.62f, trayY - 0.01f, 0.48f), new Vector3(0.62f, trayY - 0.01f, 0.08f), 0.012f, steel);
            OrKit.Box(R, "Mayo tray", new Vector3(0.62f, trayY, -0.02f), new Vector3(0.5f, 0.012f, 0.36f), steel);
            OrKit.Box(R, "Mayo towel", new Vector3(0.62f, trayY + 0.008f, -0.02f), new Vector3(0.48f, 0.004f, 0.34f), drape);
            float top = trayY + 0.01f;
            for (int k = 0; k < 4; k++) RingInstrument(R, new Vector3(0.45f + k * 0.07f, top, -0.17f), 0f, 0.12f + (k % 2) * 0.03f);
            Scalpel(R, new Vector3(0.78f, top, -0.12f), 5f, blade);
            Forceps(R, new Vector3(0.84f, top, -0.12f), -4f);
            for (int k = 0; k < 3; k++) OrKit.Box(R, "Gauze", new Vector3(0.52f, top + 0.006f + k * 0.008f, 0.08f), new Vector3(0.09f, 0.008f, 0.09f), gauze, new Vector3(0, k * 9f, 0));
            OrKit.Box(R, "Suture packs", new Vector3(0.7f, top + 0.004f, 0.09f), new Vector3(0.1f, 0.008f, 0.06f), OrKit.Mat(new Color(0.48f, 0.28f, 0.62f), 0.3f), new Vector3(0, 8, 0));

            Transform back = Group(R, "Back table", new Vector3(1.55f, floorY, 0.55f), Quaternion.identity);
            foreach (float x in new[] { -0.32f, 0.32f })
                foreach (float z in new[] { -0.62f, 0.62f })
                    OrKit.Rod(back, "Leg", new Vector3(x, 0.02f, z), new Vector3(x, 0.89f, z), 0.014f, steel);
            OrKit.Box(back, "Top", new Vector3(0f, 0.9f, 0f), new Vector3(0.7f, 0.03f, 1.3f), steel);
            OrKit.Box(back, "Cover", new Vector3(0f, 0.918f, 0f), new Vector3(0.76f, 0.006f, 1.36f), drape);
            OrKit.Box(back, "Cover skirt", new Vector3(-0.38f, 0.74f, 0f), new Vector3(0.006f, 0.36f, 1.36f), drape);
            OrKit.Box(back, "Cover skirt", new Vector3(0.38f, 0.74f, 0f), new Vector3(0.006f, 0.36f, 1.36f), drape);
            OrKit.Box(back, "Cover skirt", new Vector3(0f, 0.74f, -0.68f), new Vector3(0.76f, 0.36f, 0.006f), drape);
            OrKit.Box(back, "Cover skirt", new Vector3(0f, 0.74f, 0.68f), new Vector3(0.76f, 0.36f, 0.006f), drape);
            float y = 0.921f;
            OrKit.Box(back, "Instrument tray", new Vector3(0f, y + 0.02f, -0.3f), new Vector3(0.48f, 0.04f, 0.34f), steel);
            for (int k = 0; k < 5; k++) RingInstrument(back, new Vector3(-0.16f + k * 0.08f, y + 0.042f, -0.42f), 0f, 0.13f + (k % 3) * 0.02f);
            for (int k = 0; k < 2; k++)
            {
                OrKit.Rod(back, "Laparoscopic instrument", new Vector3(-0.2f, y + 0.006f, 0.0f + k * 0.05f), new Vector3(0.15f, y + 0.006f, 0.0f + k * 0.05f), 0.0035f, OrKit.Mat(new Color(0.05f, 0.05f, 0.06f), 0.5f));
                OrKit.Box(back, "Laparoscopic handle", new Vector3(0.18f, y + 0.02f, 0.0f + k * 0.05f), new Vector3(0.06f, 0.03f, 0.02f), OrKit.Mat(new Color(0.3f, 0.32f, 0.35f), 0.4f));
            }
            OrKit.Part(back, "Bowl", PrimitiveType.Cylinder, new Vector3(-0.12f, y + 0.03f, 0.3f), new Vector3(0.17f, 0.03f, 0.17f), steel);
            OrKit.Part(back, "Bowl", PrimitiveType.Cylinder, new Vector3(0.12f, y + 0.025f, 0.36f), new Vector3(0.12f, 0.025f, 0.12f), steel);
            OrKit.Sphere(back, "Kidney dish", new Vector3(0.1f, y + 0.012f, 0.16f), new Vector3(0.2f, 0.03f, 0.1f), steel);
            for (int k = 0; k < 4; k++) OrKit.Box(back, "Folded towel", new Vector3(-0.15f, y + 0.012f + k * 0.022f, 0.52f), new Vector3(0.25f, 0.02f, 0.18f), drape, new Vector3(0, k * 4f, 0));
            for (int k = 0; k < 3; k++) OrKit.Box(back, "Gauze", new Vector3(0.15f, y + 0.006f + k * 0.008f, 0.55f), new Vector3(0.1f, 0.008f, 0.1f), gauze, new Vector3(0, k * 11f, 0));
        }

        private void RingInstrument(Transform parent, Vector3 at, float yaw, float length)
        {
            Transform g = Group(parent, "Ring instrument", at, Quaternion.Euler(0f, yaw, 0f));
            foreach (int s in new[] { -1, 1 })
            {
                OrKit.Box(g, "Shank", new Vector3(0.0035f * s, 0.003f, length * 0.5f), new Vector3(0.0035f, 0.003f, length), steel, new Vector3(0, -2f * s, 0));
                OrKit.MeshPart(g, "Finger ring", torus, new Vector3(0.012f * s, 0.003f, -0.012f), Quaternion.identity, Vector3.one * 0.011f, steel);
            }
        }

        private void Scalpel(Transform parent, Vector3 at, float yaw, Material blade)
        {
            Transform g = Group(parent, "Scalpel", at, Quaternion.Euler(0f, yaw, 0f));
            OrKit.Box(g, "Handle", new Vector3(0f, 0.003f, 0.06f), new Vector3(0.009f, 0.003f, 0.12f), steel);
            OrKit.Box(g, "Blade", new Vector3(0f, 0.003f, 0.135f), new Vector3(0.007f, 0.0012f, 0.03f), blade);
        }

        private void Forceps(Transform parent, Vector3 at, float yaw)
        {
            Transform g = Group(parent, "Forceps", at, Quaternion.Euler(0f, yaw, 0f));
            foreach (int s in new[] { -1, 1 })
                OrKit.Box(g, "Arm", new Vector3(0.004f * s, 0.003f, 0.06f), new Vector3(0.003f, 0.004f, 0.12f), steel, new Vector3(0, -2.5f * s, 0));
            OrKit.Box(g, "Joint", new Vector3(0f, 0.003f, 0.0f), new Vector3(0.012f, 0.005f, 0.012f), steel);
        }

        // ---------- laparoscopy tower cart (camera unit, light source, insufflator) ----------
        private void BuildTower()
        {
            Transform g = Group(R, "Laparoscopy tower", new Vector3(0.42f, floorY, 1.2f), Quaternion.identity);
            Material dark = OrKit.Mat(new Color(0.16f, 0.17f, 0.19f), 0.45f);
            Material unit = OrKit.Mat(new Color(0.88f, 0.89f, 0.9f), 0.5f);
            Material screenGlow = OrKit.Glow(new Color(0.08f, 0.18f, 0.3f), 0.9f);
            foreach (float x in new[] { -0.27f, 0.27f })
                foreach (float z in new[] { -0.22f, 0.22f })
                {
                    OrKit.Rod(g, "Post", new Vector3(x, 0.1f, z), new Vector3(x, 1.12f, z), 0.015f, steel);
                    OrKit.Part(g, "Caster", PrimitiveType.Cylinder, new Vector3(x, 0.045f, z), new Vector3(0.07f, 0.02f, 0.07f), dark, Quaternion.Euler(0, 0, 90));
                }
            OrKit.Box(g, "Base shelf", new Vector3(0f, 0.12f, 0f), new Vector3(0.6f, 0.04f, 0.5f), dark);
            OrKit.Box(g, "Top shelf", new Vector3(0f, 1.12f, 0f), new Vector3(0.6f, 0.03f, 0.5f), dark);
            string[] names = { "Camera control unit", "LED light source", "Insufflator", "Recorder" };
            for (int k = 0; k < names.Length; k++)
            {
                float y = 1.0f - k * 0.17f;
                OrKit.Box(g, names[k], new Vector3(0f, y, 0f), new Vector3(0.5f, 0.13f, 0.42f), k == 2 ? white : unit);
                OrKit.Part(g, names[k] + " display", PrimitiveType.Cube, new Vector3(-0.1f, y, -0.212f), new Vector3(0.2f, 0.06f, 0.004f), screenGlow, null, false);
                TextMesh text = OrKit.Text(g, names[k] + " text", k == 0 ? "CCU  1080p" : k == 1 ? "LED  100%" : k == 3 ? "REC  00:00" : "12 mmHg", new Vector3(-0.1f, y, -0.215f), 0.02f, new Color(0.45f, 1f, 0.6f));
                if (k == 2) insufflatorText = text;
                OrKit.Part(g, names[k] + " knob", PrimitiveType.Cylinder, new Vector3(0.15f, y, -0.215f), new Vector3(0.035f, 0.008f, 0.035f), dark, Quaternion.Euler(90, 0, 0));
            }
        }

        // ---------- peg transfer: insufflated abdomen with trocars and a laparoscope ----------
        private void BuildPegDressing(Transform parent)
        {
            // Closed abdomen: opaque iodine-prepped skin outside (the field is inside, seen only through the scope).
            var dome = new GameObject("Insufflated abdomen", typeof(MeshFilter), typeof(MeshRenderer));
            dome.transform.SetParent(parent, false);
            dome.GetComponent<MeshFilter>().sharedMesh = DomeMesh(96, 32, false);
            var outer = dome.GetComponent<MeshRenderer>();
            outer.sharedMaterial = SurgicalField.Tissue(SurgicalField.Skin, new Vector2(3f, 2f), 0.6f);
            outer.shadowCastingMode = ShadowCastingMode.TwoSided;
            // Inner wall (peritoneum), facing inward, and the floor of the cavity around the operative field.
            var wall = new GameObject("Peritoneum", typeof(MeshFilter), typeof(MeshRenderer));
            wall.transform.SetParent(parent, false);
            wall.GetComponent<MeshFilter>().sharedMesh = DomeMesh(96, 32, true);
            var inner = wall.GetComponent<MeshRenderer>();
            inner.sharedMaterial = SurgicalField.Tissue(SurgicalField.Peritoneum, new Vector2(3f, 2f), 0.8f);
            inner.shadowCastingMode = ShadowCastingMode.Off;
            inner.reflectionProbeUsage = ReflectionProbeUsage.Off;
            SurgicalField.Add(parent, "Cavity floor", CavityFloor(), SurgicalField.Tissue(SurgicalField.Mesentery, Vector2.one, 1f),
                Vector3.zero, Quaternion.identity, Vector3.one, false);
            Material body = OrKit.Mat(new Color(0.16f, 0.17f, 0.19f), 0.55f);
            Material cap = OrKit.Mat(new Color(0.24f, 0.25f, 0.28f), 0.5f);
            Material cameraCap = OrKit.Mat(new Color(0.15f, 0.36f, 0.62f), 0.5f);
            Material valve = OrKit.Mat(new Color(0.92f, 0.92f, 0.9f), 0.4f);
            Material puncture = OrKit.Mat(new Color(0.45f, 0.12f, 0.1f), 0.5f);
            Trocar(parent, "Left trocar", InstrumentPort(-1), 0.006f, body, cap, valve, puncture);
            Trocar(parent, "Right trocar", InstrumentPort(1), 0.006f, body, cap, valve, puncture);
            Trocar(parent, "Camera trocar", CameraPort, 0.0068f, body, cameraCap, valve, puncture);
            // The retrieval bag's applicator enters through its own port on the far right.
            Vector3 bagPort = BagPort(out Vector3 bagAxis);
            Trocar(parent, "Bag trocar", bagPort, 0.0068f, body, cap, valve, puncture, bagAxis);
            // The laparoscope itself: rigid scope out of the camera port, camera head, cables to the tower.
            Vector3 dir = (ScopeTarget - CameraPort).normalized;
            Vector3 headEnd = CameraPort - dir * 0.33f;
            OrKit.Rod(parent, "Laparoscope", CameraPort + dir * 0.04f, CameraPort - dir * 0.3f, 0.005f, steel);
            OrKit.Part(parent, "Camera head", PrimitiveType.Cube, headEnd, new Vector3(0.036f, 0.042f, 0.075f), OrKit.Mat(new Color(0.12f, 0.13f, 0.15f), 0.6f), Quaternion.LookRotation(dir));
            Vector3 lightPost = CameraPort - dir * 0.27f;
            Vector3 postTip = lightPost + Vector3.Cross(dir, Vector3.right).normalized * 0.03f;
            OrKit.Rod(parent, "Light post", lightPost, postTip, 0.004f, steel);
            Vector3 tower = new Vector3(0.42f, floorY + 1.0f, 0.99f);
            Cable(parent, "Camera cable", headEnd - dir * 0.04f, 0.45f, tower, 0.004f, OrKit.Mat(new Color(0.05f, 0.05f, 0.06f), 0.4f));
            Cable(parent, "Light cable", postTip, 0.48f, tower + Vector3.down * 0.17f, 0.0045f, OrKit.Mat(new Color(0.55f, 0.57f, 0.6f), 0.4f));
            Cable(parent, "Insufflation tube", InstrumentPort(-1) + new Vector3(0f, 0.03f, -0.02f), 0.51f, tower + Vector3.down * 0.34f, 0.004f, OrKit.Fade(new Color(0.85f, 0.92f, 1f, 0.6f), 0.9f));
        }

        // Cables leave the field, run across the drape over the thighs, then hang down to the tower.
        private void Cable(Transform parent, string name, Vector3 from, float crossX, Vector3 to, float radius, Material material)
        {
            Vector3 near = new Vector3(crossX, DrapeHeight(crossX, -0.25f) + 0.008f, -0.25f);
            var across = new Vector3[8];
            for (int i = 0; i < across.Length; i++)
            {
                float z = Mathf.Lerp(-0.25f, 0.3f, i / (across.Length - 1f));
                across[i] = new Vector3(crossX, DrapeHeight(crossX, z) + radius + 0.004f, z);
            }
            OrKit.Tube(parent, name, Join(Join(OrKit.Sag(from, near, 0.05f, 8), across), OrKit.Sag(across[across.Length - 1], to, 0.25f, 10)), radius, material);
        }

        // Where the bag applicator shaft (PegBoard.BagShaftStart -> End) crosses the abdominal wall.
        private static Vector3 BagPort(out Vector3 axis)
        {
            Vector3 a = PegBoard.BagShaftStart, b = PegBoard.BagShaftEnd;
            axis = (b - a).normalized;
            float lo = 0f, hi = 1f;
            for (int i = 0; i < 30; i++)
            {
                float mid = (lo + hi) * 0.5f;
                Vector3 p = Vector3.Lerp(a, b, mid);
                if (p.y < DomeY(p.x, p.z)) lo = mid; else hi = mid;
            }
            return Vector3.Lerp(a, b, hi);
        }

        // Ring of tissue between the operative field and the abdominal wall.
        private static Mesh CavityFloor()
        {
            const int rings = 12, segments = 96;
            var vertices = new Vector3[(rings + 1) * (segments + 1)];
            var uvs = new Vector2[vertices.Length];
            for (int i = 0; i <= rings; i++)
                for (int j = 0; j <= segments; j++)
                {
                    float r = Mathf.Lerp(0.8f, 0.99f, i / (float)rings), a = j * Mathf.PI * 2f / segments;
                    float x = DomeRx * r * Mathf.Cos(a), z = DomeRz * r * Mathf.Sin(a);
                    vertices[i * (segments + 1) + j] = new Vector3(x, DomeBase + 0.004f + 0.01f * Mathf.Sin(a * 7f) * (1f - r), z);
                    uvs[i * (segments + 1) + j] = new Vector2(x * 2.4f, z * 2.4f);
                }
            return SurgicalField.GridMesh("Cavity floor", vertices, uvs, rings, segments, true);
        }

        /// <summary>A trocar at a port; the axis points out of the body (default: away from the field centre).</summary>
        private void Trocar(Transform parent, string name, Vector3 port, float radius, Material body, Material cap, Material valve, Material puncture, Vector3? outward = null)
        {
            Vector3 axis = outward.HasValue ? outward.Value.normalized : (port - FieldTarget).normalized;
            Quaternion along = Quaternion.FromToRotation(Vector3.up, axis);
            OrKit.Part(parent, name, PrimitiveType.Cylinder, port - axis * 0.01f, new Vector3(radius * 2f, 0.035f, radius * 2f), body, along);
            OrKit.Part(parent, name + " cap", PrimitiveType.Cylinder, port + axis * 0.03f, new Vector3(radius * 4.2f, 0.011f, radius * 4.2f), cap, along);
            Vector3 side = Vector3.Cross(axis, Vector3.forward).normalized;
            OrKit.Part(parent, name + " stopcock", PrimitiveType.Cube, port + axis * 0.026f + side * (radius * 2.6f), new Vector3(0.012f, 0.006f, 0.006f), valve, Quaternion.LookRotation(axis) * Quaternion.Euler(0, 90, 0));
            Vector3 normal = new Vector3(port.x / (DomeRx * DomeRx), (port.y - DomeBase) / (DomeHeight * DomeHeight), port.z / (DomeRz * DomeRz)).normalized;
            OrKit.MeshPart(parent, name + " incision", torus, port, Quaternion.FromToRotation(Vector3.up, normal), Vector3.one * radius * 1.5f, puncture);
        }

        private static Mesh DomeMesh(int segments, int rings, bool inward)
        {
            float shrink = inward ? 0.985f : 1f;
            var vertices = new Vector3[(rings + 1) * (segments + 1)];
            var normals = new Vector3[vertices.Length];
            var uvs = new Vector2[vertices.Length];
            for (int i = 0; i <= rings; i++)
            {
                float theta = i * Mathf.PI * 0.5f / rings;
                for (int j = 0; j <= segments; j++)
                {
                    float phi = j * Mathf.PI * 2f / segments;
                    var p = new Vector3(DomeRx * Mathf.Sin(theta) * Mathf.Cos(phi), DomeHeight * Mathf.Cos(theta), DomeRz * Mathf.Sin(theta) * Mathf.Sin(phi)) * shrink;
                    Vector3 n = new Vector3(p.x / (DomeRx * DomeRx), p.y / (DomeHeight * DomeHeight), p.z / (DomeRz * DomeRz)).normalized;
                    vertices[i * (segments + 1) + j] = p + Vector3.up * DomeBase;
                    normals[i * (segments + 1) + j] = inward ? -n : n;
                    uvs[i * (segments + 1) + j] = new Vector2(j / (float)segments * 2f, i / (float)rings);
                }
            }
            var triangles = new int[rings * segments * 6];
            int k = 0;
            for (int i = 0; i < rings; i++)
                for (int j = 0; j < segments; j++)
                {
                    int a = i * (segments + 1) + j, b = a + 1, c = a + segments + 1, d = c + 1;
                    if (inward) { triangles[k++] = a; triangles[k++] = c; triangles[k++] = b; triangles[k++] = b; triangles[k++] = c; triangles[k++] = d; }
                    else { triangles[k++] = a; triangles[k++] = b; triangles[k++] = c; triangles[k++] = b; triangles[k++] = d; triangles[k++] = c; }
                }
            var mesh = new Mesh { name = inward ? "Peritoneum" : "Abdomen dome", vertices = vertices, normals = normals, uv = uvs, triangles = triangles };
            mesh.RecalculateTangents(); mesh.RecalculateBounds();
            return mesh;
        }

        // ---------- wound closure: towels square off the pad on the abdomen, prepped skin, towel clips ----------
        private void BuildWoundDressing(Transform parent)
        {
            Material iodine = OrKit.Mat(new Color(0.66f, 0.38f, 0.22f), 0.45f);
            Material towel = OrKit.Mat(new Color(0.2f, 0.47f, 0.57f), 0.1f);
            towel.mainTexture = drape.mainTexture;
            OrKit.Box(parent, "Prepped skin", new Vector3(0f, 0.0015f, 0f), new Vector3(0.25f, 0.003f, 0.23f), iodine);
            OrKit.Box(parent, "Towel left", new Vector3(-0.17f, 0.004f, 0f), new Vector3(0.09f, 0.005f, 0.5f), towel);
            OrKit.Box(parent, "Towel right", new Vector3(0.17f, 0.004f, 0f), new Vector3(0.09f, 0.005f, 0.5f), towel);
            OrKit.Box(parent, "Towel far", new Vector3(0f, 0.0045f, 0.1825f), new Vector3(0.25f, 0.006f, 0.135f), towel);
            OrKit.Box(parent, "Towel near", new Vector3(0f, 0.0045f, -0.1825f), new Vector3(0.25f, 0.006f, 0.135f), towel);
            OrKit.Box(parent, "Drape skirt", new Vector3(-0.214f, -0.02f, 0f), new Vector3(0.006f, 0.05f, 0.5f), towel);
            OrKit.Box(parent, "Drape skirt", new Vector3(0.214f, -0.02f, 0f), new Vector3(0.006f, 0.05f, 0.5f), towel);
            OrKit.Box(parent, "Drape skirt", new Vector3(0f, -0.02f, -0.249f), new Vector3(0.434f, 0.05f, 0.006f), towel);
            OrKit.Box(parent, "Drape skirt", new Vector3(0f, -0.02f, 0.249f), new Vector3(0.434f, 0.05f, 0.006f), towel);
            foreach (int sx in new[] { -1, 1 })
                foreach (int sz in new[] { -1, 1 })
                {
                    Vector3 at = new Vector3(0.135f * sx, 0.009f, 0.125f * sz);
                    OrKit.MeshPart(parent, "Towel clip ring", torus, at + new Vector3(0.02f * sx, 0f, 0.02f * sz), Quaternion.identity, Vector3.one * 0.008f, steel);
                    OrKit.Box(parent, "Towel clip", at + new Vector3(0.01f * sx, 0f, 0.01f * sz), new Vector3(0.004f, 0.003f, 0.03f), steel, new Vector3(0f, 45f * sx * sz, 0f));
                }
        }

        // ---------- per-frame: follow the board, animate the clock and the vitals ----------
        private void LateUpdate()
        {
            if (board != null) transform.SetPositionAndRotation(board.position, board.rotation);
            System.DateTime now = System.DateTime.Now;
            float seconds = now.Second + now.Millisecond / 1000f, minutes = now.Minute + seconds / 60f, hours = now.Hour % 12 + minutes / 60f;
            if (secondHand != null)
            {
                secondHand.localRotation = Quaternion.Euler(0f, 0f, -seconds * 6f);
                minuteHand.localRotation = Quaternion.Euler(0f, 0f, -minutes * 6f);
                hourHand.localRotation = Quaternion.Euler(0f, 0f, -hours * 30f);
            }
            if (ecg == null) return;
            float t = Time.unscaledTime;
            if (t > nextVitals)
            {
                nextVitals = t + 3f;
                heartRate = Mathf.Clamp(heartRate + Random.Range(-2f, 2f), 66f, 80f);
                heartRateText.text = Mathf.RoundToInt(heartRate).ToString();
            }
            int n = ecg.positionCount;
            for (int i = 0; i < n; i++)
            {
                float f = i / (n - 1f), x = -0.225f + 0.31f * f, ts = t - (1f - f) * 4f;
                float beat = ts * heartRate / 60f, p = beat - Mathf.Floor(beat);
                ecg.SetPosition(i, new Vector3(x, 0.08f + 0.04f * Ecg(p), -0.028f));
                pleth.SetPosition(i, new Vector3(x, -0.01f + 0.035f * (Gauss(p - 0.3f, 0.11f) + 0.3f * Gauss(p - 0.55f, 0.07f)), -0.028f));
                float breath = ts * 0.2f, c = breath - Mathf.Floor(breath);
                capno.SetPosition(i, new Vector3(x, -0.095f + 0.03f * (Mathf.SmoothStep(0f, 1f, (c - 0.05f) / 0.1f) - Mathf.SmoothStep(0f, 1f, (c - 0.55f) / 0.1f)), -0.028f));
            }
        }

        private static float Ecg(float p) =>
            0.12f * Gauss(p - 0.12f, 0.035f) - 0.12f * Gauss(p - 0.225f, 0.011f) + Gauss(p - 0.25f, 0.014f)
            - 0.25f * Gauss(p - 0.275f, 0.014f) + 0.28f * Gauss(p - 0.45f, 0.055f);

        private static Transform Group(Transform parent, string name, Vector3 position, Quaternion rotation)
        {
            var g = new GameObject(name).transform;
            g.SetParent(parent, false);
            g.localPosition = position; g.localRotation = rotation;
            return g;
        }
    }

    // Small shape, material and texture helpers for the preview environment (built-in Standard shader).
    public static class OrKit
    {
        private static Material lineMaterial;
        private static Font font;

        public static Material Mat(Color color, float gloss = 0.25f, float metallic = 0f)
        {
            var material = new Material(Shader.Find("Standard")) { color = color };
            material.SetFloat("_Glossiness", gloss);
            material.SetFloat("_Metallic", metallic);
            return material;
        }

        /// <summary>Standard material from a CC0 PBR set in Resources/OR (ambientCG: _Color, _Normal, _MetallicGloss,
        /// optional _AO). Null if the set is missing.</summary>
        public static Material Pbr(string id, Color tint, Vector2 tiling, float bump, float smoothnessScale = 1f)
        {
            var albedo = Resources.Load<Texture2D>("OR/" + id + "_Color");
            if (albedo == null) return null;
            Material material = Mat(tint, 0.5f);
            material.mainTexture = albedo; material.mainTextureScale = tiling;
            var normal = Resources.Load<Texture2D>("OR/" + id + "_Normal");
            if (normal != null) { material.SetTexture("_BumpMap", normal); material.SetFloat("_BumpScale", bump); material.EnableKeyword("_NORMALMAP"); }
            var gloss = Resources.Load<Texture2D>("OR/" + id + "_MetallicGloss");
            if (gloss != null) { material.SetTexture("_MetallicGlossMap", gloss); material.SetFloat("_GlossMapScale", smoothnessScale); material.EnableKeyword("_METALLICGLOSSMAP"); }
            var ao = Resources.Load<Texture2D>("OR/" + id + "_AO");
            if (ao != null) { material.SetTexture("_OcclusionMap", ao); material.SetFloat("_OcclusionStrength", 0.8f); }
            return material;
        }

        public static Material Glow(Color color, float strength)
        {
            Material material = Mat(color, 0.3f);
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * strength);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            return material;
        }

        // Standard shader in Fade mode: the same keywords, blend state and queue the material inspector sets.
        public static Material Fade(Color color, float gloss)
        {
            Material material = Mat(color, gloss);
            material.SetFloat("_Mode", 2f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = (int)RenderQueue.Transparent;
            return material;
        }

        public static Material Unlit(Texture texture, bool transparent)
        {
            Shader shader = Shader.Find(transparent ? "Unlit/Transparent" : "Unlit/Texture");
            if (shader == null) shader = Shader.Find("Standard");
            return new Material(shader) { mainTexture = texture };
        }

        public static GameObject Part(Transform parent, string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material,
            Quaternion? rotation = null, bool castShadows = true)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = name;
            Object.DestroyImmediate(part.GetComponent<Collider>());
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localRotation = rotation ?? Quaternion.identity;
            part.transform.localScale = scale;
            var renderer = part.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            if (!castShadows) renderer.shadowCastingMode = ShadowCastingMode.Off;
            return part;
        }

        public static GameObject Box(Transform parent, string name, Vector3 position, Vector3 size, Material material, Vector3 euler = default) =>
            Part(parent, name, PrimitiveType.Cube, position, size, material, Quaternion.Euler(euler));

        public static GameObject Sphere(Transform parent, string name, Vector3 position, Vector3 size, Material material) =>
            Part(parent, name, PrimitiveType.Sphere, position, size, material);

        public static GameObject Rod(Transform parent, string name, Vector3 a, Vector3 b, float radius, Material material)
        {
            Vector3 d = b - a;
            return Part(parent, name, PrimitiveType.Cylinder, (a + b) * 0.5f, new Vector3(radius * 2f, d.magnitude * 0.5f, radius * 2f), material,
                Quaternion.FromToRotation(Vector3.up, d));
        }

        public static GameObject Capsule(Transform parent, string name, Vector3 a, Vector3 b, float radius, Material material)
        {
            Vector3 d = b - a;
            return Part(parent, name, PrimitiveType.Capsule, (a + b) * 0.5f, new Vector3(radius * 2f, d.magnitude * 0.5f + radius, radius * 2f), material,
                Quaternion.FromToRotation(Vector3.up, d));
        }

        public static GameObject MeshPart(Transform parent, string name, Mesh mesh, Vector3 position, Quaternion rotation, Vector3 scale, Material material)
        {
            var part = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position; part.transform.localRotation = rotation; part.transform.localScale = scale;
            part.GetComponent<MeshFilter>().sharedMesh = mesh;
            part.GetComponent<MeshRenderer>().sharedMaterial = material;
            return part;
        }

        // A chain of short cylinders along a polyline (cables, tubing); lit and shadowed, unlike a LineRenderer.
        public static void Tube(Transform parent, string name, Vector3[] points, float radius, Material material)
        {
            var group = new GameObject(name).transform;
            group.SetParent(parent, false);
            for (int i = 1; i < points.Length; i++)
            {
                Vector3 overlap = (points[i] - points[i - 1]).normalized * radius * 0.8f;
                Rod(group, "Segment", points[i - 1] - overlap, points[i] + overlap, radius, material);
            }
        }

        public static Vector3[] Sag(Vector3 a, Vector3 b, float drop, int count)
        {
            var points = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                float t = i / (count - 1f);
                points[i] = Vector3.Lerp(a, b, t) + Vector3.down * (drop * 4f * t * (1f - t));
            }
            return points;
        }

        public static LineRenderer Line(Transform parent, string name, Vector3[] points, float width, Color color)
        {
            var item = new GameObject(name);
            item.transform.SetParent(parent, false);
            var line = item.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            if (lineMaterial == null) lineMaterial = new Material(Shader.Find("Sprites/Default"));
            line.sharedMaterial = lineMaterial;
            line.startColor = line.endColor = color;
            line.startWidth = line.endWidth = width;
            line.numCapVertices = 2; line.numCornerVertices = 2;
            line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false;
            line.positionCount = points.Length;
            line.SetPositions(points);
            return line;
        }

        // TextMesh lines are about fontSize * characterSize / 10 units tall.
        public static TextMesh Text(Transform parent, string name, string text, Vector3 position, float height, Color color, TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var item = new GameObject(name);
            item.transform.SetParent(parent, false);
            item.transform.localPosition = position;
            var renderer = item.AddComponent<MeshRenderer>();
            var mesh = item.AddComponent<TextMesh>();
            mesh.font = font; mesh.fontSize = 64; mesh.characterSize = height * 10f / 64f;
            mesh.anchor = anchor; mesh.alignment = TextAlignment.Left; mesh.color = color; mesh.text = text;
            renderer.sharedMaterial = font.material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return mesh;
        }

        // Flat ring (torus) in the xz plane: major radius r, tube radius t.
        public static Mesh Torus(float r, float t, int segments, int sides)
        {
            var vertices = new Vector3[(segments + 1) * (sides + 1)];
            var normals = new Vector3[vertices.Length];
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                var center = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                for (int j = 0; j <= sides; j++)
                {
                    float b = j * Mathf.PI * 2f / sides;
                    Vector3 n = center.normalized * Mathf.Cos(b) + Vector3.up * Mathf.Sin(b);
                    vertices[i * (sides + 1) + j] = center + n * t;
                    normals[i * (sides + 1) + j] = n;
                }
            }
            var triangles = new int[segments * sides * 6];
            int k = 0;
            for (int i = 0; i < segments; i++)
                for (int j = 0; j < sides; j++)
                {
                    int a0 = i * (sides + 1) + j, a1 = a0 + 1, b0 = a0 + sides + 1, b1 = b0 + 1;
                    triangles[k++] = a0; triangles[k++] = a1; triangles[k++] = b0;
                    triangles[k++] = b0; triangles[k++] = a1; triangles[k++] = b1;
                }
            var mesh = new Mesh { name = "Torus", vertices = vertices, normals = normals, triangles = triangles };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Texture2D NewTexture(int width, int height, string name, bool mips = true) =>
            new Texture2D(width, height, TextureFormat.RGBA32, mips) { name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };

        // Two-tone floor tiles with grout: 2 x 2 tiles per texture repeat.
        public static Texture2D TileTexture()
        {
            const int size = 128;
            Texture2D texture = NewTexture(size, size, "OR floor tile");
            var pixels = new Color32[size * size];
            var random = new System.Random(7);
            Color a = new Color(0.8f, 0.83f, 0.84f), b = new Color(0.71f, 0.78f, 0.79f), grout = new Color(0.55f, 0.6f, 0.61f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    bool line = x % 64 < 2 || y % 64 < 2;
                    Color c = line ? grout : ((x / 64 + y / 64) % 2 == 0 ? a : b);
                    float n = (float)random.NextDouble() * 0.03f - 0.015f;
                    pixels[y * size + x] = new Color(c.r + n, c.g + n, c.b + n, 1f);
                }
            texture.SetPixels32(pixels);
            texture.Apply(true);
            return texture;
        }

        // Subtle woven texture multiplied with the drape colour.
        public static Texture2D FabricTexture()
        {
            const int size = 64;
            Texture2D texture = NewTexture(size, size, "Drape fabric");
            var pixels = new Color32[size * size];
            var random = new System.Random(3);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float v = 1f - 0.06f * (float)random.NextDouble() - ((x + y) % 4 == 0 ? 0.05f : 0f);
                    pixels[y * size + x] = new Color(v, v, v, 1f);
                }
            texture.SetPixels32(pixels);
            texture.Apply(true);
            return texture;
        }

        // Dark corners and a faint cool tint, laid over the scope image.
        public static Texture2D VignetteTexture()
        {
            const int width = 128, height = 72;
            Texture2D texture = NewTexture(width, height, "Scope vignette", false);
            texture.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color32[width * height];
            Color tint = new Color(0.1f, 0.3f, 0.6f), edge = new Color(0f, 0f, 0.02f);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    float u = (x + 0.5f) / width * 2f - 1f, v = (y + 0.5f) / height * 2f - 1f;
                    float a = Mathf.SmoothStep(0f, 1f, (Mathf.Sqrt(u * u + v * v) - 0.85f) / 0.5f);
                    Color c = Color.Lerp(tint, edge, Mathf.Clamp01(a * 3f));
                    c.a = Mathf.Max(0.07f, a);
                    pixels[y * width + x] = c;
                }
            texture.SetPixels32(pixels);
            texture.Apply(false);
            return texture;
        }

        // A stylised chest film for the wall viewer.
        public static Texture2D XrayTexture()
        {
            const int size = 128;
            Texture2D texture = NewTexture(size, size, "Chest film", false);
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (size - 1f) * 2f - 1f, v = y / (size - 1f) * 2f - 1f, g = 0.06f;
                    if (u * u / 0.72f + v * v / 0.95f < 1f) g = 0.38f;
                    float lung = Mathf.Min((u + 0.38f) * (u + 0.38f) / 0.07f + (v - 0.1f) * (v - 0.1f) / 0.36f, (u - 0.38f) * (u - 0.38f) / 0.07f + (v - 0.1f) * (v - 0.1f) / 0.36f);
                    if (lung < 1f) g = 0.13f + 0.05f * Mathf.Abs(Mathf.Sin(v * 22f));
                    if ((u + 0.12f) * (u + 0.12f) / 0.05f + (v + 0.15f) * (v + 0.15f) / 0.06f < 1f) g = 0.42f;
                    if (Mathf.Abs(u) < 0.05f) g = 0.62f + 0.08f * Mathf.Sin(v * 40f);
                    g += 0.6f * (1f - Mathf.Abs(u)) * 0.2f;
                    pixels[y * size + x] = new Color(g * 0.95f, g, g * 1.05f, 1f);
                }
            texture.SetPixels32(pixels);
            texture.Apply(false);
            return texture;
        }
    }
}
