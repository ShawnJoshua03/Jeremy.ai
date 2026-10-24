using UnityEngine;

namespace Jeremy
{
    // One specimen (gallstone / tissue fragment). ObjectId stays peg_N for the backend.
    public sealed class PegObject : MonoBehaviour
    {
        public PegBoard Board { get; private set; }
        public int Index { get; private set; }
        public string ObjectId => "peg_" + (Index + 1);
        public InstrumentController Holder { get; private set; }
        public bool Scored { get; private set; }
        private Renderer visual;
        private int highlights;
        private bool settling;
        private float fallSpeed;
        private Quaternion restRotation;
        private static readonly Color Hover = new Color(1.45f, 1.35f, 0.75f);
        public void Initialize(PegBoard board, int index)
        {
            Board = board; Index = index; visual = GetComponent<Renderer>();
            restRotation = Quaternion.Euler(index * 47f % 30f - 15f, index * 71f % 360f, index * 29f % 24f - 12f);
        }
        public bool TryGrab(InstrumentController instrument)
        {
            if (Scored || Holder != null || instrument == null) return false;
            Holder = instrument;
            settling = false;
            return true;
        }
        public void Follow(Vector3 tip) { transform.position = tip; }
        public void ClearHolder() { Holder = null; }
        public void SetHighlight(bool highlighted)
        {
            if (visual == null) return;
            if (!Scored && (highlighted || highlights != 0)) PegBoard.Tint(visual, Hover);
            else visual.SetPropertyBlock(null);
        }
        public void HighlightFor(InstrumentController instrument, bool active)
        {
            int mask = instrument.instrument == "right" ? 2 : 1;
            highlights = active ? highlights | mask : highlights & ~mask;
            SetHighlight(false);
        }
        /// <summary>Counted: the specimen falls from where it was released onto its resting spot in the bag.</summary>
        public void MarkScored()
        {
            Holder = null;
            Scored = true;
            Vector3 rest = Board.Target(Index);
            Vector3 from = transform.localPosition;
            // Start over the resting spot at the release height, so it drops straight into the pouch.
            transform.localPosition = new Vector3(rest.x, Mathf.Max(rest.y, from.y), rest.z);
            settling = transform.localPosition.y > rest.y + 1e-4f;
            fallSpeed = 0f;
            if (!settling) transform.localPosition = rest;
            SetHighlight(false);
        }
        private void Update()
        {
            if (!settling) return;
            Vector3 rest = Board.Target(Index);
            fallSpeed += 9.81f * 0.6f * Time.deltaTime;
            Vector3 p = transform.localPosition;
            p.y -= fallSpeed * Time.deltaTime;
            transform.localRotation = Quaternion.RotateTowards(transform.localRotation, restRotation * Quaternion.Euler(0f, 40f, 0f), 360f * Time.deltaTime);
            if (p.y <= rest.y) { p = rest; settling = false; }
            transform.localPosition = p;
        }
        public void ReturnToSource()
        {
            Holder = null; settling = false;
            transform.localPosition = Board.Source(Index);
            transform.localRotation = restRotation;
            SetHighlight(false);
        }
        public void ResetObject() { Scored = false; highlights = 0; ReturnToSource(); }
    }
}
