#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

namespace LavaRiseB
{
    /// <summary>Isolated state-machine checks. Placement is deliberate setup, never a full-route claim.</summary>
    public static class BFlowVerification
    {
        public static readonly string StatusPath=Path.GetFullPath("Temp/B_HareketHissi/flow-status.txt");
        static readonly string Evidence=Path.GetFullPath("Belge/B_TestKaniti");
        static readonly List<string> Results=new List<string>();
        static readonly List<string> Errors=new List<string>();
        static readonly List<GameObject> OriginalRoots=new List<GameObject>();
        static readonly List<bool> OriginalStates=new List<bool>();
        static IEnumerator sequence;
        static double nextStep;
        static int failures;
        static Scene originalScene,testScene;
        static PhysicsScene2D physics;
        static SimulationMode2D previousSimulation;
        static bool simulationSaved;
        static ulong pendingSceneHandle=0;
        static BGameController game;
        static BPlayerMotor player;
        static BCameraFollow follow;
        public static bool IsRunning => sequence!=null;
        public static int FailureCount => failures;

        [MenuItem("Lava Rise B/Verify isolated game flow in Play Mode")]
        public static void Begin()
        {
            if(!EditorApplication.isPlaying)throw new InvalidOperationException("B flow verification requires Play Mode.");
            if(sequence!=null)throw new InvalidOperationException("B flow verification is already running.");
            if(pendingSceneHandle!=0)throw new InvalidOperationException("The previous temporary flow scene is still loading for cleanup.");
            if(Keyboard.current==null)throw new InvalidOperationException("Input System keyboard is required.");
            Results.Clear();Errors.Clear();OriginalRoots.Clear();OriginalStates.Clear();failures=0;
            game=null;player=null;follow=null;testScene=default;simulationSaved=false;
            Directory.CreateDirectory(Path.GetDirectoryName(StatusPath));Directory.CreateDirectory(Evidence);
            foreach(string file in new[]{"flow_instruction.png","flow_death.png","flow_lost.png"})
            {
                string existing=Path.Combine(Evidence,file);if(File.Exists(existing))File.Delete(existing);
            }
            File.WriteAllText(StatusPath,"RUNNING");
            Application.logMessageReceived+=OnLog;
            sequence=Run();nextStep=0;EditorApplication.update+=Advance;
        }

        static void Advance()
        {
            if(EditorApplication.timeSinceStartup<nextStep)return;
            try
            {
                if(!EditorApplication.isPlaying)throw new InvalidOperationException("Play Mode ended during flow verification.");
                if(sequence.MoveNext())
                {
                    nextStep=EditorApplication.timeSinceStartup+Convert.ToDouble(sequence.Current??0f,CultureInfo.InvariantCulture);
                    return;
                }
                Finish();
            }
            catch(Exception exception){Check(false,"Verification exception: "+exception);Finish();}
        }

