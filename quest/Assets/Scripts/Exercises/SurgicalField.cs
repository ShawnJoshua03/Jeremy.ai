using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Jeremy
{
    // Procedural surgical content for the laparoscopic exercise: tileable noise, generated tissue / stone textures
    // (albedo, normal, metallic-gloss) and smooth subdivided meshes. Built-in pipeline, Standard shader.
    // Used on the headset and in the desktop preview, so nothing here depends on the preview room.
    public static class SurgicalField
    {
        // ---------- tileable gradient noise (period in lattice cells, coordinates in [0, period)) ----------
        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xffffff) / 16777216f;
            }
        }

        private static float Grad(int x, int y, int period, int seed, float dx, float dy)
        {
            x = ((x % period) + period) % period; y = ((y % period) + period) % period;
            float a = Hash(x, y, seed) * Mathf.PI * 2f;
            return Mathf.Cos(a) * dx + Mathf.Sin(a) * dy;
        }

        private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

        /// <summary>Gradient noise in about [-0.7, 0.7], tileable on [0,1) for an integer period.</summary>
        public static float Noise(float u, float v, int period, int seed)
        {
            float x = u * period, y = v * period;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0, sx = Fade(fx), sy = Fade(fy);
            float n00 = Grad(x0, y0, period, seed, fx, fy), n10 = Grad(x0 + 1, y0, period, seed, fx - 1, fy);
            float n01 = Grad(x0, y0 + 1, period, seed, fx, fy - 1), n11 = Grad(x0 + 1, y0 + 1, period, seed, fx - 1, fy - 1);
            return Mathf.Lerp(Mathf.Lerp(n00, n10, sx), Mathf.Lerp(n01, n11, sx), sy);
        }

        public static float Fbm(float u, float v, int period, int octaves, int seed)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int o = 0; o < octaves; o++, period *= 2, amp *= 0.5f)
            {
                sum += amp * Noise(u, v, period, seed + o * 31);
                norm += amp;
            }
            return sum / norm; // about [-0.6, 0.6]
        }

        /// <summary>Thin branching lines (blood vessels): 1 on a ridge of the noise, 0 elsewhere.</summary>
        public static float Ridge(float u, float v, int period, int seed, float width)
        {
            float n = Mathf.Abs(Fbm(u, v, period, 3, seed));
            return Mathf.Clamp01(1f - n / width);
        }

        // ---------- textures ----------
        public sealed class TissueStyle
        {
            public Color baseColor, darkColor, vesselColor, fatColor;
            public float vessels = 1f, fat = 0f, mottling = 1f, bump = 1f;
            public float smoothMin = 0.62f, smoothMax = 0.9f;
            public int seed = 1;
        }

        public static readonly TissueStyle Bowel = new TissueStyle
        {
            baseColor = new Color(0.80f, 0.42f, 0.38f), darkColor = new Color(0.55f, 0.20f, 0.19f),
            vesselColor = new Color(0.45f, 0.06f, 0.08f), fatColor = new Color(0.92f, 0.78f, 0.46f), fat = 0.55f, seed = 11
        };
        public static readonly TissueStyle Mesentery = new TissueStyle
        {
            baseColor = new Color(0.74f, 0.34f, 0.30f), darkColor = new Color(0.46f, 0.13f, 0.12f),
            vesselColor = new Color(0.36f, 0.03f, 0.06f), fatColor = new Color(0.94f, 0.80f, 0.48f), fat = 0.8f, bump = 1.2f, seed = 3
        };
        public static readonly TissueStyle Liver = new TissueStyle
        {
            baseColor = new Color(0.42f, 0.12f, 0.09f), darkColor = new Color(0.26f, 0.05f, 0.04f),
            vesselColor = new Color(0.3f, 0.05f, 0.05f), vessels = 0.25f, mottling = 1.4f, bump = 0.5f,
            smoothMin = 0.74f, smoothMax = 0.93f, seed = 21
        };
        public static readonly TissueStyle Gallbladder = new TissueStyle
        {
            baseColor = new Color(0.46f, 0.56f, 0.46f), darkColor = new Color(0.30f, 0.38f, 0.32f),
            vesselColor = new Color(0.55f, 0.14f, 0.14f), vessels = 0.8f, bump = 0.5f, smoothMin = 0.7f, seed = 31
        };
        public static readonly TissueStyle Peritoneum = new TissueStyle
        {
            baseColor = new Color(0.86f, 0.60f, 0.56f), darkColor = new Color(0.68f, 0.38f, 0.36f),
            vesselColor = new Color(0.62f, 0.12f, 0.12f), vessels = 0.8f, fat = 0.2f, fatColor = new Color(0.93f, 0.82f, 0.62f),
            bump = 0.7f, smoothMin = 0.55f, smoothMax = 0.85f, seed = 41
        };

        /// <summary>Iodine-prepped abdominal skin (outside of the closed abdomen).</summary>
        public static readonly TissueStyle Skin = new TissueStyle
        {
            baseColor = new Color(0.80f, 0.52f, 0.36f), darkColor = new Color(0.62f, 0.34f, 0.20f),
            vesselColor = new Color(0.62f, 0.34f, 0.2f), vessels = 0f, mottling = 0.7f, bump = 0.35f,
            smoothMin = 0.3f, smoothMax = 0.62f, seed = 51
        };

        public struct TextureSet { public Texture2D albedo, normal, gloss; }
        private static readonly Dictionary<TissueStyle, TextureSet> tissueCache = new Dictionary<TissueStyle, TextureSet>();

        private static Texture2D NewTexture(int size, string name, bool linear) =>
            new Texture2D(size, size, TextureFormat.RGBA32, true, linear)
            { name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };

        public static TextureSet TissueTextures(TissueStyle s, int size = 512)
        {
            if (tissueCache.TryGetValue(s, out TextureSet cached) && cached.albedo != null) return cached;
            var albedo = new Color[size * size];
            var height = new float[size * size];
            var gloss = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    float mottle = Fbm(u, v, 4, 5, s.seed) * s.mottling;
                    float fine = Fbm(u, v, 32, 3, s.seed + 7);
                    float big = Ridge(u, v, 3, s.seed + 13, 0.045f) * s.vessels;
                    float small = Ridge(u, v, 8, s.seed + 17, 0.03f) * s.vessels * 0.7f;
                    float capillary = Ridge(u, v, 20, s.seed + 19, 0.05f) * s.vessels * 0.35f;
                    float fatMask = s.fat > 0f ? Mathf.SmoothStep(0f, 1f, (Fbm(u, v, 3, 4, s.seed + 23) + 0.05f) * 6f) * s.fat : 0f;
                    Color c = Color.Lerp(s.baseColor, s.darkColor, Mathf.Clamp01(0.5f + mottle * 1.2f));
                    c = Color.Lerp(c, s.fatColor, fatMask * (0.7f + 0.3f * fine));
                    float vessel = Mathf.Max(big, Mathf.Max(small, capillary) * (1f - fatMask * 0.6f));
                    c = Color.Lerp(c, s.vesselColor, vessel * 0.85f);
                    c *= 0.94f + 0.12f * fine;
                    c.a = 1f;
                    albedo[y * size + x] = c;
                    // Height: soft lobules, raised vessels, fat globules.
                    float lobules = Fbm(u, v, 12, 3, s.seed + 29);
                    height[y * size + x] = s.bump * (0.5f * lobules + 0.35f * fine + 0.6f * big + 0.3f * small + 0.5f * fatMask * (0.5f + Fbm(u, v, 24, 2, s.seed + 37)));
                    float wet = Mathf.Lerp(s.smoothMin, s.smoothMax, Mathf.Clamp01(0.5f + Fbm(u, v, 6, 3, s.seed + 43) * 1.4f));
                    gloss[y * size + x] = new Color(0f, 0f, 0f, wet);
                }
            var set = new TextureSet
            {
                albedo = Bake(size, "Tissue albedo", albedo, false),
                normal = NormalFromHeight(size, height, 3.5f, "Tissue normal"),
                gloss = Bake(size, "Tissue gloss", gloss, true)
            };
            tissueCache[s] = set;
            return set;
        }

        private static TextureSet stoneSet;
        public static TextureSet StoneTextures(int size = 256)
        {
            if (stoneSet.albedo != null) return stoneSet;
            var albedo = new Color[size * size];
            var height = new float[size * size];
            var gloss = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    float n = Fbm(u, v, 6, 5, 71);
                    float speck = Mathf.Clamp01((Fbm(u, v, 48, 2, 73) - 0.18f) * 5f);
                    float crack = Ridge(u, v, 5, 79, 0.04f);
                    float layer = 0.5f + 0.5f * Mathf.Sin((u + n * 0.6f) * 40f);
                    float g = Mathf.Clamp01(0.55f + n * 1.1f) * (0.85f + 0.15f * layer);
                    Color c = new Color(g, g, g, 1f) * (1f - 0.35f * speck) * (1f - 0.5f * crack);
                    c.a = 1f;
                    albedo[y * size + x] = c;
                    height[y * size + x] = 0.8f * n + 0.25f * speck - 0.6f * crack + 0.08f * layer;
                    gloss[y * size + x] = new Color(0f, 0f, 0f, 0.18f + 0.22f * Mathf.Clamp01(0.5f + n));
                }
            stoneSet = new TextureSet
            {
                albedo = Bake(size, "Stone albedo", albedo, false),
                normal = NormalFromHeight(size, height, 6f, "Stone normal"),
                gloss = Bake(size, "Stone gloss", gloss, true)
            };
            return stoneSet;
        }

        private static Texture2D bagNormal;
        /// <summary>Crinkled thin plastic: fine creases for the retrieval bag highlights.</summary>
        public static Texture2D PlasticCreases(int size = 256)
        {
            if (bagNormal != null) return bagNormal;
            var height = new float[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    height[y * size + x] = 0.6f * Ridge(u, v, 3, 91, 0.12f) + 0.3f * Ridge(u, v, 7, 93, 0.1f) + 0.2f * Fbm(u, v, 4, 3, 95);
                }
            bagNormal = NormalFromHeight(size, height, 2.5f, "Bag creases");
            return bagNormal;
        }

        private static Texture2D Bake(int size, string name, Color[] pixels, bool linear)
        {
            Texture2D t = NewTexture(size, name, linear);
            t.SetPixels(pixels);
            t.Apply(true);
            return t;
        }

        // Unity's UnpackNormal reads x from r*a, so store x in r with a = 1 (works with and without DXT5nm).
        private static Texture2D NormalFromHeight(int size, float[] h, float strength, string name)
        {
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float l = h[y * size + (x + size - 1) % size], r = h[y * size + (x + 1) % size];
                    float d = h[((y + size - 1) % size) * size + x], u = h[((y + 1) % size) * size + x];
                    var n = new Vector3((l - r) * strength, (d - u) * strength, 1f).normalized;
                    pixels[y * size + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                }
            return Bake(size, name, pixels, true);
        }

        // ---------- materials ----------
        public static Material Standard(Color color, float smoothness, float metallic = 0f)
        {
            var m = new Material(Shader.Find("Standard")) { color = color };
            m.SetFloat("_Glossiness", smoothness);
            m.SetFloat("_Metallic", metallic);
            return m;
        }

        public static Material Textured(TextureSet set, Vector2 tiling, Color tint, float bump)
        {
            Material m = Standard(tint, 0.8f);
            m.mainTexture = set.albedo; m.mainTextureScale = tiling;
            if (set.normal != null)
            {
                m.SetTexture("_BumpMap", set.normal); m.SetFloat("_BumpScale", bump); m.EnableKeyword("_NORMALMAP");
            }
            if (set.gloss != null)
            {
                m.SetTexture("_MetallicGlossMap", set.gloss); m.SetFloat("_GlossMapScale", 1f); m.EnableKeyword("_METALLICGLOSSMAP");
            }
            return m;
        }

        public static Material Tissue(TissueStyle style, Vector2 tiling, float bump = 1f) =>
            Textured(TissueTextures(style), tiling, Color.white, bump);

        /// <summary>Standard "Transparent" mode: the surface fades but specular highlights stay (plastic, film).</summary>
        public static Material ClearPlastic(Color color, float smoothness)
        {
            Material m = Standard(color, smoothness);
            m.SetFloat("_Mode", 3f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)BlendMode.One);
            m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON");
            m.DisableKeyword("_ALPHABLEND_ON");
            m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetTexture("_BumpMap", PlasticCreases()); m.SetFloat("_BumpScale", 0.6f); m.EnableKeyword("_NORMALMAP");
            m.mainTextureScale = new Vector2(3f, 2f);
            return m;
        }

        // ---------- meshes ----------
        /// <summary>Tube along a Catmull-Rom curve; radius tapers to zero at both ends (ends tuck under tissue).</summary>
        public static Mesh Tube(Vector3[] controls, float radius, int segments, int sides, int seed, float bulge = 0.12f)
        {
            var path = new Vector3[segments + 1];
            for (int i = 0; i <= segments; i++) path[i] = CatmullRom(controls, i / (float)segments);
            var vertices = new Vector3[(segments + 1) * (sides + 1)];
            var uvs = new Vector2[vertices.Length];
            Vector3 normal = Vector3.up;
            float length = 0f;
            for (int i = 0; i <= segments; i++)
            {
                if (i > 0) length += Vector3.Distance(path[i], path[i - 1]);
                Vector3 t = (path[Mathf.Min(i + 1, segments)] - path[Mathf.Max(i - 1, 0)]).normalized;
                Vector3 side = Vector3.Cross(t, normal).normalized;
                if (side.sqrMagnitude < 0.5f) side = Vector3.Cross(t, Vector3.forward).normalized;
                normal = Vector3.Cross(side, t).normalized;
                float f = i / (float)segments;
                float taper = Mathf.Sqrt(Mathf.Clamp01(Mathf.Sin(f * Mathf.PI) * 3.2f));
                float wobble = 1f + bulge * Mathf.Sin(f * 37f + seed) + 0.5f * bulge * Mathf.Sin(f * 83f + seed * 2f);
                for (int j = 0; j <= sides; j++)
                {
                    float a = j * Mathf.PI * 2f / sides;
                    float squash = 1f - 0.18f * Mathf.Max(0f, -Mathf.Sin(a)); // flatter where it rests on the bed
                    Vector3 offset = (side * Mathf.Cos(a) + normal * Mathf.Sin(a) * squash) * radius * taper * wobble;
                    vertices[i * (sides + 1) + j] = path[i] + offset;
                    uvs[i * (sides + 1) + j] = new Vector2(length * 6f, j / (float)sides * radius * 40f);
                }
            }
            return GridMesh("Tube", vertices, uvs, segments, sides, false);
        }

        /// <summary>Smooth tube along a Catmull-Rom curve with radius(t), t = 0..1 from the first control point.
        /// A radius of 0 at an end closes it (rounded cap); otherwise the end is left open (tucked into an organ).</summary>
        public static Mesh Tube(Vector3[] controls, System.Func<float, float> radius, int segments = 96, int sides = 32)
        {
            var path = new Vector3[segments + 1];
            for (int i = 0; i <= segments; i++) path[i] = CatmullRom(controls, i / (float)segments);
            var vertices = new Vector3[(segments + 1) * (sides + 1)];
            var uvs = new Vector2[vertices.Length];
            Vector3 normal = Vector3.Cross(path[Mathf.Min(1, segments)] - path[0], Vector3.right).normalized;
            if (normal.sqrMagnitude < 0.5f) normal = Vector3.up;
            float length = 0f;
            for (int i = 0; i <= segments; i++)
            {
                if (i > 0) length += Vector3.Distance(path[i], path[i - 1]);
                Vector3 t = (path[Mathf.Min(i + 1, segments)] - path[Mathf.Max(i - 1, 0)]).normalized;
                Vector3 side = Vector3.Cross(t, normal).normalized;
                if (side.sqrMagnitude < 0.5f) side = Vector3.Cross(t, Vector3.forward).normalized;
                normal = Vector3.Cross(side, t).normalized; // parallel transport, no twisting
                float r = radius(i / (float)segments);
                for (int j = 0; j <= sides; j++)
                {
                    float a = j * Mathf.PI * 2f / sides;
                    vertices[i * (sides + 1) + j] = path[i] + (side * Mathf.Cos(a) + normal * Mathf.Sin(a)) * r;
                    uvs[i * (sides + 1) + j] = new Vector2(length * 8f, j / (float)sides);
                }
            }
            return GridMesh("Tube", vertices, uvs, segments, sides, false);
        }

        public static Vector3 CurvePoint(Vector3[] controls, float t) => CatmullRom(controls, t);

        /// <summary>Surface of revolution about +y from a (radius, height) profile listed bottom to top; smooth normals.
        /// Ends with radius 0 close the shape. ridges adds axial grip ribs (count, depth as a fraction of radius).</summary>
        public static Mesh Lathe(Vector2[] profile, int segments = 48, int ridges = 0, float ridgeDepth = 0f)
        {
            int rows = profile.Length - 1;
            var vertices = new Vector3[(rows + 1) * (segments + 1)];
            var uvs = new Vector2[vertices.Length];
            for (int i = 0; i <= rows; i++)
                for (int j = 0; j <= segments; j++)
                {
                    float a = j * Mathf.PI * 2f / segments;
                    float r = profile[i].x * (ridges > 0 ? 1f - ridgeDepth * Mathf.Pow(Mathf.Abs(Mathf.Sin(a * ridges * 0.5f)), 6f) : 1f);
                    vertices[i * (segments + 1) + j] = new Vector3(Mathf.Cos(a) * r, profile[i].y, Mathf.Sin(a) * r);
                    uvs[i * (segments + 1) + j] = new Vector2(j / (float)segments, i / (float)rows);
                }
            return GridMesh("Lathe", vertices, uvs, rows, segments, false);
        }

        /// <summary>Box with rounded edges and corners (radius r), smooth shading, centred on the origin.</summary>
        public static Mesh RoundedBox(Vector3 size, float r, int n = 6)
        {
            Vector3 half = size * 0.5f, inner = half - Vector3.one * r;
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();
            Vector3[] axes = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            int res = n * 2 + 2;
            foreach (Vector3 nrm in axes)
            {
                Vector3 u = Mathf.Abs(nrm.y) > 0.5f ? Vector3.right : Vector3.up;
                Vector3 v = Vector3.Cross(nrm, u);
                int start = vertices.Count;
                for (int i = 0; i <= res; i++)
                    for (int j = 0; j <= res; j++)
                    {
                        Vector3 p = Vector3.Scale(nrm + u * (i / (float)res * 2f - 1f) + v * (j / (float)res * 2f - 1f), half);
                        Vector3 c = new Vector3(Mathf.Clamp(p.x, -inner.x, inner.x), Mathf.Clamp(p.y, -inner.y, inner.y), Mathf.Clamp(p.z, -inner.z, inner.z));
                        Vector3 d = (p - c).normalized;
                        vertices.Add(c + d * r);
                        normals.Add(d);
                    }
                for (int i = 0; i < res; i++)
                    for (int j = 0; j < res; j++)
                    {
                        int a0 = start + i * (res + 1) + j, a1 = a0 + 1, b0 = a0 + res + 1, b1 = b0 + 1;
                        triangles.Add(a0); triangles.Add(b0); triangles.Add(a1);
                        triangles.Add(a1); triangles.Add(b0); triangles.Add(b1);
                    }
            }
            var mesh = new Mesh { name = "Rounded box" };
            mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetTriangles(triangles, 0);
            // Fix winding per face so normals point out.
            var t = mesh.triangles; var vv = mesh.vertices; var nn = mesh.normals;
            for (int k = 0; k < t.Length; k += 3)
            {
                Vector3 fn = Vector3.Cross(vv[t[k + 1]] - vv[t[k]], vv[t[k + 2]] - vv[t[k]]);
                if (Vector3.Dot(fn, nn[t[k]] + nn[t[k + 1]] + nn[t[k + 2]]) < 0f) { int tmp = t[k + 1]; t[k + 1] = t[k + 2]; t[k + 2] = tmp; }
            }
            mesh.triangles = t;
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Vector3 CatmullRom(Vector3[] p, float t)
        {
            int n = p.Length - 1;
            float f = t * n;
            int i = Mathf.Clamp(Mathf.FloorToInt(f), 0, n - 1);
            float u = f - i;
            Vector3 p0 = p[Mathf.Max(i - 1, 0)], p1 = p[i], p2 = p[i + 1], p3 = p[Mathf.Min(i + 2, n)];
            return 0.5f * (2f * p1 + (-p0 + p2) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u * u + (-p0 + 3f * p1 - 3f * p2 + p3) * u * u * u);
        }

        /// <summary>Deformed ellipsoid (organ): semi-axes, lumpy noise, flattened underside.</summary>
        public static Mesh Blob(Vector3 axes, int seed, float lumps, float flattenBelow, int rings = 40, int segments = 64)
        {
            var vertices = new Vector3[(rings + 1) * (segments + 1)];
            var uvs = new Vector2[vertices.Length];
            for (int i = 0; i <= rings; i++)
            {
                float th = i * Mathf.PI / rings;
                for (int j = 0; j <= segments; j++)
                {
                    float ph = j * Mathf.PI * 2f / segments;
                    var d = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                    float r = 1f + lumps * (Mathf.Sin(d.x * 5f + seed) * Mathf.Sin(d.z * 4f + seed * 0.7f) + 0.5f * Mathf.Sin(d.x * 11f - d.z * 9f + seed));
                    var p = Vector3.Scale(d * r, axes);
                    if (p.y < -flattenBelow * axes.y) p.y = -flattenBelow * axes.y + (p.y + flattenBelow * axes.y) * 0.15f;
                    vertices[i * (segments + 1) + j] = p;
                    uvs[i * (segments + 1) + j] = new Vector2(j / (float)segments * 3f, i / (float)rings * 1.5f);
                }
            }
            return GridMesh("Organ", vertices, uvs, rings, segments, true);
        }

        /// <summary>Lumpy, faceted small stone (gallstone) of about unit radius.</summary>
        public static Mesh Stone(int seed)
        {
            var random = new System.Random(seed * 7919 + 3);
            float R() => (float)random.NextDouble();
            var planes = new List<Vector4>();
            int facets = 4 + (int)(R() * 5);
            for (int k = 0; k < facets; k++)
            {
                Vector3 n = new Vector3(R() * 2 - 1, R() * 2 - 1, R() * 2 - 1).normalized;
                planes.Add(new Vector4(n.x, n.y, n.z, 0.72f + R() * 0.2f));
            }
            var waves = new Vector4[5];
            for (int k = 0; k < waves.Length; k++)
            {
                Vector3 n = new Vector3(R() * 2 - 1, R() * 2 - 1, R() * 2 - 1).normalized * (2f + R() * 5f);
                waves[k] = new Vector4(n.x, n.y, n.z, R() * 6f);
            }
            const int rings = 18, segments = 28;
            var vertices = new Vector3[(rings + 1) * (segments + 1)];
            var uvs = new Vector2[vertices.Length];
            for (int i = 0; i <= rings; i++)
            {
                float th = i * Mathf.PI / rings;
                for (int j = 0; j <= segments; j++)
                {
                    float ph = j * Mathf.PI * 2f / segments;
                    var d = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                    float r = 1f;
                    foreach (Vector4 w in waves) r += 0.06f * Mathf.Sin(Vector3.Dot(d, w) + w.w);
                    foreach (Vector4 p in planes)
                    {
                        float c = Vector3.Dot(d, p);
                        if (c > 0.05f) r = Mathf.Min(r, Mathf.Lerp(r, p.w / c, 0.85f));
                    }
                    vertices[i * (segments + 1) + j] = d * r;
                    uvs[i * (segments + 1) + j] = new Vector2(j / (float)segments * 2f, i / (float)rings);
                }
            }
            return GridMesh("Stone", vertices, uvs, rings, segments, true);
        }

        /// <summary>Open specimen pouch: surface of revolution with soft folds, outside and inside faces.</summary>
        public static Mesh Pouch(float topRadius, float top, float bottom, bool inside, int rings = 24, int segments = 72)
        {
            var vertices = new Vector3[(rings + 1) * (segments + 1)];
            var uvs = new Vector2[vertices.Length];
            for (int i = 0; i <= rings; i++)
            {
                float f = i / (float)rings; // 0 at the ring, 1 at the bottom centre
                float y = Mathf.Lerp(top, bottom, Mathf.Sin(f * Mathf.PI * 0.5f) * 0.92f + f * 0.08f);
                float profile = f < 0.82f ? 1f + 0.12f * Mathf.Sin(f / 0.82f * Mathf.PI) : Mathf.Cos((f - 0.82f) / 0.18f * Mathf.PI * 0.5f);
                for (int j = 0; j <= segments; j++)
                {
                    float a = j * Mathf.PI * 2f / segments;
                    float folds = 1f + (0.05f * Mathf.Sin(a * 9f + f * 4f) + 0.03f * Mathf.Sin(a * 17f - f * 9f)) * Mathf.Sin(Mathf.Min(f, 0.9f) * Mathf.PI);
                    float r = topRadius * profile * folds * (inside ? 0.985f : 1f);
                    vertices[i * (segments + 1) + j] = new Vector3(Mathf.Cos(a) * r, y + (inside ? 0.0008f : 0f), Mathf.Sin(a) * r);
                    uvs[i * (segments + 1) + j] = new Vector2(j / (float)segments, f);
                }
            }
            return GridMesh(inside ? "Pouch inside" : "Pouch", vertices, uvs, rings, segments, !inside);
        }

        /// <summary>Grid of (rows+1) x (cols+1) vertices; winding chosen so normals face outward (or inward).</summary>
        public static Mesh GridMesh(string name, Vector3[] vertices, Vector2[] uvs, int rows, int cols, bool flip)
        {
            var triangles = new int[rows * cols * 6];
            int k = 0;
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                {
                    int a = i * (cols + 1) + j, b = a + 1, c = a + cols + 1, d = c + 1;
                    if (flip) { triangles[k++] = a; triangles[k++] = b; triangles[k++] = c; triangles[k++] = b; triangles[k++] = d; triangles[k++] = c; }
                    else { triangles[k++] = a; triangles[k++] = c; triangles[k++] = b; triangles[k++] = b; triangles[k++] = c; triangles[k++] = d; }
                }
            var mesh = new Mesh { name = name };
            if (vertices.Length > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = vertices; mesh.uv = uvs; mesh.triangles = triangles;
            mesh.RecalculateNormals(); WeldSeamNormals(mesh, rows, cols);
            mesh.RecalculateTangents(); mesh.RecalculateBounds();
            return mesh;
        }

        // The first and last column share positions on closed surfaces: average their normals so no seam shows.
        private static void WeldSeamNormals(Mesh mesh, int rows, int cols)
        {
            Vector3[] v = mesh.vertices, n = mesh.normals;
            for (int i = 0; i <= rows; i++)
            {
                int a = i * (cols + 1), b = a + cols;
                if ((v[a] - v[b]).sqrMagnitude > 1e-10f) continue;
                Vector3 m = (n[a] + n[b]).normalized;
                n[a] = n[b] = m;
            }
            // Poles collapse to one point: give them the average normal of their ring.
            foreach (int i in new[] { 0, rows })
            {
                int a = i * (cols + 1);
                bool pole = true;
                for (int j = 1; j <= cols && pole; j++) pole = (v[a + j] - v[a]).sqrMagnitude < 1e-10f;
                if (!pole) continue;
                Vector3 m = Vector3.zero;
                for (int j = 0; j <= cols; j++) m += n[a + j];
                m.Normalize();
                for (int j = 0; j <= cols; j++) n[a + j] = m;
            }
            mesh.normals = n;
        }

        public static MeshRenderer Add(Transform parent, string name, Mesh mesh, Material material, Vector3 position, Quaternion rotation, Vector3 scale, bool castShadows = true)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position; go.transform.localRotation = rotation; go.transform.localScale = scale;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off; // never mirror the OR inside the abdomen
            return renderer;
        }
    }
}
