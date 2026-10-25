using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace GallbladderTrainer.Guide
{
    /// <summary>
    /// Shared look for the guide, crew, report and replay: translucent dark-teal glass, thin cyan rims,
    /// soft outer glow, eased motion. Everything is built from code at runtime (no extra assets to merge).
    /// </summary>
    public static class HoloUi
    {
        public static readonly Color Glass = new Color(0.025f, 0.075f, 0.11f, 0.86f);
        public static readonly Color GlassDeep = new Color(0.015f, 0.045f, 0.07f, 0.92f);
        public static readonly Color Rim = new Color(0.24f, 0.86f, 1f, 0.85f);
        public static readonly Color Glow = new Color(0.24f, 0.86f, 1f, 0.16f);
        public static readonly Color TextMain = new Color(0.92f, 0.97f, 1f);
        public static readonly Color TextBody = new Color(0.80f, 0.89f, 0.95f);
        public static readonly Color TextDim = new Color(0.55f, 0.68f, 0.78f);
        public static readonly Color Cyan = new Color(0.24f, 0.86f, 1f);
        public static readonly Color Green = new Color(0.49f, 0.86f, 0.61f);
        public static readonly Color Amber = new Color(0.95f, 0.72f, 0.29f);
        public static readonly Color Red = new Color(1f, 0.48f, 0.42f);
        public static readonly Color Violet = new Color(0.70f, 0.53f, 1f);
        public static readonly Color BtnNormal = new Color(0.07f, 0.20f, 0.27f, 0.95f);
        public static readonly Color BtnHover = new Color(0.10f, 0.33f, 0.43f, 1f);
        public static readonly Color BtnPressed = new Color(0.14f, 0.56f, 0.68f, 1f);
        public static readonly Color BtnDisabled = new Color(0.06f, 0.10f, 0.13f, 0.8f);

        // ------------------------------------------------------------------ easing

        public static float OutCubic(float t) { t = Mathf.Clamp01(t); float u = 1f - t; return 1f - u * u * u; }
        public static float InOutSine(float t) { t = Mathf.Clamp01(t); return -(Mathf.Cos(Mathf.PI * t) - 1f) * 0.5f; }
        public static float OutBack(float t)
        {
            t = Mathf.Clamp01(t);
            const float c1 = 1.20158f, c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }
        /// <summary>Frame-rate independent smoothing factor for Lerp.</summary>
        public static float Damp(float speed, float dt) { return 1f - Mathf.Exp(-speed * dt); }

        public static Color SeverityColor(string severity)
        {
            switch (severity)
            {
                case "on_track": return Green;
                case "slow": return Amber;
                case "off_track": return new Color(1f, 0.55f, 0.30f);
                case "critical": return new Color(1f, 0.25f, 0.25f);
                default: return Cyan;
            }
        }

        public static string SeverityLabel(string severity)
        {
            switch (severity)
            {
                case "on_track": return "on track";
                case "slow": return "a little slow";
                case "off_track": return "off track";
                case "critical": return "critical";
                default: return "";
            }
        }

        // ------------------------------------------------------------------ canvases and widgets

        public sealed class Panel
        {
            public Canvas Canvas;
            public CanvasGroup Group;
            public RectTransform Root;
            public Image RimImage, GlowImage, Background;
        }

        public static Panel MakePanel(Transform parent, string name, float w, float h, float scale, bool interactive)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * scale;
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 10;
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(w, h);
            go.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
            if (interactive)
            {
                go.AddComponent<GraphicRaycaster>();
                go.AddComponent<TrackedDeviceGraphicRaycaster>();
            }
            var p = new Panel { Canvas = canvas, Group = go.GetComponent<CanvasGroup>(), Root = rt };
            p.GlowImage = Box(rt, "Glow", Glow); Stretch(p.GlowImage.rectTransform, -10f);
            p.RimImage = Box(rt, "Rim", Rim); Stretch(p.RimImage.rectTransform, 0f);
            p.Background = Box(rt, "Glass", Glass); Stretch(p.Background.rectTransform, 3f);
            return p;
        }

        public static Text Label(Transform parent, string name, string text, int size, Color color, TextAnchor anchor,
                                 float x, float y, float w, float h, bool bold = false)
        {
            RectTransform r = Rect(parent, name);
            TopLeft(r, x, y, w, h);
            var t = r.gameObject.AddComponent<Text>();
            t.font = Font();
            t.fontSize = size;
            t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            t.color = color;
            t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.supportRichText = true;
            t.raycastTarget = false;
            t.lineSpacing = 1.08f;
            t.text = text;
            return t;
        }

        public static Image Box(Transform parent, string name, Color c)
        {
            RectTransform r = Rect(parent, name);
            var img = r.gameObject.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
            return img;
        }

        public static Image Box(Transform parent, string name, Color c, float x, float y, float w, float h)
        {
            Image i = Box(parent, name, c);
            TopLeft(i.rectTransform, x, y, w, h);
            return i;
        }

        public static Button Button(Transform parent, string name, string label, int size, float x, float y, float w, float h,
                                    UnityAction onClick, out Text labelText)
        {
            RectTransform r = Rect(parent, name);
            TopLeft(r, x, y, w, h);
            Image rim = Box(r, "Rim", new Color(Rim.r, Rim.g, Rim.b, 0.55f)); Stretch(rim.rectTransform, 0f);
            var face = Box(r, "Face", Color.white); Stretch(face.rectTransform, 2f);
            face.raycastTarget = true;
            var btn = r.gameObject.AddComponent<Button>();
            btn.targetGraphic = face;
            ColorBlock cb = ColorBlock.defaultColorBlock;
            cb.normalColor = BtnNormal; cb.highlightedColor = BtnHover; cb.pressedColor = BtnPressed;
            cb.selectedColor = BtnNormal; cb.disabledColor = BtnDisabled; cb.colorMultiplier = 1f; cb.fadeDuration = 0.08f;
            btn.colors = cb;
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            if (onClick != null) btn.onClick.AddListener(onClick);
            labelText = Label(r, "Label", label, size, TextMain, TextAnchor.MiddleCenter, 0, 0, w, h, true);
            Stretch(labelText.rectTransform, 6f);
            return btn;
        }

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        public static void TopLeft(RectTransform r, float x, float y, float w, float h)
        {
            r.anchorMin = new Vector2(0f, 1f); r.anchorMax = new Vector2(0f, 1f); r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, -y);
            r.sizeDelta = new Vector2(w, h);
        }

        public static void Stretch(RectTransform r, float inset)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.pivot = new Vector2(0.5f, 0.5f);
            r.offsetMin = new Vector2(inset, inset); r.offsetMax = new Vector2(-inset, -inset);
        }

        static Font font;
        public static Font Font()
        {
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = UnityEngine.Font.CreateDynamicFontFromOSFont("Arial", 16);
            return font;
        }

        // ------------------------------------------------------------------ procedural sprites

        static Sprite ring, disc;

        /// <summary>Anti-aliased ring, used with Image.Type.Filled for the score dial.</summary>
        public static Sprite Ring()
        {
            if (ring == null) ring = MakeRadial("HoloRing", 256, 0.80f, 1f);
            return ring;
        }

        public static Sprite Disc()
        {
            if (disc == null) disc = MakeRadial("HoloDisc", 64, 0f, 1f);
            return disc;
        }

        static Sprite MakeRadial(string name, int size, float inner, float outer)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            float half = size * 0.5f, aa = 1.5f / half;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - half) / half, dy = (y + 0.5f - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01((outer - r) / aa) * (inner <= 0f ? 1f : Mathf.Clamp01((r - inner) / aa));
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        // ------------------------------------------------------------------ materials

        /// <summary>Lit glossy shell with optional emission (URP Lit; falls back to Standard).</summary>
        public static Material Lit(Color color, Color emission, float smoothness = 0.8f)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            var m = new Material(sh) { name = "HoloLit" };
            SetColor(m, color);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0.2f);
            if (emission.maxColorComponent > 0f && m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            return m;
        }

        /// <summary>Flat bright colour for eyes, mouth and cores (reads as a light source).</summary>
        public static Material Glowing(Color color)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            var m = new Material(sh) { name = "HoloGlow" };
            SetColor(m, color);
            return m;
        }

        /// <summary>Alpha-blended, vertex-coloured material (trails, halos). Sprites/Default is always included in builds.</summary>
        public static Material Translucent(Color color)
        {
            Shader sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
            var m = new Material(sh) { name = "HoloTranslucent" };
            SetColor(m, color);
            m.renderQueue = (int)RenderQueue.Transparent;
            return m;
        }

        public static void SetColor(Material m, Color c)
        {
            if (m == null) return;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        public static GameObject Primitive(Transform parent, string name, PrimitiveType type, Vector3 pos, Vector3 scale, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.Destroy(col);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        /// <summary>A flat glowing circle made with a LineRenderer (holographic hover rings).</summary>
        public static LineRenderer Circle(Transform parent, string name, float radius, float width, Material mat, int segments = 64)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.positionCount = segments;
            lr.widthMultiplier = width;
            lr.sharedMaterial = mat;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.alignment = LineAlignment.View;
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                lr.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f));
            }
            return lr;
        }

        public static string Money(double usd)
        {
            return usd >= 1000 ? "$" + usd.ToString("#,##0", System.Globalization.CultureInfo.InvariantCulture)
                               : "$" + usd.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        }

        public static string Humanize(string id)
        {
            if (string.IsNullOrEmpty(id)) return "-";
            return id.Replace('_', ' ');
        }

        /// <summary>Destroys a runtime-created object in play mode and edit mode alike.</summary>
        public static void Kill(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o); else UnityEngine.Object.DestroyImmediate(o);
        }

        public static Exercise FindExercise(string id)
        {
            foreach (var e in ExerciseLibrary.All) if (e.Id == id) return e;
            return null;
        }

        /// <summary>Readable form of a decision detail: decision label, "identify:x" -> "selected x", anything else as-is.</summary>
        public static string DecisionText(string detail)
        {
            if (string.IsNullOrEmpty(detail)) return "no answer";
            if (detail.StartsWith("identify:")) return "selected " + Humanize(detail.Substring(9));
            string label = ExerciseLibrary.DecisionLabel(detail);
            return string.IsNullOrEmpty(label) ? Humanize(detail) : label;
        }

        public static void Fade(CanvasGroup g, float a)
        {
            if (g == null) return;
            g.alpha = a;
            g.blocksRaycasts = a > 0.5f;
            g.interactable = a > 0.5f;
        }

        public static void AssignCamera(Canvas c)
        {
            if (c != null && c.worldCamera == null && Camera.main != null) c.worldCamera = Camera.main;
        }

        public static Action Noop = () => { };
    }
}
