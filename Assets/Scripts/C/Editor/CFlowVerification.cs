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

namespace CollisionC
{
    /// <summary>Isolated saved-scene flow checks. Deliberate fixture placement is not a full-run claim.</summary>
    public static class CFlowVerification
    {
        public const string StatusPath = "Temp/C_Carpisma/flow-status.txt";
        public const string ReportPath = "Belge/C_Akis_Test_Raporu.md";
        private static readonly List<string> results = new List<string>();
        private static readonly List<string> errors = new List<string>();
        private static readonly List<GameObject> originalRoots = new List<GameObject>();
        private static readonly List<bool> originalStates = new List<bool>();
        private static readonly Dictionary<CPlate, Color> plateColors = new Dictionary<CPlate, Color>();
        private static IEnumerator sequence;
        private static double nextStep;
        private static int failures;
        private static Scene originalScene, testScene;
        private static PhysicsScene2D physics;
        private static SimulationMode2D previousSimulation;
        private static bool simulationSaved, previousRunInBackground;
        private static ulong pendingSceneHandle;
        private static CGameController game;
        private static CPlayerMotor player;
        private static COrbMotor orb;
        private static CPlate normal, armored;
        public static bool IsRunning => sequence != null;
        public static int FailureCount => failures;
        public static int CheckCount => results.Count;

        [MenuItem("Collision C/Verify saved-scene flow in Play Mode")]
        public static void Begin()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("C flow verification requires Play Mode.");
            if (sequence != null || pendingSceneHandle != 0) throw new InvalidOperationException("C flow verification or its scene cleanup is already running.");
            if (Keyboard.current == null) throw new InvalidOperationException("Input System keyboard is required.");
            results.Clear(); errors.Clear(); originalRoots.Clear(); originalStates.Clear(); plateColors.Clear(); failures = 0;
            originalScene = SceneManager.GetActiveScene(); testScene = default;
            game = null; player = null; orb = null; normal = null; armored = null; simulationSaved = false;
            previousRunInBackground = Application.runInBackground;
            Application.runInBackground = true;
            Directory.CreateDirectory("Temp/C_Carpisma"); Directory.CreateDirectory("Belge");
            File.WriteAllText(StatusPath, "RUNNING checks=0 failures=0");
            Application.logMessageReceived += OnLog;
            sequence = Run(); nextStep = 0;
            EditorApplication.update += Advance;
        }

        private static void Advance()
        {
            if (EditorApplication.timeSinceStartup < nextStep) return;
            try
            {
                if (!EditorApplication.isPlaying) throw new InvalidOperationException("Play Mode ended during C flow verification.");
                if (sequence.MoveNext())
                {
                    nextStep = EditorApplication.timeSinceStartup + Convert.ToDouble(sequence.Current ?? 0f, CultureInfo.InvariantCulture);
                    return;
                }
                Finish();
            }
            catch (Exception error) { Check(false, "Verification exception: " + error); Finish(); }
        }

