#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// A full-length integration run: actual frame time, actual InputSystem events,
// and the scene's normal DefenseInput.Update. It is never active in normal play.
public static class ARealtimeReplay
{
    static DefenseGame game;
    static AttackPlan[] lasers, missiles;
    static bool previous, overlapShot, fiveShot, reflectedShot;
    static double started;
    static int runtimeErrors;
    static bool running;
    public static void Begin()
    {
        if (!EditorApplication.isPlaying || running) throw new InvalidOperationException("Enter Play Mode; replay must be idle.");
        game = UnityEngine.Object.FindAnyObjectByType<DefenseGame>();
        game.GetComponent<DefenseInput>().enabled = true;
        game.AutoSchedule = true;
        game.BeginRun();
        var plans = game.Director.PlannedAttacks.ToArray();
        lasers = plans.Where(p=>p.Kind==AttackKind.Laser).ToArray();
        missiles = plans.Where(p=>p.Kind==AttackKind.Missile).ToArray();
        previous=false; overlapShot=fiveShot=reflectedShot=false;
        runtimeErrors=0; running=true; started=EditorApplication.timeSinceStartup;
        Send(false);
        Application.logMessageReceived += Log;
        EditorApplication.update += Step;
        File.WriteAllText("Temp/A_TekTus/replay-status.txt", "RUNNING: 90 second real-time InputSystem integration run");
    }
    static void Log(string text,string stack,LogType type)
    { if(type==LogType.Error || type==LogType.Exception || type==LogType.Assert) runtimeErrors++; }
    static void Send(bool held)
    { InputSystem.QueueStateEvent(Keyboard.current,held?new KeyboardState(UnityEngine.InputSystem.Key.E):new KeyboardState()); }
    static void Step()
    {
        if (!EditorApplication.isPlaying || game == null) { End("ABORTED"); return; }
        if (game.Phase!=GamePhase.Playing)
        {
            bool passed=game.Phase==GamePhase.Won && game.ParryCount==missiles.Length && runtimeErrors==0;
            ScreenCapture.CaptureScreenshot(Path.GetFullPath("Belge/TestKaniti/13-gercek-90sn-sonuc.png"));
            string report=(passed?"PASS":"FAIL")+" — Gerçek zamanlı 90 saniyelik Unity Play Mode koşusu\n"+
                "Wall seconds="+(EditorApplication.timeSinceStartup-started).ToString("F2")+"\n"+
                "Game seconds="+game.Elapsed.ToString("F2")+"\nPhase="+game.Phase+
                "\nParries="+game.ParryCount+"/"+missiles.Length+"\nLaser blocks="+game.BlockCount+
                "/"+lasers.Length+"\nRuntime errors="+runtimeErrors+"\n"+
                "Girdiler InputSystem.QueueStateEvent ile gönderildi; oyun normal DefenseInput.Update ve Time.deltaTime ile çalıştı.\n";
            File.WriteAllText("Belge/A_Gercek_Zamanli_Test.txt",report);
            End(report);return;
        }
        float t=game.Elapsed;
        bool held=lasers.Any(l=>t>=l.StartTime-.14f && t<l.EndTime+.04f) || missiles.Any(m=>t>=m.ImpactTime-.19f && t<m.ImpactTime-.10f);
        if(held!=previous){Send(held);previous=held;}
        if(!reflectedShot && game.Missiles.Any(m=>m.IsReflected)){reflectedShot=true;ScreenCapture.CaptureScreenshot(Path.GetFullPath("Belge/TestKaniti/14-gercek-parry.png"));}
        if(!overlapShot && game.ActiveLaserCount>0 && game.Missiles.Any(m=>!m.IsReflected)){overlapShot=true;ScreenCapture.CaptureScreenshot(Path.GetFullPath("Belge/TestKaniti/15-gercek-lazer-fuze.png"));}
        if(!fiveShot && game.ActiveLaserCount==5){fiveShot=true;ScreenCapture.CaptureScreenshot(Path.GetFullPath("Belge/TestKaniti/16-gercek-bes-lazer.png"));}
    }
    static void End(string report)
    {
        EditorApplication.update-=Step;Application.logMessageReceived-=Log;
        running=false;Send(false);
        File.WriteAllText("Temp/A_TekTus/replay-status.txt",report);
    }
}
#endif
