using System;
using System.Collections.Generic;
using UnityEngine;

namespace GallbladderTrainer
{
    /// <summary>
    /// Procedural, schematic geometry (metres, in the Anatomy root's local space; viewer looks along +Z, so
    /// front surfaces face -Z). Shapes are approximations for teaching and are NOT medically accurate.
    /// </summary>
    public static class ProceduralAnatomy
    {
        public static readonly Vector3 RightLobeCenter = new Vector3(0.08f, 0f, 0f);
        public static readonly Vector3 RightLobeRadii = new Vector3(0.30f, 0.22f, 0.10f);
        public static readonly Vector3 LeftLobeCenter = new Vector3(-0.22f, 0.05f, 0.01f);
        public static readonly Vector3 LeftLobeRadii = new Vector3(0.17f, 0.13f, 0.07f);

        public static readonly Vector3 GallbladderNeck = new Vector3(0.07f, 0.075f, -0.12f);
        const float GbLength = 0.20f, GbRadius = 0.042f, GbBend = 0.015f;

        public static readonly Vector3 PlateCenter = new Vector3(0.075f, -0.055f, 0f);
        public static readonly Vector2 PlateRadii = new Vector2(0.042f, 0.07f);

        public static readonly Vector3[] CysticDuctPath =
        {
            new Vector3(0.070f, 0.070f, -0.120f), new Vector3(0.050f, 0.100f, -0.108f),
            new Vector3(0.015f, 0.118f, -0.098f), new Vector3(-0.030f, 0.122f, -0.090f),
            new Vector3(-0.085f, 0.118f, -0.086f), new Vector3(-0.100f, 0.115f, -0.085f)
        };
        public static readonly Vector3[] CysticArteryPath =
        {
            new Vector3(-0.138f, 0.145f, -0.090f), new Vector3(-0.100f, 0.168f, -0.096f),
            new Vector3(-0.040f, 0.172f, -0.105f), new Vector3(0.020f, 0.150f, -0.115f),
            new Vector3(0.058f, 0.108f, -0.125f), new Vector3(0.074f, 0.052f, -0.133f)
        };
        public static readonly Vector3[] CommonDuctPath =
        {
            new Vector3(-0.105f, 0.220f, -0.075f), new Vector3(-0.100f, 0.150f, -0.082f),
            new Vector3(-0.100f, 0.090f, -0.086f), new Vector3(-0.100f, 0.000f, -0.088f),
            new Vector3(-0.095f, -0.080f, -0.090f)
        };
        public static readonly Vector3[] HepaticArteryPath =
        {
            new Vector3(-0.150f, 0.200f, -0.080f), new Vector3(-0.140f, 0.150f, -0.088f),
            new Vector3(-0.135f, 0.080f, -0.090f), new Vector3(-0.130f, 0.000f, -0.092f),
            new Vector3(-0.120f, -0.070f, -0.094f)
        };

        static readonly Vector3 TriA = new Vector3(-0.100f, 0.115f, -0.097f);
        static readonly Vector3 TriB = new Vector3(0.066f, 0.082f, -0.140f);
        static readonly Vector3 TriC = new Vector3(0.000f, 0.200f, -0.095f);

        /// <summary>Front surface Z of the right liver lobe at (x, y); used to seat the plate overlay on the liver.</summary>
        public static float RightLobeFrontZ(float x, float y)
        {
            float u = (x - RightLobeCenter.x) / RightLobeRadii.x;
            float v = (y - RightLobeCenter.y) / RightLobeRadii.y;
            float k = 1f - u * u - v * v;
            return RightLobeCenter.z - RightLobeRadii.z * Mathf.Sqrt(Mathf.Max(0f, k));
        }

        // ---------- public mesh factories ----------

        public static Mesh CreateLiver()
        {
            var mb = new MeshBuilder();
            mb.AddGrid(Ellipsoid(RightLobeCenter, RightLobeRadii), 48, 32);
            mb.AddGrid(Ellipsoid(LeftLobeCenter, LeftLobeRadii), 40, 28);
            return mb.ToMesh("GT_Liver");
        }

