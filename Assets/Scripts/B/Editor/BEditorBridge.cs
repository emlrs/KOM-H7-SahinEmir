#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LavaRiseB
{
    // Local, editor-only commands used during construction and verification.
    [InitializeOnLoad]
    public static class BEditorBridge
    {
        static readonly string Folder = Path.GetFullPath(Path.Combine(Application.dataPath,"../Temp/B_HareketHissi"));
        static double nextPoll;
        static BEditorBridge() { EditorApplication.update += Poll; }
        static void Poll()
        {
            if(EditorApplication.timeSinceStartup<nextPoll || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            nextPoll=EditorApplication.timeSinceStartup+.25;
            Directory.CreateDirectory(Folder);
            string path=Path.Combine(Folder,"command.txt");
            if(!File.Exists(path))return;
            string command=File.ReadAllText(path).Trim();File.Delete(path);
            try
            {
                switch(command)
                {
                    case "build": Invoke("LavaRiseB.BSceneBuilder","Build");break;
                    case "open-a": Open("Assets/Scenes/A_TekTus.unity");break;
                    case "open-b": Open("Assets/Scenes/B_HareketHissi.unity");break;
                    case "play": EditorApplication.isPlaying=true;break;
                    case "stop": EditorApplication.isPlaying=false;break;
                    case "movement-test": Invoke("LavaRiseB.BMovementVerification","Begin");break;
                    case "course-test": Invoke("LavaRiseB.BCourseVerification","RunAll");break;
                    case "flow-test": Invoke("LavaRiseB.BFlowVerification","Begin");break;
                    case "route-test": Invoke("LavaRiseB.BRouteVerification","Begin");break;
                    case "route-replay": Invoke("LavaRiseB.BRouteVerification","Replay");break;
                    case "a-test": Invoke("APlayModeVerification","Begin");break;
                    case "audit": Invoke("LavaRiseB.BProjectAudit","Run");break;
                    case "capture": ScreenCapture.CaptureScreenshot(Path.GetFullPath("Belge/B_TestKaniti/manual.png"));break;
                    case "clear-console": typeof(EditorApplication).Assembly.GetType("UnityEditor.LogEntries").GetMethod("Clear",BindingFlags.Static|BindingFlags.Public).Invoke(null,null);break;
                    case "status": break;
                    default: throw new ArgumentException("Unknown B command: "+command);
                }
                File.WriteAllText(Path.Combine(Folder,"result.txt"),DateTimeOffset.Now.ToString("O")+" "+command+" OK\nplaying="+EditorApplication.isPlaying+"\nscene="+UnityEngine.SceneManagement.SceneManager.GetActiveScene().path);
            }
            catch(Exception e){Debug.LogException(e);File.WriteAllText(Path.Combine(Folder,"result.txt"),command+" FAILED\n"+e);}
        }
        static void Open(string path)
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play Mode before changing scene.");
            EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
        }
        static void Invoke(string name,string method)
        {
            Type type=typeof(BEditorBridge).Assembly.GetType(name);
            if(type==null)throw new InvalidOperationException(name+" is not compiled.");
            type.GetMethod(method,BindingFlags.Static|BindingFlags.Public).Invoke(null,null);
        }
    }
}
#endif
