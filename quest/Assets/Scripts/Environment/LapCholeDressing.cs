using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Jeremy
{
    // Desktop preview only: the laparoscopic cholecystectomy scene styled after the Teleflex animation
    // (docs/LAP_CHOLE_VISUAL_SPEC.md). Board-local frame with the origin at the navel on the insufflated skin,
    // x toward the feet, y up, +z the patient's right.
    //   - passthrough-first patient/table overlay with no virtual walls or floor
    //   - a real CC0 human mesh (MakeHuman base mesh, Art/Models/Resources/Patient), supine, arms at the sides,
    //     abdomen insufflated; one teal drape conformed to the body from the neck to the feet, abdominal window
    //   - four Teleflex-style trocars at the spec sites; the anatomy (LapCholeAnatomy) inside
    //   - instrument props for the inside / clip shots, and camera presets for verification against the video
    // Reuse: Ports / PortPosition / PortAxis, Anatomy, Skin(x, z) (skin height), ApplyPreset.
    public sealed class LapCholeDressing : MonoBehaviour
    {
        public const float TableTop = -0.276f;
        /// <summary>Skin opacity: mostly solid, a little translucent.</summary>
        public const float SkinOpacity = 0.9f;
        /// <summary>Layer for exterior-only parts (trocar cannulas): the laparoscope cameras do not draw it.</summary>
        public const int OutsideOnlyLayer = 28;
        public struct Port { public string id; public Vector2 site; public float size; public Vector3 position, axis; }
        /// <summary>Spec sites (x, z) and diameters; position (on the skin) and outward axis are filled in by Build.</summary>
        public readonly List<Port> Ports = new List<Port>
        {
            new Port { id = "umbilical", site = new Vector2(0f, 0f), size = 0.012f },
            new Port { id = "epigastric", site = new Vector2(-0.15f, 0.01f), size = 0.010f },
            new Port { id = "rsc_medial", site = new Vector2(-0.11f, 0.08f), size = 0.005f },
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
            // The sheet around the window rises and falls with the belly, so the whole abdomen visibly inflates.
            if (drapeMesh != null) drapeMesh.vertices = Lowered(drapeBase, 1f - amount);
            if (edgeMesh != null) edgeMesh.vertices = Lowered(edgeBase, 1f - amount);
            if (drapeMesh != null) { drapeMesh.RecalculateNormals(); drapeMesh.RecalculateBounds(); }
            if (edgeMesh != null) edgeMesh.RecalculateBounds();
            // Re-cook the collider at the ends and in coarse steps (cooking every frame is too slow on Quest).
            if (bodyCollider != null && (amount <= 0f || amount >= 1f || Mathf.Abs(amount - colliderAmount) >= 0.25f))
            {
                colliderAmount = amount;
                bodyCollider.sharedMesh = null; bodyCollider.sharedMesh = bodyMesh;
                Physics.SyncTransforms();
            }
        }
        private float colliderAmount = 1f;
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
            BuildLighting();
            BuildTable(floorY);
            BuildBody();
            BuildDrape();
            RetuckUnderDrape();
            BuildPorts();
            Anatomy = LapCholeAnatomy.Build(transform, PortPosition("umbilical"));
            BuildInstruments();
            BuildVeress();
            ShowProps(false, false);
            ShowStage(3);
        }

        // ---------- neutral lighting for virtual content; the real room remains visible through passthrough ----------
        private void BuildLighting()
        {
            Light key = new GameObject("Lap key light").AddComponent<Light>();
            key.transform.SetParent(R, false); key.transform.localPosition = new Vector3(0f, 2.4f, 0f);
            key.transform.localRotation = Quaternion.Euler(58f, 25f, 0f);
            key.type = LightType.Directional; key.intensity = 0.95f; key.color = new Color(0.9f, 0.95f, 1f);
            key.shadows = LightShadows.Soft; key.shadowStrength = 0.62f; key.shadowBias = 0.02f; key.shadowNormalBias = 0.3f;
            Light fill = new GameObject("Lap fill light").AddComponent<Light>();
            fill.transform.SetParent(R, false); fill.transform.localPosition = new Vector3(0f, 1.8f, 0f);
            fill.transform.localRotation = Quaternion.Euler(35f, 210f, 0f);
            fill.type = LightType.Directional; fill.intensity = 0.25f; fill.color = new Color(0.8f, 0.9f, 1f); fill.shadows = LightShadows.None;
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
            // Skin: our tuned tone, Atharv's skin texture as neutral grey detail (pores and fine folds without
            // changing the colour), and a little translucency. Depth writes stay on so the body still occludes itself.
            Material skin = Own(SurgicalField.Standard(new Color(0.8f, 0.57f, 0.5f, SkinOpacity), 0.42f));
            skin.SetTexture("_BumpMap", SurgicalField.TissueTextures(SurgicalField.Skin, 256).normal);
            skin.SetFloat("_BumpScale", 0.15f); skin.EnableKeyword("_NORMALMAP");
            skin.mainTextureScale = new Vector2(12f, 12f);
            Texture2D detail = Resources.Load<Texture2D>("Patient/skin_detail");
            if (detail != null)
            {
                skin.SetTexture("_DetailAlbedoMap", detail);
                skin.SetTextureScale("_DetailAlbedoMap", new Vector2(9f, 9f));
                skin.EnableKeyword("_DETAIL_MULX2");
            }
            if (SkinOpacity < 0.999f) {
            skin.SetFloat("_Mode", 2f);
            skin.SetOverrideTag("RenderType", "Transparent");
            skin.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            skin.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            skin.SetInt("_ZWrite", 1);
            skin.DisableKeyword("_ALPHATEST_ON"); skin.EnableKeyword("_ALPHABLEND_ON"); skin.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            skin.renderQueue = (int)RenderQueue.Transparent; }
            skin.EnableKeyword("_EMISSION");
            skin.SetColor("_EmissionColor", new Color(0.07f, 0.025f, 0.015f)); // a little warmth in the shadows (soft "subsurface")
            skin.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            if (data == null) { Debug.LogWarning("Lap chole: patient mesh missing (Resources/Patient/patient_body)"); return; }
            // The drape keeps the shape it had over the original smooth patient: that body is loaded as an invisible
            // form the sheet is draped on. The visible patient is tucked just under it wherever the sheet covers it,
            // so muscles, hands and toes never push bumps into the drape; the head and the surgical window show the
            // patient's own surface.
            TextAsset formData = Resources.Load<TextAsset>("Patient/drape_form");
            if (formData != null)
            {
                LoadBody(formData.bytes, true, out Vector3[] formDomed, out _, out int[] formTris);
                var formMesh = new Mesh { name = "Drape form", indexFormat = IndexFormat.UInt32, vertices = formDomed, triangles = formTris };
                var form = new GameObject("Drape form (collider only)");
                form.transform.SetParent(R, false);
                drapeForm = form.AddComponent<MeshCollider>();
                drapeForm.sharedMesh = formMesh;
                Physics.SyncTransforms();
            }
            LoadBody(data.bytes, false, out Vector3[] v, out flatShape, out int[] t);
            int nv = v.Length;
            if (drapeForm != null) TuckUnderDrape(v, flatShape);
            domedShape = (Vector3[])v.Clone();
            var mesh = new Mesh { name = "Patient", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = v; mesh.triangles = t;
            // Planar UVs over the body (metres): the skin detail and normal maps tile evenly on the front and sides.
            var uv = new Vector2[nv];
            for (int i = 0; i < nv; i++) uv[i] = new Vector2(v[i].z + 0.35f * v[i].y, v[i].x);
            mesh.uv = uv;
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

        /// <summary>Nearest point on the window outline (rounded rectangle grown by `grow`) to (x, z).</summary>
        public static Vector2 OnWindowOutline(float x, float z, float grow = 0f)
        {
            const float wx0 = -0.185f, wx1 = 0.185f, wz = 0.15f, r = 0.07f;
            float cx = Mathf.Clamp(x, wx0 + r, wx1 - r), cz = Mathf.Clamp(z, -wz + r, wz - r);
            var d = new Vector2(x - cx, z - cz);
            if (d.sqrMagnitude < 1e-10f)
            {
                // On the inner rectangle: push out toward the nearest straight side.
                float toX = Mathf.Min(x - (wx0 + r), (wx1 - r) - x), toZ = Mathf.Min(z - (-wz + r), (wz - r) - z);
                d = toX < toZ ? new Vector2(x < 0f ? -1f : 1f, 0f) : new Vector2(0f, z < 0f ? -1f : 1f);
            }
            return new Vector2(cx, cz) + d.normalized * (r + grow);
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

        /// <summary>Diagnostic: skin (patient collider), drape-form and sheet heights at (x, z).</summary>
        public string Probe(float x, float z) =>
            "skin=" + Skin(x, z).ToString("F4") + " form=" + FormSkin(x, z).ToString("F4") + " cover=" + (cover != null ? CoverAt(x, z).ToString("F4") : "-")
            + " insufflation=" + insufflation.ToString("F2") + " collider@" + colliderAmount.ToString("F2");

        private MeshCollider drapeForm;
        private Mesh drapeMesh, edgeMesh;
        private Vector3[] drapeBase, edgeBase;

        /// <summary>Insufflation dome height (m) at (x, z): the same shape the abdomen domes with.</summary>
        private static float DomeLift(float x, float z)
        {
            float ex = (x + 0.02f) / 0.2f, ez = z / 0.17f;
            float w = Mathf.Clamp01(1f - ex * ex - ez * ez);
            return 0.03f * w * w * (3f - 2f * w);
        }

        private static Vector3[] Lowered(Vector3[] baseVertices, float deflate)
        {
            var o = new Vector3[baseVertices.Length];
            for (int i = 0; i < o.Length; i++)
            {
                // Same profile the belly deflates with, including its fade toward the flanks (the "front" weight).
                Vector3 p = baseVertices[i];
                float lift = DomeLift(p.x, p.z);
                float raw = p.y + 0.03f - lift; // height before insufflation, in the body's pre-shift frame
                float front = Mathf.SmoothStep(0f, 1f, (raw + 0.12f) / 0.1f);
                p.y -= lift * front * deflate; o[i] = p;
            }
            return o;
        }

        /// <summary>After the sheet is shaped, keep the visible patient just under it wherever it covers him.</summary>
        private void RetuckUnderDrape()
        {
            if (bodyMesh == null || cover == null) return;
            const float clearance = 0.005f;
            int moved = 0;
            for (int i = 0; i < domedShape.Length; i++)
            {
                Vector3 p = domedShape[i];
                if (p.x < DrapeX0 || p.x > DrapeX1 || Mathf.Abs(p.z) > DrapeHalf) continue;
                float windowDist = Mathf.Max(Mathf.Abs(p.x) - 0.185f, Mathf.Abs(p.z) - 0.15f);
                float covered = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.0f, 0.04f, windowDist))
                    * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(DrapeX0 - 0.01f, DrapeX0 + 0.05f, p.x));
                if (covered <= 0f) continue;
                float limit = CoverAt(p.x, p.z) - clearance;
                if (p.y <= limit) continue;
                float dy = (limit - p.y) * covered;
                domedShape[i].y += dy; flatShape[i].y += dy; moved++;
            }
            float amount = insufflation;
            var v = new Vector3[domedShape.Length];
            for (int i = 0; i < v.Length; i++) v[i] = Vector3.LerpUnclamped(flatShape[i], domedShape[i], amount);
            bodyMesh.vertices = v; bodyMesh.RecalculateNormals(); bodyMesh.RecalculateBounds();
            innerMesh.vertices = v; innerMesh.RecalculateNormals(); innerMesh.RecalculateBounds();
            bodyCollider.sharedMesh = null; bodyCollider.sharedMesh = bodyMesh; Physics.SyncTransforms();
            Debug.Log("[LapChole] re-tucked " + moved + " patient vertices under the finished drape");
        }

        /// <summary>Patient mesh bytes -> insufflated (domed) and flat vertex sets, in this dressing's frame.
        /// original = the old base mesh (drape form): its own trunk widening and painted rectus contour.</summary>
        private static void LoadBody(byte[] b, bool original, out Vector3[] domed, out Vector3[] flat, out int[] t)
        {
            int nv = System.BitConverter.ToInt32(b, 0), nt = System.BitConverter.ToInt32(b, 4);
            var v = new Vector3[nv];
            int o = 8;
            for (int i = 0; i < nv; i++, o += 12) v[i] = new Vector3(System.BitConverter.ToSingle(b, o), System.BitConverter.ToSingle(b, o + 4), System.BitConverter.ToSingle(b, o + 8));
            t = new int[nt * 3];
            for (int i = 0; i < t.Length; i++, o += 2) t[i] = System.BitConverter.ToUInt16(b, o);
            for (int i = 0; i < t.Length; i += 3) { int k = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = k; } // left-handed winding
            // A slightly broader trunk, so the lateral port site (z = 0.13) sits on the flank. On the new patient the arms
            // (|z| > 0.2) are left alone so they stay on the table.
            for (int i = 0; i < nv; i++)
                v[i].z *= 1f + 0.14f * Mathf.Exp(-(v[i].z / 0.2f) * (v[i].z / 0.2f)) * Mathf.Clamp01((v[i].x + 0.45f) / 0.1f)
                    * (original ? 1f : Mathf.Clamp01((0.22f - Mathf.Abs(v[i].z)) / 0.05f));
            flat = new Vector3[nv];
            // Insufflation: the anterior abdomen domes about 3 cm at the centre; the navel ends at y = 0.
            for (int i = 0; i < nv; i++)
            {
                Vector3 p = v[i];
                float ex = (p.x + 0.02f) / 0.2f, ez = p.z / 0.17f;
                float w = Mathf.Clamp01(1f - ex * ex - ez * ez);
                float front = Mathf.SmoothStep(0f, 1f, (p.y + 0.12f) / 0.1f);
                float dome = 0.03f * w * w * (3f - 2f * w) * front;
                p.y += dome;
                if (original)
                {
                    // The old base mesh is smooth: a gentle painted rectus contour (linea alba, three pairs of segments).
                    float rect = front * Mathf.Clamp01(1f - Mathf.Abs(p.x + 0.08f) / 0.13f);
                    float columns = Mathf.Exp(-Mathf.Pow((Mathf.Abs(p.z) - 0.042f) / 0.024f, 2f));
                    float segments = 0.6f + 0.4f * Mathf.Cos((p.x + 0.025f) / 0.05f * Mathf.PI * 2f);
                    p.y += rect * 0.003f * columns * segments;
                }
                p.y -= 0.03f;
                flat[i] = new Vector3(p.x, p.y - dome, p.z);
                v[i] = p;
            }
            domed = v;
        }

        /// <summary>Under the drape (outside the surgical window and the head), keep the visible patient just beneath
        /// the original body surface the drape is shaped over, so nothing pokes through or bumps the sheet.</summary>
        private void TuckUnderDrape(Vector3[] domed, Vector3[] flat)
        {
            const float clearance = 0.006f;
            int tucked = 0;
            for (int i = 0; i < domed.Length; i++)
            {
                Vector3 p = domed[i];
                // 0 inside the window (the patient's own surface shows), 1 a few cm outside it.
                float windowDist = Mathf.Max(Mathf.Abs(p.x) - 0.185f, Mathf.Abs(p.z) - 0.15f);
                float covered = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.0f, 0.04f, windowDist));
                // Fade out at the neckline so the head and neck keep their shape (the drape starts at DrapeX0).
                covered *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(DrapeX0 - 0.01f, DrapeX0 + 0.05f, p.x));
                if (covered <= 0f || p.x > DrapeX1 + 0.05f) continue;
                float formY = FormSkin(p.x, p.z);
                float limit = (float.IsNaN(formY) ? TableTop : formY) - clearance;
                if (p.y <= limit) continue;
                float dy = (limit - p.y) * covered;
                domed[i].y += dy; flat[i].y += dy;
                tucked++;
            }
            Debug.Log("[LapChole] tucked " + tucked + " patient vertices under the drape form");
        }

        /// <summary>Height of the original (drape form) body at (x, z), board-local; NaN off it.</summary>
        private float FormSkin(float x, float z)
        {
            if (drapeForm == null) return Skin(x, z);
            Vector3 from = board.TransformPoint(new Vector3(x, 0.6f, z));
            return drapeForm.Raycast(new Ray(from, -board.up), out RaycastHit info, 2f) ? board.InverseTransformPoint(info.point).y : float.NaN;
        }

        private void BuildDrape()
        {
            gx = Mathf.CeilToInt((DrapeX1 - DrapeX0) / Step) + 1; gz = Mathf.CeilToInt(DrapeHalf * 2 / Step) + 1;
            var body = new float[gx, gz];
            for (int i = 0; i < gx; i++)
                for (int j = 0; j < gz; j++)
                {
                    // The sheet keeps its original shape away from the window; near the window it meets the patient's
                    // own skin so the two are flush (no step at the rolled edge).
                    float sx = DrapeX0 + i * Step, sz = -DrapeHalf + j * Step;
                    float near = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.0f, 0.04f, Mathf.Max(Mathf.Abs(sx) - 0.185f, Mathf.Abs(sz) - 0.15f)));
                    float formY = FormSkin(sx, sz), ownY = near > 0f ? Skin(sx, sz) : float.NaN;
                    float y = near > 0f && !float.IsNaN(ownY) ? (float.IsNaN(formY) ? ownY : Mathf.Lerp(formY, ownY, near)) : formY;
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
            // A sheet lies over the legs, feet and arms as gentle rounded forms, not every toe and knuckle: blur the
            // form there (the patient is tucked under the final sheet afterwards, so nothing can poke through).
            var soft = (float[,])body.Clone();
            for (int pass = 0; pass < 8; pass++)
            {
                var next = new float[gx, gz];
                for (int i = 0; i < gx; i++)
                    for (int j = 0; j < gz; j++)
                    {
                        float s = 0f; int c = 0;
                        for (int di = -3; di <= 3; di++)
                            for (int dj = -3; dj <= 3; dj++) { s += soft[Mathf.Clamp(i + di, 0, gx - 1), Mathf.Clamp(j + dj, 0, gz - 1)]; c++; }
                        next[i, j] = s / c;
                    }
                soft = next;
            }
            for (int i = 0; i < gx; i++)
                for (int j = 0; j < gz; j++)
                {
                    float x = DrapeX0 + i * Step, z = -DrapeHalf + j * Step;
                    float legs = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.32f, 0.55f, x));
                    float arms = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.17f, 0.24f, Mathf.Abs(z))) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.3f, -0.15f, x));
                    float k = Mathf.Max(legs, arms) * (InWindow(x, z, 0.05f) ? 0f : 1f);
                    body[i, j] = Mathf.Lerp(body[i, j], Mathf.Max(soft[i, j], TableTop), k);
                }
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
            const int nu = 280, nv = 220; // ~7 mm cells: the window cut-out follows the rounded hem closely
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
            // The window opening: drop cells wholly inside it, and snap the inside corners of the cells that cross the
            // edge onto the rounded outline, so the cut is a smooth curve under the rolled hem (no sawtooth).
            const float cutGrow = -0.002f;
            var inside = new bool[vertices.Length];
            for (int k = 0; k < vertices.Length; k++) inside[k] = InWindow(vertices[k].x, vertices[k].z, cutGrow);
            var triangles = new List<int>(nu * nv * 6);
            var snap = new bool[vertices.Length];
            for (int i = 0; i < nu; i++)
                for (int j = 0; j < nv; j++)
                {
                    int a0 = i * (nv + 1) + j, a1 = a0 + 1, b0 = a0 + nv + 1, b1 = b0 + 1;
                    if (inside[a0] && inside[a1] && inside[b0] && inside[b1]) continue;
                    if (inside[a0]) snap[a0] = true; if (inside[a1]) snap[a1] = true; if (inside[b0]) snap[b0] = true; if (inside[b1]) snap[b1] = true;
                    triangles.Add(a0); triangles.Add(a1); triangles.Add(b0);
                    triangles.Add(b0); triangles.Add(a1); triangles.Add(b1);
                }
            for (int k = 0; k < vertices.Length; k++)
            {
                if (!snap[k]) continue;
                Vector2 q = OnWindowOutline(vertices[k].x, vertices[k].z, cutGrow);
                vertices[k] = new Vector3(q.x, CoverAt(q.x, q.y), q.y);
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
            drapeMesh = mesh; drapeBase = vertices;

            // Soft rolled edge around the window and along the neck.
            Material edge = Own(SurgicalField.Standard(new Color(0.7f, 0.8f, 0.84f), 0.4f));
            var loop = new List<Vector3>();
            foreach (Vector2 q in WindowOutline(0.005f)) loop.Add(new Vector3(q.x, CoverAt(q.x, q.y) - 0.0015f, q.y));
            loop.Add(loop[0]); loop.Add(loop[1]);
            MeshRenderer edgeRenderer = SurgicalField.Add(R, "Drape window edge", SurgicalField.Tube(loop.ToArray(), tt => 0.0034f, loop.Count * 2, 10), edge, Vector3.zero, Quaternion.identity, Vector3.one);
            edgeMesh = edgeRenderer.GetComponent<MeshFilter>().sharedMesh; edgeBase = edgeMesh.vertices;
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
