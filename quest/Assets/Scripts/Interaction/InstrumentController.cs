using UnityEngine;
using UnityEngine.InputSystem;

namespace Jeremy
{
    // Bind actions to the installed OpenXR controller layout. Pose comes from the SDK rig anchor.
    // Confirmed in OpenXR 1.18 OculusTouchControllerProfile: triggerPressed, isTracked, trackingState.
    [DefaultExecutionOrder(50)]
    public sealed class InstrumentController : MonoBehaviour
    {
        public string instrument = "left";
        public Transform controllerPose;
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
        public bool TrackingValid { get; private set; }
        public PegObject Held { get; private set; }
        public string Interaction => Held != null ? "grasping" : "idle";
        private InputAction close, tracked, tracking;
        private bool ownsClose, ownsTracked, ownsTracking, previousClosed;
        private PegObject candidate;

        private void OnEnable()
        {
            string hand = instrument == "right" ? "RightHand" : "LeftHand";
            close = Resolve(closeAction, "<XRController>{" + hand + "}/triggerPressed", InputActionType.Button, out ownsClose);
            tracked = Resolve(trackedAction, "<XRController>{" + hand + "}/isTracked", InputActionType.Button, out ownsTracked);
            tracking = Resolve(trackingStateAction, "<XRController>{" + hand + "}/trackingState", InputActionType.Value, out ownsTracking);
        }
        private InputAction Resolve(InputActionReference reference, string binding, InputActionType type, out bool owned)
        {
            owned = reference == null || reference.action == null;
            InputAction action = owned ? new InputAction("Controller input", type, binding) : reference.action;
            if (owned) action.Enable(); // Shared action maps must be enabled by their owner.
            return action;
        }
        private void Update()
        {
            TrackingValid = controllerPose != null && tip != null && tracked.ReadValue<float>() > 0.5f && (tracking.ReadValue<int>() & 3) == 3;
            bool closed = close.ReadValue<float>() > 0.5f;
            if (leftJaw != null) leftJaw.localRotation = Quaternion.Euler(0, closed ? 0 : -22, 0);
            if (rightJaw != null) rightJaw.localRotation = Quaternion.Euler(0, closed ? 0 : 22, 0);
            if (TrackingValid)
            {
                Vector3 position = controllerPose.TransformPoint(tipOffset);
                Quaternion rotation = controllerPose.rotation;
                TrackingValid = Coordinates.Finite(position) && Coordinates.Finite(rotation.x) && Coordinates.Finite(rotation.y)
                    && Coordinates.Finite(rotation.z) && Coordinates.Finite(rotation.w);
                if (TrackingValid) tip.SetPositionAndRotation(position, rotation);
            }
            if (exercise == null || board == null || detector == null) return;
            if (!TrackingValid || exercise.State != ExerciseState.Running)
            {
                ReleaseHeld(!TrackingValid);
                SetCandidate(null);
                previousClosed = closed; // Reacquisition requires a fresh squeeze.
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
            TrackingValid = false;
            if (detector != null) ReleaseHeld(true); else ClearGrip();
            SetCandidate(null);
            if (ownsClose) close?.Dispose();
            if (ownsTracked) tracked?.Dispose();
            if (ownsTracking) tracking?.Dispose();
        }
    }
}