        static void Finish()
        {
            EditorApplication.update-=Advance;
            (sequence as IDisposable)?.Dispose();sequence=null;
            Key();
            if(simulationSaved)Physics2D.simulationMode=previousSimulation;
            if(testScene.IsValid() && testScene.isLoaded)
            {
                foreach(var root in testScene.GetRootGameObjects())root.SetActive(false);
                SceneManager.UnloadSceneAsync(testScene);
            }
            else if(testScene.IsValid())
            {
                // Loading can finish after an exception/timeout. Dispose that exact scene when it arrives.
                pendingSceneHandle=testScene.handle.GetRawData();
                SceneManager.sceneLoaded-=CleanupPendingScene;
                SceneManager.sceneLoaded+=CleanupPendingScene;
                EditorApplication.playModeStateChanged-=CleanupAfterPlayMode;
                EditorApplication.playModeStateChanged+=CleanupAfterPlayMode;
            }
            if(originalScene.IsValid() && originalScene.isLoaded)SceneManager.SetActiveScene(originalScene);
            for(int i=0;i<OriginalRoots.Count;i++)if(OriginalRoots[i])OriginalRoots[i].SetActive(OriginalStates[i]);
            Application.logMessageReceived-=OnLog;
            Check(Errors.Count==0,"Runtime Error / Exception / Assert: "+Errors.Count);
            string report="# B — İzole oyun akışı doğrulaması\n\n"
                +"Çalıştırma: "+DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz")+"; Unity "+Application.unityVersion+".\n\n"
                +"Kaydedilmiş B sahnesinin geçici additive kopyası, ayrı gerçek PhysicsScene2D içinde açıldı. Asıl sahnenin kök nesneleri test boyunca devre dışı tutulup önceki etkinlik durumlarına döndürüldü. Global Physics2D simülasyon modu geri yüklendi; test sahnesi kapatıldı. Space/R kontrolleri Input System KeyboardState olaylarından gerçek Update yoluyla geçti. Zaman, lav ve ölüm kontrollerinin belirli bölümleri TickRun(dt) ile deterministik yürütüldü.\n\n"
                +"**Kapsam sınırı:** Bu akış testinde ölüm, valf ve çıkış durumlarını ayrı ayrı kurmak için Rigidbody2D konumu değiştirildi. Bunlar tam parkur koşusu veya erişilebilirlik kanıtı değildir. Tam rota raporu gerçek girdiler ve gerçek fizik üzerinden ayrıca üretilir.\n\n"
                +string.Join("\n",Results)+"\n\nBaşarısız kontrol sayısı: "+failures+"\n\n"
                +"Kanıt görselleri: `B_TestKaniti/flow_instruction.png`, `B_TestKaniti/flow_death.png`, `B_TestKaniti/flow_lost.png`.\n";
            if(Errors.Count>0)report+="\n"+string.Join("\n",Errors);
            File.WriteAllText("Belge/B_Akis_Test_Raporu.md",report);
            File.WriteAllText(StatusPath,"DONE failures="+failures);
            Debug.Log("B flow verification finished. failures="+failures);
        }

        static void CleanupPendingScene(Scene loaded,LoadSceneMode mode)
        {
            if(loaded.handle.GetRawData()!=pendingSceneHandle)return;
            foreach(var root in loaded.GetRootGameObjects())root.SetActive(false);
            if(originalScene.IsValid() && originalScene.isLoaded)SceneManager.SetActiveScene(originalScene);
            SceneManager.UnloadSceneAsync(loaded);
            ClearPendingCleanup();
        }
        static void CleanupAfterPlayMode(PlayModeStateChange state)
        {if(state==PlayModeStateChange.EnteredEditMode)ClearPendingCleanup();}
        static void ClearPendingCleanup()
        {
            pendingSceneHandle=0;
            SceneManager.sceneLoaded-=CleanupPendingScene;
            EditorApplication.playModeStateChanged-=CleanupAfterPlayMode;
        }

