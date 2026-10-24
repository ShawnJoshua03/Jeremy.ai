using UnityEngine;

namespace Jeremy
{
    public sealed class PegObject : MonoBehaviour
    {
        public PegBoard Board { get; private set; }
        public int Index { get; private set; }
        public string ObjectId => "peg_" + (Index + 1);
        public InstrumentController Holder { get; private set; }
        public bool Scored { get; private set; }
        private Renderer visual;
        private int highlights;
        public void Initialize(PegBoard board, int index) { Board = board; Index = index; visual = GetComponent<Renderer>(); }
        public bool TryGrab(InstrumentController instrument)
        {
            if (Scored || Holder != null || instrument == null) return false;
            Holder = instrument;
            return true;
        }
        public void Follow(Vector3 tip) { transform.position = tip; }
        public void ClearHolder() { Holder = null; }
        public void SetHighlight(bool highlighted)
        {
            if (visual == null) return;
            if (Scored) PegBoard.Tint(visual, Color.green);
            else if (highlighted || highlights != 0) PegBoard.Tint(visual, Color.yellow);
            else visual.SetPropertyBlock(null);
        }
        public void HighlightFor(InstrumentController instrument, bool active)
        {
            int mask = instrument.instrument == "right" ? 2 : 1;
            highlights = active ? highlights | mask : highlights & ~mask;
            SetHighlight(false);
        }
        public void MarkScored()
        {
            Holder = null;
            Scored = true;
            transform.localPosition = Board.Target(Index);
            SetHighlight(false);
        }
        public void ReturnToSource() { Holder = null; transform.localPosition = Board.Source(Index); SetHighlight(false); }
        public void ResetObject() { Scored = false; highlights = 0; ReturnToSource(); }
    }
}
