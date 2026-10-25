using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Jeremy
{
    // Shared look for every panel: dark glass cards with rounded corners, one accent, status colours,
    // and pill badges. Built from code (no prefabs), with sprites generated at runtime.
    public static class UiKit
    {
        public static readonly Color Glass = new Color(0.055f, 0.075f, 0.1f, 0.94f);
        public static readonly Color Card = new Color(0.1f, 0.135f, 0.17f, 1f);
        public static readonly Color Line = new Color(1f, 1f, 1f, 0.07f);
        public static readonly Color Ink = new Color(0.92f, 0.95f, 0.97f);
        public static readonly Color Muted = new Color(0.6f, 0.67f, 0.74f);
        public static readonly Color Accent = new Color(0.18f, 0.8f, 0.75f);
        public static readonly Color Good = new Color(0.27f, 0.86f, 0.55f);
        public static readonly Color Warn = new Color(1f, 0.71f, 0.28f);
        public static readonly Color Bad = new Color(1f, 0.36f, 0.37f);
        public static readonly Color Idle = new Color(0.25f, 0.3f, 0.36f);

        static Font font;
        public static Font Font => font != null ? font : font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        static readonly Dictionary<int, Sprite> rounded = new Dictionary<int, Sprite>();

        /// <summary>White rounded-rectangle sprite (9-sliced) with an anti-aliased edge; tint it with Image.color.</summary>
        public static Sprite Rounded(int radius)
        {
            if (rounded.TryGetValue(radius, out Sprite s) && s != null) return s;
            int size = radius * 2 + 4;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            float c = size / 2f - 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(Mathf.Abs(x - c) - (c - radius), 0f), dy = Mathf.Max(Mathf.Abs(y - c) - (c - radius), 0f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    byte a = (byte)(Mathf.Clamp01(radius - d + 0.5f) * 255f);
                    px[y * size + x] = new Color32(255, 255, 255, a);
                }
            tex.SetPixels32(px); tex.Apply(false, true);
            s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                new Vector4(radius + 1, radius + 1, radius + 1, radius + 1));
            rounded[radius] = s;
            return s;
        }

        public static RectTransform Rect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = anchorMin; r.anchorMax = anchorMax; r.offsetMin = offsetMin; r.offsetMax = offsetMax;
            return r;
        }

        /// <summary>A rounded panel filling its rect.</summary>
        public static Image Panel(RectTransform r, Color color, int radius = 18)
        {
            var img = r.gameObject.AddComponent<Image>();
            img.sprite = Rounded(radius); img.type = Image.Type.Sliced; img.color = color; img.raycastTarget = false;
            return img;
        }

        /// <summary>Panel at a fixed position and size, anchored at the top-left of its parent.</summary>
        public static Image PanelAt(Transform parent, string name, float x, float y, float w, float h, Color color, int radius = 12)
        {
            RectTransform r = Rect(parent, name, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, Vector2.zero);
            r.pivot = new Vector2(0, 1); r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(w, h);
            return Panel(r, color, radius);
        }

        public static Text Label(Transform parent, string name, float x, float y, float w, float h, int size, Color color,
            FontStyle style = FontStyle.Normal, TextAnchor anchor = TextAnchor.UpperLeft)
        {
            RectTransform r = Rect(parent, name, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, Vector2.zero);
            r.pivot = new Vector2(0, 1); r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(w, h);
            var t = r.gameObject.AddComponent<Text>();
            t.font = Font; t.fontSize = size; t.color = color; t.fontStyle = style; t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate;
            t.supportRichText = true; t.raycastTarget = false; t.lineSpacing = 1.1f;
            return t;
        }

        /// <summary>Rounded button colours: normal, hover and pressed derived from one base colour.</summary>
        public static void StyleButton(Button button, Color baseColor, int radius = 12)
        {
            var img = button.GetComponent<Image>();
            img.sprite = Rounded(radius); img.type = Image.Type.Sliced; img.color = Color.white;
            ColorBlock cb = button.colors;
            cb.normalColor = baseColor;
            cb.highlightedColor = Color.Lerp(baseColor, Color.white, 0.18f);
            cb.selectedColor = baseColor;
            cb.pressedColor = Color.Lerp(baseColor, Color.black, 0.25f);
            cb.disabledColor = new Color(baseColor.r, baseColor.g, baseColor.b, 0.35f);
            cb.fadeDuration = 0.08f;
            button.colors = cb;
            Text label = button.GetComponentInChildren<Text>();
            if (label != null) { label.color = Ink; label.fontStyle = FontStyle.Bold; }
        }

        public static Color Severity(float value, float good, float warn) => value <= good ? Good : value <= warn ? Warn : Bad;
    }
}
