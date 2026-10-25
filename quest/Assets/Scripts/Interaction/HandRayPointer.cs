using UnityEngine;
using UnityEngine.UI;

namespace Jeremy
{
    // Hand-tracking UI input. A ray from each hand's pointer pose hits the world-space HUD plane;
    // an index-thumb pinch clicks the button under the ray. No EventSystem or controller needed.
    public sealed class HandRayPointer : MonoBehaviour
    {
        private const float MissLength = 0.6f;
        private Canvas canvas;
        private OVRHand[] hands;
        private LineRenderer[] lines;
        private bool[] wasPinching;

        public void Setup(Canvas target, OVRHand[] trackedHands)
        {
            canvas = target;
            hands = trackedHands;
            lines = new LineRenderer[hands.Length];
            wasPinching = new bool[hands.Length];
            for (int i = 0; i < hands.Length; i++) lines[i] = CreateLine(i);
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
            for (int i = 0; i < hands.Length; i++) UpdateHand(i);
        }

        private void UpdateHand(int index)
        {
            OVRHand hand = hands[index];
            LineRenderer line = lines[index];
            bool usable = hand != null && hand.IsTracked && hand.IsPointerPoseValid;
            line.enabled = usable;
            if (!usable) { wasPinching[index] = false; return; }

            Ray ray = new Ray(hand.PointerPose.position, hand.PointerPose.forward);
            Button hovered = null;
            float length = MissLength;
            var plane = new Plane(canvas.transform.forward, canvas.transform.position);
            if (plane.Raycast(ray, out float distance))
            {
                length = distance;
                hovered = ButtonAt(ray.GetPoint(distance));
            }
            line.SetPosition(0, ray.origin);
            line.SetPosition(1, ray.GetPoint(length));
            line.startColor = line.endColor = hovered != null ? Color.green : Color.white;

            bool pinching = hand.GetFingerIsPinching(OVRHand.HandFinger.Index);
            if (pinching && !wasPinching[index] && hovered != null) hovered.onClick.Invoke();
            wasPinching[index] = pinching;
        }

        public static bool IsOverButton(OVRHand hand)
        {
            if (hand == null || !hand.IsTracked || !hand.IsPointerPoseValid) return false;
            Ray ray = new Ray(hand.PointerPose.position, hand.PointerPose.forward);
            foreach (HandRayPointer pointer in FindObjectsByType<HandRayPointer>(FindObjectsSortMode.None))
            {
                if (!pointer.isActiveAndEnabled || pointer.canvas == null || !pointer.canvas.gameObject.activeInHierarchy) continue;
                var plane = new Plane(pointer.canvas.transform.forward, pointer.canvas.transform.position);
                if (plane.Raycast(ray, out float distance) && pointer.ButtonAt(ray.GetPoint(distance)) != null) return true;
            }
            return false;
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
