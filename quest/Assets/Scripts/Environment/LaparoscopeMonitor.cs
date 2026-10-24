using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Jeremy
{
    // Desktop preview only: the laparoscope. A scope camera inside the insufflated abdomen renders the field into a
    // texture for the boom monitor; in the laparoscopic exercise a second camera on the same scope also renders it
    // as the main screen view (that is how laparoscopy is done: eyes on the monitor), with the operating-room view
    // as a small inset. V swaps the scope view and the room view. Inside the abdomen only the scope's own light
    // counts: room lights, room ambient and the room reflection probe are switched off while the scope renders.
    public sealed class LaparoscopeMonitor : MonoBehaviour
    {
        public bool showPictureInPicture = true;
        [Tooltip("Laparoscopic exercise: show the scope image as the main view (V toggles).")]
        public bool scopeMainView = true;
        public Camera Scope { get; private set; }
        public RenderTexture Feed { get; private set; }
        /// <summary>The camera the mouse should aim with: the scope's screen camera while it is the main view, else null.</summary>
        public Camera PointerCamera => ScopeDominant ? screenScope : null;
        public bool ScopeDominant => cavity && scopeMainView && screenScope != null;
        private Camera screenScope, clearCamera, roomCamera;
        private Rect roomRect = new Rect(0, 0, 1, 1);
        private float roomDepth;
        private bool roomSaved, cavity;
        private Texture2D vignette;
        private TextMesh caption;
        private Light scopeLight, screenLight;
        private GUIStyle label;
        private Rect insetPixels;

        public void Build(Transform room, float ceilingY)
        {
            Feed = new RenderTexture(1280, 720, 24) { name = "Laparoscope feed", antiAliasing = 4 };
            Feed.Create();
            vignette = OrKit.VignetteTexture();
            var scopeObject = new GameObject("Laparoscope camera");
            scopeObject.transform.SetParent(room, false);
            Scope = scopeObject.AddComponent<Camera>();
            Scope.targetTexture = Feed;
            Scope.nearClipPlane = 0.004f; Scope.farClipPlane = 6f;
            Scope.clearFlags = CameraClearFlags.SolidColor;
            Scope.depth = -5f;
            Scope.allowMSAA = true;
            scopeObject.AddComponent<ScopeLens>();
            // Light carried by the scope tip (fibre-optic ring around the lens), as on a real laparoscope.
            scopeLight = ScopeLight(scopeObject.transform);
            // The same scope rendered straight to the screen as the main view.
            var screenObject = new GameObject("Laparoscope screen view");
            screenObject.transform.SetParent(scopeObject.transform, false);
            screenScope = screenObject.AddComponent<Camera>();
            screenScope.nearClipPlane = 0.004f; screenScope.farClipPlane = 6f;
            screenScope.clearFlags = CameraClearFlags.SolidColor;
            screenScope.depth = 10f;
            screenScope.allowMSAA = true;
            screenScope.enabled = false;
            screenObject.AddComponent<ScopeLens>();
            var clearObject = new GameObject("Screen clear");
            clearObject.transform.SetParent(room, false);
            clearCamera = clearObject.AddComponent<Camera>();
            clearCamera.cullingMask = 0; clearCamera.clearFlags = CameraClearFlags.SolidColor;
            clearCamera.backgroundColor = new Color(0.02f, 0.02f, 0.025f); clearCamera.depth = -50f;
            clearCamera.enabled = false;
            Camera.onPreCull += EnterCavity;
            Camera.onPostRender += LeaveCavity;

            Material boom = OrKit.Mat(new Color(0.86f, 0.88f, 0.9f), 0.5f), bezel = OrKit.Mat(new Color(0.05f, 0.05f, 0.06f), 0.7f);
            Vector3 mount = new Vector3(-0.1f, 0.36f, 0.98f);
            OrKit.Rod(room, "Monitor boom column", new Vector3(mount.x, ceilingY, 1.14f), new Vector3(mount.x, 0.66f, 1.14f), 0.03f, boom);
            OrKit.Sphere(room, "Monitor boom joint", new Vector3(mount.x, 0.66f, 1.14f), Vector3.one * 0.07f, boom);
            OrKit.Rod(room, "Monitor boom arm", new Vector3(mount.x, 0.66f, 1.14f), mount + new Vector3(0f, 0.03f, 0.04f), 0.02f, boom);
            var monitor = new GameObject("Laparoscopy monitor").transform;
            monitor.SetParent(room, false);
            monitor.localPosition = mount;
            monitor.localRotation = Quaternion.Euler(10f, 0f, 0f); // screen tilted up toward the surgeon's eyes
            OrKit.Box(monitor, "Bezel", Vector3.zero, new Vector3(0.76f, 0.46f, 0.045f), bezel);
            OrKit.Box(monitor, "Mount plate", new Vector3(0f, 0f, 0.03f), new Vector3(0.2f, 0.2f, 0.02f), boom);
            var screenSize = new Vector3(0.704f, 0.396f, 1f);
            OrKit.Part(monitor, "Screen", PrimitiveType.Quad, new Vector3(0f, 0.012f, -0.0235f), screenSize, OrKit.Unlit(Feed, false), null, false);
            caption = OrKit.Text(monitor, "Caption", "", new Vector3(-0.34f, -0.207f, -0.024f), 0.015f, new Color(0.7f, 0.76f, 0.82f), TextAnchor.MiddleLeft);
            OrKit.Part(monitor, "Power LED", PrimitiveType.Cube, new Vector3(0.35f, -0.207f, -0.023f), new Vector3(0.008f, 0.008f, 0.002f),
                OrKit.Glow(new Color(0.2f, 0.9f, 0.4f), 1.5f), null, false);
        }

        private static Light ScopeLight(Transform scope)
        {
            var lightObject = new GameObject("Scope light");
            lightObject.transform.SetParent(scope, false);
            lightObject.transform.localPosition = new Vector3(0.004f, 0.005f, 0f); // light fibres sit just beside the lens
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Spot; light.spotAngle = 95f; light.innerSpotAngle = 40f; light.range = 0.9f; light.intensity = 5f;
            light.color = new Color(1f, 0.93f, 0.84f);
            light.shadows = LightShadows.Soft; light.shadowStrength = 0.75f; light.shadowNearPlane = 0.1f; light.shadowBias = 0.02f;
            light.renderMode = LightRenderMode.ForcePixel;
            light.enabled = false; // only on while a scope camera renders (EnterCavity)
            return light;
        }

        /// <summary>Place the scope (room/board-local), set its field of view, tip light and the on-screen caption.
        /// tipLight = inside the insufflated abdomen (laparoscopic exercise).</summary>
        public void Aim(Vector3 position, Vector3 target, float fieldOfView, bool tipLight, string text, Color background)
        {
            if (Scope == null) return;
            Scope.transform.localPosition = position;
            Scope.transform.localRotation = Quaternion.LookRotation(target - position, Vector3.up);
            Scope.fieldOfView = fieldOfView;
            Scope.backgroundColor = background;
            screenScope.backgroundColor = background;
            cavity = tipLight;
            if (caption != null) caption.text = text;
        }

        private void Update()
        {
            Keyboard keys = Keyboard.current;
            if (keys != null && keys.vKey.wasPressedThisFrame) scopeMainView = !scopeMainView;
        }

        // Main view layout: the scope fills the screen left of the HUD panel, the room camera sits in an inset.
        private void LateUpdate()
        {
            if (screenScope == null) return;
            if (roomCamera == null || !roomCamera.enabled)
            {
                roomCamera = null;
                foreach (Camera c in Camera.allCameras)
                    if (c.CompareTag("MainCamera") && c != Scope && c != screenScope && c != clearCamera) { roomCamera = c; break; }
                if (roomCamera != null && !roomSaved) { roomRect = roomCamera.rect; roomDepth = roomCamera.depth; roomSaved = true; }
            }
            bool dominant = ScopeDominant;
            screenScope.enabled = dominant;
            clearCamera.enabled = dominant;
            screenScope.fieldOfView = Scope.fieldOfView;
            if (dominant)
            {
                float right = HudLeftEdge() - 8f;
                screenScope.pixelRect = new Rect(0f, 0f, Mathf.Max(64f, right), Screen.height);
                float s = Mathf.Max(0.6f, Screen.height / 900f), w = 360f * s, h = 203f * s;
                insetPixels = new Rect(12f, Screen.height - h - 12f, w, h);
                if (roomCamera != null) { roomCamera.pixelRect = new Rect(12f, 12f, w, h); roomCamera.depth = 20f; }
            }
            else if (roomCamera != null && roomSaved)
            {
                roomCamera.rect = roomRect; roomCamera.depth = roomDepth;
            }
        }

        // The desktop HUD panel is a screen-space overlay on the right; keep the scope image clear of it.
        private RectTransform hudPanel;
        private float HudLeftEdge()
        {
            if (hudPanel == null)
                foreach (Canvas c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                    if (c.name == "Practice HUD" && c.isRootCanvas == false) { hudPanel = (RectTransform)c.transform; break; }
            if (hudPanel == null || !hudPanel.gameObject.activeInHierarchy) return Screen.width;
            var corners = new Vector3[4];
            hudPanel.GetWorldCorners(corners);
            float left = corners[0].x;
            return left > Screen.width * 0.3f && left < Screen.width ? left : Screen.width;
        }

        // ---- inside the abdomen: only the scope light, dark warm ambient, depth haze ----
        private readonly List<Light> switchedOff = new List<Light>();
        private bool inside, fog;
        private UnityEngine.Rendering.AmbientMode ambientMode;
        private Color ambientSky, ambientEquator, ambientGround, ambientLight, fogColor;
        private float reflection, fogStart, fogEnd;
        private FogMode fogMode;

        private void EnterCavity(Camera cam)
        {
            if (!cavity || inside || (cam != Scope && cam != screenScope)) return;
            inside = true;
            switchedOff.Clear();
            foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.enabled && l != scopeLight) { l.enabled = false; switchedOff.Add(l); }
            scopeLight.enabled = true;
            ambientMode = RenderSettings.ambientMode; ambientSky = RenderSettings.ambientSkyColor; ambientEquator = RenderSettings.ambientEquatorColor;
            ambientGround = RenderSettings.ambientGroundColor; ambientLight = RenderSettings.ambientLight; reflection = RenderSettings.reflectionIntensity;
            fog = RenderSettings.fog; fogColor = RenderSettings.fogColor; fogMode = RenderSettings.fogMode;
            fogStart = RenderSettings.fogStartDistance; fogEnd = RenderSettings.fogEndDistance;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.07f, 0.03f, 0.025f);
            RenderSettings.reflectionIntensity = 0.12f;
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.1f, 0.025f, 0.02f);
            RenderSettings.fogStartDistance = 0.2f; RenderSettings.fogEndDistance = 0.75f;
        }

        private void LeaveCavity(Camera cam)
        {
            if (!inside || (cam != Scope && cam != screenScope)) return;
            inside = false;
            scopeLight.enabled = false;
            foreach (Light l in switchedOff) if (l != null) l.enabled = true;
            switchedOff.Clear();
            RenderSettings.ambientMode = ambientMode; RenderSettings.ambientSkyColor = ambientSky; RenderSettings.ambientEquatorColor = ambientEquator;
            RenderSettings.ambientGroundColor = ambientGround; RenderSettings.ambientLight = ambientLight; RenderSettings.reflectionIntensity = reflection;
            RenderSettings.fog = fog; RenderSettings.fogColor = fogColor; RenderSettings.fogMode = fogMode;
            RenderSettings.fogStartDistance = fogStart; RenderSettings.fogEndDistance = fogEnd;
        }

        private void OnGUI()
        {
            if (Feed == null || Event.current.type != EventType.Repaint) return;
            if (label == null) label = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.85f, 0.9f, 0.95f) } };
            if (ScopeDominant)
            {
                // Caption burned in at the bottom of the scope image, like the camera unit's overlay; inset frame.
                Rect view = screenScope.pixelRect;
                GUI.Label(new Rect(view.x + view.width - 330f, Screen.height - 30f, 320f, 22f), caption != null ? caption.text : "", label);
                Rect r = insetPixels;
                GUI.color = new Color(0.04f, 0.05f, 0.06f, 1f);
                GUI.DrawTexture(new Rect(r.x - 3f, r.y - 3f, r.width + 6f, 3f), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(r.x - 3f, r.yMax, r.width + 6f, 3f), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(r.x - 3f, r.y, 3f, r.height), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(r.xMax, r.y, 3f, r.height), Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(new Rect(r.x + 8f, r.y + 5f, r.width - 16f, 20f), "OPERATING ROOM   ·   V  swap views", label);
                return;
            }
            if (!showPictureInPicture) return;
            float scale = Mathf.Max(0.6f, Screen.height / 900f), w = 400f * scale, h = 225f * scale;
            var rect = new Rect(12f, Screen.height - h - 12f, w, h);
            GUI.color = new Color(0.04f, 0.05f, 0.06f, 1f);
            GUI.DrawTexture(new Rect(rect.x - 3f, rect.y - 3f, w + 6f, h + 6f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.DrawTexture(rect, Feed, ScaleMode.StretchToFill, false);
            if (!cavity) GUI.DrawTexture(rect, vignette, ScaleMode.StretchToFill, true);
            GUI.Label(new Rect(rect.x + 8f, rect.y + 5f, w - 16f, 20f), (caption != null ? caption.text : "LAPAROSCOPE") + (cavity ? "   ·   V  scope view" : ""), label);
        }

        private void OnDestroy()
        {
            Camera.onPreCull -= EnterCavity;
            Camera.onPostRender -= LeaveCavity;
            if (roomCamera != null && roomSaved) { roomCamera.rect = roomRect; roomCamera.depth = roomDepth; }
            if (Scope != null) Scope.targetTexture = null;
            if (Feed != null) { Feed.Release(); Destroy(Feed); }
            if (vignette != null) Destroy(vignette);
        }
    }
}
