using UnityEngine;
using UnityEngine.Rendering;

namespace Jeremy
{
    // Small shape, material and texture helpers for the desktop preview (built-in Standard shader).
    public static class OrKit
    {

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


        public static GameObject MeshPart(Transform parent, string name, Mesh mesh, Vector3 position, Quaternion rotation, Vector3 scale, Material material)
        {
            var part = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position; part.transform.localRotation = rotation; part.transform.localScale = scale;
            part.GetComponent<MeshFilter>().sharedMesh = mesh;
            part.GetComponent<MeshRenderer>().sharedMaterial = material;
            return part;
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

        // Subtle woven texture multiplied with the drape colour.

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
    }
}