        /// <summary>Neck tip at the origin, body hanging toward -Y.</summary>
        public static Mesh CreateGallbladder()
        {
            var mb = new MeshBuilder();
            mb.AddGrid((u, v) =>
            {
                float th = u * Mathf.PI * 2f;
                float t = 1f - v;
                float s = Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Pow(Mathf.Max(t, 0f), 0.75f)));
                float r = GbRadius * Mathf.Pow(s, 0.7f);
                return new Vector3(GbBend * Mathf.Sin(Mathf.PI * v) + r * Mathf.Cos(th), -GbLength * v, 0.9f * r * Mathf.Sin(th));
            }, 32, 28);
            return mb.ToMesh("GT_Gallbladder");
        }

        public static Mesh CreateTube(Vector3[] path, float radius, int samples, int sides, string name)
        {
            var mb = new MeshBuilder();
            AddTube(mb, path, radius, samples, sides, true);
            return mb.ToMesh(name);
        }

        public static Mesh CreateTriangleOutline()
        {
            var mb = new MeshBuilder();
            AddTube(mb, new[] { TriA, TriB }, 0.0026f, 10, 6, false);
            AddTube(mb, new[] { TriB, TriC }, 0.0026f, 10, 6, false);
            AddTube(mb, new[] { TriC, TriA }, 0.0026f, 10, 6, false);
            return mb.ToMesh("GT_HepatocysticTriangleOutline");
        }

        public static Mesh CreatePlate()
        {
            var mb = new MeshBuilder();
            mb.AddGrid((u, v) =>
            {
                float th = u * Mathf.PI * 2f;
                float x = PlateCenter.x + v * PlateRadii.x * Mathf.Cos(th);
                float y = PlateCenter.y + v * PlateRadii.y * Mathf.Sin(th);
                return new Vector3(x, y, RightLobeFrontZ(x, y) - 0.003f);
            }, 28, 6);
            return mb.ToMesh("GT_CysticPlateRegion");
        }

        // ---------- helpers ----------

        static Func<float, float, Vector3> Ellipsoid(Vector3 c, Vector3 r)
        {
            return (u, v) =>
            {
                float th = u * Mathf.PI * 2f, ph = v * Mathf.PI;
                float s = Mathf.Sin(ph);
                return c + new Vector3(r.x * s * Mathf.Cos(th), r.y * Mathf.Cos(ph), r.z * s * Mathf.Sin(th));
            };
        }

        static Vector3 Sample(Vector3[] c, float s)
        {
            int n = c.Length;
            int i = Mathf.Clamp((int)s, 0, n - 2);
            float t = s - i, t2 = t * t, t3 = t2 * t;
            Vector3 p0 = c[Mathf.Max(i - 1, 0)], p1 = c[i], p2 = c[i + 1], p3 = c[Mathf.Min(i + 2, n - 1)];
            return 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        static void AddTube(MeshBuilder mb, Vector3[] ctrl, float radius, int m, int sides, bool roundEnds)
        {
            const float EndRound = 0.04f;
            var pts = new Vector3[m + 1];
            var tan = new Vector3[m + 1];
            var nor = new Vector3[m + 1];
            for (int j = 0; j <= m; j++) pts[j] = Sample(ctrl, (float)j / m * (ctrl.Length - 1));
            for (int j = 0; j <= m; j++) tan[j] = (pts[Mathf.Min(j + 1, m)] - pts[Mathf.Max(j - 1, 0)]).normalized;
            Vector3 up = Mathf.Abs(tan[0].y) < 0.9f ? Vector3.up : Vector3.right;
            nor[0] = Vector3.Cross(tan[0], up).normalized;
            for (int j = 1; j <= m; j++)
                nor[j] = (nor[j - 1] - tan[j] * Vector3.Dot(nor[j - 1], tan[j])).normalized;

            int baseIdx = mb.V.Count;
            for (int j = 0; j <= m; j++)
            {
                float v = (float)j / m;
                float d = Mathf.Min(v, 1f - v);
                float k = 1f;
                if (roundEnds && d < EndRound) k = Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow(1f - d / EndRound, 2f)));
                Vector3 bin = Vector3.Cross(tan[j], nor[j]);
                Vector3 cap = v < 0.5f ? -tan[j] : tan[j];
                for (int i = 0; i <= sides; i++)
                {
                    float th = Mathf.PI * 2f * i / sides;
                    Vector3 dir = nor[j] * Mathf.Cos(th) + bin * Mathf.Sin(th);
                    Vector3 n = (dir * k + cap * (1f - k)).normalized;
                    mb.AddVertex(pts[j] + dir * (radius * k), n);
                }
            }

            int a0 = baseIdx + (m / 2) * (sides + 1), b0 = a0 + sides + 1;
            bool flip = Vector3.Dot(Vector3.Cross(mb.V[a0 + 1] - mb.V[a0], mb.V[b0] - mb.V[a0]), mb.N[a0]) < 0f;
            for (int j = 0; j < m; j++)
            {
                for (int i = 0; i < sides; i++)
                {
                    int a = baseIdx + j * (sides + 1) + i, b = a + sides + 1;
                    if (!flip) { mb.T.Add(a); mb.T.Add(a + 1); mb.T.Add(b); mb.T.Add(a + 1); mb.T.Add(b + 1); mb.T.Add(b); }
                    else { mb.T.Add(a); mb.T.Add(b); mb.T.Add(a + 1); mb.T.Add(a + 1); mb.T.Add(b); mb.T.Add(b + 1); }
                }
            }
        }

        sealed class MeshBuilder
        {
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<Vector3> N = new List<Vector3>();
            public readonly List<int> T = new List<int>();

            public void AddVertex(Vector3 p, Vector3 n) { V.Add(p); N.Add(n); }

            /// <summary>Parametric surface f(u,v), u around [0,1], v along [0,1]. Normal = cross(dU, dV).</summary>
            public void AddGrid(Func<float, float, Vector3> f, int nu, int nv)
            {
                const float e = 0.001f;
                int baseIdx = V.Count;
                for (int j = 0; j <= nv; j++)
                {
                    float v = (float)j / nv;
                    for (int i = 0; i <= nu; i++)
                    {
                        float u = (float)i / nu;
                        Vector3 p = f(u, v);
                        Vector3 du = f(u + e, v) - f(u - e, v);
                        Vector3 dv = f(u, Mathf.Min(v + e, 1f)) - f(u, Mathf.Max(v - e, 0f));
                        Vector3 n = Vector3.Cross(du, dv);
                        n = n.sqrMagnitude < 1e-14f ? (v < 0.5f ? Vector3.up : Vector3.down) : n.normalized;
                        AddVertex(p, n);
                    }
                }
                for (int j = 0; j < nv; j++)
                {
                    for (int i = 0; i < nu; i++)
                    {
                        int a = baseIdx + j * (nu + 1) + i, b = a + nu + 1;
                        T.Add(a); T.Add(a + 1); T.Add(b);
                        T.Add(a + 1); T.Add(b + 1); T.Add(b);
                    }
                }
            }

            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name };
                m.indexFormat = V.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
                m.SetVertices(V);
                m.SetNormals(N);
                m.SetTriangles(T, 0, true);
                m.RecalculateBounds();
                return m;
            }
        }
    }
}
