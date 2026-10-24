using System;
using UnityEngine;

namespace Jeremy
{
    public enum ExerciseState { Idle, Ready, Running, Paused, Completed, Cancelled }

    public sealed class ExerciseStateMachine : MonoBehaviour
    {
        public ExerciseState State { get; private set; } = ExerciseState.Idle;
        public string AttemptId { get; private set; }
        public double ElapsedSeconds { get; private set; }
        public long ElapsedMs => (long)(ElapsedSeconds * 1000);
        public event Action Reset;
        public event Action<ExerciseState> Changed;

        public void ResetExercise()
        {
            if (State == ExerciseState.Running || State == ExerciseState.Paused) Cancel();
            AttemptId = Guid.NewGuid().ToString("N");
            ElapsedSeconds = 0;
            State = ExerciseState.Ready;
            Reset?.Invoke();
            Changed?.Invoke(State);
        }

        public bool Begin() => Transition(ExerciseState.Ready, ExerciseState.Running);
        public bool Pause() => Transition(ExerciseState.Running, ExerciseState.Paused);
        public bool Resume() => Transition(ExerciseState.Paused, ExerciseState.Running);
        public bool Complete()
        {
            if (State != ExerciseState.Running && State != ExerciseState.Paused) return false;
            SetState(ExerciseState.Completed);
            return true;
        }
        public bool Cancel()
        {
            if (State != ExerciseState.Running && State != ExerciseState.Paused && State != ExerciseState.Ready) return false;
            SetState(ExerciseState.Cancelled);
            return true;
        }

        private void Update() => Advance(Time.unscaledDeltaTime);

        // Explicit delta also lets editor checks exercise the clock without sleeping.
        public void Advance(double seconds)
        {
            if (State != ExerciseState.Running || double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0) return;
            ElapsedSeconds = Math.Min(3600, ElapsedSeconds + seconds);
            if (ElapsedSeconds >= 3600) Complete(); // Backend's maximum accepted attempt duration.
        }
        private bool Transition(ExerciseState from, ExerciseState to)
        {
            if (State != from) return false;
            SetState(to);
            return true;
        }
        private void SetState(ExerciseState state) { State = state; Changed?.Invoke(state); }
    }
}