        private static void Finish()
        {
            EditorApplication.update -= Advance;
            (sequence as IDisposable)?.Dispose(); sequence = null;
            Key();
            if (testScene.IsValid() && testScene.isLoaded)
            {
                foreach (GameObject root in testScene.GetRootGameObjects()) root.SetActive(false);
                if (originalScene.IsValid() && originalScene.isLoaded) SceneManager.SetActiveScene(originalScene);
                SceneManager.UnloadSceneAsync(testScene);
            }
            else if (testScene.IsValid())
            {
                pendingSceneHandle = testScene.handle.GetRawData();
                SceneManager.sceneLoaded -= CleanupPendingScene;
                SceneManager.sceneLoaded += CleanupPendingScene;
                EditorApplication.playModeStateChanged -= CleanupAfterPlayMode;
                EditorApplication.playModeStateChanged += CleanupAfterPlayMode;
            }
            if (originalScene.IsValid() && originalScene.isLoaded) SceneManager.SetActiveScene(originalScene);
            if (simulationSaved) Physics2D.simulationMode = previousSimulation;
            for (int i = 0; i < originalRoots.Count; i++) if (originalRoots[i]) originalRoots[i].SetActive(originalStates[i]);
            Application.runInBackground = previousRunInBackground;
            Application.logMessageReceived -= OnLog;
            Check(errors.Count == 0, "Runtime Error / Exception / Assert: " + errors.Count);
            string report = "# C — İzole oyun akışı doğrulaması\n\n"
                + "Çalıştırma: " + DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz") + "; Unity " + Application.unityVersion + ".\n\n"
                + "Kaydedilmiş C_Carpisma sahnesi additive ve ayrı PhysicsScene2D içinde açılır; isLoaded beklenir. Önceki sahnelerin etkinlik durumları, global Physics2D simülasyon modu ve runInBackground ayarı geri yüklenir. E/R kontrolleri Input System KeyboardState olaylarından normal Update yoluyla geçer. Sayaç ve ölüm süresi Tick(dt), oyuncu ve top Step(dt), gerçek trigger olayları PhysicsScene2D.Simulate ile yürütülür.\n\n"
                + "**Kapsam sınırı:** Bu test tam oyun koşusu değildir. Bağımsız sınır koşulları için oyuncu/top Rigidbody2D konumları bilinçli olarak yerleştirilir; 45 saniye deterministik olarak ilerletilir. Şarj hiçbir setter/reflection ile verilmez: her şarj gerçek kalkan temasında COrbMotor.Step ile kazanılır. Alıcı testleri gerçek sweep girişini ve native trigger callback yolunu ayrı ayrı denetler.\n\n"
                + string.Join("\n", results) + "\n\nKontrol sayısı: " + results.Count + "\n\nBaşarısız kontrol sayısı: " + failures + "\n";
            if (errors.Count != 0) report += "\n" + string.Join("\n", errors);
            File.WriteAllText(ReportPath, report);
            File.WriteAllText(StatusPath, "DONE checks=" + results.Count + " failures=" + failures);
            Debug.Log("C flow verification finished. checks=" + results.Count + " failures=" + failures);
        }

