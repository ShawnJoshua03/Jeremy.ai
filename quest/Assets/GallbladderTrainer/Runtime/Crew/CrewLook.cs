using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GallbladderTrainer.Crew
{
    /// <summary>Runtime meshes, unlit materials, and world-space TextMesh labels. No per-frame allocation.</summary>
    static class CrewLook
    {
        internal const int FontSize = 64;
        internal static readonly int BaseId = Shader.PropertyToID("_BaseColor");
        internal static readonly int ColorId = Shader.PropertyToID("_Color");
        internal static readonly int EmitId = Shader.PropertyToID("_EmissionColor");

        const string Glyphs = "0123456789$.,ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz:'()-/%+\n ";

        static Font font;
        static float emAtUnit = -1f;
        static Shader meshShader;
        static Shader textShader;
        static bool meshSearched;
        static bool textSearched;
        static bool warnedMesh;
        static bool warnedText;
        static float glyphFix = -1f;

        internal static Font BuiltinFont()
        {
            if (font != null) return font;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (font == null) font = Font.CreateDynamicFontFromOSFont(new[] { "Segoe UI", "Arial", "Calibri" }, FontSize);
            if (font != null)
            {
                font.RequestCharactersInTexture(Glyphs, FontSize, FontStyle.Normal);
                font.RequestCharactersInTexture(Glyphs, FontSize, FontStyle.Bold);
            }
            return font;
        }

        internal static float CharacterSize(float worldHeight)
        {
            if (worldHeight <= 0f) return 0.01f;
            float em = EmAtUnit();
            if (em < 0.01f) em = 1f;
            return worldHeight / em;
        }

        static float EmAtUnit()
        {
            if (emAtUnit > 0f) return emAtUnit;
            Font face = BuiltinFont();
            if (face == null) { emAtUnit = 1f; return emAtUnit; }
            var go = new GameObject("~crew-measure");
            go.hideFlags = HideFlags.HideAndDontSave;
            go.transform.position = new Vector3(0f, -2000f, 0f);
            var tm = go.AddComponent<TextMesh>();
            tm.font = face;
            tm.fontSize = FontSize;
            tm.characterSize = 1f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.richText = false;
            tm.text = "M";
            var rend = go.GetComponent<MeshRenderer>();
            if (rend != null) rend.enabled = false;
            var filter = go.GetComponent<MeshFilter>();
            float height = 0f;
            if (filter != null && filter.sharedMesh != null) height = filter.sharedMesh.bounds.size.y;
            if (height < 0.01f) height = 1f;
            emAtUnit = height;
            Object.Destroy(go);
            return emAtUnit;
        }

        internal static Shader MeshShader()
        {
            if (meshSearched) return meshShader;
            meshSearched = true;
            meshShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (meshShader == null) meshShader = Shader.Find("Unlit/Color");
            if (meshShader == null) meshShader = Shader.Find("Universal Render Pipeline/Lit");
            if (meshShader == null) meshShader = Shader.Find("Standard");
            if (meshShader == null) meshShader = Shader.Find("Hidden/InternalErrorShader");
            if (meshShader == null && !warnedMesh)
            {
                warnedMesh = true;
                Debug.LogWarning("GallbladderTrainer crew found no unlit shader.");
            }
            return meshShader;
        }

        internal static Shader TextShader()
        {
            if (textSearched) return textShader;
            textSearched = true;
            textShader = Shader.Find("GallbladderTrainer/CrewText");
            if (textShader == null) textShader = Shader.Find("GUI/Text Shader");
            if (textShader == null) textShader = MeshShader();
            if ((textShader == null || textShader.name == "Hidden/InternalErrorShader") && !warnedText)
            {
                warnedText = true;
                Debug.LogWarning("GallbladderTrainer crew text shader was not found. Tags may not render.");
            }
            return textShader;
        }

        internal static Material MakeMesh(Color color)
        {
            Shader shader = MeshShader();
            var mat = new Material(shader);
            mat.name = "CrewMesh";
            Paint(mat, color, true);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.35f);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.04f);
            return mat;
        }

        internal static Material MakeText(Color color)
        {
            Shader shader = TextShader();
            var mat = new Material(shader);
            mat.name = "CrewText";
            Font face = BuiltinFont();
            Texture tex = face != null && face.material != null ? face.material.mainTexture : null;
            if (tex != null)
            {
                mat.SetTexture("_MainTex", tex);
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            }
            Paint(mat, color, false);
            mat.renderQueue = 3100;
            return mat;
        }

        internal static void Paint(Material mat, Color color, bool emission)
        {
            if (mat == null) return;
            if (mat.HasProperty(BaseId)) mat.SetColor(BaseId, color);
            if (mat.HasProperty(ColorId)) mat.SetColor(ColorId, color);
            if (emission && mat.HasProperty(EmitId))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor(EmitId, color * 0.85f);
            }
        }

        internal static bool HasBase(Material mat) { return mat != null && mat.HasProperty(BaseId); }
        internal static bool HasColor(Material mat) { return mat != null && mat.HasProperty(ColorId); }
        internal static bool HasEmit(Material mat) { return mat != null && mat.HasProperty(EmitId); }

        internal static TextMesh Label(Transform parent, string name, string content, float height, Color color,
            TextAnchor anchor, FontStyle style, CrewTextRegistry registry, Material shared = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var tm = go.AddComponent<TextMesh>();
            Font face = BuiltinFont();
            if (face != null) tm.font = face;
            tm.fontSize = FontSize;
            tm.characterSize = CharacterSize(height);
            tm.anchor = anchor;
            tm.alignment = TextAlignment.Center;
            tm.fontStyle = style;
            tm.lineSpacing = 0.9f;
            tm.richText = false;
            tm.color = Color.white;
            tm.text = content ?? "";
            var rend = go.GetComponent<MeshRenderer>();
            Quiet(rend);
            Material mat = shared != null ? shared : MakeText(color);
            rend.sharedMaterial = mat;
            bool canSample = !string.IsNullOrEmpty(content) && content.Length >= 3 && content.IndexOf('\n') < 0;
            float sample = canSample ? height : 0f;
            if (registry != null) registry.Add(rend, mat, sample);
            if (glyphFix > 0f && (glyphFix <= 0.92f || glyphFix >= 1.08f))
                go.transform.localScale = new Vector3(glyphFix, glyphFix, glyphFix);
            return tm;
        }

        /// <summary>Mesh height over the requested cap height. Shared by every label so a bad TextMesh em is corrected once.</summary>
        internal static float GlyphFix(Renderer sample, float desired)
        {
            if (glyphFix > 0f) return glyphFix;
            if (sample == null || desired <= 0f) return -1f;
            var filter = sample.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return -1f;
            float height = filter.sharedMesh.bounds.size.y;
            if (height < 0.0001f) return -1f;
            glyphFix = desired / height;
            return glyphFix;
        }

        internal static Transform Piece(Transform parent, PrimitiveType type, string name, Vector3 pos, Vector3 euler, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                col.enabled = false;
                Object.Destroy(col);
            }
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            var rend = go.GetComponent<Renderer>();
            Quiet(rend);
            rend.sharedMaterial = mat;
            return go.transform;
        }

        internal static Transform Empty(Transform parent, string name, Vector3 pos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            return go.transform;
        }

        static void Quiet(Renderer rend)
        {
            if (rend == null) return;
            rend.shadowCastingMode = ShadowCastingMode.Off;
            rend.receiveShadows = false;
            rend.lightProbeUsage = LightProbeUsage.Off;
            rend.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        internal static float Damp(float current, float target, float lambda, float dt)
        {
            if (lambda <= 0f) return target;
            return Mathf.Lerp(current, target, 1f - Mathf.Exp(-lambda * dt));
        }
    }

    /// <summary>Reapplies the URP text material after TextMesh.OnEnable swaps in the font shader.</summary>
    sealed class CrewTextRegistry
    {
        readonly List<Renderer> renderers = new List<Renderer>();
        readonly List<Material> materials = new List<Material>();
        Renderer sample;
        float sampleDesired;

        public void Add(Renderer renderer, Material material, float desiredHeight)
        {
            renderers.Add(renderer);
            materials.Add(material);
            if (sample == null && desiredHeight > 0f)
            {
                sample = renderer;
                sampleDesired = desiredHeight;
            }
        }

        public void Apply()
        {
            int count = renderers.Count;
            for (int i = 0; i < count; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer != null) renderer.sharedMaterial = materials[i];
            }
            float fix = CrewLook.GlyphFix(sample, sampleDesired);
            if (fix < 0f || (fix > 0.92f && fix < 1.08f)) return;
            var scale = new Vector3(fix, fix, fix);
            for (int i = 0; i < count; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null) continue;
                float x = renderer.transform.localScale.x;
                if (x > 0.99f && x < 1.01f) renderer.transform.localScale = scale;
            }
        }
    }
}
