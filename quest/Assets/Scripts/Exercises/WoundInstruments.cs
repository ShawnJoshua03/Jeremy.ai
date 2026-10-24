using UnityEngine;

namespace Jeremy
{
    // Open-surgery instrument models for wound closure: a Mayo-Hegar style needle driver and Adson toothed
    // forceps, smooth lofted brushed-steel meshes. Model frame: jaw/tip at the origin, body along -z.
    public sealed class WoundInstrumentModel
    {
        public Transform root;
        public Transform jawA, jawB; // pivot transforms; rotate about local y to open
        public float openAngle;
        public bool driver;

        public void SetClosed(bool closed, float dt)
        {
            float target = closed ? 0f : openAngle;
            current = Mathf.MoveTowards(current, target, dt * openAngle * 10f);
            if (jawA != null) jawA.localRotation = Quaternion.Euler(0, -current, 0);
            if (jawB != null) jawB.localRotation = Quaternion.Euler(0, current, 0);
        }
        private float current;

        public static WoundInstrumentModel NeedleDriver(Transform parent)
        {
            var model = new WoundInstrumentModel { driver = true, openAngle = 7f };
            model.root = new GameObject("Needle driver (wound closure)").transform;
            model.root.SetParent(parent, false);
            Material steel = WoundArt.Steel, insert = WoundArt.DarkSteel;
            const float hinge = -0.017f;
            // Jaws: two halves meeting at x = 0, short and stout, carbide inserts on the gripping faces.
            for (int s = -1; s <= 1; s += 2)
            {
                var pivot = new GameObject(s < 0 ? "Jaw A" : "Jaw B").transform;
                pivot.SetParent(model.root, false);
                pivot.localPosition = new Vector3(0, 0, hinge);
                float[] z = { 0f, 0.004f, 0.011f, 0.0165f, 0.0175f };
                var centre = new Vector2[z.Length];
                var size = new Vector2[z.Length];
                for (int i = 0; i < z.Length; i++)
                {
                    float t = z[i] / 0.0175f; // 0 at hinge .. 1 at tip
                    float w = Mathf.Lerp(0.0021f, 0.0011f, t), h = Mathf.Lerp(0.0032f, 0.0019f, t);
                    if (i == z.Length - 1) { w *= 0.7f; h *= 0.75f; }
                    centre[i] = new Vector2(s * (w / 2 + 0.00005f), 0);
                    size[i] = new Vector2(w, h);
                }
                WoundArt.MeshObject(pivot, "Jaw", WoundArt.Loft(z, centre, size, 14, 3f, "Driver jaw"), steel);
                WoundParts.Box(pivot, "Carbide insert", new Vector3(s * 0.0002f, 0, 0.0095f), new Vector3(0.0004f, 0.0015f, 0.014f), insert);
                if (s < 0) model.jawA = pivot; else model.jawB = pivot;
            }
            // Box lock.
            WoundArt.MeshObject(model.root, "Box lock", WoundArt.Loft(
                new[] { hinge - 0.0045f, hinge - 0.0035f, hinge + 0.0025f, hinge + 0.0035f },
                new[] { Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero },
                new[] { new Vector2(0.0036f, 0.0030f), new Vector2(0.0046f, 0.0040f), new Vector2(0.0046f, 0.0040f), new Vector2(0.0034f, 0.0030f) },
                16, 4f, "Box lock"), steel);
            WoundParts.Cylinder(model.root, "Lock screw", new Vector3(0, 0.0021f, hinge - 0.0005f), new Vector3(0.0022f, 0.0002f, 0.0022f), steel);
            // Shanks diverge slightly to the finger rings, with a ratchet near the rings.
            for (int s = -1; s <= 1; s += 2)
            {
                const int n = 9;
                var z = new float[n]; var centre = new Vector2[n]; var size = new Vector2[n];
                for (int i = 0; i < n; i++)
                {
                    float t = i / (n - 1f);
                    z[n - 1 - i] = hinge - 0.004f - t * 0.105f;
                    centre[n - 1 - i] = new Vector2(s * (0.0012f + 0.0075f * t * t), 0.0003f * Mathf.Sin(t * Mathf.PI));
                    size[n - 1 - i] = new Vector2(Mathf.Lerp(0.0028f, 0.0022f, t), Mathf.Lerp(0.0034f, 0.0026f, t));
                }
                WoundArt.MeshObject(model.root, "Shank", WoundArt.Loft(z, centre, size, 12, 2.6f, "Driver shank"), steel);
                Vector3 ring = new Vector3(s * 0.0175f, 0, hinge - 0.118f);
                WoundArt.MeshObject(model.root, "Finger ring", Ring(0.0085f, 0.0014f), steel).transform.localPosition = ring;
                WoundParts.Box(model.root, "Ratchet", new Vector3(s * 0.0062f, 0, hinge - 0.103f), new Vector3(0.006f, 0.0016f, 0.004f), steel, new Vector3(0, s * 12f, 0));
            }
            return model;
        }

