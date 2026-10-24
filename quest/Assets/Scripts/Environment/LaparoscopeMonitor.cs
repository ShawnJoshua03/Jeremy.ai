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
        [Tooltip("Desktop preview: scope as the main screen view with a room inset. Off on the headset, where the feed goes to a world-space monitor.")]
        public bool desktopLayout = true;
        public Renderer WorldScreen { get; private set; }
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
        private string caption = "LAPAROSCOPE";
        private Light scopeLight;
        private CavityLook look = CavityLook.VideoHaze;

        /// <summary>How the inside of the abdomen renders through the scope (ambient, haze, light, lens).</summary>
        public struct CavityLook
        {
            public Color ambient, fog;
            public float fogStart, fogEnd, light, far;
            public float bloom, vignette, warmth, contrast, saturation, grain, aberration, focus, focusRange, dof;
            /// <summary>The Teleflex animation look (lap chole): pale lavender haze, soft bright light, shallow focus.</summary>
            public static readonly CavityLook VideoHaze = new CavityLook
            {
                ambient = new Color(0.52f, 0.5f, 0.56f), fog = new Color(0.79f, 0.78f, 0.84f), fogStart = 0.08f, fogEnd = 0.26f, light = 1.25f, far = 0.4f,
                bloom = 0.22f, vignette = 0.12f, warmth = 0f, contrast = 0.12f, saturation = 1.05f, grain = 0f, aberration = 0.002f,
                focus = 0.075f, focusRange = 0.05f, dof = 1f
            };
        }

        /// <summary>Main screen view: true = the laparoscope image, false = the operating-room camera.</summary>
        public void SetMainView(bool scope) => scopeMainView = scope;
        private GUIStyle label;
        private Rect insetPixels;

        public void Build(Transform room)
        {
            // Desktop preview: keep simulating (and the terminal bridge polling) while the editor window is unfocused.
            Application.runInBackground = true;
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
            Scope.cullingMask &= ~(1 << LapCholeDressing.OutsideOnlyLayer);
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
            screenScope.cullingMask &= ~(1 << LapCholeDressing.OutsideOnlyLayer);
            var clearObject = new GameObject("Screen clear");
            clearObject.transform.SetParent(room, false);
            clearCamera = clearObject.AddComponent<Camera>();
            clearCamera.cullingMask = 0; clearCamera.clearFlags = CameraClearFlags.SolidColor;
            clearCamera.backgroundColor = new Color(0.02f, 0.02f, 0.025f); clearCamera.depth = -50f;
            clearCamera.enabled = false;
            Camera.onPreCull += EnterCavity;
            Camera.onPostRender += LeaveCavity;
        }

        /// <summary>Headset: a framed monitor showing the live scope feed, placed in parent-local space.</summary>
        public void BuildWorldScreen(Transform parent, Vector3 localPosition, Quaternion localRotation, Vector2 size)
        {
            if (Feed == null) return;
            var monitor = new GameObject("Laparoscope monitor (world)").transform;
            monitor.SetParent(parent, false);
            monitor.localPosition = localPosition;
            monitor.localRotation = localRotation;
            GameObject frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
            frame.name = "Bezel";
            Object.Destroy(frame.GetComponent<Collider>());
            frame.transform.SetParent(monitor, false);
            frame.transform.localPosition = new Vector3(0, 0, 0.012f);
            frame.transform.localScale = new Vector3(size.x + 0.04f, size.y + 0.04f, 0.02f);
            var bezel = new Material(Shader.Find("Standard")) { color = new Color(0.06f, 0.06f, 0.07f) };
            frame.GetComponent<Renderer>().sharedMaterial = bezel;
            GameObject screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
            screen.name = "Screen";
            Object.Destroy(screen.GetComponent<Collider>());
            screen.transform.SetParent(monitor, false);
            screen.transform.localScale = new Vector3(size.x, size.y, 1f);
            // A quad faces -z; turn it so the picture faces the trainee standing on the -z side of the monitor.
            screen.transform.localRotation = Quaternion.identity;
            Shader unlit = Shader.Find("Unlit/Texture");
            var image = new Material(unlit != null ? unlit : Shader.Find("Standard")) { mainTexture = Feed };
            WorldScreen = screen.GetComponent<Renderer>();
            WorldScreen.sharedMaterial = image;
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
        public void Aim(Vector3 position, Vector3 target, float fieldOfView, bool tipLight, string text, Color background, Vector3 up, CavityLook cavityLook)
        {
            if (Scope == null) return;
            look = cavityLook;
            Scope.transform.localPosition = position;
            Scope.transform.localRotation = Quaternion.LookRotation(target - position, up);
            Scope.farClipPlane = screenScope.farClipPlane = tipLight ? look.far : 6f;
            scopeLight.intensity = look.light;
            foreach (ScopeLens lens in GetLenses()) lens.Configure(look);
            Scope.fieldOfView = fieldOfView;
            Scope.backgroundColor = background;
            screenScope.backgroundColor = background;
            cavity = tipLight;
            caption = text;
        }

        private void Update()
        {
            Keyboard keys = Keyboard.current;
            if (keys != null && keys.vKey.wasPressedThisFrame) scopeMainView = !scopeMainView;
        }

        // Main view layout: the scope fills the screen (HUD on top), the room camera sits in an inset.
        private void LateUpdate()
        {
            if (screenScope == null) return;
            if (!desktopLayout) { screenScope.enabled = false; return; }
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
                // Full screen under the HUD; on narrow windows widen the vertical FOV so the 16:9 framing still fits.
                screenScope.pixelRect = new Rect(0f, 0f, Screen.width, Screen.height);
                float aspect = Screen.width / (float)Mathf.Max(1, Screen.height);
                float half = Mathf.Atan(Mathf.Tan(Scope.fieldOfView * 0.5f * Mathf.Deg2Rad) * 16f / 9f);
                screenScope.fieldOfView = Mathf.Max(Scope.fieldOfView, 2f * Mathf.Atan(Mathf.Tan(half) / aspect) * Mathf.Rad2Deg);
                float s = Mathf.Max(0.6f, Screen.height / 900f), w = 360f * s, h = 203f * s;
                insetPixels = new Rect(12f, Screen.height - h - 12f, w, h);
                if (roomCamera != null) { roomCamera.pixelRect = new Rect(12f, 12f, w, h); roomCamera.depth = 20f; }
            }
            else if (roomCamera != null && roomSaved)
            {
                roomCamera.rect = roomRect; roomCamera.depth = roomDepth;
            }
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
            RenderSettings.ambientLight = look.ambient;
            RenderSettings.reflectionIntensity = 0.12f;
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = look.fog;
            RenderSettings.fogStartDistance = look.fogStart; RenderSettings.fogEndDistance = look.fogEnd;
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

        private ScopeLens[] GetLenses() => new[] { Scope.GetComponent<ScopeLens>(), screenScope.GetComponent<ScopeLens>() };

        /// <summary>Render the current main view (scope or room camera, no HUD) to a PNG of the given size.</summary>
        public bool CaptureMainView(string path, int width, int height)
        {
            Camera cam = ScopeDominant ? screenScope : roomCamera != null ? roomCamera : Camera.main;
            if (cam == null) return false;
            var rt = new RenderTexture(width, height, 24) { antiAliasing = 4 };
            Rect rect = cam.rect; RenderTexture target = cam.targetTexture; float fov = cam.fieldOfView;
            cam.targetTexture = rt; cam.rect = new Rect(0, 0, 1, 1);
            if (cam == screenScope) cam.fieldOfView = Scope.fieldOfView;
            cam.Render();
            cam.targetTexture = target; cam.rect = rect; cam.fieldOfView = fov;
            RenderTexture active = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0); tex.Apply();
            RenderTexture.active = active;
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex); rt.Release(); Destroy(rt);
            return true;
        }

        private void OnGUI()
        {
            if (!desktopLayout || Feed == null || Event.current.type != EventType.Repaint) return;
            if (label == null) label = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.85f, 0.9f, 0.95f) } };
            if (ScopeDominant)
            {
                // Caption burned in at the bottom of the scope image, like the camera unit's overlay; inset frame.
                Rect view = screenScope.pixelRect;
                GUI.Label(new Rect(view.x + view.width - 330f, Screen.height - 30f, 320f, 22f), caption, label);
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
            GUI.Label(new Rect(rect.x + 8f, rect.y + 5f, w - 16f, 20f), caption + (cavity ? "   ·   V  scope view" : ""), label);
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
