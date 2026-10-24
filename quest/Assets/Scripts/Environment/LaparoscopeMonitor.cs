using UnityEngine;

namespace Jeremy
{
    // Desktop preview only: a scope camera renders the field into a texture shown on a boom monitor opposite the
    // trainee and as a picture-in-picture in the bottom-left corner (laparoscopic surgeons watch the monitor).
    public sealed class LaparoscopeMonitor : MonoBehaviour
    {
        public bool showPictureInPicture = true;
        public Camera Scope { get; private set; }
        public RenderTexture Feed { get; private set; }
        private Texture2D vignette;
        private TextMesh caption;
        private Light scopeLight;
        private GUIStyle label;

        public void Build(Transform room, float ceilingY)
        {
            Feed = new RenderTexture(960, 540, 24) { name = "Laparoscope feed", antiAliasing = 2 };
            Feed.Create();
            vignette = OrKit.VignetteTexture();
            var scopeObject = new GameObject("Laparoscope camera");
            scopeObject.transform.SetParent(room, false);
            Scope = scopeObject.AddComponent<Camera>();
            Scope.targetTexture = Feed;
            Scope.nearClipPlane = 0.005f; Scope.farClipPlane = 8f;
            Scope.clearFlags = CameraClearFlags.SolidColor;
            Scope.depth = -5f;
            Scope.allowMSAA = true;
            // Light carried by the scope tip, as on a real laparoscope.
            scopeLight = scopeObject.AddComponent<Light>();
            scopeLight.type = LightType.Spot; scopeLight.spotAngle = 80f; scopeLight.range = 0.7f; scopeLight.intensity = 0.9f;
            scopeLight.color = new Color(1f, 0.97f, 0.93f); scopeLight.shadows = LightShadows.None;

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
            OrKit.Part(monitor, "Screen vignette", PrimitiveType.Quad, new Vector3(0f, 0.012f, -0.024f), screenSize, OrKit.Unlit(vignette, true), null, false);
            caption = OrKit.Text(monitor, "Caption", "", new Vector3(-0.34f, -0.207f, -0.024f), 0.015f, new Color(0.7f, 0.76f, 0.82f), TextAnchor.MiddleLeft);
            OrKit.Part(monitor, "Power LED", PrimitiveType.Cube, new Vector3(0.35f, -0.207f, -0.023f), new Vector3(0.008f, 0.008f, 0.002f),
                OrKit.Glow(new Color(0.2f, 0.9f, 0.4f), 1.5f), null, false);
        }

        /// <summary>Place the scope (room/board-local), set its field of view, tip light and the on-screen caption.</summary>
        public void Aim(Vector3 position, Vector3 target, float fieldOfView, bool tipLight, string text, Color background)
        {
            if (Scope == null) return;
            Scope.transform.localPosition = position;
            Scope.transform.localRotation = Quaternion.LookRotation(target - position, Vector3.up);
            Scope.fieldOfView = fieldOfView;
            Scope.backgroundColor = background;
            scopeLight.enabled = tipLight;
            if (caption != null) caption.text = text;
        }

        private void OnGUI()
        {
            if (!showPictureInPicture || Feed == null || Event.current.type != EventType.Repaint) return;
            float scale = Mathf.Max(0.6f, Screen.height / 900f), w = 400f * scale, h = 225f * scale;
            var rect = new Rect(12f, Screen.height - h - 12f, w, h);
            GUI.color = new Color(0.04f, 0.05f, 0.06f, 1f);
            GUI.DrawTexture(new Rect(rect.x - 3f, rect.y - 3f, w + 6f, h + 6f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.DrawTexture(rect, Feed, ScaleMode.StretchToFill, false);
            GUI.DrawTexture(rect, vignette, ScaleMode.StretchToFill, true);
            if (label == null) label = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.85f, 0.9f, 0.95f) } };
            GUI.Label(new Rect(rect.x + 8f, rect.y + 5f, w - 16f, 20f), caption != null ? caption.text : "LAPAROSCOPE", label);
        }

        private void OnDestroy()
        {
            if (Scope != null) Scope.targetTexture = null;
            if (Feed != null) { Feed.Release(); Destroy(Feed); }
            if (vignette != null) Destroy(vignette);
        }
    }
}
