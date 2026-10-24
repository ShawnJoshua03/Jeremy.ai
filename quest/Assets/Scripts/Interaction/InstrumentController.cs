using UnityEngine;
using UnityEngine.InputSystem;

namespace Jeremy
{
    // One virtual instrument per hand. Input is either hand tracking (index-thumb pinch, tip at the pinch point)
    // when `hand` is assigned, or an OpenXR controller (trigger) otherwise.
    // Controller bindings confirmed in OpenXR 1.18 OculusTouchControllerProfile: triggerPressed, isTracked, trackingState.
    [DefaultExecutionOrder(50)]
    public sealed class InstrumentController : MonoBehaviour
    {
        public string instrument = "left";
        public Transform controllerPose;
        public OVRHand hand;
        public OVRSkeleton skeleton;
        public Transform tip;
        public Transform leftJaw, rightJaw;
        public Vector3 tipOffset = new Vector3(0, 0, 0.12f);
        public InputActionReference closeAction;
        public InputActionReference trackedAction;
        public InputActionReference trackingStateAction;
        public ExerciseStateMachine exercise;
        public PegBoard board;
        public TransferDetector detector;
        public float grabRadius = 0.045f;
        [Tooltip("Peg transfer only. Off in wound closure, where SutureDetector reads Closed and the tip instead.")]
        public bool pegsEnabled = true;
        [Tooltip("Desktop preview: pose comes from controllerPose and grasp from desktopClosed. No XR input.")]
        public bool desktopInput;
        [System.NonSerialized] public bool desktopClosed;
        public bool TrackingValid { get; private set; }
        public PegObject Held { get; private set; }
        /// <summary>Jaws closed this frame (trigger, pinch or desktop button), whether or not anything is held.</summary>
        public bool Closed { get; private set; }
        public string Interaction => Held != null || (!pegsEnabled && Closed && TrackingValid) ? "grasping" : "idle";
        private InputAction close, tracked, tracking;
        private bool ownsClose, ownsTracked, ownsTracking, previousClosed;
        private PegObject candidate;

