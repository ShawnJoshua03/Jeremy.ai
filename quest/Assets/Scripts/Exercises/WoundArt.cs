using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Jeremy
{
    // Materials, procedural textures and meshes for the wound-closure scene (built-in Standard shader).
    // Skin uses CC0 pore detail from ambientCG Leather038 (Assets/Art/Textures/WoundClosure/Resources),
    // re-toned to skin with procedural mottling; everything else is generated here.
    public static class WoundArt
    {
        private static Material skin, slabSide, woundBed, blood, steel, darkSteel, suture, needle, ink, glow, ring;
        private static Texture2D steelTexture;

        private static Material Standard(Color color, float smoothness, float metallic = 0f)
        {
            var material = new Material(Shader.Find("Standard")) { color = color };
            material.SetFloat("_Glossiness", smoothness);
            material.SetFloat("_Metallic", metallic);
            return material;
        }
        // Smoothness from the albedo alpha, scaled by _GlossMapScale.
        private static void SmoothnessFromAlbedo(Material material, float scale)
        {
            material.SetFloat("_SmoothnessTextureChannel", 1f);
            material.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            material.SetFloat("_GlossMapScale", scale);
        }

        public static Material Skin
        {
            get
            {
                if (skin != null) return skin;
                skin = Standard(Color.white, 0.4f);
                skin.name = "WC skin";
                var albedo = Resources.Load<Texture2D>("WC_SkinAlbedo");
                var normal = Resources.Load<Texture2D>("WC_SkinNormal");
                if (albedo != null) { skin.mainTexture = albedo; SmoothnessFromAlbedo(skin, 0.78f); }
                else skin.color = new Color(0.9f, 0.69f, 0.59f);
                if (normal != null)
                {
                    skin.SetTexture("_BumpMap", normal);
                    skin.SetFloat("_BumpScale", 0.9f);
                    skin.EnableKeyword("_NORMALMAP");
                }
                skin.mainTextureScale = Vector2.one; // UVs are already in tiles (see WoundPad)
                return skin;
            }
        }
        public static Material SlabSide
        {
            get
            {
                if (slabSide != null) return slabSide;
                slabSide = Standard(Color.white, 0.35f);
                slabSide.name = "WC tissue layers";
                slabSide.mainTexture = LayerTexture();
                SmoothnessFromAlbedo(slabSide, 0.8f);
                return slabSide;
            }
        }
        public static Material WoundBed
        {
            get
            {
                if (woundBed != null) return woundBed;
                woundBed = Standard(Color.white, 0.8f);
                woundBed.name = "WC wound bed";
                woundBed.mainTexture = WoundTexture();
                SmoothnessFromAlbedo(woundBed, 0.95f);
                return woundBed;
            }
        }
        public static Material Blood
        {
            get
            {
                if (blood != null) return blood;
                blood = Fade(Color.white, 0.75f);
                blood.name = "WC edge blood";
                blood.mainTexture = BloodTexture();
                return blood;
            }
        }
        public static Material Steel
        {
            get
            {
                if (steel != null) return steel;
                steel = Standard(new Color(0.86f, 0.87f, 0.89f), 0.72f, 0.85f);
                steel.name = "WC brushed steel";
                steel.mainTexture = SteelTexture();
                SmoothnessFromAlbedo(steel, 0.85f);
                return steel;
            }
        }
        public static Material DarkSteel
        {
            get
            {
                if (darkSteel != null) return darkSteel;
                darkSteel = Standard(new Color(0.55f, 0.56f, 0.58f), 0.6f, 0.85f); // tungsten-carbide jaw inserts
                darkSteel.name = "WC jaw insert";
                return darkSteel;
            }
        }
        public static Material Needle
        {
            get
            {
                if (needle != null) return needle;
                needle = Standard(new Color(0.9f, 0.9f, 0.92f), 0.92f, 1f);
                needle.name = "WC needle";
                return needle;
            }
        }
        // Blue polypropylene monofilament: slightly glossy, nearly no diffuse texture.
        public static Material Suture
        {
            get
            {
                if (suture != null) return suture;
                suture = Standard(new Color(0.07f, 0.10f, 0.38f), 0.78f);
                suture.name = "WC suture";
                return suture;
            }
        }
        public static Material Ink
        {
            get
            {
                if (ink != null) return ink;
                ink = Standard(new Color(0.36f, 0.16f, 0.55f), 0.15f);
                ink.name = "WC marker ink";
                ink.EnableKeyword("_EMISSION");
                ink.SetColor("_EmissionColor", Color.black);
                return ink;
            }
        }
        public static Material Glow
        {
            get
            {
                if (glow != null) return glow;
                glow = Fade(new Color(0.35f, 1f, 0.75f, 0.85f), 0.2f);
                glow.name = "WC target glow";
                glow.EnableKeyword("_EMISSION");
                glow.SetColor("_EmissionColor", new Color(0.3f, 1f, 0.7f) * 1.6f);
                return glow;
            }
        }
        public static Material Ring
        {
            get
            {
                if (ring != null) return ring;
                ring = Fade(new Color(1f, 1f, 1f, 0.8f), 0.2f);
                ring.name = "WC aim ring";
                ring.EnableKeyword("_EMISSION");
                ring.SetColor("_EmissionColor", Color.white * 0.6f);
                return ring;
            }
        }

        public static Material Fade(Color color, float smoothness)
        {
            Material material = Standard(color, smoothness);
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

        public static void SetGlow(Renderer renderer, Color color, Color emission)
        {
            var block = new MaterialPropertyBlock();
            block.SetColor("_Color", color);
            block.SetColor("_EmissionColor", emission);
            renderer.SetPropertyBlock(block);
        }

        // ---------- textures ----------
        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xffffff) / (float)0xffffff;
            }
        }
        // Tileable value noise on a period-sized lattice.
        private static float Noise(float x, float y, int period, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            int Wrap(int v) => ((v % period) + period) % period;
            float a = Hash(Wrap(x0), Wrap(y0), seed), b = Hash(Wrap(x0 + 1), Wrap(y0), seed);
            float c = Hash(Wrap(x0), Wrap(y0 + 1), seed), d = Hash(Wrap(x0 + 1), Wrap(y0 + 1), seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }
        private static float Fbm(float u, float v, int basePeriod, int octaves, int seed)
        {
            float sum = 0, amp = 0.5f, norm = 0;
            int period = basePeriod;
            for (int o = 0; o < octaves; o++)
            {
                sum += amp * Noise(u * period, v * period, period, seed + o * 17);
                norm += amp; amp *= 0.5f; period *= 2;
            }
            return sum / norm;
        }
        private static Texture2D NewTexture(int w, int h, string name, TextureWrapMode wrap = TextureWrapMode.Repeat)
        {
            return new Texture2D(w, h, TextureFormat.RGBA32, true) { name = name, wrapMode = wrap, anisoLevel = 4, filterMode = FilterMode.Trilinear };
        }

        // v = depth into the wound (0 rim .. 1 floor), u along the incision. Rim: pale dermis; then red dermis;
        // deeper: glistening yellow fat lobules under dark blood. Alpha = smoothness (wet).
        private static Texture2D WoundTexture()
        {
            const int w = 128, h = 64;
            Texture2D t = NewTexture(w, h, "WC wound bed");
            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = x / (float)w, v = y / (h - 1f);
                    float n = Fbm(u, v * 0.5f, 8, 4, 3), lob = Fbm(u, v * 0.5f, 24, 2, 9);
                    Color dermisPale = new Color(0.86f, 0.58f, 0.55f), dermis = new Color(0.62f, 0.13f, 0.12f);
                    Color fat = new Color(0.86f, 0.70f, 0.34f), bloodDark = new Color(0.30f, 0.02f, 0.03f);
                    Color c = v < 0.18f ? Color.Lerp(dermisPale, dermis, v / 0.18f)
                        : Color.Lerp(dermis, bloodDark, Mathf.SmoothStep(0, 1, (v - 0.18f) / 0.5f));
                    if (v > 0.45f && lob > 0.62f) c = Color.Lerp(c, fat, Mathf.Clamp01((lob - 0.62f) * 6f) * 0.7f * (1 - n * 0.6f));
                    c *= 0.85f + 0.3f * n;
                    c.a = Mathf.Clamp01(0.75f + 0.25f * v);
                    pixels[y * w + x] = c;
                }
            t.SetPixels(pixels); t.Apply(true);
            return t;
        }
        // u across the strip (0 at the cut edge), v along it. Alpha falls off away from the edge, patchy.
        private static Texture2D BloodTexture()
        {
            const int w = 32, h = 128;
            Texture2D t = NewTexture(w, h, "WC edge blood", TextureWrapMode.Clamp);
            t.wrapModeV = TextureWrapMode.Repeat;
            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = x / (w - 1f), v = y / (float)h;
                    float n = Fbm(0.3f, v, 16, 4, 21);
                    float reach = 0.35f + 0.65f * n;
                    float a = Mathf.Clamp01(1 - u / reach);
                    a = a * a * (0.55f + 0.45f * n);
                    pixels[y * w + x] = new Color(0.42f, 0.03f, 0.04f, a * 0.85f);
                }
            t.SetPixels(pixels); t.Apply(true);
            return t;
        }
        // Side of the practice pad: v = height (0 bottom .. 1 top). Skin, pale dermis, lobular fat, muscle.
        private static Texture2D LayerTexture()
        {
            const int w = 128, h = 64;
            Texture2D t = NewTexture(w, h, "WC tissue layers");
            t.wrapModeV = TextureWrapMode.Clamp;
            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = x / (float)w, v = y / (h - 1f);
                    float n = Fbm(u, v * 0.4f, 16, 3, 5), lob = Fbm(u, v * 0.3f, 32, 2, 11);
                    Color c;
                    if (v > 0.86f) c = new Color(0.88f, 0.66f, 0.56f);
                    else if (v > 0.74f) c = new Color(0.93f, 0.80f, 0.74f);
                    else if (v > 0.26f) c = Color.Lerp(new Color(0.94f, 0.82f, 0.48f), new Color(0.84f, 0.66f, 0.32f), lob);
                    else c = new Color(0.58f, 0.14f, 0.13f);
                    c *= 0.9f + 0.2f * n;
                    c.a = v > 0.74f ? 0.45f : v > 0.26f ? 0.6f : 0.5f;
                    pixels[y * w + x] = c;
                }
            t.SetPixels(pixels); t.Apply(true);
            return t;
        }
        // Brushed stainless: fine streaks along u. Alpha = smoothness.
        private static Texture2D SteelTexture()
        {
            if (steelTexture != null) return steelTexture;
            const int w = 256, h = 256;
            steelTexture = NewTexture(w, h, "WC brushed steel");
            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = x / (float)w, v = y / (float)h;
                    float streak = Noise(u * 2, v * 256, 256, 31) * 0.6f + Noise(u * 8, v * 128, 128, 37) * 0.4f;
                    float g = 0.86f + 0.14f * streak;
                    pixels[y * w + x] = new Color(g, g, g * 1.01f, 0.62f + 0.3f * streak);
                }
            steelTexture.SetPixels(pixels); steelTexture.Apply(true);
            return steelTexture;
        }

        // ---------- meshes ----------
        /// <summary>A lofted bar along local z. Each station: z, centre (x, y), width, height. Superelliptic sections.</summary>
        public static Mesh Loft(float[] z, Vector2[] centre, Vector2[] size, int sides = 14, float exponent = 3f, string name = "Loft")
        {
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            int n = z.Length;
            for (int i = 0; i < n; i++)
                for (int s = 0; s < sides; s++)
                {
                    float a = s / (float)sides * Mathf.PI * 2, c = Mathf.Cos(a), sn = Mathf.Sin(a);
                    float px = Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), 2f / exponent), py = Mathf.Sign(sn) * Mathf.Pow(Mathf.Abs(sn), 2f / exponent);
                    vertices.Add(new Vector3(centre[i].x + px * size[i].x / 2, centre[i].y + py * size[i].y / 2, z[i]));
                    uvs.Add(new Vector2(z[i] * 8f, s / (float)sides));
                }
            for (int i = 0; i < n - 1; i++)
                for (int s = 0; s < sides; s++)
                {
                    int a = i * sides + s, b = i * sides + (s + 1) % sides, c = a + sides, d = b + sides;
                    triangles.AddRange(new[] { a, b, c, b, d, c }); // outward (Unity front = Cross(b - a, c - a))
                }
            for (int end = 0; end < 2; end++)
            {
                int ring = end == 0 ? 0 : (n - 1) * sides;
                int centreIndex = vertices.Count;
                vertices.Add(new Vector3(centre[end == 0 ? 0 : n - 1].x, centre[end == 0 ? 0 : n - 1].y, z[end == 0 ? 0 : n - 1]));
                uvs.Add(Vector2.zero);
                for (int s = 0; s < sides; s++)
                {
                    int a = ring + s, b = ring + (s + 1) % sides;
                    if (end == 0) triangles.AddRange(new[] { centreIndex, b, a }); else triangles.AddRange(new[] { centreIndex, a, b });
                }
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices); mesh.SetUVs(0, uvs); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            return mesh;
        }
        /// <summary>Tube along a polyline with per-point radius. `up` must not be parallel to the path.</summary>
        public static Mesh Tube(Vector3[] path, float[] radius, Vector3 up, int sides = 10, string name = "Tube")
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            for (int i = 0; i < path.Length; i++)
            {
                Vector3 tangent = (path[Mathf.Min(i + 1, path.Length - 1)] - path[Mathf.Max(i - 1, 0)]).normalized;
                Vector3 side = Vector3.Cross(tangent, up).normalized, top = Vector3.Cross(side, tangent);
                for (int s = 0; s <= sides; s++)
                {
                    float a = s / (float)sides * Mathf.PI * 2;
                    Vector3 dir = side * Mathf.Cos(a) + top * Mathf.Sin(a);
                    vertices.Add(path[i] + dir * radius[i]);
                    normals.Add(dir);
                    uvs.Add(new Vector2(i / (path.Length - 1f), s / (float)sides));
                }
            }
            int ring = sides + 1;
            for (int i = 0; i < path.Length - 1; i++)
                for (int s = 0; s < sides; s++)
                {
                    int a = i * ring + s, b = a + 1, c = a + ring, d = c + 1;
                    triangles.AddRange(new[] { a, b, c, b, d, c });
                }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uvs); mesh.SetTriangles(triangles, 0);
            // Make the winding agree with the outward normals.
            int[] t = mesh.triangles;
            if (t.Length >= 3 && Vector3.Dot(Vector3.Cross(vertices[t[1]] - vertices[t[0]], vertices[t[2]] - vertices[t[0]]), normals[t[0]]) < 0)
            {
                for (int i = 0; i < t.Length; i += 3) { int k = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = k; }
                mesh.triangles = t;
            }
            mesh.RecalculateTangents(); mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>3/8-circle surgical needle in its local XY plane, centred on the origin: point at angle 0,
        /// body running clockwise to the swage at -135 degrees.</summary>
        public static Mesh NeedleMesh(float radius, float wire)
        {
            const int count = 40;
            var path = new Vector3[count];
            var radii = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = i / (count - 1f), a = -t * 135f * Mathf.Deg2Rad;
                path[i] = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * radius;
                float taper = Mathf.Clamp01(t / 0.22f); // sharp taper point
                radii[i] = wire * (0.08f + 0.92f * Mathf.Sqrt(taper)) * (t > 0.93f ? 1.12f : 1f); // swage is a little thicker
            }
            return Tube(path, radii, Vector3.forward, 10, "Needle");
        }

        /// <summary>Flat annulus in XZ (for the aim ring and the target halo), inner/outer radius 0.7/1.</summary>
        public static Mesh Annulus(int segments = 40, float inner = 0.72f)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2;
                Vector3 d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                vertices.Add(d * inner); vertices.Add(d);
            }
            for (int i = 0; i < segments; i++)
            {
                int a = i * 2;
                triangles.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 });
            }
            var mesh = new Mesh { name = "Annulus" };
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0);
            var normals = new Vector3[vertices.Count];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;
            mesh.normals = normals;
            int[] t = mesh.triangles;
            if (Vector3.Dot(Vector3.Cross(vertices[t[1]] - vertices[t[0]], vertices[t[2]] - vertices[t[0]]), Vector3.up) < 0)
            {
                for (int i = 0; i < t.Length; i += 3) { int k = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = k; }
                mesh.triangles = t;
            }
            mesh.RecalculateBounds();
            return mesh;
        }

        public static MeshRenderer MeshObject(Transform parent, string name, Mesh mesh, Material material, bool shadows = true)
        {
            var item = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            item.transform.SetParent(parent, false);
            item.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = item.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            if (!shadows) renderer.shadowCastingMode = ShadowCastingMode.Off;
            return renderer;
        }

        /// <summary>Lit thread: LineRenderer with generated normals so the Standard suture material shades.</summary>
        public static LineRenderer Thread(Transform parent, string name, float width)
        {
            var line = new GameObject(name).AddComponent<LineRenderer>();
            line.transform.SetParent(parent, false);
            line.useWorldSpace = false;
            line.widthMultiplier = width;
            line.numCapVertices = 3;
            line.numCornerVertices = 3;
            line.generateLightingData = true;
            line.alignment = LineAlignment.View;
            line.shadowCastingMode = ShadowCastingMode.On;
            line.sharedMaterial = Suture;
            line.positionCount = 0;
            return line;
        }
    }
}
