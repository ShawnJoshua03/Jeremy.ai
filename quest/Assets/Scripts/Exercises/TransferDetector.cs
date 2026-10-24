using UnityEngine;

namespace Jeremy
{
    public sealed class TransferDetector : MonoBehaviour
    {
        public ExerciseStateMachine exercise;
        public PegBoard board;
        public EventRecorder recorder;
        public void Release(PegObject peg, string instrument, Vector3 worldPosition, bool trackingLost = false)
        {
            if (peg == null || peg.Board != board || peg.Scored) return;
            peg.ClearHolder();
            if (exercise.State != ExerciseState.Running) { peg.ReturnToSource(); return; }
            Vector3 local = board.transform.InverseTransformPoint(worldPosition);
            bool success = !trackingLost && board.IsInsideTarget(peg.Index, local);
            string kind = success ? "transfer_success" : (!trackingLost && board.IsOverBoard(local) ? "invalid_release" : "drop");
            if (success) peg.MarkScored(); else peg.ReturnToSource();
            recorder.Record(kind, instrument, peg.ObjectId, board.Source(peg.Index), success ? board.Target(peg.Index) : local, local);
            if (success && recorder.SuccessfulTransfers == PegBoard.ObjectCount) exercise.Complete();
        }
    }
}