        private static void CleanupPendingScene(Scene loaded, LoadSceneMode mode)
        {
            if (loaded.handle.GetRawData() != pendingSceneHandle) return;
            foreach (GameObject root in loaded.GetRootGameObjects()) root.SetActive(false);
            if (originalScene.IsValid() && originalScene.isLoaded) SceneManager.SetActiveScene(originalScene);
            SceneManager.UnloadSceneAsync(loaded);
            ClearPendingCleanup();
        }
        private static void CleanupAfterPlayMode(PlayModeStateChange state)
        { if (state == PlayModeStateChange.EnteredEditMode) ClearPendingCleanup(); }
        private static void ClearPendingCleanup()
        {
            pendingSceneHandle = 0;
            SceneManager.sceneLoaded -= CleanupPendingScene;
            EditorApplication.playModeStateChanged -= CleanupAfterPlayMode;
        }
        private static void OnLog(string message, string stack, LogType type)
        { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); }
        private static void Check(bool passed, string description)
        {
            results.Add("- " + (passed ? "PASS" : "FAIL") + " — " + description);
            if (!passed) failures++;
            File.WriteAllText(StatusPath, "RUNNING checks=" + results.Count + " failures=" + failures);
        }
        private static void Require(bool passed, string description)
        { if (!passed) throw new InvalidOperationException(description); }
        private static bool Near(float first, float second, float tolerance = .001f) => Mathf.Abs(first - second) <= tolerance;
        private static bool Near(Vector2 first, Vector2 second, float tolerance = .001f) => Vector2.Distance(first, second) <= tolerance;
        private static void Key(params Key[] keys)
        { if (Keyboard.current != null) InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(keys)); }
        private static void PhysicalStep()
        { Physics2D.SyncTransforms(); Require(physics.Simulate(.02f), "Local PhysicsScene2D.Simulate failed."); }
        private static void PositionOrb(Vector2 position)
        {
            orb.Body.position = position; orb.transform.position = position;
            Physics2D.SyncTransforms();
        }
        private static void PositionPlayer(Vector2 position)
        {
            player.Body.position = position; player.transform.position = position;
            Physics2D.SyncTransforms();
        }
        private static void FixtureStart()
        {
            game.BeginRun();
            player.UseExternalInput = true;
            game.ManualSimulation = player.ManualSimulation = orb.ManualSimulation = true;
            player.Body.interpolation = orb.Body.interpolation = RigidbodyInterpolation2D.None;
        }
        private static void EarnParry(string label)
        {
            player.ResetAt(CGameController.PlayerStart, 1); player.UseExternalInput = true;
            player.SetInput(new CInputFrame(0, true, true, false)); player.Step(.06f);
            player.SetInput(new CInputFrame(0, false, false, true)); player.Step(.02f);
            Require(player.ParryActive && player.ShieldCollider.enabled, label + ": short-release fixture failed.");
            int previous = game.ParryCount;
            orb.SetRandomSeed(71591);
            orb.ResetOrb(player.Position + new Vector2(1.2f, .15f), Vector2.left);
            orb.Step(.10f);
            Require(player.Alive && orb.ParryCount == 1 && game.ParryCount == previous + 1,
                label + ": real shield sweep did not produce exactly one successful parry.");
            Check(true, label + ": gerçek kalkan poligonu / top sweep teması tek başarılı parry üretir");
        }
        private static void BreakAllPlates(int tokenBase)
        {
            for (int i = 0; i < game.Plates.Length; i++)
            {
                CPlate plate = game.Plates[i];
                plate.ApplyImpact(tokenBase + i * 2);
                if (!plate.Broken) plate.ApplyImpact(tokenBase + i * 2 + 1);
            }
        }
        private static bool AllPlatesRestored()
        {
            foreach (CPlate plate in game.Plates)
                if (!plate || plate.Broken || plate.RemainingHits != plate.MaximumHits || !plate.Solid.enabled || plate.Solid.isTrigger
                    || !plate.Face.enabled || !plate.Frame.enabled || plate.Crack.enabled
                    || !plateColors.TryGetValue(plate, out Color color) || plate.Face.color != color) return false;
            return true;
        }
        private static bool EffectsCleared()
        {
            foreach (SpriteRenderer renderer in game.Environment.DeathSparks) if (renderer && renderer.enabled) return false;
            foreach (SpriteRenderer renderer in game.Environment.ImpactRings) if (renderer && renderer.enabled) return false;
            return true;
        }
        private static void CheckReset(string label, int previousSerial)
        {
            Check(game.Phase == CGamePhase.Playing && game.RunSerial == previousSerial + 1, label + ": yeni R kenarı tam bir yeni koşu açar");
            Check(player.Alive && player.InputEnabled && player.Body.simulated && player.BodyCollider.enabled,
                label + ": oyuncu canlı, hareket ve fizik yeniden açık");
            Check(Near(player.Position, CGameController.PlayerStart) && player.Facing == 1 && player.Velocity.sqrMagnitude < .0001f,
                label + ": oyuncu konumu, sağ yön ve hız sıfırlanır");
            Check(player.ParrySerial == 0 && !player.ParryActive && !player.ShieldCollider.enabled,
                label + ": eski E kuyruğu/parry/kalkan temizlenir");
            Check(Near(game.Elapsed, 0) && Near(game.Remaining, 45) && Near(game.DeathElapsed, 0) && game.ParryCount == 0,
                label + ": süre, 45 saniye, ölüm saati ve başarılı parry sayısı sıfırlanır");
            Check(orb.Active && orb.CollisionShape.enabled && Near(orb.Position, CGameController.OrbStart)
                && Near(orb.Velocity.normalized, CGameController.OrbDirection), label + ": top başlangıç konumu/yönü ve aktif collider geri gelir");
            Check(!orb.ReceiverCharged && orb.ParryCount == 0 && orb.WallBounceCount == 0 && Near(orb.ActiveElapsed, 0)
                && Near(orb.CurrentSpeed, 5.5f), label + ": top şarjı, çarpışma sayaçları ve hız saati sıfırlanır");
            Check(AllPlatesRestored(), label + ": tüm plaka canları, fizik, renkler ve çatlaklar sıfırlanır");
            Check(Near(game.Environment.DoorLeft.localPosition.x, -.49f) && Near(game.Environment.DoorRight.localPosition.x, .49f)
                && Near(game.Environment.ReceiverCoverLeft.localPosition.x, -.42f)
                && Near(game.Environment.ReceiverCoverRight.localPosition.x, .42f), label + ": ana kapı ve ayrı alıcı kapakları kapanır");
            Check(EffectsCleared() && !game.Environment.Overlay.activeSelf, label + ": ölüm/temas efektleri ve sonuç ekranı temizlenir");
        }

        private static IEnumerator Run()
        {
            previousSimulation = Physics2D.simulationMode; simulationSaved = true;
            int originalCount = SceneManager.sceneCount;
            for (int i = 0; i < originalCount; i++)
                foreach (GameObject root in SceneManager.GetSceneAt(i).GetRootGameObjects())
                { originalRoots.Add(root); originalStates.Add(root.activeSelf); root.SetActive(false); }
            testScene = EditorSceneManager.LoadSceneInPlayMode(CSceneBuilder.ScenePath,
                new LoadSceneParameters(LoadSceneMode.Additive, LocalPhysicsMode.Physics2D));
            double loadDeadline = EditorApplication.timeSinceStartup + 20;
            while (testScene.IsValid() && !testScene.isLoaded && EditorApplication.timeSinceStartup < loadDeadline) yield return .05f;
            Require(testScene.IsValid() && testScene.isLoaded, "Saved C scene did not finish loading within 20 seconds.");
            SceneManager.SetActiveScene(testScene); physics = testScene.GetPhysicsScene2D();
            game = UnityEngine.Object.FindAnyObjectByType<CGameController>();
            Require(game && game.gameObject.scene == testScene && game.Player && game.Orb && game.Environment && game.Receiver && game.Exit,
                "Saved C scene is missing its serialized flow references.");
            player = game.Player; orb = game.Orb;
            game.ManualSimulation = player.ManualSimulation = orb.ManualSimulation = true;
            player.Body.interpolation = orb.Body.interpolation = RigidbodyInterpolation2D.None;
            Physics2D.simulationMode = SimulationMode2D.Script;
            Require(game.Plates != null && game.Plates.Length > 0, "Saved C plate instances are missing.");
            foreach (CPlate plate in game.Plates)
            {
                Require(plate && plate.Solid && plate.Face && plate.Frame && plate.Crack, "Saved plate has incomplete collider/presentation references.");
                plateColors[plate] = plate.Face.color;
                if (plate.MaximumHits == 1 && !normal) normal = plate;
                if (plate.MaximumHits == 2 && !armored) armored = plate;
            }
            Require(normal && armored && game.Environment.ReceiverCoverLeft && game.Environment.ReceiverCoverRight,
                "Saved C scene needs both plate types and independent receiver shutters.");
            Key(); yield return .12f;
            Check(physics.IsValid() && testScene.handle.GetRawData() != originalScene.handle.GetRawData(),
                "Kaydedilmiş C sahnesi ayrı gerçek PhysicsScene2D içinde yüklendi");
            Check(Near(game.CountdownDuration, 45), "Kaydedilmiş geri sayım 45 saniye");
            Check(game.Receiver.Zone.isTrigger && game.Exit.Zone.isTrigger && !normal.Solid.isTrigger && !armored.Solid.isTrigger,
                "Alıcı/çıkış gerçek trigger; plaka yüzeyleri gerçek solid Collider2D");
            Check(game.Phase == CGamePhase.Instruction && !player.InputEnabled && !orb.Active && game.Environment.Overlay.activeSelf,
                "Başlangıç bilgi ekranı görünür; oyuncu girdisi ve top hareketi kapalı");
            game.Tick(12f); orb.Step(12f);
            Check(Near(game.Elapsed, 0) && Near(game.Remaining, 45) && Near(orb.Position, CGameController.OrbStart),
                "Bilgi ekranında 12 saniyelik adım sayacı ve topu ilerletmez");

            player.UseExternalInput = false;
            Key(UnityEngine.InputSystem.Key.E); yield return .30f;
            player.Step(.02f);
            Check(game.Phase == CGamePhase.Playing && game.RunSerial == 1 && !player.InputEnabled,
                "Gerçek E bilgi ekranından başlatır; ilk basılı E oyuncuya aktarılmaz");
            Check(player.ParrySerial == 0 && !player.ParryActive && !player.ShieldCollider.enabled,
                "Başlatan E uzun tutulsa da parry/guard oluşmaz");
            Key(); yield return .10f; player.Step(.02f);
            Check(player.InputEnabled && player.ParrySerial == 0 && !player.ParryActive,
                "Başlatan E bırakılması tüketilir; sonraki Update'te hareket açılır");
            Key(UnityEngine.InputSystem.Key.D); yield return .06f; player.Step(.02f);
            Check(player.Position.x > CGameController.PlayerStart.x, "İlk E bırakıldıktan sonra gerçek D ile yürünebilir");
            Key(); yield return .05f;
            Key(UnityEngine.InputSystem.Key.E); yield return .06f;
            Key(); yield return .06f; player.Step(.02f);
            Check(player.ParrySerial == 1 && player.ParryActive, "Sonraki yeni gerçek kısa E bas-bırak tam bir parry açar");
            int ignoredSerial = game.RunSerial;
            Key(UnityEngine.InputSystem.Key.R); yield return .08f;
            Check(game.RunSerial == ignoredSerial && game.Phase == CGamePhase.Playing, "Playing sırasında gerçek R yeniden başlatmaz");
            Key(); yield return .06f;

            FixtureStart();
            PositionPlayer(new Vector2(0, -3.24f)); PhysicalStep();
            Check(game.Phase == CGamePhase.Playing, "Kapalı çıkışa gerçek gövde trigger girişi kazanım üretmez");
            player.ResetAt(CGameController.PlayerStart, 1);
            PositionOrb(game.Receiver.Zone.bounds.center); PhysicalStep();
            Check(game.Phase == CGamePhase.Playing && orb.Active && !game.Receiver.TryAccept(orb),
                "Süre dolmadan gerçek alıcı teması reddedilir; kapı kapalı ve top aktif kalır");
            EarnParry("Süre öncesi");
            Check(!orb.ReceiverCharged, "45 saniye öncesi başarılı parry topu şarj etmez");
            game.Tick(44.999f);
            Check(game.Phase == CGamePhase.Playing && game.Remaining > 0 && !orb.ReceiverCharged,
                "44.999 saniyede alıcı kapalı, önceki parry şarjsız");
            game.Tick(.0011f);
            Check(game.ReceiverReady && !orb.ReceiverCharged && Near(game.Remaining, 0),
                "45 saniyede yalnız alıcı hazır olur; eski parry geriye dönük şarj kazanmaz");
            Check(game.Environment.PhaseText.text.Contains("Topu enerji alıcısına gönder!"), "Hazır olunca hedef yönergesi görünür");
            orb.ResetOrb(new Vector2(-2.3f, -1.05f), Vector2.right);
            orb.Step(.8f); PhysicalStep();
            Check(game.ReceiverReady && orb.Active && !orb.ReceiverCharged && orb.Position.x > 1.8f && orb.WallBounceCount == 0,
                "Hazır alıcıdan parry/şarj olmadan gerçek sweep geçişi kabul edilmez ve yansımaz");

            FixtureStart(); game.Tick(45f); EarnParry("Süre sonrası");
            Check(orb.ReceiverCharged && game.ReceiverReady, "Hazır durumdan sonraki gerçek parry topu şarj eder");
            Check(!game.AcceptOrb(orb) && !game.Receiver.TryAccept(orb), "Şarjlı ama alıcıdan uzaktaki top doğrudan çağrıyla kabul edilemez");
            PositionOrb(new Vector2(9.5f, 1.1f));
            int bounces = orb.WallBounceCount; orb.Step(.20f);
            Check(orb.WallBounceCount > bounces && orb.ReceiverCharged && game.ReceiverReady,
                "Şarj fiziksel duvar/plaka yansımasından sonra korunur");
            Require(orb.Active && Mathf.Abs(orb.Velocity.x) > .1f, "Charged wall fixture lost a usable horizontal heading.");
            Vector2 direction = orb.Velocity.normalized;
            Bounds receiverBounds = game.Receiver.Zone.bounds;
            Vector2 edge = new Vector2(receiverBounds.center.x - Mathf.Sign(direction.x) * (receiverBounds.extents.x + orb.Radius), receiverBounds.center.y);
            PositionOrb(edge);
            Check(game.Receiver.TouchesOrb(orb) && !game.Receiver.Zone.OverlapPoint(orb.Position),
                "İlk temas kenarında merkez trigger dışında olsa da gerçek top yarıçapı hesaba katılır");
            PositionOrb(edge - direction * .12f); orb.Step(.06f);
            Check(game.Phase == CGamePhase.DoorOpen && !orb.Active && !orb.CollisionShape.enabled,
                "Şarjlı top gerçek swept trigger girişinde emilir ve ana kapı açılır");
            Check(!game.AcceptOrb(orb) && !game.Receiver.TryAccept(orb) && game.Phase == CGamePhase.DoorOpen,
                "Aynı topun tekrar kabul çağrıları ikinci geçiş veya kazanım üretmez");
            bool hidden = true;
            foreach (Renderer renderer in orb.GetComponentsInChildren<Renderer>(true)) hidden &= !renderer.enabled;
            Check(hidden, "Emilen topun tüm renderer/trail bileşenleri gizlenir");
            game.Tick(.70f);
            Check(game.Environment.DoorLeft.localPosition.x < -.9f && game.Environment.DoorRight.localPosition.x > .9f,
                "Ana kapı kanatları gerçekten ayrılır");
            Check(!game.TryExit(player) && game.Phase == CGamePhase.DoorOpen, "Uzaktaki oyuncu TryExit çağrısıyla kazanamaz");
            PositionPlayer(new Vector2(0, -3.24f)); PhysicalStep();
            Check(game.Phase == CGamePhase.Won && player.Alive && !player.InputEnabled,
                "Açık çıkışa gerçek gövde trigger girişi kazanımı üretir");
            int winSerial = game.RunSerial;
            Key(UnityEngine.InputSystem.Key.R); yield return .08f;
            CheckReset("Won sonrası", winSerial);
            Key(); yield return .06f;

            FixtureStart(); game.Tick(45f); EarnParry("Native alıcı callback");
            PositionOrb(game.Receiver.Zone.bounds.center); PhysicalStep();
            Check(game.Phase == CGamePhase.DoorOpen && !orb.Active,
                "Native OnTriggerEnter/Stay yolu da gerçek şarjlı top temasını kabul eder");
            FixtureStart();
            GameObject normalPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/C/C_NormalPlate.prefab");
            GameObject armoredPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/C/C_ArmoredPlate.prefab");
            Check(normalPrefab && armoredPrefab && normalPrefab.GetComponent<CPlate>().MaximumHits == 1
                && armoredPrefab.GetComponent<CPlate>().MaximumHits == 2, "Kalıcı normal/zırhlı prefabların dayanıklılık değerleri 1 ve 2");
            Check(normal.Face.color != armored.Face.color, "Normal ve zırhlı plaka renkleri görsel olarak ayrıdır");
            Check(normal.ApplyImpact(101) && normal.Broken && !normal.Solid.enabled && !normal.Face.enabled && !normal.Frame.enabled,
                "Normal plaka tek temas jetonunda kırılır, collider ve çizimi kapanır");
            Check(!normal.ApplyImpact(101), "Kırılmış normal plakaya yinelenen temas ek hasar oluşturmaz");
            Check(armored.ApplyImpact(201) && !armored.Broken && armored.RemainingHits == 1 && armored.Crack.enabled
                && armored.Solid.enabled, "Zırhlı ilk temasta bir can kalır; çatlak görünür ve fizik sürer");
            Check(!armored.ApplyImpact(201) && armored.RemainingHits == 1, "Aynı zırhlı temas jetonu ikinci hasarı vermez");
            Check(armored.ApplyImpact(202) && armored.Broken && !armored.Solid.enabled && !armored.Face.enabled
                && !armored.Frame.enabled && !armored.Crack.enabled, "İkinci ayrı zırhlı temas jetonu plakayı kırar");
            normal.ResetPlate(); armored.ResetPlate();
            Check(AllPlatesRestored(), "ResetPlate can, collider, özgün renkler ve çatlak durumunu geri getirir");
            Check(armored.ApplyImpact(201) && armored.RemainingHits == 1, "ResetPlate önceki temas jetonlarını da temizler");
            armored.ResetPlate();

            for (int cycle = 1; cycle <= 3; cycle++)
            {
                FixtureStart(); game.Tick(45f); EarnParry("Ölüm döngüsü " + cycle);
                BreakAllPlates(1000 + cycle * 100);
                player.SetInput(new CInputFrame(-1, false, false, false)); player.Step(.02f);
                player.InputEnabled = false;
                PositionOrb(player.Position); orb.Step(.02f);
                Require(game.Phase == CGamePhase.Dying, "Death fixture must enter Dying through a real orb/body contact.");
                Check(!player.Alive && !player.Body.simulated && !orb.Active && !game.Environment.Overlay.activeSelf,
                    "Döngü " + cycle + ": fiziksel ölümde oyuncu/top donar; sonuç ekranı erken açılmaz");
                int serial = game.RunSerial;
                Vector2 frozenOrb = orb.Position; float frozenTime = game.Elapsed;
                Key(UnityEngine.InputSystem.Key.R); yield return .08f;
                game.RestartRun(); game.Tick(1.399f); orb.Step(.5f);
                Check(game.Phase == CGamePhase.Dying && game.RunSerial == serial && Near(game.DeathElapsed, 1.399f)
                    && Near(orb.Position, frozenOrb) && Near(game.Elapsed, frozenTime),
                    "Döngü " + cycle + ": 1.399 saniyede R / doğrudan restart reddedilir; ölüm animasyonu ve donmuş koşu korunur");
                game.Tick(.0011f); yield return .08f;
                Check(game.Phase == CGamePhase.Lost && Near(game.DeathElapsed, 1.4f) && game.RunSerial == serial
                    && game.Environment.Overlay.activeSelf, "Döngü " + cycle + ": 1.4 saniyede Lost açılır; eskiden basılı R yeniden başlatmaz");
                Key(); yield return .08f;
                Check(game.Phase == CGamePhase.Lost && game.RunSerial == serial, "Döngü " + cycle + ": R bırakma tek başına yeniden başlatmaz");
                Key(UnityEngine.InputSystem.Key.R); yield return .08f;
                CheckReset("Döngü " + cycle, serial);
                Key(); yield return .06f;
            }
            Check(game.Receiver.Zone.isTrigger && game.Exit.Zone.isTrigger && !orb.CollisionShape.isTrigger,
                "Üç yeniden başlatmadan sonra trigger/solid ayrımı korunur");
            Key(); yield return .05f;
        }
    }
}
#endif
