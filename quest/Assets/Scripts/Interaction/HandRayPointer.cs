using UnityEngine;
using UnityEngine.UI;

namespace Jeremy
{
    public sealed class PanelDragHandle : MonoBehaviour
    {
        public Transform target;

        public float DistanceTo(Vector3 worldPoint)
        {
            RectTransform rect = (RectTransform)transform;
            Vector3 local = rect.InverseTransformPoint(worldPoint);
            Vector3 nearest = new Vector3(
                Mathf.Clamp(local.x, rect.rect.xMin, rect.rect.xMax),
                Mathf.Clamp(local.y, rect.rect.yMin, rect.rect.yMax),
                0f);
            return Vector3.Distance(worldPoint, rect.TransformPoint(nearest));
        }
    }

    // Hand rays click UI buttons. Panel dragging is intentionally separate: a tracked hand must be
    // physically near the panel side and pinch it, so the ray never grabs a panel.
    public sealed class HandRayPointer : MonoBehaviour
    {
        private const float GrabDistance = 0.14f;
        private Canvas canvas;
        private OVRHand[] hands;
        private Transform[] controllers;
        private LineRenderer[] lines;
        private bool[] wasPinching;
        private PanelDragHandle[] dragging;
        private Vector3[] dragOffsets;

        public void Setup(Canvas target, OVRHand[] trackedHands)
        {
            Setup(target, trackedHands, null);
        }

        public void Setup(Canvas target, OVRHand[] trackedHands, Transform[] controllerPoses)
        {
            canvas = target;
            hands = trackedHands;
            controllers = controllerPoses;
            int count = (hands == null ? 0 : hands.Length) + (controllers == null ? 0 : controllers.Length);
            lines = new LineRenderer[count];
            wasPinching = new bool[count];
            dragging = new PanelDragHandle[count];
            dragOffsets = new Vector3[count];
            for (int i = 0; i < count; i++) lines[i] = CreateLine(i);
        }

        private LineRenderer CreateLine(int index)
        {
            var line = new GameObject("Hand ray " + index).AddComponent<LineRenderer>();
            line.transform.SetParent(transform, false);
            line.positionCount = 2;
            line.widthMultiplier = 0.004f;
            line.useWorldSpace = true;
            line.material = new Material(Shader.Find("Sprites/Default"));
            line.enabled = false;
            return line;
        }

        private void Update()
        {
            if (canvas == null) return;
            int handCount = hands == null ? 0 : hands.Length;
            for (int i = 0; i < handCount; i++) UpdateHand(i);
            int controllerCount = controllers == null ? 0 : controllers.Length;
            for (int i = 0; i < controllerCount; i++)
                UpdatePointer(handCount + i, controllers[i], ControllerPinching(i));
        }

        private void UpdateHand(int index)
        {
            OVRHand hand = hands[index];
            bool usable = hand != null && hand.IsTracked;
            if (!usable)
            {
                StopPointer(index);
                return;
            }
            bool pinching = hand.GetFingerIsPinching(OVRHand.HandFinger.Index);
            Vector3 grabPoint = hand.IsPointerPoseValid ? hand.PointerPose.position : hand.transform.position;
            UpdateDirectDrag(index, grabPoint, pinching);
            if (hand.IsPointerPoseValid)
                UpdateRay(index, hand.PointerPose.position, hand.PointerPose.forward, pinching);
            else
                lines[index].enabled = false;
        }

        private void UpdatePointer(int index, Transform pose, bool pinching)
        {
            if (pose == null) { StopPointer(index); return; }
            UpdateRay(index, pose.position, pose.forward, pinching);
        }

        private void UpdateDirectDrag(int index, Vector3 grabPoint, bool pinching)
        {
            PanelDragHandle handle = DragHandleAt(grabPoint);
            if (pinching && !wasPinching[index])
            {
                if (handle != null && handle.target != null)
                {
                    dragging[index] = handle;
                    dragOffsets[index] = handle.target.position - grabPoint;
                }
            }
            if (pinching && dragging[index] != null)
                dragging[index].target.position = grabPoint + dragOffsets[index];
            if (!pinching) dragging[index] = null;
        }

        private void UpdateRay(int index, Vector3 origin, Vector3 direction, bool pinching)
        {
            LineRenderer line = lines[index];
            line.enabled = true;
            Ray ray = new Ray(origin, direction);
            Button hovered = null;
            float length = 0.6f;
            var plane = new Plane(canvas.transform.forward, canvas.transform.position);
            if (plane.Raycast(ray, out float distance))
            {
                length = distance;
                hovered = ButtonAt(ray.GetPoint(distance));
            }
            line.SetPosition(0, ray.origin);
            line.SetPosition(1, ray.GetPoint(length));
            line.startColor = line.endColor = hovered != null ? Color.green : Color.white;
            if (pinching && !wasPinching[index] && hovered != null) hovered.onClick.Invoke();
            wasPinching[index] = pinching;
        }

        private void StopPointer(int index)
        {
            lines[index].enabled = false;
            wasPinching[index] = false;
            dragging[index] = null;
        }

        private static bool ControllerPinching(int index)
        {
            OVRInput.Controller controller = index == 0 ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;
            OVRInput.Button button = index == 0 ? OVRInput.Button.PrimaryIndexTrigger : OVRInput.Button.SecondaryIndexTrigger;
            return OVRInput.Get(button, controller);
        }

        public static bool IsOverButton(OVRHand hand)
        {
            if (hand == null || !hand.IsTracked || !hand.IsPointerPoseValid) return false;
            Ray ray = new Ray(hand.PointerPose.position, hand.PointerPose.forward);
            foreach (HandRayPointer pointer in FindObjectsByType<HandRayPointer>(FindObjectsSortMode.None))
            {
                if (!pointer.isActiveAndEnabled || pointer.canvas == null || !pointer.canvas.gameObject.activeInHierarchy) continue;
                var plane = new Plane(pointer.canvas.transform.forward, pointer.canvas.transform.position);
                if (!plane.Raycast(ray, out float distance)) continue;
                Vector3 point = ray.GetPoint(distance);
                foreach (Button button in pointer.canvas.GetComponentsInChildren<Button>())
                {
                    if (!button.IsInteractable()) continue;
                    var rect = (RectTransform)button.transform;
                    if (rect.rect.Contains(rect.InverseTransformPoint(point))) return true;
                }
            }
            return false;
        }

        private PanelDragHandle DragHandleAt(Vector3 worldPoint)
        {
            foreach (PanelDragHandle handle in canvas.GetComponentsInChildren<PanelDragHandle>())
            {
                if (handle.target == null) continue;
                if (handle.DistanceTo(worldPoint) <= GrabDistance) return handle;
            }
            return null;
        }

        private Button ButtonAt(Vector3 worldPoint)
        {
            foreach (Button button in canvas.GetComponentsInChildren<Button>())
            {
                if (!button.IsInteractable()) continue;
                var rect = (RectTransform)button.transform;
                if (rect.rect.Contains(rect.InverseTransformPoint(worldPoint))) return button;
            }
            return null;
        }
    }
}
