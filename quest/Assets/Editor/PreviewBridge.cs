using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Lets a terminal drive the editor for testing the desktop preview without clicking.
// Write commands, one per line, to %TEMP%/jeremy-bridge/editor.txt:
//   refresh            reimport and recompile changed scripts
//   play               open Assets/Scenes/PegTransfer.unity and enter Play mode
//   stop               leave Play mode
// Runtime commands (autopilot, exercise 1|2, start, reset, coach, screenshot <path>, status) go to
// runtime.txt and are read by PreviewAutopilot while playing. The editor writes editor-status.txt.
[InitializeOnLoad]
static class PreviewBridge
{
    const string Scene = "Assets/Scenes/PegTransfer.unity";
    static double nextPoll;
    static string Folder => Path.Combine(Path.GetTempPath(), "jeremy-bridge");

    static PreviewBridge()
    {
        EditorApplication.update += Poll;
        WriteStatus();
    }

    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 0.5;
        string file = Path.Combine(Folder, "editor.txt");
        if (!File.Exists(file)) return;
        string[] lines;
        try { lines = File.ReadAllLines(file); File.Delete(file); } catch (IOException) { return; }
        foreach (string raw in lines)
        {
            switch (raw.Trim())
            {
                case "refresh": AssetDatabase.Refresh(); break;
                case "play":
                    if (EditorApplication.isPlaying) break;
                    if (EditorSceneManager.GetActiveScene().path != Scene) EditorSceneManager.OpenScene(Scene);
                    EditorApplication.isPlaying = true;
                    break;
                case "stop": EditorApplication.isPlaying = false; break;
            }
        }
        WriteStatus();
    }

    static void WriteStatus()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Path.Combine(Folder, "editor-status.txt"),
                "playing=" + EditorApplication.isPlaying + " compiling=" + EditorApplication.isCompiling
                + " scene=" + EditorSceneManager.GetActiveScene().path + " time=" + System.DateTime.Now.ToString("HH:mm:ss"));
        }
        catch (IOException) { }
    }
}
