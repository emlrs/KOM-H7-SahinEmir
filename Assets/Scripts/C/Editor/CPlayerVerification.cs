#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

namespace CollisionC.Editor
{
    /// <summary>Play Mode only. Real Input System events plus deterministic collider-side checks.</summary>
    public static class CPlayerVerification
    {
        public const string StatusPath="Temp/C_Carpisma/player_status.txt";
        private static readonly List<string> results=new List<string>(),errors=new List<string>();
        private static readonly List<GameObject> originalRoots=new List<GameObject>();
        private static readonly List<bool> originalStates=new List<bool>();
        private static IEnumerator sequence;
        private static double nextStep;
        private static int failures;
        private static Scene originalScene,testScene;
        private static CPlayerMotor player;
        public static bool IsRunning=>sequence!=null;
        public static int FailureCount=>failures;

        [MenuItem("Collision C/Verify player and real keyboard in Play Mode")]
        public static void Begin()
        {
            if(!EditorApplication.isPlaying)throw new InvalidOperationException("C player verification requires Play Mode.");
            if(sequence!=null)throw new InvalidOperationException("C player verification already running.");
            if(Keyboard.current==null)throw new InvalidOperationException("Input System keyboard required.");
            results.Clear();errors.Clear();originalRoots.Clear();originalStates.Clear();failures=0;
            originalScene=SceneManager.GetActiveScene();testScene=default;player=null;
            Directory.CreateDirectory("Belge");Directory.CreateDirectory("Temp/C_Carpisma");
            File.WriteAllText(StatusPath,"RUNNING");
            Application.logMessageReceived+=OnLog;
            sequence=Run();nextStep=0;EditorApplication.update+=Advance;
        }
        private static void Advance()
        {
            if(EditorApplication.timeSinceStartup<nextStep)return;
            try
            {
                if(!EditorApplication.isPlaying)throw new InvalidOperationException("Play Mode ended during test.");
                if(sequence.MoveNext())
                {nextStep=EditorApplication.timeSinceStartup+Convert.ToDouble(sequence.Current??0f,CultureInfo.InvariantCulture);return;}
                Finish();
            }
            catch(Exception e){Check(false,"Verification exception: "+e);Finish();}
        }
        private static void Finish()
        {
            EditorApplication.update-=Advance;(sequence as IDisposable)?.Dispose();sequence=null;
            Key();
            if(testScene.IsValid()&&testScene.isLoaded)
            {foreach(GameObject root in testScene.GetRootGameObjects())root.SetActive(false);SceneManager.UnloadSceneAsync(testScene);}
            if(originalScene.IsValid()&&originalScene.isLoaded)SceneManager.SetActiveScene(originalScene);
            for(int i=0;i<originalRoots.Count;i++)if(originalRoots[i])originalRoots[i].SetActive(originalStates[i]);
            Application.logMessageReceived-=OnLog;
            Check(errors.Count==0,"Runtime Error / Exception / Assert: "+errors.Count);
            string report="# C — Oyuncu, girdi ve kalkan doğrulaması\n\n"
                +"Çalıştırma: "+DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz")+"; Unity "+Application.unityVersion+".\n\n"
                +"Geçici, ayrı PhysicsScene2D içindeki C oyuncusu kullanılır. Gerçek Input System KeyboardState olayları normal Update metodunda toplanır; Step çağrıları sabit adım tüketimini belirler. Sonraki kontroller dış girdili deterministik adımlarla sınır sürelerini ve gerçek PolygonCollider2D.ClosestPoint konum sorgularını denetler. Bu test orb çarpışma/ölüm önceliği veya tam oyun koşusu kanıtı değildir; bunlar ayrı testlerde yürütülür.\n\n"
                +string.Join("\n",results)+"\n\nBaşarısız kontrol sayısı: "+failures+"\n";
            if(errors.Count>0)report+="\n"+string.Join("\n",errors);
            File.WriteAllText("Belge/C_Oyuncu_Test_Raporu.md",report);
            File.WriteAllText(StatusPath,"DONE failures="+failures);
            Debug.Log("C player verification finished. failures="+failures);
        }
        private static void OnLog(string message,string stack,LogType type)
        {if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message);}
        private static void Check(bool passed,string text)
        {results.Add("- "+(passed?"PASS":"FAIL")+" — "+text);if(!passed)failures++;}
        private static bool Near(float a,float b,float e=.001f)=>Mathf.Abs(a-b)<=e;
        private static void Key(params Key[] keys)=>InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState(keys));
        private static void Neutral()=>player.SetInput(new CInputFrame(0,false,false,false));
        private static void Reset(int facing=1)
        {
            player.ResetAt(new Vector2(0,-3.24f),facing);
            player.ManualSimulation=true;player.UseExternalInput=true;player.InputEnabled=true;
            Physics2D.SyncTransforms();
        }
        private static void Tap(float held=.10f)
        {
            player.SetInput(new CInputFrame(0,true,true,false));player.Step(held);
            player.SetInput(new CInputFrame(0,false,false,true));player.Step(.02f);
            Physics2D.SyncTransforms();
        }
        private static bool Contact(Vector2 relative,Vector2 incoming,float radius=.20f)
        =>player.CanParryAt(player.Position+relative,radius,incoming);

        private static IEnumerator Run()
        {
            foreach(GameObject root in originalScene.GetRootGameObjects())
            {originalRoots.Add(root);originalStates.Add(root.activeSelf);root.SetActive(false);}
            testScene=SceneManager.CreateScene("C Player Verification "+DateTime.UtcNow.Ticks,new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            SceneManager.SetActiveScene(testScene);
            player=CPlayerMotor.BuildPlayer(new Vector2(0,-3.24f));
            player.ManualSimulation=true;player.InputEnabled=true;player.UseExternalInput=false;
            player.Body.interpolation=RigidbodyInterpolation2D.None;
            Key();yield return .08f;
            Check(testScene.GetPhysicsScene2D().IsValid() && player.Body.bodyType==RigidbodyType2D.Kinematic,
                "Ayrı PhysicsScene2D ve kinematik oyuncu kuruldu");
            Check(player.BodyCollider is CapsuleCollider2D && !player.BodyCollider.isTrigger
                && player.ShieldCollider is PolygonCollider2D && !player.ShieldCollider.isTrigger,
                "Gövde kapsülü ve kalkan poligonu gerçek, trigger olmayan Collider2D bileşenleridir");
            Key(UnityEngine.InputSystem.Key.D);yield return .06f;player.Step(.02f);
            Check(player.Position.x>0 && player.Facing==1,"Gerçek D girdisi sağa taşır ve sağa döndürür");
            Key();yield return .05f;float x=player.Position.x;player.Step(.02f);
            Check(Near(player.Position.x,x)&&player.Facing==1,"D bırakılınca durur; son sağ yön korunur");
            Key(UnityEngine.InputSystem.Key.LeftArrow);yield return .05f;player.Step(.02f);
            Check(player.Position.x<x&&player.Facing==-1,"Gerçek sol ok sola taşır ve sola döndürür");
            Key();yield return .05f;player.Step(.02f);
            Check(player.Facing==-1,"Ok bırakılınca son sol yön korunur");
            Key(UnityEngine.InputSystem.Key.Space,UnityEngine.InputSystem.Key.W,UnityEngine.InputSystem.Key.UpArrow);
            yield return .05f;float y=player.Position.y;player.Step(.30f);
            Check(Near(player.Position.y,y)&&Near(player.Body.linearVelocity.y,0),"Space/W/yukarı ok zıplama üretmez; zemin yüksekliği sabittir");
            Key();yield return .04f;

            // Neither edge is consumed by a fixed step until both have arrived via Update.
            int serial=player.ParrySerial;
            Key(UnityEngine.InputSystem.Key.E);yield return .04f;
            Check(!player.ParryActive&&!player.ShieldCollider.enabled,"E basılması / basılı tutma kendi başına kalkan açmaz");
            Key();yield return .04f;
            Check(player.ParrySerial==serial,"Update kenarları Fixed Step gelene kadar savunma oluşturmaz");
            player.Step(.02f);
            Check(player.ParrySerial==serial+1&&Near(player.ParryRemaining,.20f)&&player.ShieldCollider.enabled,
                "İki Update kenarı aynı Step içinde kaybolmadan tek .20 sn kısa-bırakma parry üretir");
            player.View.TickVisual(0);
            Check(player.View.ShieldVisible&&player.View.AnimationState=="Parry","Aktif fiziksel kalkan ve açık mavi görsel aynı anda görünür");
            player.Step(.199f);Check(player.ParryActive,".199 sn sonra pencerenin son .001 saniyesi aktiftir");
            player.Step(.0011f);player.View.TickVisual(0);
            Check(!player.ParryActive&&!player.ShieldCollider.enabled&&!player.View.ShieldVisible,".20 sn tamamlanınca savunma ve çizimi kapanır");
            serial=player.ParrySerial;
            Key(UnityEngine.InputSystem.Key.E);yield return .31f;player.Step(.02f);
            Check(!player.ParryActive&&player.ParrySerial==serial,"Uzun gerçek E tutuşu bekçi/guard veya tekrar parry oluşturmaz");
            Key();yield return .05f;player.Step(.02f);
            Check(player.ParrySerial==serial&&!player.ParryActive,".22 sn üzerinde gerçek E bırakılması parry oluşturmaz");

            Reset();Tap(.22f);Check(player.ParryActive,"Tam .22 sn kısa basış kabul edilir");
            Reset();Tap(.2202f);Check(!player.ParryActive,".2202 sn basış kısa basış sınırını aşar");
            Reset();player.SetInput(new CInputFrame(0,true,true,false));player.Step(.03f);
            player.InputEnabled=false;player.InputEnabled=true;
            player.SetInput(new CInputFrame(0,false,false,true));player.Step(.02f);
            Check(!player.ParryActive,"Girdi kapatılması kuyruk/başlangıç tutuşunu temizler; eski bırakma parry olamaz");
            Reset();Tap();player.InputEnabled=false;
            Check(!player.ParryActive&&!player.ShieldCollider.enabled,"Girdi kapatılması açık parry penceresini anında siler");
            Reset();player.SetInput(new CInputFrame(-1,false,false,false));player.Step(4);
            Check(Near(player.Position.x,-9.4f)&&Near(player.Position.y,-3.24f),"Büyük adımda sol sınır ve sabit zemin korunur");
            player.SetInput(new CInputFrame(1,false,false,false));player.Step(4);
            Check(Near(player.Position.x,9.4f),"Büyük adımda sağ sınır korunur");

            Reset(1);Tap();
            Check(Contact(new Vector2(.83f,.10f),Vector2.left),"Sağdan yatay gerçek kalkan yakınlığı parry kabul eder");
            Check(Contact(new Vector2(.52f,.83f),new Vector2(-1,-1).normalized),"Sağ üst çapraz temas sağ kalkanla kabul edilir");
            Check(!Contact(new Vector2(-.52f,.83f),new Vector2(1,-1).normalized),"Sol üst çapraz temas sağ kalkanla kabul edilmez");
            Check(!Contact(new Vector2(-.83f,.1f),Vector2.right),"Yanlış taraftaki yatay temas kabul edilmez");
            Check(Contact(new Vector2(0,1.06f),Vector2.down),"Tam tepeden temas görünür taç kapsamı ve mevcut yöne göre kabul edilir");
            Check(Contact(new Vector2(-.079f,1.02f),Vector2.down),"Taçta .079 birim merkez toleransı kabul edilir");
            Check(!Contact(new Vector2(-.081f,1.02f),Vector2.down),"Taçta .081 birim yanlış taraf toleransı aşar");
            Check(!Contact(new Vector2(1.8f,.1f),Vector2.left),"Uzak sağ top sırf yön doğru diye parry olamaz");
            Check(!Contact(new Vector2(.83f,.1f),Vector2.right),"Kalkandan uzaklaşan top yeniden parry olamaz");
            Check(!Contact(new Vector2(.3f,-1.1f),Vector2.up),"Alt / kalkan dışında kalan temas korunmaz");
            Reset(-1);Tap();
            Check(Contact(new Vector2(-.83f,.1f),Vector2.right),"Sol yönde gerçek poligon aynalanır ve soldan yatay temas kabul edilir");
            Check(Contact(new Vector2(-.52f,.83f),new Vector2(1,-1).normalized),"Sol üst çapraz temas sol kalkanla kabul edilir");
            Check(!Contact(new Vector2(.52f,.83f),new Vector2(-1,-1).normalized),"Sağ üst çapraz temas sol kalkanla kabul edilmez");
            Check(Contact(new Vector2(0,1.06f),Vector2.down),"Sol yönün taç kapsamı tam tepe temasını kabul eder");
            int died=0;player.Died+=()=>died++;player.Kill();player.Kill();player.View.TickVisual(.35f);
            Check(died==1&&!player.Alive&&!player.ShieldCollider.enabled&&!player.Body.simulated,
                "Kill tek olay üretir; fizik, girdi ve kalkan kapanır");
            Check(player.View.AnimationState=="Death fragments","Ölümde düzenlenebilir karakter parçaları dağılır");
            Reset();Neutral();player.View.TickVisual(0);
            Check(player.Alive&&player.Body.simulated&&player.View.AnimationState=="Idle"&&!player.View.ShieldVisible,
                "ResetAt karakteri, parçaları ve savunma durumunu geri yükler");
            foreach(SpriteRenderer renderer in player.GetComponentsInChildren<SpriteRenderer>(true))
            {
                string asset=AssetDatabase.GetAssetPath(renderer.sprite),material=AssetDatabase.GetAssetPath(renderer.sharedMaterial);
                Check(asset.StartsWith("Assets/Art/C/",StringComparison.Ordinal)&&material.StartsWith("Assets/Art/C/",StringComparison.Ordinal),
                    "Kalıcı C çizim varlıkları: "+renderer.name);
            }
            Key();yield return .04f;
        }
    }
}
#endif
