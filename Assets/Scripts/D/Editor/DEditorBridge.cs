#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

namespace CombinedD
{
    // Local Editor-only tools. No task automation is included in the Windows player.
    [InitializeOnLoad]
    public static class DEditorBridge
    {
        static readonly string Folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/D_Birlesik"));
        static double nextPoll;
        static DEditorBridge() { EditorApplication.update += Poll; WriteIdentity(); }

        [MenuItem("D/Editor status")]
        public static void WriteIdentity()
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Path.Combine(Folder, "identity.txt"), "Assets=" + Application.dataPath + "\nWorking directory=" + Directory.GetCurrentDirectory());
        }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            nextPoll = EditorApplication.timeSinceStartup + .25;
            Directory.CreateDirectory(Folder);
            string path = Path.Combine(Folder, "command.txt");
            if (!File.Exists(path)) return;
            string action = File.ReadAllText(path).Trim();
            File.Delete(path);
            try
            {
                switch (action)
                {
                    case "fix-inspect": Invoke("DFixTools", "Inspect"); break;
                    case "fix-layout": Invoke("DFixTools", "SnapshotLayout"); break;
                    case "fix-shield-foot": Invoke("DFixTools", "RepairShieldFoot"); break;
                    case "shield-foot-test": Invoke("DShieldFootVerification", "Begin"); break;
                    case "fix-settings": Invoke("DFixTools", "ApplyFirstSettings"); break;
                    case "fix-demo": Invoke("DFixDemoVerification", "Begin"); break;
                    case "fix-demo-replay": Invoke("DFixDemoVerification", "Replay"); break;
                    case "camera-fix-test": Invoke("DCameraFixVerification", "Begin"); break;
                    case "laser-fix-test": Invoke("DLaserFixVerification", "Begin"); break;
                    case "open": EditorSceneManager.OpenScene("Assets/Scenes/D_Birlesik.unity"); break;
                    case "play": EditorApplication.isPlaying = true; break;
                    case "stop": EditorApplication.isPlaying = false; break;
                    case "player-test": Invoke("DPlayerVerification", "Begin"); break;
                    case "attack-test": Invoke("DAttackVerification", "Begin"); break;
                    case "orb-test": Invoke("DOrbVerification", "Begin"); break;
                    case "flow-test": Invoke("DFlowVerification", "Begin"); break;
                    case "link-test": Invoke("DLinkVerification", "Begin"); break;
                    case "route-test": Invoke("DRouteVerification", "Begin"); break;
                    case "replay": Invoke("DRouteVerification", "Replay"); break;
                    case "audit": Invoke("DProjectAudit", "Run"); break;
                    case "refresh": AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate); break;
                    case "lock-import": AssetDatabase.DisallowAutoRefresh(); break;
                    case "unlock-import": AssetDatabase.AllowAutoRefresh(); AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate); break;
                    case "build-player": BuildPlayer(); break;
                    case "capture":
                        Directory.CreateDirectory("Belge/D_TestKaniti");
                        ScreenCapture.CaptureScreenshot(Path.GetFullPath("Belge/D_TestKaniti/manual.png")); break;
                    case "clear-console":
                        typeof(EditorApplication).Assembly.GetType("UnityEditor.LogEntries").GetMethod("Clear", BindingFlags.Static | BindingFlags.Public).Invoke(null, null); break;
                    case "console-status":
                        object[] counts = { 0, 0, 0 };
                        MethodInfo method = typeof(EditorApplication).Assembly.GetType("UnityEditor.LogEntries").GetMethod("GetCountsByType", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                        method.Invoke(null, counts);
                        ParameterInfo[] names = method.GetParameters();
                        File.WriteAllText(Path.Combine(Folder, "console-status.txt"), names[0].Name + "=" + counts[0] + " " + names[1].Name + "=" + counts[1] + " " + names[2].Name + "=" + counts[2]); break;
                    case "status":
                        var keyboard = Keyboard.current;
                        var game = UnityEngine.Object.FindAnyObjectByType<DGameController>();
                        File.WriteAllText(Path.Combine(Folder, "input-status.txt"),
                            "Focused=" + Application.isFocused + " keyboard=" + keyboard + " native=" + (keyboard != null && keyboard.native)
                            + " enabled=" + (keyboard != null && keyboard.enabled) + " update=" + InputSystem.settings.updateMode
                            + "\nGame=" + game + " manual=" + (game && game.ManualSimulation) + " phase=" + (game ? game.Phase.ToString() : "none")
                            + "\nPlayer external=" + (game && game.Player && game.Player.UseExternalInput) + " manual=" + (game && game.Player && game.Player.ManualSimulation)); break;
                    default: throw new ArgumentException("Unknown D command: " + action);
                }
                File.WriteAllText(Path.Combine(Folder, "result.txt"), DateTimeOffset.Now.ToString("O") + " " + action + " OK\nplaying=" + EditorApplication.isPlaying + "\ntransition=" + EditorApplication.isPlayingOrWillChangePlaymode + "\nscene=" + SceneManager.GetActiveScene().path);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                File.WriteAllText(Path.Combine(Folder, "result.txt"), action + " FAILED\n" + error);
            }
        }
        static void Invoke(string typeName, string methodName)
        {
            Type type = typeof(DEditorBridge).Assembly.GetType("CombinedD." + typeName);
            if (type == null) type = typeof(DEditorBridge).Assembly.GetType("CombinedD.Editor." + typeName);
            if (type == null) throw new InvalidOperationException(typeName + " has not compiled.");
            type.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public).Invoke(null, null);
        }
        public static void BuildPlayer()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before building.");
            EditorSceneManager.SaveOpenScenes();
            PlayerSettings.productName = "D_Birlesik";
            PlayerSettings.companyName = "KOM";
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.runInBackground = true;
            // The local DX12 player crashed at shutdown; DX11 opens and closes cleanly.
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { UnityEngine.Rendering.GraphicsDeviceType.Direct3D11 });
            PlayerSettings.graphicsJobs = false;
            var stripping = UnityEngine.Rendering.GraphicsSettings.GetRenderPipelineSettings<UnityEngine.Rendering.Universal.URPShaderStrippingSetting>();
            if (stripping != null) stripping.stripUnusedPostProcessingVariants = false;
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Build/D_Birlesik"); Directory.CreateDirectory("Belge");
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/D_Birlesik.unity" },
                locationPathName = "Build/D_Birlesik/D_Birlesik.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });
            string result = report.summary.result + "\nErrors=" + report.summary.totalErrors + "\nWarnings=" + report.summary.totalWarnings + "\nBytes=" + report.summary.totalSize + "\nDuration=" + report.summary.totalTime;
            File.WriteAllText("Belge/D_Build_Raporu.txt", result);
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException(result);
        }
    }
}
#endif



