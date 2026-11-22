#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

// Runs inside the real Editor Play Mode. No test code is shipped in a player.
public static class APlayModeVerification
{
    static readonly List<string> results = new List<string>();
    static readonly List<string> runtimeErrors = new List<string>();
    static IEnumerator sequence;
    static double resumeAt;
    static DefenseGame main;
    static DefenseInput input;
    static int failures;
    static readonly string Folder = Path.GetFullPath("Belge/TestKaniti");

    [MenuItem("Tek Tus/Play Mode dogrulamasini calistir")]
    public static void Begin()
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("Play Mode is required.");
        if (sequence != null) throw new InvalidOperationException("Verification already running.");
        main = Object.FindAnyObjectByType<DefenseGame>();
        input = main.GetComponent<DefenseInput>();
        Directory.CreateDirectory(Folder);
        results.Clear(); runtimeErrors.Clear(); failures = 0;
        Application.logMessageReceived += Log;
        main.AutoSchedule = false;
        sequence = Run(); resumeAt = 0;
        EditorApplication.update += Step;
        File.WriteAllText("Temp/A_TekTus/test-status.txt", "RUNNING");
    }

    static void Log(string message, string stack, LogType type)
    {
        if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
            runtimeErrors.Add(message);
    }

    static void Step()
    {
        if (EditorApplication.timeSinceStartup < resumeAt) return;
        try
        {
            if (sequence.MoveNext())
            {
                resumeAt = EditorApplication.timeSinceStartup + Convert.ToDouble(sequence.Current ?? 0f);
                return;
            }
            Finish();
        }
        catch (Exception exception) { Check(false, "Test harness exception: " + exception); Finish(); }
    }

    static void Finish()
    {
        sequence = null;
        EditorApplication.update -= Step;
        Application.logMessageReceived -= Log;
        Check(runtimeErrors.Count == 0, "Verification boyunca Unity runtime Error/Exception sayısı: " + runtimeErrors.Count);
        if (main != null) { main.Settings.RunDuration = 90; main.AutoSchedule = true; main.BeginRun(); }
        if (input != null) input.enabled = true;
        Key(false);
        string text = "# Oyun A - Unity Play Mode doğrulaması\n\n" +
            "Çalıştırma: " + DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz") +
            "\n\nUnity: " + Application.unityVersion + "; gerçek Editor Play Mode.\n\n" +
            "İlk girdi kontrolleri Unity Input System klavye olayları ile gerçek DefenseInput.Update üzerinden; " +
            "deterministik kontroller Play Mode içindeki bağımsız DefenseGame örneklerinde Tick ile çalıştırıldı. " +
            "Animasyon kanıtları normal kare döngüsünde oluşturuldu. Bu kayıt insan eşli oyun testi yerine geçmez.\n\n" +
            string.Join("\n", results) + "\n\nBaşarısız kontrol sayısı: " + failures + "\n";
        if (runtimeErrors.Count > 0) text += "\n" + string.Join("\n", runtimeErrors);
        File.WriteAllText("Belge/A_PlayMode_Test_Raporu.md", text);
        File.WriteAllText("Temp/A_TekTus/test-status.txt", "DONE failures=" + failures);
        Debug.Log("A PlayMode verification finished. failures=" + failures);
    }

    static void Check(bool passed, string label)
    {
        results.Add("- " + (passed ? "PASS" : "FAIL") + " — " + label);
        if (!passed) failures++;
    }

    static void Key(bool down)
    {
        if (Keyboard.current == null) throw new InvalidOperationException("No keyboard device.");
        InputSystem.QueueStateEvent(Keyboard.current, down ? new KeyboardState(UnityEngine.InputSystem.Key.E) : new KeyboardState());
    }

    public static void Capture() => Shot("manual");
    static void Shot(string name) => ScreenCapture.CaptureScreenshot(Path.Combine(Folder, name + ".png"));

    static IEnumerator Run()
    {
        Shot("01-baslangic"); yield return .15f;
        Key(true); yield return .08f;
        Key(false); yield return .08f;
        Check(main.Phase == GamePhase.Playing, "Gerçek E olayı ve Update ile başlangıç");
        Key(true); yield return .08f;
        Check(main.Mode == DefenseMode.Guard, "Gerçek E key-down sırasında gard hemen açık");
        Key(false); yield return .04f;
        Check(main.Mode == DefenseMode.Parry, "Gerçek kısa E bas-bırak bir parry oluşturuyor");
        Shot("02-parry"); yield return .3f;
        Key(true); yield return .38f;
        Check(main.Mode == DefenseMode.Guard, "Gerçek uzun E basışı gardı sürdürüyor");
        Shot("03-gard"); yield return .08f; Key(false); yield return .08f;
        Check(main.Mode == DefenseMode.None, "Gerçek uzun gardı bırakmak parry üretmiyor");

        input.enabled = false;
        ModelChecks();
        FairnessChecks();

        // Visible five-laser overlap using the same runtime attack system.
        main.BeginRun(); main.Tick(0, false, false, true);
        for (int i=0; i<5; i++) main.Director.ScheduleLaser(i, 0, 1.1f, 1.0f+i*.28f, 2.1f-i);
        main.Tick(.4f, false, false, false);
        Shot("04-bes-lazer-uyari"); yield return .35f;
        main.Tick(.8f, true, true, false);
        yield return .15f; Shot("05-bes-lazer-gard"); yield return .35f;
        main.Tick(.1f, false, false, true);
        Check(main.Phase == GamePhase.Dying && main.LastHitWasLaser, "Ana sahnede lazer ölümü tetiklendi");
        for(int i=0;i<26;i++) { main.Tick(.1f,false,false,false); if(i==2) Shot("06-lazer-yanma"); if(i==12) Shot("07-lazer-kullesme"); yield return .1f; }
        Check(main.Phase == GamePhase.Lost, "Lazer ölüm animasyonunun ardından Lost durumu");
        Shot("08-lazer-sonuc"); yield return .15f;

        main.BeginRun(); main.Tick(0,false,false,true);
        main.Director.ScheduleMissile(0, .5f, main.Settings.HeroCenterY);
        main.Tick(.55f,true,true,false);
        Check(main.Phase==GamePhase.Dying && !main.LastHitWasLaser, "Ana sahnede garddaki karakterin füze ölümü tetiklendi");
        for(int i=0;i<18;i++) { main.Tick(.1f,false,false,false); if(i==1) Shot("09-fuze-patlama"); if(i==7) Shot("10-fuze-dagilma"); yield return .1f; }
        Check(main.Phase==GamePhase.Lost,"Füze ölüm animasyonunun ardından Lost durumu");

        main.BeginRun(); main.Tick(0,false,false,true);
        main.Director.ScheduleMissile(0,.5f,main.Settings.HeroCenterY);
        main.Tick(.38f,false,false,false); main.Tick(.04f,true,true,false); main.Tick(.09f,false,false,true);
        Check(main.ParryCount==1 && main.Missiles.Any(m=>m.IsReflected),"Ana sahnede füze parry ile yansıdı ve varlığını koruyor");
        for(int i=0;i<9;i++) { main.Tick(.08f,false,false,false); if(i==2) Shot("11-fuze-sapma"); yield return .08f; }

        main.BeginRun(); main.Tick(0,false,false,true); main.Tick(90,false,false,false);
        Check(main.Phase==GamePhase.Won,"Ana sahnede 90 saniye başarı durumu");
        Shot("12-basari"); yield return .4f;
    }

    static DefenseGame Fixture()
    {
        var game = new GameObject("QA temporary fixture").AddComponent<DefenseGame>();
        game.AutoSchedule=false; Reset(game); return game;
    }
    static void Reset(DefenseGame game) { game.BeginRun(); game.Tick(0,false,false,true); }
    static void Advance(DefenseGame game,float duration,bool held=false,float step=.01f)
    {
        float time=0;
        while(time < duration-0.000001f) { float dt=Mathf.Min(step,duration-time); game.Tick(dt,false,held,false); time+=dt; }
    }

    static void ModelChecks()
    {
        var game=Fixture();
        try
        {
            int parryTransitions=0;
            game.DefenseChanged += mode=> {if(mode==DefenseMode.Parry)parryTransitions++;};
            game.Tick(.07f,true,true,false);
            Check(game.Mode==DefenseMode.Guard,"Gard uzun basış eşiğini beklemiyor");
            game.Tick(.01f,false,false,true);
            Check(game.Mode==DefenseMode.Parry && parryTransitions==1,"Kısa bırakış yalnızca bir parry üretir");
            Advance(game,.3f);
            Check(game.Mode==DefenseMode.None && parryTransitions==1,"Parry penceresi kapanır; tekrarlanan parry yok");
            game.Tick(.01f,true,true,false); Advance(game,.5f,true); game.Tick(.01f,false,false,true);
            Check(game.Mode==DefenseMode.None && parryTransitions==1,"Uzun basış ve bırakış ilave parry üretmez");

            Reset(game); game.Director.ScheduleMissile(0,.4f,game.Settings.HeroCenterY);
            Advance(game,.3f);game.Tick(.03f,true,true,false);game.Tick(.01f,false,false,true);Advance(game,.1f);
            Check(game.Phase==GamePhase.Playing && game.ParryCount==1 && game.Missiles.Any(m=>m.IsReflected),"Füze aktif parry ile sapar; hemen silinmez");
            Advance(game,3f);
            Check(game.Phase==GamePhase.Playing && game.MissileCount==0,"Sapan füze zarar vermez ve ekran dışında temizlenir");

            Reset(game);game.Director.ScheduleMissile(0,.15f,game.Settings.HeroCenterY);game.Tick(.2f,true,true,false);
            Check(game.Phase==GamePhase.Dying && !game.LastHitWasLaser,"Füze gardı aşar ve tek isabette öldürür");
            float frozen=game.Elapsed;Advance(game,game.Settings.MissileDeathDuration+.1f,true);
            Check(game.Phase==GamePhase.Lost && game.Elapsed==frozen && game.MissileCount==0 && game.ActiveLaserCount==0,"Ölüm saldırıları ve sayacı durdurur; basılı E otomatik restart yapmaz");
            game.Tick(.01f,false,false,true);game.Tick(.01f,true,true,false);
            Check(game.Phase==GamePhase.Playing && game.ParryCount==0 && game.Elapsed==0,"Ölüm sonrası yeni E basışı temiz başlangıç sağlar");

            Reset(game);game.Director.ScheduleLaser(0,0,.1f,.5f,-.7f);game.Tick(.01f,true,true,false);Advance(game,.25f,true);
            Check(game.Phase==GamePhase.Playing && game.ActiveLaserCount==1,"Lazer gard ile engellenir");
            game.Tick(.01f,false,false,true);
            Check(game.Phase==GamePhase.Dying && game.LastHitWasLaser,"Aktif lazer bitmeden gardı bırakmak yanarak öldürür");

            Reset(game);game.Director.ScheduleLaser(0,0,.1f,.4f,-.7f);game.Tick(.04f,true,true,false);game.Tick(.01f,false,false,true);Advance(game,.1f);
            Check(game.Phase==GamePhase.Dying && game.LastHitWasLaser,"Aktif parry lazeri engellemez");

            Reset(game);
            for(int i=0;i<5;i++)game.Director.ScheduleLaser(i,0,.1f,.3f+i*.2f,-.7f+(i-2)*.8f);
            game.Tick(.15f,true,true,false);
            bool independent=game.ActiveLaserCount==5;
            Advance(game,.30f,true);independent &= game.ActiveLaserCount==4;
            for(int expected=3;expected>=0;expected--){Advance(game,.2f,true);independent &= game.ActiveLaserCount==expected;}
            Check(independent && game.Phase==GamePhase.Playing,"Beş lazer farklı sürelerde bağımsız biter (5→4→3→2→1→0)");
            Reset(game);for(int i=0;i<5;i++)game.Director.ScheduleLaser(i,0,.1f,.3f+i*.2f,0);
            game.Tick(.45f,true,true,false);game.Tick(.01f,false,false,true);
            Check(game.Phase==GamePhase.Dying,"İlk lazerin bitişi kalan dört lazerin hasarını kapatmıyor");

            Reset(game);for(int i=0;i<5;i++)game.Director.ScheduleLaser(i,0,.1f,.2f+i*.15f,0);
            game.Director.ScheduleMissile(.35f,1.36f,-.7f);
            game.Tick(.92f,true,true,false);game.Tick(.01f,false,false,true);
            Check(game.Mode==DefenseMode.None && game.ActiveLaserCount==0,"Son lazer sonrası uzun gard bırakışı savunmayı kapatır");
            Advance(game,.30f);game.Tick(.05f,true,true,false);game.Tick(.01f,false,false,true);Advance(game,.10f);
            Check(game.Phase==GamePhase.Playing && game.ParryCount==1,"Beş lazer → bırak → yeniden bas-bırak → füze parry geçişi yapılabilir");

            Reset(game);Advance(game,90f,false,.1f);
            Check(game.Phase==GamePhase.Won && game.Remaining==0,"Tam 90 saniye hedefi başarı üretir");
            for(int i=0;i<5;i++){game.Tick(.01f,false,false,true);game.Tick(.01f,true,true,false);game.Tick(0,false,false,true);game.Director.ScheduleMissile(0,.1f,-.7f);Advance(game,3f);}
            Check(game.Phase==GamePhase.Lost && game.ParryCount==0 && game.ActiveLaserCount==0,"Beş art arda yeniden başlatmada saldırı, puan ve savunma sıfırlanır");
            Reset(game);
            Check(game.Lasers.All(l=>l.StartTime==0 && l.EndTime==0 && l.ActiveDuration==0 && l.WarningDuration==0 && Mathf.Abs(l.OriginY-(2.1f-l.Slot))<.001f),"Yeniden başlatma lazer kaynak konumlarını ve tüm zamanlayıcı alanlarını sıfırlar");
        }
        finally {Object.DestroyImmediate(game.gameObject);}
    }

    static void FairnessChecks()
    {
        var game=Fixture();int runs=0;int maxLasers=0;float minimumGap=999;bool fair=true;int missileTotal=0;
        try
        {
            game.AutoSchedule=true;
            foreach(int fps in new[]{30,60,120})for(int seed=5731;seed<5741;seed++)
            {
                game.Settings.PatternSeed=seed;Reset(game);
                var plans=game.Director.PlannedAttacks.ToArray();
                var lasers=plans.Where(p=>p.Kind==AttackKind.Laser).ToArray();
                var missiles=plans.Where(p=>p.Kind==AttackKind.Missile).ToArray();
                foreach(var missile in missiles)
                {
                    foreach(var laser in lasers)
                    {
                        if(laser.StartTime<=missile.ImpactTime && laser.EndTime>missile.ImpactTime) fair=false;
                        if(laser.EndTime<=missile.ImpactTime)minimumGap=Mathf.Min(minimumGap,missile.ImpactTime-laser.EndTime);
                    }
                }
                foreach(var laser in lasers)
                {
                    maxLasers=Mathf.Max(maxLasers,lasers.Count(l=>l.StartTime<=laser.StartTime+.0001f && l.EndTime>laser.StartTime+.0001f));
                    if(laser.StartTime-laser.WarningTime < .3f-.0001f)fair=false;
                }
                bool previous=false;float dt=1f/fps;int safety=0;
                while(game.Phase==GamePhase.Playing && safety++<fps*92)
                {
                    float t=game.Elapsed;
                    bool held=lasers.Any(l=>t>=l.StartTime-.08f && t<l.EndTime+.025f) || missiles.Any(m=>t>=m.ImpactTime-.15f && t<m.ImpactTime-.075f);
                    game.Tick(dt,held&&!previous,held,!held&&previous);previous=held;
                }
                if(game.Phase!=GamePhase.Won || game.ParryCount!=missiles.Length)fair=false;
                missileTotal+=game.ParryCount;runs++;
            }
            Check(fair && maxLasers==5 && minimumGap>=.419f,"30/60/120 FPS × 10 örüntü tohumu: "+runs+" tam 90 saniyelik çözülebilir koşu; parry="+missileTotal+", en çok lazer="+maxLasers+", ölçülen en küçük lazer-füze aralığı="+minimumGap.ToString("F3")+" s");
        }
        finally {Object.DestroyImmediate(game.gameObject);}
    }
}
#endif
