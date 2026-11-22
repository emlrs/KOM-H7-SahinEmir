#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CollisionC
{
    // Editor-only local command queue for building and verifying this scene.
    [InitializeOnLoad]
    public static class CEditorBridge
    {
        static readonly string Folder = Path.GetFullPath("Temp/C_Carpisma");
        static double nextPoll;
        static CEditorBridge() { EditorApplication.update += Poll; }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            nextPoll = EditorApplication.timeSinceStartup + .25;
            Directory.CreateDirectory(Folder);
            string file = Path.Combine(Folder, "command.txt");
            if (!File.Exists(file)) return;
            string command = File.ReadAllText(file).Trim();
            File.Delete(file);
            try
            {
                switch (command)
                {
                    case "build": Invoke("CollisionC.CSceneBuilder", "Build"); break;
                    case "open-a": Open("Assets/Scenes/A_TekTus.unity"); break;
                    case "open-b": Open("Assets/Scenes/B_HareketHissi.unity"); break;
                    case "open-c": Open("Assets/Scenes/C_Carpisma.unity"); break;
                    case "play": EditorApplication.isPlaying = true; break;
                    case "stop": EditorApplication.isPlaying = false; break;
                    case "player-test": Invoke("CollisionC.Editor.CPlayerVerification", "Begin"); break;
                    case "orb-test": Invoke("CollisionC.COrbVerification", "Begin"); break;
                    case "flow-test": Invoke("CollisionC.CFlowVerification", "Begin"); break;
                    case "run-test": Invoke("CollisionC.CRunVerification", "Begin"); break;
                    case "real-run": Invoke("CollisionC.CRunVerification", "Replay"); break;
                    case "visual-test": Invoke("CollisionC.CRunVerification", "VisualTest"); break;
                    case "console-status":
                        object[] counts = { 0, 0, 0 };
                        MethodInfo counter = typeof(EditorApplication).Assembly.GetType("UnityEditor.LogEntries").GetMethod("GetCountsByType", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                        if (counter == null) throw new MissingMethodException("UnityEditor.LogEntries.GetCountsByType");
                        counter.Invoke(null, counts);
                        ParameterInfo[] parameters = counter.GetParameters();
                        File.WriteAllText(Path.Combine(Folder, "console-status.txt"), parameters[0].Name + "=" + counts[0] + " " + parameters[1].Name + "=" + counts[1] + " " + parameters[2].Name + "=" + counts[2]); break;
                    case "a-test": Invoke("APlayModeVerification", "Begin"); break;
                    case "b-movement": Invoke("LavaRiseB.BMovementVerification", "Begin"); break;
                    case "b-flow": Invoke("LavaRiseB.BFlowVerification", "Begin"); break;
                    case "b-route": Invoke("LavaRiseB.BRouteVerification", "Begin"); break;
                    case "audit": Invoke("CollisionC.CProjectAudit", "Run"); break;
                    case "capture":
                        Directory.CreateDirectory("Belge/C_TestKaniti");
                        ScreenCapture.CaptureScreenshot(Path.GetFullPath("Belge/C_TestKaniti/manual.png")); break;
                    case "clear-console":
                        typeof(EditorApplication).Assembly.GetType("UnityEditor.LogEntries").GetMethod("Clear", BindingFlags.Static | BindingFlags.Public).Invoke(null, null); break;
                    case "status": break;
                    default: throw new ArgumentException("Unknown C command: " + command);
                }
                File.WriteAllText(Path.Combine(Folder, "result.txt"), DateTimeOffset.Now.ToString("O") + " " + command + " OK\nplaying=" + EditorApplication.isPlaying + "\ntransition=" + EditorApplication.isPlayingOrWillChangePlaymode + "\nscene=" + SceneManager.GetActiveScene().path);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                File.WriteAllText(Path.Combine(Folder, "result.txt"), command + " FAILED\n" + error);
            }
        }

        static void Open(string scene)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before opening another scene.");
            EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
        }
        static void Invoke(string typeName, string methodName)
        {
            Type type = typeof(CEditorBridge).Assembly.GetType(typeName);
            if (type == null) throw new InvalidOperationException(typeName + " has not compiled.");
            MethodInfo method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public);
            if (method == null) throw new MissingMethodException(typeName, methodName);
            method.Invoke(null, null);
        }
    }
}
#endif
