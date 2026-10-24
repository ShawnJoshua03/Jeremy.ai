using UnityEditor;
using UnityEngine;

namespace Jeremy.Tests
{
    [InitializeOnLoad]
    public static class VerificationEntry
    {
        static VerificationEntry() { EditorApplication.playModeStateChanged += OnPlayMode; }
        public static void Run()
        {
            SessionState.SetBool("Jeremy.Verify", true);
            EditorApplication.EnterPlaymode();
        }
        private static void OnPlayMode(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("Jeremy.Verify", false)) return;
            SessionState.SetBool("Jeremy.Verify", false);
            var runner = new GameObject("Verification").AddComponent<VerificationRunner>();
            runner.Finished = success => EditorApplication.Exit(success ? 0 : 1);
        }
    }
}
