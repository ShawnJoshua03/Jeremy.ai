using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Jeremy
{
    // Desktop preview only: the laparoscopic cholecystectomy scene styled after the Teleflex animation
    // (docs/LAP_CHOLE_VISUAL_SPEC.md). Board-local frame with the origin at the navel on the insufflated skin,
    // x toward the feet, y up, +z the patient's right.
    //   - minimal blue-grey room, light table with a head pillow (the busy OR is hidden by OperatingRoom)
    //   - a real CC0 human mesh (MakeHuman base mesh, Art/Models/Resources/Patient), supine, arms at the sides,
    //     abdomen insufflated; one teal drape conformed to the body from the neck to the feet, abdominal window
    //   - four Teleflex-style trocars at the spec sites; the anatomy (LapCholeAnatomy) inside
    //   - instrument props for the inside / clip shots, and camera presets for verification against the video
    // Reuse: Ports / PortPosition / PortAxis, Anatomy, Skin(x, z) (skin height), ApplyPreset.
    public sealed class LapCholeDressing : MonoBehaviour
    {
        public const float TableTop = -0.276f;
        /// <summary>Layer for exterior-only parts (trocar cannulas): the laparoscope cameras do not draw it.</summary>
        public const int OutsideOnlyLayer = 28;
        public struct Port { public string id; public Vector2 site; public float size; public Vector3 position, axis; }
        /// <summary>Spec sites (x, z) and diameters; position (on the skin) and outward axis are filled in by Build.</summary>
        public readonly List<Port> Ports = new List<Port>
        {
            new Port { id = "umbilical", site = new Vector2(0f, 0f), size = 0.012f },
            new Port { id = "epigastric", site = new Vector2(-0.15f, 0.01f), size = 0.010f },
            // 100 mm from the midline, outside the 40-80 mm vessel zone the port score checks (docs/PORT_PLACEMENT.md).
            new Port { id = "rsc_medial", site = new Vector2(-0.11f, 0.10f), size = 0.005f },
            new Port { id = "rsc_lateral", site = new Vector2(-0.05f, 0.13f), size = 0.005f },
        };
        public LapCholeAnatomy Anatomy { get; private set; }
        public Transform Body { get; private set; }
        private Transform board;
        private MeshCollider bodyCollider;
        private Mesh bodyMesh, innerMesh;
        private Vector3[] flatShape, domedShape;
        private float insufflation = 1f;

        /// <summary>0 = flat abdomen (before the Veress needle), 1 = insufflated (+3 cm dome). Ports and the drape are
        /// placed for the insufflated shape.</summary>
        public void SetInsufflation(float amount)
        {
            amount = Mathf.Clamp01(amount);
            if (bodyMesh == null || Mathf.Approximately(amount, insufflation)) return;
            insufflation = amount;
            var v = new Vector3[domedShape.Length];
            for (int i = 0; i < v.Length; i++) v[i] = Vector3.LerpUnclamped(flatShape[i], domedShape[i], amount);
            bodyMesh.vertices = v; bodyMesh.RecalculateNormals(); bodyMesh.RecalculateBounds();
            innerMesh.vertices = v; innerMesh.RecalculateNormals(); innerMesh.RecalculateBounds();
        }
        private GameObject graspers, clipApplier, veress;
        private readonly Dictionary<string, GameObject> trocars = new Dictionary<string, GameObject>();
        private readonly List<Material> owned = new List<Material>();

        public static LapCholeDressing Create(Transform parent, Transform boardTransform, float floorY)
        {
            var go = new GameObject("Lap chole dressing");
            go.transform.SetParent(parent, false);
            var d = go.AddComponent<LapCholeDressing>();
            d.board = boardTransform;
            d.Build(floorY);
            return d;
        }

        private Material Own(Material m) { owned.Add(m); return m; }
        private Transform R => transform;

        public Vector3 PortPosition(string id) { foreach (Port p in Ports) if (p.id == id) return p.position; return Vector3.zero; }
        public Vector3 PortAxis(string id) { foreach (Port p in Ports) if (p.id == id) return p.axis; return Vector3.up; }
        /// <summary>Procedure hooks (LapCholeProcedure): a trocar's root object (origin on the skin, local +y = outward axis), the Veress needle.</summary>
        public GameObject TrocarObject(string id) => trocars.TryGetValue(id, out GameObject g) ? g : null;
        public GameObject VeressObject => veress;

        /// <summary>Skin (or drape-free body) height at (x, z), board-local; NaN off the body.</summary>
        public float Skin(float x, float z)
        {
            if (bodyCollider == null) return float.NaN;
            Vector3 from = board.TransformPoint(new Vector3(x, 0.6f, z));
            bool on = bodyCollider.enabled;
            bodyCollider.enabled = true;
            bool hit = bodyCollider.Raycast(new Ray(from, -board.up), out RaycastHit info, 2f);
            bodyCollider.enabled = on;
            return hit ? board.InverseTransformPoint(info.point).y : float.NaN;
        }

        private void Build(float floorY)
        {
            BuildRoom(floorY);
            BuildTable(floorY);
            BuildBody();
            BuildDrape();
            BuildPorts();
            Anatomy = LapCholeAnatomy.Build(transform, PortPosition("umbilical"));
            BuildInstruments();
            BuildVeress();
            ShowProps(false, false);
            ShowStage(3);
        }

        // ---------- minimal room: blue-grey walls with a darker band, dark tiles, soft cool light ----------
        private void BuildRoom(float floorY)
        {
            // Dark slate floor (#2c3e48) with only a faint tile relief; slate walls (#3d5a66), darker band.
            Material floor = OrKit.Pbr("Tiles107", new Color(0.19f, 0.27f, 0.32f), new Vector2(9f, 9f), 0.25f, 0.55f) ?? OrKit.Mat(new Color(0.17f, 0.24f, 0.28f), 0.45f);
            floor.mainTexture = null; floor.SetTexture("_OcclusionMap", null);
            Material wall = Own(SurgicalField.Standard(new Color(0.27f, 0.39f, 0.45f), 0.22f));
            Material band = Own(SurgicalField.Standard(new Color(0.13f, 0.18f, 0.21f), 0.35f));
            Material seam = Own(SurgicalField.Standard(new Color(0.2f, 0.29f, 0.34f), 0.2f));
            float x0 = -2.2f, x1 = 2.6f, z0 = -1.8f, z1 = 1.6f, h = 2.8f;
            Quad("Floor", new Vector3((x0 + x1) / 2, floorY, (z0 + z1) / 2), new Vector2(x1 - x0, z1 - z0), Quaternion.Euler(90f, 0f, 0f), floor);
            Quad("Ceiling", new Vector3((x0 + x1) / 2, floorY + h, (z0 + z1) / 2), new Vector2(x1 - x0, z1 - z0), Quaternion.Euler(-90f, 0f, 0f), wall);
            // Walls face the room (quads face -z by default; rotate each to look inward).
            Wall(new Vector3((x0 + x1) / 2, floorY, z1), x1 - x0, h, Quaternion.identity, wall, band, seam);
            Wall(new Vector3((x0 + x1) / 2, floorY, z0), x1 - x0, h, Quaternion.Euler(0f, 180f, 0f), wall, band, seam);
            Wall(new Vector3(x0, floorY, (z0 + z1) / 2), z1 - z0, h, Quaternion.Euler(0f, -90f, 0f), wall, band, seam);
            Wall(new Vector3(x1, floorY, (z0 + z1) / 2), z1 - z0, h, Quaternion.Euler(0f, 90f, 0f), wall, band, seam);
            Light key = new GameObject("Lap key light").AddComponent<Light>();
            key.transform.SetParent(R, false); key.transform.localRotation = Quaternion.Euler(58f, 25f, 0f);
            key.type = LightType.Directional; key.intensity = 0.95f; key.color = new Color(0.9f, 0.95f, 1f);
            key.shadows = LightShadows.Soft; key.shadowStrength = 0.62f; key.shadowBias = 0.02f; key.shadowNormalBias = 0.3f;
            Light fill = new GameObject("Lap fill light").AddComponent<Light>();
            fill.transform.SetParent(R, false); fill.transform.localRotation = Quaternion.Euler(35f, 210f, 0f);
            fill.type = LightType.Directional; fill.intensity = 0.25f; fill.color = new Color(0.8f, 0.9f, 1f); fill.shadows = LightShadows.None;
        }

        private void Wall(Vector3 baseCentre, float width, float height, Quaternion facing, Material wall, Material band, Material seam)
        {
            // facing rotates the default wall (at +z, facing -z toward the room centre).
            Quad("Wall", baseCentre + Vector3.up * height / 2, new Vector2(width, height), facing, wall);
            Vector3 inward = facing * Vector3.back;
            Quad("Wall band", baseCentre + Vector3.up * 0.55f + inward * 0.004f, new Vector2(width, 0.06f), facing, band);
            Quad("Baseboard", baseCentre + Vector3.up * 0.07f + inward * 0.004f, new Vector2(width, 0.14f), facing, band);
            Vector3 along = facing * Vector3.right;
            for (float s = -width / 2 + 1.2f; s < width / 2; s += 1.2f)
                Quad("Panel seam", baseCentre + along * s + Vector3.up * (0.58f + (height - 0.58f) / 2) + inward * 0.003f, new Vector2(0.012f, height - 0.58f), facing, seam);
        }

        private void Quad(string name, Vector3 position, Vector2 size, Quaternion rotation, Material material)
        {
            GameObject q = OrKit.Part(R, name, PrimitiveType.Quad, position, new Vector3(size.x, size.y, 1f), material, rotation, false);
            q.GetComponent<Renderer>().reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        // ---------- light blue-grey table with a head pillow ----------
        private void BuildTable(float floorY)
        {
            Material pad = Own(SurgicalField.Standard(new Color(0.48f, 0.6f, 0.66f), 0.35f));
            Material frame = Own(SurgicalField.Standard(new Color(0.5f, 0.58f, 0.63f), 0.5f, 0.3f));
            Material column = Own(SurgicalField.Standard(new Color(0.42f, 0.5f, 0.55f), 0.4f, 0.2f));
            Rounded("Table mattress", new Vector3(2.02f, 0.08f, 0.62f), 0.03f, new Vector3(0.17f, TableTop - 0.04f, 0f), pad);
            Rounded("Table frame", new Vector3(2.0f, 0.05f, 0.58f), 0.015f, new Vector3(0.17f, TableTop - 0.105f, 0f), frame);
            float top = TableTop - 0.13f, bottom = floorY + 0.1f;
            Rounded("Table column", new Vector3(0.34f, top - bottom, 0.26f), 0.04f, new Vector3(0.15f, (top + bottom) / 2, 0f), column);
            Rounded("Table base", new Vector3(0.95f, 0.1f, 0.5f), 0.04f, new Vector3(0.15f, floorY + 0.05f, 0f), column);
        }

        private GameObject Rounded(string name, Vector3 size, float radius, Vector3 position, Material material, Quaternion? rotation = null) =>
            SurgicalField.Add(R, name, SurgicalField.RoundedBox(size, radius), material, position, rotation ?? Quaternion.identity, Vector3.one).gameObject;

        // ---------- patient ----------
        private void BuildBody()
        {
            TextAsset data = Resources.Load<TextAsset>("Patient/patient_body");
            Material skin = Own(SurgicalField.Standard(new Color(0.8f, 0.57f, 0.5f), 0.42f));
            skin.SetTexture("_BumpMap", SurgicalField.TissueTextures(SurgicalField.Skin, 256).normal);
            skin.SetFloat("_BumpScale", 0.15f); skin.EnableKeyword("_NORMALMAP");
            skin.mainTextureScale = new Vector2(12f, 12f);
            skin.EnableKeyword("_EMISSION");
            skin.SetColor("_EmissionColor", new Color(0.07f, 0.025f, 0.015f)); // a little warmth in the shadows (soft "subsurface")
            skin.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            if (data == null) { Debug.LogWarning("Lap chole: patient mesh missing (Resources/Patient/patient_body)"); return; }
            byte[] b = data.bytes;
            int nv = System.BitConverter.ToInt32(b, 0), nt = System.BitConverter.ToInt32(b, 4);
            var v = new Vector3[nv];
            int o = 8;
            for (int i = 0; i < nv; i++, o += 12) v[i] = new Vector3(System.BitConverter.ToSingle(b, o), System.BitConverter.ToSingle(b, o + 4), System.BitConverter.ToSingle(b, o + 8));
            var t = new int[nt * 3];
            for (int i = 0; i < t.Length; i++, o += 2) t[i] = System.BitConverter.ToUInt16(b, o);
            for (int i = 0; i < t.Length; i += 3) { int k = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = k; } // left-handed winding
            // Broader torso than the neutral base mesh, so the lateral port site (z = 0.13) sits on the flank.
            for (int i = 0; i < nv; i++) v[i].z *= 1f + 0.14f * Mathf.Exp(-(v[i].z / 0.2f) * (v[i].z / 0.2f)) * Mathf.Clamp01((v[i].x + 0.45f) / 0.1f);
            flatShape = new Vector3[nv];
            // Insufflation: the anterior abdomen domes about 3 cm at the centre; the navel ends at y = 0.
            for (int i = 0; i < nv; i++)
            {
                Vector3 p = v[i];
                Vector3 flat = p;
                float ex = (p.x + 0.02f) / 0.2f, ez = p.z / 0.17f;
                float w = Mathf.Clamp01(1f - ex * ex - ez * ez);
                float front = Mathf.SmoothStep(0f, 1f, (p.y + 0.12f) / 0.1f);
                p.y += 0.03f * w * w * (3f - 2f * w) * front;
                // Gentle rectus contour: linea alba groove and three pairs of segments above the navel.
                float rect = front * Mathf.Clamp01(1f - Mathf.Abs(p.x + 0.08f) / 0.13f);
                float columns = Mathf.Exp(-Mathf.Pow((Mathf.Abs(p.z) - 0.042f) / 0.024f, 2f));
                float segments = 0.6f + 0.4f * Mathf.Cos((p.x + 0.025f) / 0.05f * Mathf.PI * 2f);
                p.y += rect * 0.003f * columns * segments;
                p.y -= 0.03f;
                flat.y += p.y - 0.03f * w * w * (3f - 2f * w) * front - flat.y; // same contour, no dome
                flatShape[i] = flat;
                v[i] = p;
            }
            domedShape = (Vector3[])v.Clone();
            var mesh = new Mesh { name = "Patient", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = v; mesh.triangles = t;
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var renderer = SurgicalField.Add(R, "Patient", mesh, skin, Vector3.zero, Quaternion.identity, Vector3.one);
            Body = renderer.transform;
            bodyMesh = mesh;
            // Inside of the abdominal wall, seen only by the scope (anatomy layer): blocks the outside world.
            var inner = new Mesh { name = "Patient inside", indexFormat = IndexFormat.UInt32 };
            var flipped = (int[])t.Clone();
            for (int i = 0; i < flipped.Length; i += 3) { int k = flipped[i + 1]; flipped[i + 1] = flipped[i + 2]; flipped[i + 2] = k; }
            inner.vertices = v; inner.triangles = flipped; inner.RecalculateNormals(); inner.RecalculateBounds();
            innerMesh = inner;
            Material wallInside = Own(SurgicalField.Standard(new Color(0.76f, 0.74f, 0.82f), 0.2f));
            var innerRenderer = SurgicalField.Add(R, "Abdominal wall (inside)", inner, wallInside, Vector3.zero, Quaternion.identity, Vector3.one, false);
            innerRenderer.gameObject.layer = LapCholeAnatomy.Layer;
            bodyCollider = Body.gameObject.AddComponent<MeshCollider>();
            bodyCollider.sharedMesh = mesh;
            Physics.SyncTransforms();
            // Pillow under the head.
            float headBack = float.MaxValue;
            foreach (Vector3 p in v) if (p.x < -0.5f) headBack = Mathf.Min(headBack, p.y);
            Material pillow = Own(SurgicalField.Standard(new Color(0.5f, 0.63f, 0.69f), 0.3f));
            float pillowTop = Mathf.Min(headBack + 0.006f, TableTop + 0.11f);
            Rounded("Head pillow", new Vector3(0.34f, pillowTop - TableTop + 0.01f, 0.46f), 0.035f, new Vector3(-0.56f, (pillowTop + TableTop - 0.01f) / 2, 0f), pillow);
        }

        // ---------- drape: conformed to the body from the neck to past the feet, abdominal window ----------
        private const float DrapeX0 = -0.395f, DrapeX1 = 1.06f, DrapeHalf = 0.32f, Step = 0.008f;
        private int gx, gz;
        private float[,] cover;
        private float CoverAt(float x, float z)
        {
            float fx = Mathf.Clamp((x - DrapeX0) / Step, 0, gx - 1.001f), fz = Mathf.Clamp((z + DrapeHalf) / Step, 0, gz - 1.001f);
            int i = (int)fx, j = (int)fz; float u = fx - i, w = fz - j;
            return Mathf.Lerp(Mathf.Lerp(cover[i, j], cover[i + 1, j], u), Mathf.Lerp(cover[i, j + 1], cover[i + 1, j + 1], u), w);
        }
        // Window: anterior abdomen from below the xiphoid to the pubis, flank to flank (rounded rectangle).
        public static bool InWindow(float x, float z, float grow = 0f)
        {
            const float wx0 = -0.185f, wx1 = 0.185f, wz = 0.15f, r = 0.07f;
            float cx = Mathf.Clamp(x, wx0 + r, wx1 - r), cz = Mathf.Clamp(z, -wz + r, wz - r);
            return new Vector2(x - cx, z - cz).magnitude <= r + grow;
        }

        /// <summary>Points along the window outline (rounded rectangle), evenly spaced by arc length.</summary>
        public static List<Vector2> WindowOutline(float spacing)
        {
            const float wx0 = -0.185f, wx1 = 0.185f, wz = 0.15f, r = 0.07f;
            var corners = new[] { new Vector2(wx1 - r, wz - r), new Vector2(wx0 + r, wz - r), new Vector2(wx0 + r, -wz + r), new Vector2(wx1 - r, -wz + r) };
            var points = new List<Vector2>();
            for (int c = 0; c < 4; c++)
            {
                Vector2 a = corners[c], b = corners[(c + 1) % 4];
                float a0 = c * Mathf.PI * 0.5f;
                int arc = Mathf.CeilToInt(r * Mathf.PI * 0.5f / spacing);
                for (int k = 0; k < arc; k++) { float t = a0 + k / (float)arc * Mathf.PI * 0.5f; points.Add(a + new Vector2(Mathf.Cos(t), Mathf.Sin(t)) * r); }
                Vector2 from = a + new Vector2(Mathf.Cos(a0 + Mathf.PI * 0.5f), Mathf.Sin(a0 + Mathf.PI * 0.5f)) * r;
                Vector2 to = b + new Vector2(Mathf.Cos(a0 + Mathf.PI * 0.5f), Mathf.Sin(a0 + Mathf.PI * 0.5f)) * r;
                int line = Mathf.CeilToInt(Vector2.Distance(from, to) / spacing);
                for (int k = 0; k < line; k++) points.Add(Vector2.Lerp(from, to, k / (float)line));
            }
            return points;
        }

        private void BuildDrape()
        {
            gx = Mathf.CeilToInt((DrapeX1 - DrapeX0) / Step) + 1; gz = Mathf.CeilToInt(DrapeHalf * 2 / Step) + 1;
            var body = new float[gx, gz];
            for (int i = 0; i < gx; i++)
                for (int j = 0; j < gz; j++)
                {
                    float y = Skin(DrapeX0 + i * Step, -DrapeHalf + j * Step);
                    body[i, j] = float.IsNaN(y) ? TableTop : Mathf.Max(y, TableTop);
                }
            // Fill small holes in the raycast (open mesh seams) with the local maximum.
            var dilated = new float[gx, gz];
            for (int i = 0; i < gx; i++)
                for (int j = 0; j < gz; j++)
                {
                    float m = body[i, j];
                    for (int di = -2; di <= 2; di++)
                        for (int dj = -2; dj <= 2; dj++)
                            m = Mathf.Max(m, body[Mathf.Clamp(i + di, 0, gx - 1), Mathf.Clamp(j + dj, 0, gz - 1)]);
                    dilated[i, j] = m;
                }
            for (int i = 0; i < gx; i++)
                for (int j = 0; j < gz; j++)
                    if (!InWindow(DrapeX0 + i * Step, -DrapeHalf + j * Step, 0.02f)) body[i, j] = dilated[i, j];
            // The sheet bridges hollows (between arms and torso, between the legs) and never dips into the body.
            cover = new float[gx, gz];
            for (int i = 0; i < gx; i++) for (int j = 0; j < gz; j++) cover[i, j] = body[i, j] + 0.012f;
            for (int pass = 0; pass < 10; pass++)
            {
                var next = new float[gx, gz];
                for (int i = 0; i < gx; i++)
                    for (int j = 0; j < gz; j++)
                    {
                        float s = 0f; int c = 0;
                        for (int di = -2; di <= 2; di++)
                            for (int dj = -2; dj <= 2; dj++)
                            {
                                int a = Mathf.Clamp(i + di, 0, gx - 1), e = Mathf.Clamp(j + dj, 0, gz - 1);
                                s += cover[a, e]; c++;
                            }
                        float x = DrapeX0 + i * Step, z = -DrapeHalf + j * Step;
                        float min = body[i, j] + (InWindow(x, z, 0.03f) ? 0.003f : 0.008f);
                        next[i, j] = Mathf.Max(s / c, min);
                    }
                cover = next;
            }
            // Tenting: high points (feet, knees, chest) lift the sheet around them in a soft cone instead of a spike.
            var tent = new float[gx, gz];
            const int reach = 9;
            for (int i = 0; i < gx; i++)
                for (int j = 0; j < gz; j++)
                {
                    float m = cover[i, j];
                    float x = DrapeX0 + i * Step;
                    if (x > -0.2f && x < 0.22f) { tent[i, j] = m; continue; } // keep the window region conformed
                    for (int di = -reach; di <= reach; di++)
                        for (int dj = -reach; dj <= reach; dj++)
                        {
                            int a2 = i + di, e2 = j + dj;
                            if (a2 < 0 || e2 < 0 || a2 >= gx || e2 >= gz) continue;
                            float d = Mathf.Sqrt(di * di + dj * dj) * Step;
                            m = Mathf.Max(m, cover[a2, e2] - d * 0.9f - d * d * 6f);
                        }
                    tent[i, j] = m;
                }
            for (int pass = 0; pass < 4; pass++)
            {
                var next = new float[gx, gz];
                for (int i = 0; i < gx; i++)
                    for (int j = 0; j < gz; j++)
                    {
                        float sum = 0f; int c = 0;
                        for (int di = -1; di <= 1; di++)
                            for (int dj = -1; dj <= 1; dj++) { sum += tent[Mathf.Clamp(i + di, 0, gx - 1), Mathf.Clamp(j + dj, 0, gz - 1)]; c++; }
                        next[i, j] = Mathf.Max(sum / c, cover[i, j]);
                    }
                tent = next;
            }
            cover = tent;
            // Soft folds away from the window.
            for (int i = 0; i < gx; i++)
                for (int j = 0; j < gz; j++)
                {
                    float x = DrapeX0 + i * Step, z = -DrapeHalf + j * Step;
                    float away = Mathf.Clamp01((Mathf.Abs(x) - 0.25f) * 6f);
                    cover[i, j] += away * 0.006f * (SurgicalField.Fbm(x * 0.6f + 0.5f, z * 0.6f + 0.5f, 3, 3, 81) + 0.2f);
                }

            const float hang = 0.5f;
            const int nu = 220, nv = 110;
            float u0 = DrapeX0, u1 = DrapeX1 + hang, v0 = -(DrapeHalf + hang);
            var vertices = new Vector3[(nu + 1) * (nv + 1)];
            var uvs = new Vector2[vertices.Length];
            for (int i = 0; i <= nu; i++)
                for (int j = 0; j <= nv; j++)
                {
                    float u = Mathf.Lerp(u0, u1, i / (float)nu), v = Mathf.Lerp(v0, -v0, j / (float)nv);
                    float xc = Mathf.Min(u, DrapeX1), zc = Mathf.Clamp(v, -DrapeHalf, DrapeHalf);
                    float ox = Mathf.Max(0f, u - DrapeX1), oz = Mathf.Max(0f, Mathf.Abs(v) - DrapeHalf), s = Mathf.Sqrt(ox * ox + oz * oz);
                    var p = new Vector3(xc, CoverAt(xc, zc) - s * s / (s + 0.025f), zc);
                    float flare = 0.03f * (1f - Mathf.Exp(-s / 0.03f)), fold = 0.012f * Mathf.Sin(u * 21f + v * 4f) * Mathf.Clamp01(s * 5f);
                    if (s > 0f) { p.x += flare * ox / s; p.z += Mathf.Sign(v) * flare * oz / s; }
                    if (oz > 0f) p.z += Mathf.Sign(v) * fold; else if (ox > 0f) p.x += fold;
                    vertices[i * (nv + 1) + j] = p;
                    uvs[i * (nv + 1) + j] = new Vector2(u * 3f, v * 3f);
                }
            var triangles = new List<int>(nu * nv * 6);
            for (int i = 0; i < nu; i++)
                for (int j = 0; j < nv; j++)
                {
                    float uc = Mathf.Lerp(u0, u1, (i + 0.5f) / nu), vc = Mathf.Lerp(v0, -v0, (j + 0.5f) / nv);
                    if (InWindow(uc, vc)) continue;
                    int a0 = i * (nv + 1) + j, a1 = a0 + 1, b0 = a0 + nv + 1, b1 = b0 + 1;
                    triangles.Add(a0); triangles.Add(a1); triangles.Add(b0);
                    triangles.Add(b0); triangles.Add(a1); triangles.Add(b1);
                }
            var mesh = new Mesh { name = "Lap drape", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = vertices; mesh.uv = uvs; mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            // Smooth light teal sheet (#5fa3b5 .. #3f8296 in shade) with only a faint weave in the normal map.
            // Muted dusty teal (#4f8c9c), darker in the folds.
            Material drape = OrKit.Pbr("Fabric030", new Color(0.31f, 0.55f, 0.61f), new Vector2(1f, 1f), 0.08f, 0.2f)
                ?? OrKit.Mat(new Color(0.31f, 0.55f, 0.61f), 0.3f);
            drape.mainTexture = null; drape.DisableKeyword("_METALLICGLOSSMAP"); drape.SetTexture("_MetallicGlossMap", null);
            drape.SetTexture("_OcclusionMap", null);
            drape.SetFloat("_Glossiness", 0.45f);
            owned.Add(drape);
            SurgicalField.Add(R, "Lap drape", mesh, drape, Vector3.zero, Quaternion.identity, Vector3.one);

            // Soft rolled edge around the window and along the neck.
            Material edge = Own(SurgicalField.Standard(new Color(0.7f, 0.8f, 0.84f), 0.4f));
            var loop = new List<Vector3>();
            foreach (Vector2 q in WindowOutline(0.005f)) loop.Add(new Vector3(q.x, CoverAt(q.x, q.y) - 0.0015f, q.y));
            loop.Add(loop[0]); loop.Add(loop[1]);
            SurgicalField.Add(R, "Drape window edge", SurgicalField.Tube(loop.ToArray(), tt => 0.0022f, loop.Count * 2, 10), edge, Vector3.zero, Quaternion.identity, Vector3.one);
            var neck = new List<Vector3>();
            for (int k = 0; k <= 40; k++) { float z = Mathf.Lerp(-DrapeHalf - 0.05f, DrapeHalf + 0.05f, k / 40f); neck.Add(new Vector3(DrapeX0 + 0.004f, CoverAt(DrapeX0, Mathf.Clamp(z, -DrapeHalf, DrapeHalf)) - 0.002f - Mathf.Max(0f, Mathf.Abs(z) - DrapeHalf) * 1.2f, z)); }
            SurgicalField.Add(R, "Drape neck hem", SurgicalField.Tube(neck.ToArray(), tt => 0.004f, 80, 12), drape, Vector3.zero, Quaternion.identity, Vector3.one);
        }

        // ---------- Teleflex-style trocars ----------
        private void BuildPorts()
        {
            Material blue = Own(SurgicalField.Standard(new Color(0.07f, 0.22f, 0.72f), 0.72f));
            Material white = Own(SurgicalField.Standard(new Color(0.9f, 0.91f, 0.9f), 0.5f));
            Material steel = Own(SurgicalField.Standard(new Color(0.82f, 0.84f, 0.87f), 0.85f, 0.95f));
            Material clear = Own(SurgicalField.ClearPlastic(new Color(0.85f, 0.9f, 0.95f, 0.35f), 0.92f));
            Material puncture = Own(SurgicalField.Standard(new Color(0.42f, 0.14f, 0.12f), 0.5f));
            Vector3 gb = LapCholeAnatomy.CalotCenter + new Vector3(0.03f, 0.02f, 0.01f);
            for (int k = 0; k < Ports.Count; k++)
            {
                Port p = Ports[k];
                float y = Skin(p.site.x, p.site.y);
                p.position = new Vector3(p.site.x, float.IsNaN(y) ? 0f : y, p.site.y);
                Vector3 toward = (gb - p.position).normalized;
                float tilt = p.id == "umbilical" ? 0f : p.id == "epigastric" ? 20f : 32f;
                p.axis = Vector3.Slerp(Vector3.up, -toward, tilt / Mathf.Max(1f, Vector3.Angle(Vector3.up, -toward))).normalized;
                if (p.id == "umbilical") p.axis = Vector3.up;
                Ports[k] = p;
                Trocar(p, blue, white, steel, clear, puncture);
                Debug.Log($"[LapChole] port {p.id} site=({p.site.x:F3},{p.site.y:F3}) placed=({p.position.x:F3},{p.position.y:F3},{p.position.z:F3}) tilt={Vector3.Angle(Vector3.up, p.axis):F1}deg");
            }
        }

        private void Trocar(Port p, Material blue, Material white, Material steel, Material clear, Material puncture)
        {
            Transform t = new GameObject("Trocar " + p.id).transform;
            t.SetParent(R, false);
            trocars[p.id] = t.gameObject;
            t.localPosition = p.position; t.localRotation = Quaternion.FromToRotation(Vector3.up, p.axis);
            float s = p.size >= 0.01f ? 1f : 0.72f;
            bool camera = p.id == "umbilical";
            // Cannula into the abdomen (seen only through the skin / scope).
            GameObject cannula = SurgicalField.Add(t, "Cannula", SurgicalField.Lathe(new[] { new Vector2(0f, -0.022f), new Vector2(p.size * 0.5f, -0.02f), new Vector2(p.size * 0.55f, 0.0f), new Vector2(p.size * 0.55f, 0.02f) }, 32),
                camera ? steel : clear, Vector3.zero, Quaternion.identity, Vector3.one, false).gameObject;
            cannula.layer = OutsideOnlyLayer; // the video's scope view never shows the cannula tips
            float h = 0f;
            if (camera)
            {
                // White threaded collar on the skin, metal valve body with a side stopcock, blue cap.
                SurgicalField.Add(t, "Threaded collar", SurgicalField.Lathe(new[] { new Vector2(0.006f, -0.004f), new Vector2(0.0135f, -0.002f), new Vector2(0.0145f, 0.012f), new Vector2(0.012f, 0.016f), new Vector2(0.006f, 0.016f) }, 48, 24, 0.08f), white, Vector3.zero, Quaternion.identity, Vector3.one);
                SurgicalField.Add(t, "Valve body", SurgicalField.Lathe(new[] { new Vector2(0.0085f, 0.016f), new Vector2(0.0095f, 0.02f), new Vector2(0.0095f, 0.036f), new Vector2(0.0075f, 0.04f) }, 40, 16, 0.06f), steel, Vector3.zero, Quaternion.identity, Vector3.one);
                h = 0.04f;
            }
            else
            {
                SurgicalField.Add(t, "Valve housing", SurgicalField.Lathe(new[] { new Vector2(0.004f * s, 0.0f), new Vector2(0.0075f * s, 0.004f), new Vector2(0.0085f * s, 0.014f), new Vector2(0.0075f * s, 0.018f) }, 40), white, Vector3.zero, Quaternion.identity, Vector3.one);
                h = 0.018f * s;
            }
            // Stopcock: luer body and lever on the side.
            Vector3 side = Vector3.right;
            SurgicalField.Add(t, "Stopcock", SurgicalField.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.0028f * s, 0.001f), new Vector2(0.0028f * s, 0.016f * s), new Vector2(0.0018f * s, 0.022f * s), new Vector2(0f, 0.0225f * s) }, 20),
                steel, new Vector3(0.006f * s, h - 0.012f * s, 0f), Quaternion.FromToRotation(Vector3.up, side), Vector3.one);
            SurgicalField.Add(t, "Stopcock lever", SurgicalField.RoundedBox(new Vector3(0.012f * s, 0.003f * s, 0.004f * s), 0.0012f * s, 2), white,
                new Vector3(0.017f * s, h - 0.006f * s, 0f), Quaternion.Euler(0f, 0f, 20f), Vector3.one);
            // Blue cap housing with grip ribs and a seal ring on top.
            float cr = camera ? 0.0135f : 0.0105f * s, ch = camera ? 0.032f : 0.022f * s;
            SurgicalField.Add(t, "Cap base", SurgicalField.Lathe(new[] { new Vector2(cr * 0.75f, h), new Vector2(cr * 1.12f, h + 0.002f), new Vector2(cr * 1.12f, h + 0.008f * s), new Vector2(cr * 0.95f, h + 0.0095f * s) }, 56),
                blue, Vector3.zero, Quaternion.identity, Vector3.one);
            SurgicalField.Add(t, "Cap", SurgicalField.Lathe(new[] { new Vector2(cr * 0.95f, h + 0.0095f * s), new Vector2(cr, h + 0.011f * s), new Vector2(cr, h + ch - 0.004f), new Vector2(cr * 0.88f, h + ch), new Vector2(cr * 0.45f, h + ch), new Vector2(cr * 0.4f, h + ch - 0.002f) }, 64, 20, 0.09f),
                blue, Vector3.zero, Quaternion.identity, Vector3.one);
            SurgicalField.Add(t, "Seal", SurgicalField.Lathe(new[] { new Vector2(cr * 0.42f, h + ch - 0.0015f), new Vector2(0f, h + ch - 0.0015f) }, 32), white, Vector3.zero, Quaternion.identity, Vector3.one);
            // Skin incision ring.
            SurgicalField.Add(t, "Incision", OrKit.Torus(1f, 0.25f, 32, 8), puncture, Vector3.up * 0.0005f, Quaternion.Inverse(t.localRotation), Vector3.one * p.size * 0.6f);
        }

        // Veress needle at the navel: steel needle, blue-grey hub, spring-loaded blunt stylet.
        private void BuildVeress()
        {
            Material steel = Own(SurgicalField.Standard(new Color(0.8f, 0.82f, 0.86f), 0.85f, 0.95f));
            Material hub = Own(SurgicalField.Standard(new Color(0.62f, 0.62f, 0.7f), 0.6f, 0.4f));
            veress = new GameObject("Veress needle");
            veress.transform.SetParent(R, false);
            veress.transform.localPosition = PortPosition("umbilical");
            veress.transform.localRotation = Quaternion.Euler(0f, 0f, -12f);
            SurgicalField.Add(veress.transform, "Needle", SurgicalField.Lathe(new[] { new Vector2(0f, -0.04f), new Vector2(0.0012f, -0.038f), new Vector2(0.0012f, 0.07f) }, 16), steel, Vector3.zero, Quaternion.identity, Vector3.one);
            SurgicalField.Add(veress.transform, "Hub", SurgicalField.Lathe(new[] { new Vector2(0.0012f, 0.07f), new Vector2(0.0045f, 0.075f), new Vector2(0.005f, 0.115f), new Vector2(0.0035f, 0.12f), new Vector2(0f, 0.121f) }, 28), hub, Vector3.zero, Quaternion.identity, Vector3.one);
        }

        // ---------- instrument props (video look): graspers inside, the Endo5 clip applier outside ----------
        private void BuildInstruments()
        {
            Material black = Own(SurgicalField.Standard(new Color(0.03f, 0.035f, 0.04f), 0.7f));
            Material jaw = Own(SurgicalField.Standard(new Color(0.62f, 0.64f, 0.66f), 0.75f, 0.85f));
            Material white = Own(SurgicalField.Standard(new Color(0.93f, 0.94f, 0.94f), 0.55f));
            Material green = Own(SurgicalField.Standard(new Color(0.08f, 0.5f, 0.2f), 0.6f));
            Material clip = Own(SurgicalField.Standard(new Color(0.95f, 0.95f, 0.93f), 0.45f));
            graspers = new GameObject("Grasper props").transform.gameObject;
            graspers.transform.SetParent(R, false);
            Transform view = Anatomy.View;
            // Assistant grasper on the fundus (lateral port), working grasper on Hartmann's pouch (medial port).
            Grasper(graspers.transform, PortPosition("rsc_lateral"), R.InverseTransformPoint(view.TransformPoint(new Vector3(0.012f, -0.012f, -0.006f))), black, jaw);
            // Clip applier through the epigastric port onto the cystic duct.
            clipApplier = new GameObject("Clip applier prop");
            clipApplier.transform.SetParent(R, false);
            Vector3 port = PortPosition("epigastric");
            Vector3 tip = port - Vector3.Lerp(PortAxis("epigastric"), Vector3.up, 0.75f).normalized * 0.12f; // exterior shot: held near vertical as in the video
            Transform ca = Shaft(clipApplier.transform, port, tip, 0.0026f, white, 0.2f);
            // Jaws holding a white Hem-o-lok clip.
            foreach (int sgn in new[] { -1, 1 })
                SurgicalField.Add(ca, "Clip jaw", SurgicalField.RoundedBox(new Vector3(0.0016f, 0.0022f, 0.011f), 0.0007f, 2), jaw, new Vector3(0.0022f * sgn, 0f, 0.005f), Quaternion.Euler(0f, -6f * sgn, 0f), Vector3.one);
            SurgicalField.Add(ca, "Hem-o-lok clip", OrKit.Torus(1f, 0.16f, 24, 6), clip, new Vector3(0f, 0f, 0.008f), Quaternion.Euler(0f, 0f, 90f), new Vector3(0.006f, 0.003f, 0.0045f));
            // Endo5-style handle outside the body: green rotation knob, curved white pistol body, green trigger.
            Transform handle = new GameObject("Handle").transform;
            handle.SetParent(ca, false); handle.localPosition = new Vector3(0f, 0f, -(Vector3.Distance(port, tip) + 0.2f));
            BuildEndo5Handle(handle, white, green);
        }

        // Handle frame: +z toward the tip (down the shaft), +x toward the finger bar. Smooth swept shapes only.
        private static void BuildEndo5Handle(Transform h, Material white, Material green)
        {
            Quaternion alongShaft = Quaternion.Euler(90f, 0f, 0f); // lathe +y onto the shaft (+z)
            SurgicalField.Add(h, "Rotation knob", SurgicalField.Lathe(new[]
            {
                new Vector2(0.0026f, 0.052f), new Vector2(0.004f, 0.046f), new Vector2(0.0075f, 0.032f), new Vector2(0.0112f, 0.012f),
                new Vector2(0.0122f, 0.004f), new Vector2(0.0118f, 0f), new Vector2(0.009f, -0.002f),
            }, 48, 14, 0.07f), green, Vector3.zero, alongShaft, Vector3.one);
            // Body: a rounded column rising from the knob, swelling slightly, capped.
            SurgicalField.Add(h, "Body", SurgicalField.Tube(new[]
            {
                new Vector3(0f, 0f, 0.002f), new Vector3(0f, 0f, -0.03f), new Vector3(0.002f, 0f, -0.06f), new Vector3(0.006f, 0f, -0.082f), new Vector3(0.012f, 0f, -0.092f),
            }, t => 0.0145f * (t > 0.86f ? Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow((t - 0.86f) / 0.14f, 2f))) : 1f) * (1f + 0.08f * Mathf.Sin(t * Mathf.PI)), 48, 32),
                white, Vector3.zero, Quaternion.identity, new Vector3(1f, 0.8f, 1f));
            // Finger bar: sweeps out sideways from the top of the body and tapers, drooping slightly toward the tip.
            SurgicalField.Add(h, "Finger bar", SurgicalField.Tube(new[]
            {
                new Vector3(0.0f, 0f, -0.08f), new Vector3(0.03f, 0f, -0.084f), new Vector3(0.065f, 0f, -0.08f), new Vector3(0.1f, 0f, -0.07f), new Vector3(0.112f, 0f, -0.066f),
            }, t => Mathf.Lerp(0.0125f, 0.0068f, t) * (t > 0.88f ? Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow((t - 0.88f) / 0.12f, 2f))) : 1f), 60, 28),
                white, Vector3.zero, Quaternion.identity, new Vector3(1f, 0.62f, 1f));
            // Trigger: hangs from under the bar near the body, curving down and out.
            SurgicalField.Add(h, "Trigger", SurgicalField.Tube(new[]
            {
                new Vector3(0.008f, 0f, -0.058f), new Vector3(0.02f, 0f, -0.046f), new Vector3(0.034f, 0f, -0.03f), new Vector3(0.05f, 0f, -0.018f), new Vector3(0.062f, 0f, -0.016f),
            }, t => Mathf.Lerp(0.0062f, 0.0045f, t) * (t > 0.85f ? Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow((t - 0.85f) / 0.15f, 2f))) : 1f), 48, 24),
                green, Vector3.zero, Quaternion.identity, new Vector3(1f, 0.7f, 1f));
        }

        private Transform Shaft(Transform parent, Vector3 port, Vector3 tip, float radius, Material material, float outside)
        {
            Vector3 dir = (tip - port).normalized;
            Transform t = new GameObject("Instrument").transform;
            t.SetParent(parent, false);
            t.localPosition = tip; t.localRotation = Quaternion.LookRotation(dir, Mathf.Abs(dir.y) > 0.8f ? Vector3.forward : Vector3.up);
            float length = Vector3.Distance(port, tip) + outside;
            // Lathe runs along +y; rotated -90 about x it runs along -z (back from the tip through the port).
            SurgicalField.Add(t, "Shaft", SurgicalField.Lathe(new[] { new Vector2(0f, -0.002f), new Vector2(radius, 0f), new Vector2(radius, length) }, 28), material,
                Vector3.zero, Quaternion.Euler(-90f, 0f, 0f), Vector3.one);
            return t;
        }

        private void Grasper(Transform parent, Vector3 port, Vector3 tip, Material black, Material jaw)
        {
            Transform g = Shaft(parent, port, tip - (tip - port).normalized * 0.016f, 0.0026f, black, 0.3f);
            // Ribbed insulation collar behind the jaws, as on the Teleflex graspers.
            SurgicalField.Add(g, "Collar", SurgicalField.Lathe(new[] { new Vector2(0.0026f, 0f), new Vector2(0.0031f, 0.002f), new Vector2(0.0031f, 0.022f), new Vector2(0.0026f, 0.024f) }, 28, 0, 0f), black,
                Vector3.zero, Quaternion.Euler(-90f, 0f, 0f), Vector3.one);
            for (int k = 0; k < 5; k++)
                SurgicalField.Add(g, "Rib", OrKit.Torus(1f, 0.12f, 28, 6), black, new Vector3(0f, 0f, -0.006f - k * 0.0035f), Quaternion.Euler(90f, 0f, 0f), Vector3.one * 0.0032f);
            foreach (int sgn in new[] { -1, 1 })
                SurgicalField.Add(g, "Jaw", SurgicalField.RoundedBox(new Vector3(0.0018f, 0.0016f, 0.015f), 0.0007f, 2), jaw, new Vector3(0.0015f * sgn, 0f, 0.0075f), Quaternion.Euler(0f, -5f * sgn, 0f), Vector3.one);
        }

        public void ShowProps(bool inside, bool clip)
        {
            if (graspers != null) graspers.SetActive(inside);
            if (clipApplier != null) clipApplier.SetActive(clip);
        }

        /// <summary>Procedure stage for the exterior: 0 nothing placed, 1 Veress needle, 2 umbilical port, 3 all ports.</summary>
        public void ShowStage(int stage)
        {
            SetInsufflation(stage >= 2 ? 1f : 0f);
            if (veress != null) veress.SetActive(stage == 1);
            foreach (var kv in trocars) kv.Value.SetActive(kv.Key == "umbilical" ? stage >= 2 : stage >= 3);
        }

        // ---------- camera presets for verification against the video (board-local) ----------
        public struct Preset { public Vector3 position, target; public float fov; public bool scope, inside, clip; public int stage; }
        public static readonly Dictionary<string, Preset> Presets = new Dictionary<string, Preset>
        {
            { "room", new Preset { position = new Vector3(0.72f, 0.2f, -0.46f), target = new Vector3(-0.3f, -0.04f, 0.1f), fov = 38f, stage = 0 } },
            { "veress", new Preset { position = new Vector3(0.12f, 0.08f, 0.62f), target = new Vector3(-0.1f, -0.01f, 0.0f), fov = 34f, stage = 1 } },
            { "umbilical", new Preset { position = new Vector3(0.09f, 0.085f, 0.3f), target = new Vector3(-0.07f, 0.03f, 0.0f), fov = 36f, stage = 2 } },
            { "ports", new Preset { position = new Vector3(0.06f, 0.2f, -0.11f), target = new Vector3(-0.22f, -0.05f, 0.05f), fov = 56f, stage = 3 } },
            { "inside", new Preset { scope = true, inside = true, stage = 3 } },
            { "clip", new Preset { position = new Vector3(-0.05f, 0.3f, -0.55f), target = new Vector3(-0.2f, 0.12f, 0.02f), fov = 40f, clip = true, stage = 3 } },
        };

        private void OnDestroy() { foreach (Material m in owned) if (m != null) Destroy(m); }
    }
}
