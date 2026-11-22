#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CombinedD
{
    /// <summary>Paired, real-physics counterfactuals. Only the named connection flag changes within a pair.</summary>
    public static class DLinkVerification
    {
        public const string StatusPath = "Temp/D_Birlesik/link-status.txt";
        private const string ReportPath = "Belge/D_Baglanti_Testleri.md";
        private const float Dt = .02f;
        private static readonly List<GameObject> suspended = new List<GameObject>();
        private static readonly List<string> errors = new List<string>();
        private static readonly StringBuilder report = new StringBuilder();
        private static Scene originalScene, scene;
        private static PhysicsScene2D physics;
        private static GameObject root;
        private static DGameController game;
        private static DPlayerMotor player;
        private static DOrbMotor orb;
        private static DAttackDirector attacks;
        private static DReceiver receiver;
        private static AsyncOperation unload;
        private static bool running;
        private static int stage, failures;
        private static double deadline;
        private static float impactPause;
        private static int impactMissiles, impactCancellations;
        public static bool IsRunning => running;
        public static int FailureCount => failures;

        [MenuItem("D/Verify mechanic connections in Play Mode")]
        public static void Begin()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("DLinkVerification.Begin requires Play Mode.");
            if (running) throw new InvalidOperationException("Link verification is already running.");
            running = true; stage = failures = 0; suspended.Clear(); errors.Clear(); report.Clear();
            scene = default; root = null; game = null; player = null; orb = null; attacks = null; unload = null;
            originalScene = SceneManager.GetActiveScene(); deadline = EditorApplication.timeSinceStartup + 90;
            Directory.CreateDirectory("Temp/D_Birlesik"); Directory.CreateDirectory("Belge");
            File.WriteAllText(StatusPath, "RUNNING");
            report.AppendLine("# D — Mekanik bağlantı / çıkarma testleri\n");
            report.AppendLine("Çalıştırma: " + DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz") + "; Unity " + Application.unityVersion + ".\n");
            report.AppendLine("Aynı başlangıç, aynı .02 sn fizik adımları ve aynı girdiler eşleştirilir. Her çiftte yalnız belirtilen davranış bayrağı değişir; bütün bir kontrolcü silinmez. "
                + "Oyuncu ve top açık D sahnesindeki gerçek bileşenlerden kopyalanır; roket/lazer gerçek D prefablarıdır. "
                + "Ayrı PhysicsScene2D içindeki düz test alanı kullanılır. Lav çalışır; başlangıcı -30 seçilerek beş saniyelik saldırı karşılaştırmasının lav ölümüyle karışması önlenir. "
                + "Bu kontrollü deneyler tam parkurun tamamlanabildiği veya her bağlantının her koşulda zorunlu olduğu iddiası değildir.\n");
            Application.logMessageReceived += OnLog;
            EditorApplication.update += Update;
        }

        private static void Update()
        {
            if (!running) return;
            if (stage >= 6)
            {
                if (unload != null && !unload.isDone && EditorApplication.timeSinceStartup <= deadline) return;
                Finish(); return;
            }
            try
            {
                if (!EditorApplication.isPlaying) throw new InvalidOperationException("Play Mode ended during link verification.");
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Link verification exceeded 90 seconds.");
                switch (stage++)
                {
                    case 0: BuildFixture(); break;
                    case 1: Case("Hızlanma / fren mesafesi / guard hazırlığı", AccelerationPair); break;
                    case 2: Case("Guard hareket kilidi / zıplama / yükselen lav", GuardPair); break;
                    case 3: Case("Gerçek top parry / saldırı iptali / beş saniyelik pencere", WavePair); break;
                    case 4: Case("Yönlendirilmiş top çıkışı / gerçek alıcı teması", DirectedPair); break;
                    case 5: Cleanup(); break;
                    default: Finish(); break;
                }
            }
            catch (Exception ex)
            {
                failures++; report.AppendLine("\nFAIL — harness: " + ex);
                if (stage <= 6) { Cleanup(); stage = 6; deadline = EditorApplication.timeSinceStartup + 15; }
                else Finish();
            }
        }

        private static void BuildFixture()
        {
            DGameController source = Object.FindAnyObjectByType<DGameController>();
            Require(source && source.Player && source.Orb && source.Attacks && source.Level && source.Lava, "Open the built D scene before Begin; authored gameplay references are required.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                foreach (GameObject item in SceneManager.GetSceneAt(i).GetRootGameObjects())
                    if (item.activeSelf) { suspended.Add(item); item.SetActive(false); }
            scene = SceneManager.CreateScene("D Link Verification " + DateTime.UtcNow.Ticks,
                new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            Require(scene.isLoaded, "Isolated fixture scene did not load.");
            SceneManager.SetActiveScene(scene); physics = scene.GetPhysicsScene2D();
            Require(physics.IsValid(), "Fixture has no local PhysicsScene2D.");
            root = new GameObject("D connection verification fixture");
            player = Object.Instantiate(source.Player.gameObject, root.transform).GetComponent<DPlayerMotor>();
            player.gameObject.SetActive(true); player.ManualSimulation = true; player.UseExternalInput = true;
            player.Body.interpolation = RigidbodyInterpolation2D.None;
            orb = Object.Instantiate(source.Orb.gameObject, root.transform).GetComponent<DOrbMotor>();
            orb.gameObject.SetActive(true); orb.ManualSimulation = true; orb.Player = player; orb.Effects = null;
            orb.CollisionMask = (1 << 10) | (1 << 11) | (1 << 13);
            orb.gameObject.layer = 14;
            var floor = Child("Real floor"); floor.layer = 8; floor.transform.position = new Vector2(0, -.5f);
            floor.AddComponent<BoxCollider2D>().size = new Vector2(120, 1);
            var camera = Child("Fixture camera").AddComponent<Camera>();
            camera.enabled = false; camera.orthographic = true; camera.orthographicSize = 9.5f; camera.aspect = 16f / 9f;
            camera.transform.position = new Vector3(0, 2, -10);
            attacks = Child("Real attack scheduler").AddComponent<DAttackDirector>();
            attacks.ManualSimulation = true; attacks.Player = player; attacks.Orb = orb; attacks.ViewCamera = camera;
            attacks.InitialMissileSpeed = source.Attacks.InitialMissileSpeed; attacks.MaximumMissileSpeed = source.Attacks.MaximumMissileSpeed;
            attacks.InitialInterval = source.Attacks.InitialInterval; attacks.MinimumInterval = source.Attacks.MinimumInterval;
            attacks.DifficultyDuration = source.Attacks.DifficultyDuration;
            attacks.MinimumLaserWarning = source.Attacks.MinimumLaserWarning; attacks.MaximumLaserWarning = source.Attacks.MaximumLaserWarning;
            attacks.MinimumLaserDuration = source.Attacks.MinimumLaserDuration; attacks.MaximumLaserDuration = source.Attacks.MaximumLaserDuration;
            attacks.MinimumLaserStartGap = source.Attacks.MinimumLaserStartGap;
            attacks.MissilePrefab = AssetDatabase.LoadAssetAtPath<DMissile>("Assets/Prefabs/D/D_Missile.prefab");
            attacks.LaserPrefab = AssetDatabase.LoadAssetAtPath<DLaser>("Assets/Prefabs/D/D_Laser.prefab");
            Require(attacks.MissilePrefab && attacks.LaserPrefab, "The built real D missile/laser prefab assets are missing.");
            attacks.Points = new[] { Point(-1), Point(1) };
            game = Child("Real game and lava clock").AddComponent<DGameController>(); game.ManualSimulation = true;
            game.Level = Child("Level manager").AddComponent<DLevelManager>();
            game.Lava = Child("Lava controller").AddComponent<DLavaController>();
            game.Player = player; game.Orb = orb; game.Attacks = attacks; game.Lava.RiseSpeed = source.Lava.RiseSpeed;
            game.DeathDuration = source.DeathDuration; game.Level.InitialOrbDirection = source.Level.InitialOrbDirection;
            game.Level.PlayerSpawn = Child("Player spawn").transform; game.Level.PlayerSpawn.position = new Vector2(0, .9f);
            game.Level.OrbSpawn = Child("Orb spawn").transform; game.Level.OrbSpawn.position = new Vector2(8, 5);
            game.Lava.StartMarker = Child("Fixture low lava start").transform; game.Lava.StartMarker.position = new Vector2(0, -30);
            player.Game = game; player.SpawnPoint = game.Level.PlayerSpawn; orb.Game = game; attacks.Game = game;
            var zone = Child("Real receiver fixture"); zone.layer = 13;
            zone.transform.position = new Vector2(50, 50);
            var box = zone.AddComponent<BoxCollider2D>(); box.isTrigger = true; box.size = new Vector2(1.4f, 1.8f);
            receiver = zone.AddComponent<DReceiver>(); receiver.Zone = box; receiver.Game = game; game.Level.Receiver = receiver;
            Physics2D.SyncTransforms();
            report.AppendLine("Gerçek ayarlar: MaxSpeed=" + F(player.MaxSpeed) + ", GroundAcceleration=" + F(player.GroundAcceleration)
                + ", GroundBrake=" + F(player.GroundBrake) + ", GuardHold=" + F(player.GuardHoldSeconds)
                + ", JumpSpeed=" + F(player.JumpSpeed) + ", orb Speed=" + F(orb.Speed) + ", lavaSpeed=" + F(game.Lava.RiseSpeed)
                + ", attack interval=" + F(attacks.InitialInterval) + "→" + F(attacks.MinimumInterval) + ".\n");
            Require(player.BodyCollider.gameObject.layer == 11 && player.ShieldCollider.gameObject.layer == 10,
                "Authored body/shield layers must be 11/10.");
            Require(Physics2D.GetIgnoreLayerCollision(10, 8) && Physics2D.GetIgnoreLayerCollision(14, 11),
                "Native shield/orb collision isolation must already be configured by the D builder.");
        }
        private static GameObject Child(string name)
        { var value = new GameObject(name); value.transform.SetParent(root.transform, false); return value; }
        private static DAttackPoint Point(int side)
        {
            var point = Child(side < 0 ? "Left authored test source" : "Right authored test source").AddComponent<DAttackPoint>();
            point.Side = side; point.transform.position = new Vector2(side * 10, 2.8f); return point;
        }
        private static void Reset(int facing = 1)
        {
            RestoreFlags(); receiver.transform.position = new Vector2(50, 50);
            game.BeginRun(); orb.Freeze(); attacks.StopAll();
            player.ResetAt(game.Level.PlayerSpawn.position, facing); player.ManualSimulation = true; player.UseExternalInput = true;
            for (int i = 0; i < 25; i++) Tick(Input());
            Require(player.Grounded && player.Alive, "Player did not settle on the actual fixture floor.");
            impactPause = 0; impactMissiles = impactCancellations = 0;
        }
        private static DInputFrame Input(float move = 0, bool jumpPress = false, bool jumpHeld = false,
            bool jumpRelease = false, bool press = false, bool held = false, bool release = false)
            => new DInputFrame(move, jumpPress, jumpHeld, jumpRelease, press, held, release);
        private static void Tick(DInputFrame input, bool runOrb = false, bool runAttacks = false)
        {
            game.Tick(Dt);
            player.SetInput(input); player.Step(Dt); Physics2D.SyncTransforms();
            if (runOrb)
            {
                int before = orb.ParryCount; orb.Step(Dt);
                if (orb.ParryCount > before)
                {
                    // Read immediately after the actual collision callback, before this frame's timer decrement.
                    impactPause = attacks.PauseRemaining; impactMissiles = attacks.ActiveMissileCount;
                    impactCancellations = attacks.CancelledCount;
                }
            }
            if (game.CameraRig) game.CameraRig.FollowStep(Dt);
            if (runAttacks) attacks.Step(Dt);
            Physics2D.SyncTransforms(); physics.Simulate(Dt); Physics2D.SyncTransforms();
        }
        private static void RestoreFlags()
        {
            if (player) player.UseAcceleration = player.GuardLocksMovement = player.RequireStoppedGuard = true;
            if (attacks) attacks.WaveEnabled = true;
            if (orb) orb.DirectedParry = true;
        }

        private struct BrakeResult { public float Distance, GuardTime, LavaRise, PeakSpeed; }
        private static BrakeResult BrakeTrial(bool acceleration)
        {
            Reset(); player.UseAcceleration = acceleration;
            for (int i = 0; i < 40; i++) Tick(Input(1));
            float startX = player.Position.x, startLava = game.LavaHeight, speed = player.Velocity.x;
            var value = new BrakeResult { GuardTime = -1, PeakSpeed = speed };
            for (int i = 0; i < 100; i++)
            {
                Tick(Input(press: i == 0, held: true));
                if (value.GuardTime < 0 && player.GuardActive)
                {
                    value.GuardTime = (i + 1) * Dt; value.Distance = player.Position.x - startX;
                    value.LavaRise = game.LavaHeight - startLava;
                }
            }
            Require(value.GuardTime > 0, "No grounded stopped guard occurred in the two-second observation.");
            return value;
        }
        private static void AccelerationPair()
        {
            BrakeResult baseline = BrakeTrial(true), removed = BrakeTrial(false);
            report.AppendLine("\n| Aynı girdi: .8 sn sağ, ardından 2 sn nötr+E | Bırakma hızı | Fren mesafesi | Guard'a süre | Bu sürede lav artışı |");
            report.AppendLine("|---|---:|---:|---:|---:|");
            report.AppendLine(BrakeRow("Normal", baseline)); report.AppendLine(BrakeRow("UseAcceleration=false", removed));
            Require(Mathf.Abs(baseline.PeakSpeed - removed.PeakSpeed) < .02f, "Pair did not start braking from equal speeds.");
            Require(baseline.Distance > removed.Distance + .25f && baseline.GuardTime > removed.GuardTime + .1f,
                "Disabling acceleration did not reduce both braking distance and guard preparation time.");
            report.AppendLine("\nYorum: bu girdide atalet gerçek bir durma mesafesi ve guard hazırlık süresi yaratır; lav bu bekleme sırasında yükselmeye devam eder. "
                + "Bayrak hem hızlanmayı hem frenlemeyi anlık hedef hıza çevirir; sonuç yalnız hızlanmanın tek başına etkisi diye yorumlanamaz.");
        }
        private static string BrakeRow(string label, BrakeResult value) => "| " + label + " | " + F(value.PeakSpeed) + " | "
            + F(value.Distance) + " | " + F(value.GuardTime) + " | " + F(value.LavaRise) + " |";

        private struct GuardResult { public float GuardedWalk, JumpRise, JumpSpeed, LavaRise; public bool GroundedWalkObserved; }
        private static GuardResult GuardTrial(bool locked)
        {
            Reset(); player.GuardLocksMovement = locked;
            for (int i = 0; i < 16; i++) Tick(Input(press: i == 0, held: true));
            Require(player.GuardActive, "Neutral hold failed to establish guard before comparison.");
            float initialY = player.Position.y, initialLava = game.LavaHeight;
            var value = new GuardResult();
            for (int i = 0; i < 40; i++)
            {
                float x = player.Position.x; Tick(Input(1, held: true));
                if (player.GuardActive && player.Grounded)
                {
                    value.GuardedWalk += Mathf.Abs(player.Position.x - x);
                    value.GroundedWalkObserved |= Mathf.Abs(player.Velocity.x) > .2f;
                }
            }
            for (int i = 0; i < 35; i++)
            {
                Tick(Input(1, jumpPress: i == 0, jumpHeld: i < 10, jumpRelease: i == 10, held: true));
                value.JumpRise = Mathf.Max(value.JumpRise, player.Position.y - initialY);
                value.JumpSpeed = Mathf.Max(value.JumpSpeed, player.Velocity.y);
            }
            value.LavaRise = game.LavaHeight - initialLava;
            return value;
        }
        private static void GuardPair()
        {
            GuardResult baseline = GuardTrial(true), removed = GuardTrial(false);
            report.AppendLine("\n| Aynı girdi: guard'da .8 sn sağ, sonra Space bas/bırak ve sağ | Guard açıkken yürüme | Tepe yükselmesi | En büyük vy | Lav artışı |");
            report.AppendLine("|---|---:|---:|---:|---:|");
            report.AppendLine(GuardRow("Normal", baseline)); report.AppendLine(GuardRow("GuardLocksMovement=false", removed));
            Require(baseline.GuardedWalk < .02f && !baseline.GroundedWalkObserved && removed.GuardedWalk > .5f && removed.GroundedWalkObserved,
                "Guard lock removal did not create grounded walking while guard remained active.");
            Require(baseline.JumpSpeed < .1f && removed.JumpSpeed > 5f && removed.JumpRise > .5f,
                "Guard lock removal did not permit the same Space input to launch a jump.");
            Require(baseline.LavaRise > 0f && Mathf.Abs(baseline.LavaRise - removed.LavaRise) < .005f,
                "Paired lava clocks differed or stopped during guard.");
            report.AppendLine("\nYorum: normal guard savunurken konum değiştirip lavdan yukarı kaçmayı engeller. Kilit kaldırılınca aynı tutuşta yerde kalkanla yürünebilir ve zıplama başlatılabilir; "
                + "havaya çıkınca yerde olma şartı hâlâ guard'ı kapatır. İki deneyde de lav aynı miktarda yükselir. Bu, lavın guard'ı her konumda ölümcül yaptığı anlamına gelmez.");
        }
        private static string GuardRow(string label, GuardResult value) => "| " + label + " | " + F(value.GuardedWalk) + " | "
            + F(value.JumpRise) + " | " + F(value.JumpSpeed) + " | " + F(value.LavaRise) + " |";

        private static void ActualOrbParry(bool runAttacks)
        {
            Tick(Input(press: true, held: true), false, runAttacks);
            for (int i = 0; i < 3; i++) Tick(Input(held: true), false, runAttacks);
            Tick(Input(release: true), false, runAttacks);
            Require(player.ParryActive, "Short release failed to arm the real shield.");
            orb.ResetOrb(player.Position + new Vector2(player.Facing * 1.4f, .12f), new Vector2(-player.Facing, 0));
            orb.SetRandomSeed(17);
            for (int i = 0; i < 10 && orb.ParryCount == 0 && player.Alive; i++) Tick(Input(), true, runAttacks);
            Require(player.Alive && orb.ParryCount == 1 && game.OrbParries == 1,
                "The swept orb must physically hit the active shield and call Game.OnOrbParry exactly once.");
            Require(Mathf.Abs(orb.Velocity.magnitude - orb.Speed) < .01f, "Actual parry changed world speed.");
        }
        private struct WaveResult
        {
            public float Pause, PressureSeconds, PausedPressureSeconds, DeathAfterParry, FirstResumeTime;
            public int MissilesAtImpact, Cancelled, Spawned, OrbParries, MaxSpawnDelta;
            public bool AliveAfterFive;
        }
        private static WaveResult WaveTrial(bool wave)
        {
            Reset(-1); attacks.WaveEnabled = wave; attacks.ResetAttacks();
            int budget = Mathf.CeilToInt((attacks.InitialInterval + 2) / Dt);
            for (int i = 0; i < budget && !attacks.CurrentMissile; i++) Tick(Input(), false, true);
            Require(attacks.CurrentMissile && attacks.ActiveMissileCount == 1 && attacks.SpawnCount == 1,
                "The real scheduler did not instantiate its first missile from the authored prefab.");
            // First episode and orb both approach from the left, avoiding the opposite-side fairness cancellation.
            Require(attacks.CurrentMissile.Position.x < player.Position.x, "Expected the first episode on the left.");
            ActualOrbParry(true);
            var value = new WaveResult
            {
                Pause = impactPause, MissilesAtImpact = impactMissiles, Cancelled = impactCancellations,
                OrbParries = game.OrbParries, DeathAfterParry = -1, FirstResumeTime = -1
            };
            for (int i = 0; i < 250; i++)
            {
                bool pressure = attacks.ActiveMissileCount + attacks.ActiveLaserCount + attacks.PendingWarningCount > 0;
                if (pressure) value.PressureSeconds += Dt;
                if (pressure && attacks.PauseRemaining > .0001f) value.PausedPressureSeconds += Dt;
                int before = attacks.SpawnCount;
                Tick(Input(), true, true);
                int delta = attacks.SpawnCount - before;
                value.MaxSpawnDelta = Mathf.Max(value.MaxSpawnDelta, delta);
                if (delta > 0 && value.FirstResumeTime < 0) value.FirstResumeTime = (i + 1) * Dt;
                if (!player.Alive && value.DeathAfterParry < 0) value.DeathAfterParry = (i + 1) * Dt;
            }
            value.AliveAfterFive = player.Alive; value.Spawned = attacks.SpawnCount;
            return value;
        }
        private static void WavePair()
        {
            WaveResult baseline = WaveTrial(true), removed = WaveTrial(false);
            report.AppendLine("\n| Aynı gerçek kalkan-top teması; ardından 5 sn nötr | Callback parry | Temasta pause | Temasta roket | İptal | Baskı süresi | Ölüm zamanı | 5 sn canlı |");
            report.AppendLine("|---|---:|---:|---:|---:|---:|---:|---|");
            report.AppendLine(WaveRow("Normal", baseline)); report.AppendLine(WaveRow("WaveEnabled=false", removed));
            Require(baseline.OrbParries == 1 && removed.OrbParries == 1, "Both variants must use a real, counted orb parry.");
            Require(Mathf.Abs(baseline.Pause - 5f) < .0001f && removed.Pause == 0f,
                "Real collision callback did not distinguish five-second pause from the disconnected zero-pause path.");
            Require(baseline.MissilesAtImpact == 0 && baseline.Cancelled >= 1 && removed.MissilesAtImpact == 1 && removed.Cancelled == 0,
                "Wave flag did not distinguish immediate cancellation from surviving attack pressure.");
            Require(baseline.PausedPressureSeconds == 0f && baseline.PressureSeconds <= .04f && removed.PressureSeconds > .1f,
                "The active five-second pause must have zero pressure; its exact end may spawn one eligible attack.");
            Require(baseline.FirstResumeTime >= 4.96f && baseline.FirstResumeTime <= 5.001f && baseline.MaxSpawnDelta == 1,
                "The first eligible post-wave attack must resume at five seconds, with no backlog burst or extra interval.");
            report.AppendLine("\nNormal dalgada duraklama içindeki baskı=" + F(baseline.PausedPressureSeconds)
                + " sn; callback karesinden sonraki ilk saldırı=" + F(baseline.FirstResumeTime)
                + " sn; tek adımda en çok yeni saldırı=" + baseline.MaxSpawnDelta + ". Callback karesinde .02 sn timer adımı sayıldığı için gözlemde ilk saldırı 4.98–5.00 sn sınırına gelir.");
            report.AppendLine("\nYorum: topu aynı şekilde parry etmek, normal oyunda mevcut rokete anında son verir ve beş saniyelik saldırısız alan açar. "
                + "Yalnız dalga bağlantısı çıkarılınca top parry sayacı ve topun hareketi korunur; mevcut roket yaşamaya devam eder. "
                + "Nötr kalan test oyuncusunun gözlenen ölüm/canlılık sonucu yukarıdadır; bu her stratejinin dalga olmadan kaybedeceği iddiası değildir. "
                + "Parry callback'i test tarafından doğrudan çağrılmaz; yalnız Orb.Step içindeki gerçek temas çağırır.");
        }
        private static string WaveRow(string label, WaveResult value) => "| " + label + " | " + value.OrbParries + " | " + F(value.Pause)
            + " | " + value.MissilesAtImpact + " | " + value.Cancelled + " | " + F(value.PressureSeconds) + " | "
            + (value.DeathAfterParry < 0 ? "yok" : F(value.DeathAfterParry)) + " | " + (value.AliveAfterFive ? "evet" : "hayır") + " |";

        private struct DirectionResult { public Vector2 Exit; public float Speed, ReceiverTime; public bool Accepted; }
        private static DirectionResult DirectionTrial(bool directed)
        {
            Reset(); orb.DirectedParry = directed;
            // Fixed before either run; no target placement based on a measured outgoing trajectory.
            receiver.transform.position = new Vector2(5f, 5.5f); Physics2D.SyncTransforms();
            ActualOrbParry(false);
            var value = new DirectionResult { Exit = orb.LastParryDirection, Speed = orb.Velocity.magnitude, ReceiverTime = -1 };
            for (int i = 0; i < 150 && !game.DoorOpen && player.Alive; i++)
            {
                Tick(Input(), true);
                if (game.DoorOpen) value.ReceiverTime = (i + 1) * Dt;
            }
            value.Accepted = game.DoorOpen && !orb.Active;
            return value;
        }
        private static void DirectedPair()
        {
            DirectionResult baseline = DirectionTrial(true), removed = DirectionTrial(false);
            float difference = Vector2.Angle(baseline.Exit, removed.Exit);
            report.AppendLine("\nSabit geometri: sağdan yatay top; oyuncu sağa dönük; tohum17; alıcı merkezi(5,5.5), boyutu(1.4,1.8); gözlem3 sn.\n");
            report.AppendLine("| Aynı gerçek parry | Çıkış yönü | Dünya hızı | Gerçek alıcı teması | Temasa süre |");
            report.AppendLine("|---|---|---:|---|---:|");
            report.AppendLine(DirectionRow("Normal", baseline)); report.AppendLine(DirectionRow("DirectedParry=false", removed));
            float angle = Mathf.Atan2(Mathf.Abs(baseline.Exit.y), Mathf.Abs(baseline.Exit.x)) * Mathf.Rad2Deg;
            Require(baseline.Exit.x > 0 && angle >= orb.MinimumAngle - .1f && angle <= orb.MaximumAngle + .1f,
                "Directed parry did not select the facing half-plane and configured angle bounds.");
            Require(Mathf.Abs(baseline.Speed - removed.Speed) < .01f && difference > .1f,
                "Direction-only ablation failed to change outgoing direction while preserving actual speed.");
            report.AppendLine("\nYön farkı: " + F(difference) + "°. "
                + (baseline.Accepted && !removed.Accepted
                    ? "Bu sabit atışta yönlendirme alıcıya ulaşmayı sağladı, normal yansıma üç saniyelik gözlemde ulaşmadı. Bu yalnız bu konum/girdi için güçlü bir farktır."
                    : baseline.Accepted && removed.Accepted
                        ? "Her iki yol da alıcıya ulaştı: bu atış yönlendirme-alıcı zorunluluğunu desteklemiyor; bağlantı bu ölçütte zayıf."
                        : !baseline.Accepted && removed.Accepted
                            ? "Yalnız normal yansıma alıcıya ulaştı: seçilen atış yönlendirme için avantaj kanıtı değildir."
                            : "İki yol da alıcıya ulaşmadı: çıkış yönü farkı ölçülür, ancak bu atış alıcı bağlantısının gücüne kanıt sunmaz.")
                + " Normal yansıma başka başlangıçlardan veya duvar sekmelerinden yine hedefe gidebilir; bu test tam oyunda yönlendirme olmadan hedefin imkânsız olduğunu söylemez.");
        }
        private static string DirectionRow(string label, DirectionResult value) => "| " + label + " | (" + F(value.Exit.x) + ", " + F(value.Exit.y)
            + ") | " + F(value.Speed) + " | " + (value.Accepted ? "evet" : "hayır") + " | " + (value.ReceiverTime < 0 ? "—" : F(value.ReceiverTime)) + " |";

        private static void Case(string name, Action action)
        {
            report.AppendLine("\n## " + name);
            try { action(); report.AppendLine("\nPASS — mekanik bağlantı kontrolü."); }
            catch (Exception ex) { failures++; report.AppendLine("\nFAIL — " + ex.Message); }
            finally { RestoreFlags(); if (attacks) attacks.StopAll(); if (orb) orb.Freeze(); }
        }
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
        private static string F(float value) => value.ToString("F3", CultureInfo.InvariantCulture);
        private static void OnLog(string message, string stack, LogType type)
        { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); }
        private static void Cleanup()
        {
            RestoreFlags();
            if (player && attacks && orb)
                report.AppendLine("\nTeslim bayrakları yeniden true: " + (player.UseAcceleration && player.GuardLocksMovement && player.RequireStoppedGuard
                    && attacks.WaveEnabled && orb.DirectedParry) + ". Orijinal sahnenin bayrakları değiştirilmedi.");
            if (attacks) attacks.StopAll(); if (orb) orb.Freeze();
            if (scene.IsValid() && scene.isLoaded)
            {
                foreach (GameObject item in scene.GetRootGameObjects()) item.SetActive(false);
                unload = SceneManager.UnloadSceneAsync(scene);
            }
            if (originalScene.IsValid() && originalScene.isLoaded) SceneManager.SetActiveScene(originalScene);
            foreach (var item in suspended) if (item) item.SetActive(true);
            suspended.Clear();
        }
        private static void Finish()
        {
            EditorApplication.update -= Update; Application.logMessageReceived -= OnLog; running = false;
            if (errors.Count > 0) { failures++; report.AppendLine("\nRuntime Error/Exception/Assert: " + errors.Count + "\n" + string.Join("\n", errors)); }
            report.AppendLine("\nBaşarısız kontrol: " + failures + ". Tam parkur, oyuncu build'i ve kayıt ayrı doğrulanır.");
            File.WriteAllText(ReportPath, report.ToString()); File.WriteAllText(StatusPath, "DONE failures=" + failures);
            Debug.Log("D link verification finished. failures=" + failures);
        }
    }
}
#endif