        static void OnLog(string message,string stack,LogType type)
        {if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)Errors.Add(message);}
        static void Check(bool passed,string text)
        {Results.Add("- "+(passed?"PASS":"FAIL")+" — "+text);if(!passed)failures++;}
        static string F(float n)=>n.ToString("0.000",CultureInfo.InvariantCulture);
        static bool Near(float a,float b,float tolerance=.001f)=>Mathf.Abs(a-b)<=tolerance;
        static void Key(params Key[] keys)
        {if(Keyboard.current!=null)InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState(keys));}
        static void Position(Vector2 p,int profile=0)
        {
            player.ResetAt(p,profile);player.UseExternalInput=true;player.ManualSimulation=true;
            player.Body.interpolation=RigidbodyInterpolation2D.None;Physics2D.SyncTransforms();
        }
        static void PlaceOnPlatform(int id,int profile=0)
        {Position(BCourseDefinition.Platform(id).StandingPosition,profile);}
        static void Tick(float dt) {game.TickRun(dt);}
        static void PhysicalStep()
        {player.ManualFixedStep(.02f);physics.Simulate(.02f);}
        static void Capture(string file)
        {ScreenCapture.CaptureScreenshot(Path.Combine(Evidence,file));}
        static bool AnyExplosionActive()
        {
            if(!game.Environment || game.Environment.ExplosionFragments==null)return false;
            foreach(var t in game.Environment.ExplosionFragments)if(t && t.gameObject.activeSelf)return true;
            return false;
        }

        static void CheckReset(string prefix,int serialBefore)
        {
            Check(game.Phase==BGamePhase.Playing && game.RunSerial==serialBefore+1,prefix+" yeni R basışı yeni koşu açar");
            Check(player.IsAlive && player.Body.simulated && player.InputEnabled,prefix+" canlı, fizik ve girdi açık");
            Check(Vector2.Distance(player.Body.position,BCourseDefinition.StartPosition)<.04f && player.Velocity.sqrMagnitude<.001f,
                prefix+" başlangıç gövde konumu ve hız sıfırlanır");
            Check(player.ProfileIndex==0 && player.JumpCount==0 && game.RelayMask==0,prefix+" ilk profil, sıçrayış ve valfler sıfırlanır");
            Check(game.Elapsed>=0 && game.Elapsed<.45f && Near(game.LavaY,BCourseDefinition.LavaStartY+game.FixedLavaSpeed*game.Elapsed,.002f),
                prefix+" süre/lav yeni koşu başına döner; elapsed="+F(game.Elapsed));
            Check(Near(game.DeathElapsed,0) && !AnyExplosionActive(),prefix+" ölüm sayacı ve patlama parçaları temizlenir");
            Check(game.Environment.RelayGates[0].activeSelf && game.Environment.RelayGates[1].activeSelf,prefix+" iki fiziksel kapı tekrar kapanır");
            Vector3 expected=new Vector3(BCourseDefinition.StartPosition.x,BCourseDefinition.StartPosition.y+follow.VerticalOffset,-10);
            Check(Vector3.Distance(follow.transform.position,expected)<.04f,prefix+" kamera başlangıca sıçrar");
        }

        public static IEnumerator Run()
        {
            originalScene=SceneManager.GetActiveScene();
            previousSimulation=Physics2D.simulationMode;simulationSaved=true;
            foreach(var root in originalScene.GetRootGameObjects())
            {OriginalRoots.Add(root);OriginalStates.Add(root.activeSelf);root.SetActive(false);}
            testScene=EditorSceneManager.LoadSceneInPlayMode(BSceneBuilder.ScenePath,new LoadSceneParameters(LoadSceneMode.Additive,LocalPhysicsMode.Physics2D));
            double loadDeadline=EditorApplication.timeSinceStartup+15.0;
            while(testScene.IsValid() && !testScene.isLoaded && EditorApplication.timeSinceStartup<loadDeadline)yield return .05f;
            if(!testScene.IsValid() || !testScene.isLoaded)throw new TimeoutException("Temporary B flow scene did not finish loading within 15 seconds.");
            SceneManager.SetActiveScene(testScene);physics=testScene.GetPhysicsScene2D();
            foreach(var root in testScene.GetRootGameObjects())
            {game=root.GetComponentInChildren<BGameController>(true);if(game)break;}
            if(!game || !game.Player || !game.Environment || !game.FollowCamera)throw new InvalidOperationException("Saved B scene is missing its flow references.");
            player=game.Player;follow=game.FollowCamera;
            Physics2D.simulationMode=SimulationMode2D.Script;
            game.ManualSimulation=true;player.ManualSimulation=true;follow.ManualSimulation=true;
            player.Body.interpolation=RigidbodyInterpolation2D.None;
            Key();yield return .15f;
            Check(physics.IsValid() && testScene.handle!=originalScene.handle,"Ayrı B kopyası ve gerçek yerel PhysicsScene2D kuruldu");
            Check(game.Phase==BGamePhase.Instruction && !player.InputEnabled,"Bilgi ekranı ilk durumda görünür; hareket girdisi kapalı");
            Tick(12f);
            Check(Near(game.Elapsed,0) && Near(game.LavaY,BCourseDefinition.LavaStartY),"Bilgi ekranında 12 sn TickRun süreyi veya lavı ilerletmez");
            game.ManualSimulation=false;yield return .2f;
            Check(Near(game.Elapsed,0) && Near(game.LavaY,BCourseDefinition.LavaStartY),"Gerçek Update beklemesinde de bilgi ekranı zamanı/lavı dondurur");
            Capture("flow_instruction.png");yield return .25f;

            // Real Input System events, processed through the ordinary Update methods.
            player.UseExternalInput=false;
            Key(UnityEngine.InputSystem.Key.Space);yield return .20f;
            for(int i=0;i<12;i++)PhysicalStep();
            Check(game.Phase==BGamePhase.Playing && game.StartJumpConsumed && !player.InputEnabled,"Gerçek Space başlatır ve ilk basılı Space girdisini tüketir");
            Check(player.JumpCount==0,"Başlatan Space basılı kalınca karakter zıplamaz");
            Key();yield return .10f;
            Check(!game.StartJumpConsumed && player.InputEnabled,"Space bırakılınca hareket/sıçrayış yeniden açılır");
            Key(UnityEngine.InputSystem.Key.Space);yield return .10f;PhysicalStep();
            Check(player.JumpCount==1 && player.Velocity.y>0,"Sonraki yeni Space basışı gerçek motor sıçrayışını üretir");
            Key();yield return .08f;
            game.ManualSimulation=true;player.UseExternalInput=true;

            float[] lavaTravel=new float[2];
            for(int profile=0;profile<2;profile++)
            {
                game.RestartRun();Position(profile==0?new Vector2(0,52):new Vector2(-21,26),profile);
                float initial=game.LavaY;Tick(10f);lavaTravel[profile]=game.LavaY-initial;
                Check(Near(game.Elapsed,10f) && Near(lavaTravel[profile],game.FixedLavaSpeed*10f,.002f),
                    "Profil "+profile+" / farklı yükseklik: 10 sn sabit lav artışı="+F(lavaTravel[profile]));
            }
            Check(Near(lavaTravel[0],lavaTravel[1],.001f),"Lav hızı profil veya oyuncu konumuna bağlı değişmez");
            game.RestartRun();Position(new Vector2(0,52));Tick(90f);
            Check(game.Phase==BGamePhase.Playing && Near(game.Elapsed,90f),"90 saniye geçmesi tek başına kazanım üretmez");

            game.RestartRun();
            BPlatformSpec exit=BCourseDefinition.Platform(32);
            Vector2 exitPosition=new Vector2(exit.CenterX+.65f,exit.TopY+.73f);
            Position(exitPosition);
            Check(!game.TryExit() && game.Phase==BGamePhase.Playing,"Fiziksel çıkışta valfler kapalıyken kazanılamaz");
            PlaceOnPlatform(9);Check(game.ActivateRelay(0) && game.RelayMask==1,"Alt valf 01 gerçek gövde/sensör alanında açılır");
            Position(exitPosition);
            Check(!game.TryExit() && game.Phase==BGamePhase.Playing,"Fiziksel çıkışta yalnız bir valf yeterli değildir");
            PlaceOnPlatform(23);Check(game.ActivateRelay(1) && game.RelayMask==3,"Alt valf 02 gerçek gövde/sensör alanında açılır");
            Position(new Vector2(0,52));
            Check(!game.TryExit() && game.Phase==BGamePhase.Playing,"İki valf açık olsa da üst kapıdan uzakta kazanılamaz");
            Position(exitPosition);
            Check(game.TryExit() && game.Phase==BGamePhase.Won && !player.Body.simulated && player.Velocity.sqrMagnitude<.001f,
                "İki valf + gerçek fiziksel çıkış bölgesi kazanır ve gövdeyi dondurur");
            for(int i=0;i<8;i++)player.ManualFixedStep(.02f);
            Check(game.Phase==BGamePhase.Won && !player.Body.simulated && player.Velocity.sqrMagnitude<.001f,
                "Kazanımdan sonraki 8 gerçek motor adımında donmuş gövdenin hızı sıfır kalır");

            int winSerial=game.RunSerial;
            game.ManualSimulation=false;Key();yield return .08f;
            Key(UnityEngine.InputSystem.Key.R);yield return .10f;
            CheckReset("Başarı ekranından dönüş:",winSerial);
            Key();yield return .08f;
            game.ManualSimulation=true;

            game.RestartRun();Position(new Vector2(0,30));follow.Snap();float highCamera=follow.transform.position.y;
            Position(new Vector2(0,20));for(int i=0;i<80;i++)follow.TickFollow(.02f);
            Check(follow.transform.position.y<highCamera-8f && Mathf.Abs(follow.transform.position.y-(20+follow.VerticalOffset))<.2f,
                "Kamera 10 m inişte oyuncuyu aşağı takip eder; yukarı kilidi yoktur");

            game.RestartRun();Position(new Vector2(0,30),1);Tick(3f);
            PlaceOnPlatform(9,1);game.ActivateRelay(0);PlaceOnPlatform(23,1);game.ActivateRelay(1);
            Position(new Vector2(-14.5f,game.LavaY+BCourseDefinition.FeetOffset-.02f),1);follow.Snap();
            Tick(.02f);
            Check(game.Phase==BGamePhase.Dying && !player.IsAlive && !player.InputEnabled && !player.Body.simulated && player.Velocity.sqrMagnitude<.001f,
                "Gerçek lav temas konumu aynı TickRun adımında öldürür, hareketi/gövdeyi durdurur");
            Check(AnyExplosionActive(),"Ölüm anında patlama parçaları görünür hale gelir");
            float elapsedAtDeath=game.Elapsed,lavaAtDeath=game.LavaY;
            Tick(.45f);Check(game.Phase==BGamePhase.Dying,"0.45 sn sonra ölüm animasyonu sürer; sonuç ekranı erken açılmaz");
            yield return .35f;
            Check(player.GetComponentInChildren<BCharacterView>().AnimationState=="Death fragments",
                "Gerçek LateUpdate içinde karakter parçalanma animasyonu oynar");
            Capture("flow_death.png");yield return .25f;
            int serialBefore=game.RunSerial;
            game.ManualSimulation=false;Key(UnityEngine.InputSystem.Key.R);yield return .13f;
            Check(game.Phase==BGamePhase.Dying && game.RunSerial==serialBefore,"Ölüm animasyonu sırasında gerçek R basışı yeniden başlatamaz");
            game.ManualSimulation=true;
            Tick(Mathf.Max(0,1.49f-game.DeathElapsed));
            Check(game.Phase==BGamePhase.Dying && game.DeathElapsed<1.5f,"Ölüm gecikmesi 1.49 sn iken hâlâ Dying");
            Tick(.021f);
            Check(game.Phase==BGamePhase.Lost && game.DeathElapsed>=1.5f,"1.5 sn tamamlandıktan sonra Lost ekranı açılır");
            Check(Near(game.Elapsed,elapsedAtDeath) && Near(game.LavaY,lavaAtDeath),"Ölüm sırasında koşu süresi ve lav donmuş kalır");
            game.ManualSimulation=false;yield return .12f;
            Check(game.Phase==BGamePhase.Lost && game.RunSerial==serialBefore,"Dying sırasında basılı kalan R, Lost geçişinde otomatik yeniden başlatmaz");
            Capture("flow_lost.png");yield return .25f;
            Key();yield return .08f;
            Key(UnityEngine.InputSystem.Key.R);yield return .10f;
            CheckReset("İlk sonuçtan dönüş:",serialBefore);
            Key();yield return .08f;

            for(int cycle=1;cycle<=3;cycle++)
            {
                game.ManualSimulation=true;
                Position(new Vector2(-14.5f,game.LavaY+BCourseDefinition.FeetOffset-.02f),1);Tick(.02f);Tick(1.51f);
                Check(game.Phase==BGamePhase.Lost,"Tekrar "+cycle+": temas → Dying → Lost sırası tamamlanır");
                serialBefore=game.RunSerial;
                game.ManualSimulation=false;Key();yield return .05f;
                Key(UnityEngine.InputSystem.Key.R);yield return .10f;
                CheckReset("Tekrar "+cycle+":",serialBefore);
                Key();yield return .07f;
            }
            foreach(string file in new[]{"flow_instruction.png","flow_death.png","flow_lost.png"})
            {
                string path=Path.Combine(Evidence,file);
                Check(File.Exists(path) && new FileInfo(path).Length>1000,"ScreenCapture kanıtı kaydedildi: "+file);
            }
        }
    }
}
#endif
