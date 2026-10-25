using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;
using XRDevice = UnityEngine.XR.InputDevice;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace GallbladderTrainer.Backend
{
    /// <summary>
    /// Records head + left/right controller (or tracked-hand) poses at 20 Hz in workstation-local space and hands them
    /// to JeremyBackendHook, which uploads ~1 s batches to /samples. Positions are relative to the workstation root so
    /// recordings survive re-centering and future anatomy changes.
    /// Desktop testing: with no XR device, the mouse ray acts as the right hand (pointing on hover, selecting while held).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MovementRecorder : MonoBehaviour
    {
        [SerializeField] internal JeremyBackendHook hook;
        [Tooltip("Pose origin. Defaults to the TrainingController (workstation root).")]
        [SerializeField] internal Transform workstationRoot;
        [SerializeField] float sampleHz = 20f;
        [SerializeField] float pointRayLength = 3f;
        [SerializeField] bool desktopMouseAsRightHand = true;

        public long SampleCount { get; private set; }

        readonly Dictionary<string, int> idx = new Dictionary<string, int>();
        readonly HashSet<string> seen = new HashSet<string>();
        readonly List<XRDevice> devices = new List<XRDevice>();
        float acc;
        XROrigin origin;
        float nextOriginLookup;
        Camera cam;

        void Awake()
        {
            if (hook == null) hook = GetComponent<JeremyBackendHook>();
            if (workstationRoot == null)
            {
                var ctrl = GetComponentInParent<TrainingController>();
                workstationRoot = ctrl != null ? ctrl.transform : transform;
            }
        }

        void Update()
        {
            if (hook == null || hook.State == BackendState.Completing || hook.State == BackendState.Completed) return;
            acc += Time.unscaledDeltaTime;
            float step = 1f / Mathf.Max(1f, sampleHz);
            if (acc < step) return;
            acc = Mathf.Min(acc - step, step); // never burst-catch-up after a hitch
            long t = hook.ElapsedMs;

            if (cam == null) cam = Camera.main;
            if (Time.unscaledTime >= nextOriginLookup && origin == null)
            {
                nextOriginLookup = Time.unscaledTime + 2f;
                origin = FindAnyObjectByType<XROrigin>();
            }

            if (cam != null) Emit("head", t, cam.transform.position, cam.transform.rotation, true, "idle");

            bool leftXr = SampleXr(XRNode.LeftHand, "left", t);
            bool rightXr = SampleXr(XRNode.RightHand, "right", t);
            if (!rightXr && !leftXr && desktopMouseAsRightHand) SampleMouse(t);
        }

        bool SampleXr(XRNode node, string name, long t)
        {
            devices.Clear();
            InputDevices.GetDevicesAtXRNode(node, devices);
            if (devices.Count == 0 || !devices[0].isValid)
            {
                // Report tracking loss for a device we had, so path metrics skip the gap instead of jumping.
                if (seen.Contains(name)) Emit(name, t, Vector3.zero, Quaternion.identity, false, "idle", true);
                return false;
            }
            XRDevice dev = devices[0];
            Vector3 p; Quaternion q; bool tracked;
            bool ok = dev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out p)
                    & dev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out q);
            if (dev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out tracked)) ok &= tracked;
            Transform space = TrackingSpace();
            Vector3 world = space != null ? space.TransformPoint(p) : p;
            Quaternion worldQ = space != null ? space.rotation * q : q;

            bool pressed, grip;
            dev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out pressed);
            dev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.gripButton, out grip);
            string interaction = pressed || grip ? "selecting" : (ok && PointsAtStructure(new Ray(world, worldQ * Vector3.forward)) ? "pointing" : "idle");
            seen.Add(name);
            Emit(name, t, world, worldQ, ok, interaction);
            return true;
        }

        void SampleMouse(long t)
        {
            if (cam == null) return;
            Vector2 pos; bool held;
            if (!ReadMouse(out pos, out held)) return;
            Ray ray = cam.ScreenPointToRay(pos);
            RaycastHit hit;
            bool onStructure = Physics.Raycast(ray, out hit, pointRayLength, ~0, QueryTriggerInteraction.Ignore)
                               && hit.collider.GetComponentInParent<StructureView>() != null;
            // The virtual "hand" sits where the pointer touches the anatomy, or 0.6 m along the ray.
            Vector3 p = onStructure ? hit.point : ray.origin + ray.direction * 0.6f;
            seen.Add("right");
            Emit("right", t, p, Quaternion.LookRotation(ray.direction, Vector3.up), true,
                 held ? "selecting" : onStructure ? "pointing" : "idle");
        }

        bool PointsAtStructure(Ray ray)
        {
            RaycastHit hit;
            return Physics.Raycast(ray, out hit, pointRayLength, ~0, QueryTriggerInteraction.Ignore)
                   && hit.collider.GetComponentInParent<StructureView>() != null;
        }

        Transform TrackingSpace()
        {
            if (origin != null && origin.CameraFloorOffsetObject != null) return origin.CameraFloorOffsetObject.transform;
            return cam != null ? cam.transform.parent : null;
        }

        void Emit(string instrument, long t, Vector3 world, Quaternion worldQ, bool valid, string interaction, bool rawZero = false)
        {
            Vector3 lp = rawZero ? Vector3.zero : workstationRoot.InverseTransformPoint(world);
            Quaternion lq = rawZero ? Quaternion.identity : Quaternion.Inverse(workstationRoot.rotation) * worldQ;
            int i;
            idx.TryGetValue(instrument, out i);
            idx[instrument] = i + 1;
            string json = new Json.Writer().BeginObject()
                .Field("instrument", instrument).Field("idx", i).Field("t_ms", t)
                .Floats("pos", lp.x, lp.y, lp.z).Floats("quat", lq.x, lq.y, lq.z, lq.w)
                .Field("valid", valid).Field("interaction", interaction)
                .EndObject().ToString();
            hook.EnqueueSample(json);
            if (valid) hook.Replay.AddPoint(instrument, t, lp);
            SampleCount++;
        }

        static bool ReadMouse(out Vector2 pos, out bool held)
        {
#if ENABLE_INPUT_SYSTEM
            Mouse m = Mouse.current;
            if (m != null) { pos = m.position.ReadValue(); held = m.leftButton.isPressed; return true; }
            pos = default(Vector2); held = false; return false;
#elif ENABLE_LEGACY_INPUT_MANAGER
            pos = Input.mousePosition; held = Input.GetMouseButton(0); return true;
#else
            pos = default(Vector2); held = false; return false;
#endif
        }
    }
}
