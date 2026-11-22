#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Local development commands. Not included in player builds.
[InitializeOnLoad]
public static class AEditorBridge
{
    static readonly string Folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/A_TekTus"));
    static double nextPoll;
    static AEditorBridge() { EditorApplication.update += Poll; }

    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        nextPoll = EditorApplication.timeSinceStartup + .3;
        Directory.CreateDirectory(Folder);
        string path = Path.Combine(Folder, "command.txt");
        if (!File.Exists(path)) return;
        string command = File.ReadAllText(path).Trim();
        File.Delete(path);
        try
        {
            switch (command)
            {
                case "play": EditorApplication.isPlaying = true; break;
                case "stop": EditorApplication.isPlaying = false; break;
                case "build": Invoke("ASceneBuilder", "Build"); break;
                case "test": Invoke("APlayModeVerification", "Begin"); break;
                case "capture": Invoke("APlayModeVerification", "Capture"); break;
                case "audit": Invoke("ASceneBuilder", "Audit"); break;
                case "timeline": Invoke("ATimelineAudit", "Write"); break;
                case "replay": Invoke("ARealtimeReplay", "Begin"); break;
                case "clear-console": typeof(EditorApplication).Assembly.GetType("UnityEditor.LogEntries").GetMethod("Clear", BindingFlags.Static | BindingFlags.Public).Invoke(null, null); break;
                case "refresh": AssetDatabase.Refresh(); break;
                case "status": break;
                default: throw new ArgumentException("Unknown local command: " + command);
            }
            File.WriteAllText(Path.Combine(Folder, "result.txt"), DateTime.Now.ToString("O") + " " + command + " OK\nplaying=" + EditorApplication.isPlaying + "\nscene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().path);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            File.WriteAllText(Path.Combine(Folder, "result.txt"), command + " FAILED\n" + exception);
        }
    }

    static void Invoke(string name, string method)
    {
        Type type = typeof(AEditorBridge).Assembly.GetType(name);
        if (type == null) throw new InvalidOperationException(name + " is not compiled yet.");
        type.GetMethod(method, BindingFlags.Static | BindingFlags.Public).Invoke(null, null);
    }
}
#endif