        public static WoundInstrumentModel Forceps(Transform parent)
        {
            var model = new WoundInstrumentModel { driver = false, openAngle = 2.6f };
            model.root = new GameObject("Toothed forceps (wound closure)").transform;
            model.root.SetParent(parent, false);
            Material steel = WoundArt.Steel;
            const float length = 0.11f;
            for (int s = -1; s <= 1; s += 2)
            {
                var pivot = new GameObject(s < 0 ? "Arm A" : "Arm B").transform;
                pivot.SetParent(model.root, false);
                pivot.localPosition = new Vector3(0, 0, -length);
                const int n = 12;
                var z = new float[n]; var centre = new Vector2[n]; var size = new Vector2[n];
                for (int i = 0; i < n; i++)
                {
                    float t = i / (n - 1f); // 0 at the spring end .. 1 at the tip
                    z[i] = t * length;
                    float bow = Mathf.Sin(Mathf.Clamp01(t * 1.15f) * Mathf.PI) * 0.0032f;
                    float thick = Mathf.Lerp(0.0016f, 0.0009f, t);
                    float wide = t < 0.55f ? Mathf.Lerp(0.0075f, 0.0085f, t / 0.55f) : Mathf.Lerp(0.0085f, 0.0014f, Mathf.SmoothStep(0, 1, (t - 0.55f) / 0.45f));
                    centre[i] = new Vector2(s * (thick / 2 + 0.00003f + bow), 0);
                    size[i] = new Vector2(thick, wide);
                }
                WoundArt.MeshObject(pivot, "Arm", WoundArt.Loft(z, centre, size, 12, 3.2f, "Forceps arm"), steel);
                // Grip serrations on the outer face of the wide part.
                for (int k = 0; k < 7; k++)
                    WoundParts.Box(pivot, "Serration", new Vector3(s * (0.0034f + 0.0009f), 0, 0.03f + k * 0.0035f), new Vector3(0.0003f, 0.0062f, 0.0009f), steel);
                // 1x2 teeth at the tip.
                if (s < 0) WoundParts.Box(pivot, "Tooth", new Vector3(0.0001f, 0, length - 0.0005f), new Vector3(0.0007f, 0.0005f, 0.0006f), steel);
                else
                    for (int k = -1; k <= 1; k += 2)
                        WoundParts.Box(pivot, "Tooth", new Vector3(-0.0001f, k * 0.0004f, length - 0.0005f), new Vector3(0.0007f, 0.0004f, 0.0006f), steel);
                if (s < 0) model.jawA = pivot; else model.jawB = pivot;
            }
            WoundParts.Box(model.root, "Spring weld", new Vector3(0, 0, -length - 0.002f), new Vector3(0.0034f, 0.0074f, 0.006f), steel);
            return model;
        }

        private static Mesh ringMesh;
        private static Mesh Ring(float radius, float wire)
        {
            if (ringMesh != null) return ringMesh;
            const int count = 33;
            var path = new Vector3[count];
            var radii = new float[count];
            for (int i = 0; i < count; i++)
            {
                float a = i / (count - 1f) * Mathf.PI * 2;
                path[i] = new Vector3(Mathf.Cos(a) * radius, 0, Mathf.Sin(a) * radius * 1.15f);
                radii[i] = wire;
            }
            ringMesh = WoundArt.Tube(path, radii, Vector3.up, 10, "Finger ring");
            return ringMesh;
        }
    }

    // Tiny primitive helpers without colliders (avoids depending on the OR environment's OrKit).
    internal static class WoundParts
    {
        public static GameObject Box(Transform parent, string name, Vector3 position, Vector3 size, Material material, Vector3 euler = default) =>
            Part(parent, name, PrimitiveType.Cube, position, size, material, Quaternion.Euler(euler));
        public static GameObject Cylinder(Transform parent, string name, Vector3 position, Vector3 size, Material material) =>
            Part(parent, name, PrimitiveType.Cylinder, position, size, material, Quaternion.identity);
        private static GameObject Part(Transform parent, string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material, Quaternion rotation)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = name;
            Object.Destroy(part.GetComponent<Collider>());
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localRotation = rotation;
            part.transform.localScale = scale;
            part.GetComponent<Renderer>().sharedMaterial = material;
            return part;
        }
    }
}
