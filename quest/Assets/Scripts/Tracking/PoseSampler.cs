using UnityEngine;

namespace Jeremy
{
    [DefaultExecutionOrder(100)]
    public sealed class PoseSampler : MonoBehaviour
    {
        public ExerciseStateMachine exercise;
        public PegBoard board;
        public EventRecorder recorder;
        public InstrumentController[] instruments;
        [Range(20, 30)] public int frequency = 25;
        private int leftIndex, rightIndex;
        private double nextTime;

        public void ResetSampling() { leftIndex = rightIndex = 0; nextTime = 0; }
        private void LateUpdate()
        {
            if (exercise == null || exercise.State != ExerciseState.Running) return;
            double now = Time.realtimeSinceStartupAsDouble;
            if (now < nextTime) return;
            nextTime = now + 1.0 / Mathf.Clamp(frequency, 20, 30); // Never backfill tracking gaps with repeated poses.
            Capture(false);
        }
        public void BreakTrackingPath() => Capture(true);
        private void Capture(bool forceInvalid)
        {
            if (instruments == null || recorder == null || board == null) return;
            foreach (InstrumentController instrument in instruments)
            {
                if (instrument == null) continue;
                Vector3 position = instrument.tip == null ? Vector3.zero : board.transform.InverseTransformPoint(instrument.tip.position);
                Quaternion rotation = instrument.tip == null ? Quaternion.identity : Quaternion.Inverse(board.transform.rotation) * instrument.tip.rotation;
                bool valid = !forceInvalid && instrument.isActiveAndEnabled && instrument.TrackingValid && Coordinates.InRange(position)
                    && Coordinates.Finite(rotation.x) && Coordinates.Finite(rotation.y) && Coordinates.Finite(rotation.z) && Coordinates.Finite(rotation.w);
                recorder.RecordSample(new PoseSample { instrument = instrument.instrument,
                    idx = instrument.instrument == "left" ? leftIndex++ : rightIndex++, t_ms = exercise.ElapsedMs,
                    pos = Coordinates.Array(valid ? position : Vector3.zero),
                    quat = valid ? new[] { rotation.x, rotation.y, rotation.z, rotation.w } : new[] { 0f, 0f, 0f, 1f },
                    valid = valid, interaction = instrument.Interaction });
            }
        }
    }
}