        private void OnEnable()
        {
            if (hand != null || desktopInput) return;
            string side = instrument == "right" ? "RightHand" : "LeftHand";
            close = Resolve(closeAction, "<XRController>{" + side + "}/triggerPressed", InputActionType.Button, out ownsClose);
            tracked = Resolve(trackedAction, "<XRController>{" + side + "}/isTracked", InputActionType.Button, out ownsTracked);
            tracking = Resolve(trackingStateAction, "<XRController>{" + side + "}/trackingState", InputActionType.Value, out ownsTracking);
        }
        private InputAction Resolve(InputActionReference reference, string binding, InputActionType type, out bool owned)
        {
            owned = reference == null || reference.action == null;
            InputAction action = owned ? new InputAction("Controller input", type, binding) : reference.action;
            if (owned) action.Enable(); // Shared action maps must be enabled by their owner.
            return action;
        }
        private bool ReadController(out Vector3 position, out Quaternion rotation)
        {
            TrackingValid = controllerPose != null && tip != null && tracked.ReadValue<float>() > 0.5f && (tracking.ReadValue<int>() & 3) == 3;
            position = TrackingValid ? controllerPose.TransformPoint(tipOffset) : default;
            rotation = TrackingValid ? controllerPose.rotation : default;
            return close.ReadValue<float>() > 0.5f;
        }
        private bool ReadDesktop(out Vector3 position, out Quaternion rotation)
        {
            TrackingValid = controllerPose != null && tip != null;
            position = TrackingValid ? controllerPose.position : default;
            rotation = TrackingValid ? controllerPose.rotation : default;
            return desktopClosed;
        }
        private bool ReadHand(out Vector3 position, out Quaternion rotation)
        {
            // OpenXR hand skeletons number bones differently: the legacy Hand_ThumbTip/Hand_IndexTip
            // values (19, 20) are ring-finger bones there. Pick the ID set that matches the skeleton.
            bool openXr = skeleton != null && (skeleton.GetSkeletonType() == OVRSkeleton.SkeletonType.XRHandLeft
                || skeleton.GetSkeletonType() == OVRSkeleton.SkeletonType.XRHandRight);
            Transform thumbTip = BoneTransform(openXr ? OVRSkeleton.BoneId.XRHand_ThumbTip : OVRSkeleton.BoneId.Hand_ThumbTip);
            Transform indexTip = BoneTransform(openXr ? OVRSkeleton.BoneId.XRHand_IndexTip : OVRSkeleton.BoneId.Hand_IndexTip);
            TrackingValid = tip != null && thumbTip != null && indexTip != null
                && hand.IsTracked && hand.HandConfidence == OVRHand.TrackingConfidence.High;
            position = TrackingValid ? (thumbTip.position + indexTip.position) * 0.5f : default;
            rotation = TrackingValid ? hand.transform.rotation : default;
            return TrackingValid && hand.GetFingerIsPinching(OVRHand.HandFinger.Index);
        }
        private Transform BoneTransform(OVRSkeleton.BoneId id)
        {
            if (skeleton == null || !skeleton.IsInitialized) return null;
            foreach (OVRBone bone in skeleton.Bones) if (bone.Id == id) return bone.Transform;
            return null;
        }
        private void Update()
        {
            Vector3 position;
            Quaternion rotation;
            bool closed = desktopInput ? ReadDesktop(out position, out rotation)
                : hand != null ? ReadHand(out position, out rotation) : ReadController(out position, out rotation);
            Closed = closed;
            if (leftJaw != null) leftJaw.localRotation = Quaternion.Euler(0, closed ? 0 : -22, 0);
            if (rightJaw != null) rightJaw.localRotation = Quaternion.Euler(0, closed ? 0 : 22, 0);
            if (TrackingValid)
            {
                TrackingValid = Coordinates.Finite(position) && Coordinates.Finite(rotation.x) && Coordinates.Finite(rotation.y)
                    && Coordinates.Finite(rotation.z) && Coordinates.Finite(rotation.w);
                if (TrackingValid) tip.SetPositionAndRotation(position, rotation);
            }
            if (exercise == null || board == null || detector == null) return;
            if (!pegsEnabled)
            {
                if (Held != null) ClearGrip();
                SetCandidate(null);
                previousClosed = closed;
                return;
            }
            if (!TrackingValid || exercise.State != ExerciseState.Running)
            {
                ReleaseHeld(!TrackingValid);
                SetCandidate(null);
                previousClosed = closed; // Reacquisition requires a fresh squeeze or pinch.
                return;
            }
            if (Held != null) Held.Follow(tip.position);
            PegObject nearest = null;
            float distance = grabRadius * grabRadius;
            if (Held == null)
                foreach (PegObject peg in board.Objects)
                {
                    if (peg.Scored || peg.Holder != null) continue;
                    float squared = (tip.position - peg.transform.position).sqrMagnitude;
                    if (squared < distance) { distance = squared; nearest = peg; }
                }
            SetCandidate(nearest);
            if (closed && !previousClosed && candidate != null && candidate.TryGrab(this)) Held = candidate;
            if (!closed && previousClosed) ReleaseHeld(false);
            previousClosed = closed;
        }
        private void SetCandidate(PegObject value)
        {
            if (candidate == value) return;
            if (candidate != null) candidate.HighlightFor(this, false);
            candidate = value;
            if (candidate != null) candidate.HighlightFor(this, true);
        }
        public void ClearGrip()
        {
            if (Held != null) Held.ReturnToSource();
            Held = null;
            SetCandidate(null);
        }
        private void ReleaseHeld(bool lost)
        {
            PegObject peg = Held;
            Held = null;
            if (peg != null) detector.Release(peg, instrument, peg.transform.position, lost);
        }
        private void OnDisable()
        {
            TrackingValid = false; Closed = false;
            if (detector != null) ReleaseHeld(true); else ClearGrip();
            SetCandidate(null);
            if (ownsClose) close?.Dispose();
            if (ownsTracked) tracked?.Dispose();
            if (ownsTracking) tracking?.Dispose();
        }
    }
}
