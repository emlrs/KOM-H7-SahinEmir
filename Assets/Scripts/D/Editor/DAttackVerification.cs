#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CombinedD.Editor
{
    // Begin after the authored D scene is loaded in Play Mode. No live scene is modified.
    public static class DAttackVerification
    {
        private static Scene scene;
        private static GameObject root;
        private static PhysicsScene2D physics;
        private static DPlayerMotor player;
        private static DOrbMotor orb;
        private static DGameController game;
        private static DAttackDirector director;
        private static DMissile missileTemplate;
        private static DLaser laserTemplate;
        private static readonly StringBuilder report = new StringBuilder();
        private static string statusPath, reportPath;
        private static int passed, failed;
        private static bool running;

        [MenuItem("D/Verify/Attack rules (Play Mode)")]
        public static void Begin()
        {
            if (running) return;
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            statusPath = Path.Combine(project, "Temp/D_Birlesik/attack-status.txt");
            reportPath = Path.Combine(project, "Belge/D_AttackTests.md");
            Directory.CreateDirectory(Path.GetDirectoryName(statusPath));
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
            if (!Application.isPlaying)
            {
                File.WriteAllText(statusPath, "BLOCKED: Begin requires Play Mode with the built D scene loaded.");
                return;
            }
            running = true;
            passed = failed = 0;
            report.Clear();
            report.AppendLine("# D attack verification");
            report.AppendLine("\nExecuted UTC: " + DateTime.UtcNow.ToString("O"));
            report.AppendLine("\nIsolated local Physics2D scene; real player body/shield colliders and manual inputs.");
            File.WriteAllText(statusPath, "RUNNING");
            EditorApplication.delayCall += Run;
        }

        private static void Run()
        {
            try
            {
                BuildFixture();
                RunCase("Wave clears all attacks, refreshes five seconds, resumes immediately without backlog", WaveTiming);
                RunCase("Wave cancels warning and active laser synchronously", WaveCancelsLaser);
                RunCase("Correct missile parry: harmless deflection and no energy wave", MissileParry);
                RunCase("Left-facing missile parry uses the mirrored real shield", LeftMissileParry);
                RunCase("Airborne correct missile parry", AirborneMissileParry);
                RunCase("Wrong-facing missile parry fails", WrongMissileParry);
                RunCase("Held guard does not stop a missile", MissileIgnoresGuard);
                RunCase("Correct directional guard stops laser", LaserGuard);
                RunCase("Wrong directional guard fails against laser", WrongLaserGuard);
                RunCase("Parry does not stop a laser", LaserIgnoresParry);
                RunCase("Deflected missile never damages again", HarmlessDeflection);
                RunCase("Warning and active beam both track the real player", AimAndFiniteBeam);
                RunCase("Close-source geometry never cancels an authorized beam", UnsafeFireGeometry);
                RunCase("Laser warning minimum and variable bounded active duration", LaserDurations);
                RunCase("Actual orb path blocks before start only; authorized warning and beam persist", OrbFairness);
                RunCase("Orb path permission includes an actual wall reflection", OrbWallPrediction);
                RunCase("Opposite imminent orb/missile parries cancel missile; same side remains", OppositeParryFairness);
                RunCase("Independent spawn clock and at most three missiles", OneEpisode);
                RunCase("Difficulty clamps at new endpoints", DifficultyBounds);
                RunCase("Missile pulse and source provide .75-second reaction", MissileTelegraph);
                RunCase("Grounded laser requires support under the stopped footprint", GroundSupportPermission);
                RunCase("Airborne laser requires an actual supported landing", AirSupportPermission);
                RunCase("Laser start gap leaves the missile production clock running", LaserGapKeepsMissiles);
                RunCase("Wave ablation disables attack interruption only", WaveAblation);
            }
            catch (Exception ex)
            {
                failed++;
                report.AppendLine("\n- FAIL fixture: " + ex);
            }
            finally
            {
                Cleanup();
            }
        }

        private static void Cleanup()
        {
            if (root) Object.DestroyImmediate(root);
            if (scene.IsValid() && scene.isLoaded)
            {
                AsyncOperation unload = SceneManager.UnloadSceneAsync(scene);
                if (unload != null) { unload.completed += _ => Finish(); return; }
            }
            Finish();
        }

        private static void BuildFixture()
        {
            DPlayerMotor sourcePlayer = Object.FindAnyObjectByType<DPlayerMotor>();
            DOrbMotor sourceOrb = Object.FindAnyObjectByType<DOrbMotor>();
            if (!sourcePlayer || !sourceOrb)
                throw new InvalidOperationException("Load the built D scene before invoking Begin.");
            scene = SceneManager.CreateScene("D_AttackVerification_" + DateTime.UtcNow.Ticks,
                new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            physics = scene.GetPhysicsScene2D();
            root = new GameObject("D Attack Verification (isolated)");
            SceneManager.MoveGameObjectToScene(root, scene);
            player = Object.Instantiate(sourcePlayer, root.transform);
            player.ManualSimulation = player.UseExternalInput = true;
            player.Game = null;
            player.Body.interpolation = RigidbodyInterpolation2D.None;
            orb = Object.Instantiate(sourceOrb, root.transform);
            orb.ManualSimulation = true;
            orb.Player = player;
            orb.Game = null;
            orb.Effects = null;
            orb.CollisionMask = 0;
            orb.Freeze();

            GameObject floor = Child("Fixture floor");
            floor.layer = 8;
            floor.transform.position = new Vector3(0f, -.25f, 0f);
            floor.AddComponent<BoxCollider2D>().size = new Vector2(40f, .5f);
            director = Child("Fixture director").AddComponent<DAttackDirector>();
            director.ManualSimulation = true;
            director.Player = player;
            Camera camera = Child("Fixture camera").AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = 9.5f;
            camera.aspect = 16f / 9f;
            camera.transform.position = new Vector3(0f, 2f, -10f);
            director.ViewCamera = camera;
            director.Points = new[] { Point(-1), Point(1) };

            DMissile missile = Child("Missile template").AddComponent<DMissile>();
            missile.CollisionShape = missile.gameObject.AddComponent<CircleCollider2D>();
            missile.CollisionShape.radius = .16f;
            missile.gameObject.SetActive(false);
            director.MissilePrefab = missileTemplate = missile;
            DLaser laser = Child("Laser template").AddComponent<DLaser>();
            laser.Beam = laser.gameObject.AddComponent<LineRenderer>();
            laser.Beam.enabled = false;
            laser.gameObject.SetActive(false);
            director.LaserPrefab = laserTemplate = laser;

            game = Child("Fixture game").AddComponent<DGameController>();
            game.ManualSimulation = true;
            game.Level = Child("Level manager").AddComponent<DLevelManager>();
            game.Lava = Child("Lava controller").AddComponent<DLavaController>();
            game.Player = player;
            game.Attacks = director;
            game.Level.PlayerSpawn = Child("Player spawn").transform;
            game.Level.PlayerSpawn.position = new Vector3(0f, .9f, 0f);
            player.Game = game;
            director.Game = game;
            Physics2D.SyncTransforms();
        }

        private static GameObject Child(string name)
        {
            var value = new GameObject(name);
            value.transform.SetParent(root.transform, false);
            return value;
        }

        private static DAttackPoint Point(int side)
        {
            DAttackPoint point = Child(side < 0 ? "Left diagonal source" : "Right diagonal source").AddComponent<DAttackPoint>();
            point.Side = side;
            point.transform.position = new Vector3(side * 13f, 2.8f, 0f);
            return point;
        }

        private static void RunCase(string name, Action test)
        {
            try
            {
                director.Player = player;
                director.MissilePrefab = missileTemplate; director.LaserPrefab = laserTemplate;
                director.InitialMissileSpeed = 9f; director.MaximumMissileSpeed = 14f;
                director.InitialInterval = 2f; director.MinimumInterval = 1f;
                director.MinimumLaserStartGap = 7f;
                director.Orb = null;
                director.WaveEnabled = true;
                orb.Freeze();
                orb.CollisionMask = 0;
                game.BeginRun();
                Settle(1);
                test();
                passed++;
                report.AppendLine("\n- PASS: " + name);
            }
            catch (Exception ex)
            {
                failed++;
                report.AppendLine("\n- FAIL: " + name + " — " + ex.Message);
            }
            finally
            {
                director.StopAll();
                foreach (DMissile missile in root.GetComponentsInChildren<DMissile>()) missile.Cancel();
                foreach (DLaser laser in root.GetComponentsInChildren<DLaser>()) laser.Cancel();
            }
        }

        private static void Settle(int facing)
        {
            player.ResetAt(new Vector2(0f, .9f), facing);
            for (int i = 0; i < 30; i++) StepPlayer(Input(), .02f);
            Check(player.Grounded, "Fixture player did not settle on the real ground collider.");
        }

        private static DInputFrame Input(bool press = false, bool held = false, bool release = false)
            => new DInputFrame(0f, false, false, false, press, held, release);

        private static void StepPlayer(DInputFrame input, float dt)
        {
            player.SetInput(input);
            player.Step(dt);
            Physics2D.SyncTransforms();
            physics.Simulate(dt);
            Physics2D.SyncTransforms();
        }

        private static void Parry()
        {
            StepPlayer(Input(true, true), .02f);
            StepPlayer(Input(false, true), .06f);
            StepPlayer(Input(false, false, true), .001f);
            Check(player.ParryActive, "Short release did not create a parry.");
        }

        private static void Guard()
        {
            StepPlayer(Input(true, true), .02f);
            for (int i = 0; i < 15; i++) StepPlayer(Input(false, true), .02f);
            Check(player.GuardActive, "Stopped grounded hold did not enter guard.");
        }

        private static DMissile Missile(float side = 1f)
        {
            var missile = Object.Instantiate(director.MissilePrefab, director.transform);
            missile.gameObject.SetActive(true);
            missile.Initialize(director, player.Position + Vector2.right * (side * 2f), player.Position, 5f);
            return missile;
        }

        private static DLaser Laser(float duration = 1f)
        {
            var laser = Object.Instantiate(director.LaserPrefab, director.transform);
            laser.gameObject.SetActive(true);
            Vector2 source = player.Position + new Vector2(8f, 2f);
            laser.Initialize(director, source, player.Position + (player.Position - source).normalized * 4f, 1.3f, duration);
            return laser;
        }

        private static DLaser StartWarning()
        {
            director.ResetAttacks();
            director.Step(director.CurrentInterval + .01f);
            var first = director.GetComponentInChildren<DMissile>();
            Check(first && director.ActiveMissileCount == 1, "First episode did not instantiate a missile prefab.");
            first.Cancel();
            director.Step(.001f);
            director.Step(director.CurrentInterval + .01f);
            var laser = director.GetComponentInChildren<DLaser>();
            Check(laser && director.PendingWarningCount == 1, "Second episode did not instantiate a laser warning.");
            return laser;
        }

        private static void WaveTiming()
        {
            director.Step(director.CurrentInterval + .01f);
            game.OnOrbParry(player.Position);
            Check(director.ActiveMissileCount == 0 && director.PauseRemaining == 5f, "Wave did not clear missile and set five seconds.");
            director.Step(1.5f); Near(director.PauseRemaining, 3.5f, .0001f, "Pause countdown");
            game.OnOrbParry(player.Position); Near(director.PauseRemaining, 5f, .0001f, "Refresh replaces rather than adds");
            director.Step(4.999f);
            Check(director.PauseRemaining > 0f && director.SpawnCount == 1, "Spawn before five seconds.");
            director.Step(.001f);
            Check(director.PauseRemaining == 0f && director.SpawnCount == 2, "Eligible flow did not resume at five seconds.");
            for (int i = 0; i < 20; i++) { game.OnOrbParry(player.Position); director.Step(.2f); }
            Check(game.OrbParries == 22 && director.SpawnCount == 2, "Refresh created a backlog.");
            director.Step(100f);
            Check(director.SpawnCount == 3 && director.PauseRemaining == 0f, "Oversized expiry must produce only one eligible spawn.");
            game.OnOrbParry(player.Position);
            for (int i = 0; i < 249; i++) director.Step(.02f);
            Check(director.PauseRemaining > 0f && director.SpawnCount == 3, "Fixed-step pause expired before tick250.");
            director.Step(.02f);
            Check(director.PauseRemaining == 0f && director.SpawnCount == 4, "Float residue extended the five-second pause past tick250.");
        }

        private static void WaveCancelsLaser()
        {
            DLaser warning = StartWarning();
            game.OnOrbParry(player.Position);
            Check(warning.Finished && director.PendingWarningCount == 0, "Warning survived the wave.");
            DLaser active = StartWarning();
            Guard();
            director.Step(active.Remaining + .001f);
            Check(active.Active && director.ActiveLaserCount == 1, "Expected an active guarded laser.");
            game.OnOrbParry(player.Position);
            Check(active.Finished && director.ActiveLaserCount == 0 && director.PauseRemaining == 5f, "Active damage survived the wave.");
        }

        private static void MissileParry()
        {
            Parry();
            DMissile missile = Missile();
            missile.Step(.7f);
            Check(player.Alive && missile.Deflected, "Front-facing real shield contact was not parried.");
            Check(game.MissileParries == 1 && director.PauseRemaining == 0f, "Normal missile parry incorrectly triggered the energy wave.");
        }

        private static void AirborneMissileParry()
        {
            player.ResetAt(new Vector2(0f, 3f), 1);
            Parry();
            Check(!player.Grounded, "Expected airborne parry fixture.");
            DMissile missile = Missile();
            missile.Step(.7f);
            Check(player.Alive && missile.Deflected, "Airborne valid shield contact failed.");
        }

        private static void LeftMissileParry()
        {
            Settle(-1);
            Parry();
            DMissile missile = Missile(-1f);
            missile.Step(.7f);
            Check(player.Alive && missile.Deflected && game.MissileParries == 1,
                "Left-facing real shield contact failed to parry.");
        }

        private static void WrongMissileParry()
        {
            Settle(-1);
            Parry();
            Missile().Step(.7f);
            Check(!player.Alive, "Wrong-facing parry protected the body.");
        }

        private static void MissileIgnoresGuard()
        {
            Guard();
            Missile().Step(.7f);
            Check(!player.Alive, "Held guard incorrectly stopped a missile.");
        }

        private static void LaserGuard()
        {
            Guard(); DLaser laser = Laser(); laser.Step(1.299f);
            Check(laser.Warning && !laser.Active && player.Alive, "Laser fired before 1.3 seconds.");
            laser.Step(.002f); Check(laser.Active && player.Alive && laser.GuardBlocked, "Correct guard failed.");
            Check(Vector2.Distance(laser.Beam.GetPosition(1), laser.Target) < .001f, "Guard shortened the required viewport-crossing beam.");
        }

        private static void WrongLaserGuard()
        {
            Settle(-1);
            Guard();
            Laser().Step(1.301f);
            Check(!player.Alive, "Back-facing guard incorrectly protected against laser.");
        }

        private static void LaserIgnoresParry()
        {
            Parry();
            Laser().Step(1.301f);
            Check(!player.Alive, "Parry incorrectly protected against laser.");
        }

        private static void HarmlessDeflection()
        {
            DMissile missile = Missile(-1f);
            missile.Deflect(1);
            for (int i = 0; i < 8; i++) missile.Step(.1f);
            Check(player.Alive && missile.Deflected, "Deflected missile damaged the player while crossing the body.");
            missile.Step(.11f);
            Check(!missile.Active, "Wobbling deflection failed to expire.");
        }

        private static void AimAndFiniteBeam()
        {
            Guard(); DLaser laser = Laser(); Vector2 first = laser.AimPoint;
            player.Body.position += Vector2.left; player.transform.position = player.Body.position; Physics2D.SyncTransforms();
            laser.Step(.7f); Check(Vector2.Distance(first, laser.AimPoint) > .5f, "Warning did not track player.");
            laser.Step(.601f); Check(laser.Active && player.Alive, "Laser did not fire into correct guard.");
            Vector2 fired = laser.AimPoint;
            player.Body.position += Vector2.left; player.transform.position = player.Body.position; Physics2D.SyncTransforms();
            laser.Step(.02f);
            Check(player.Alive && laser.Active && Vector2.Distance(fired, laser.AimPoint) > .5f, "Active beam did not keep tracking.");
        }

        private static void UnsafeFireGeometry()
        {
            DLaser laser = Laser(); player.Body.position = laser.Source + Vector2.left;
            player.transform.position = player.Body.position; player.BodyCollider.enabled = false; Physics2D.SyncTransforms();
            laser.Step(1.301f); Check(laser.Active && !laser.Finished, "Close source cancelled authorized beam.");
            player.BodyCollider.enabled = true;
        }

        private static void LaserDurations()
        {
            Guard(); DLaser shortBeam = Laser(1f); shortBeam.Step(1.3f);
            Near(shortBeam.Remaining, 1f, .001f, "Short duration"); shortBeam.Step(1.01f); Check(shortBeam.Finished, "Short laser did not finish.");
            DLaser longBeam = Laser(1.8f); longBeam.Step(1.3f); Near(longBeam.Remaining, 1.8f, .001f, "Long duration");
            longBeam.Step(1.01f); Check(longBeam.Active, "Long laser ended too early."); longBeam.Step(.8f); Check(longBeam.Finished, "Long laser exceeded duration.");
        }

        private static void OrbFairness()
        {
            DLaser warning = StartWarning(); Guard(); director.Orb = orb;
            orb.ResetOrb(player.Position + Vector2.right * 3f, Vector2.left); director.Step(.01f);
            Check(warning.Warning && !warning.Finished, "Orb approach cancelled authorized warning.");
            director.Step(warning.Remaining + .001f);
            Check(warning.Active && director.LaserFireCount == 1, "Warning did not fire/count once.");
            director.Step(.01f); Check(warning.Active && player.Alive, "Orb proximity interrupted active beam.");
            orb.Freeze(); director.ResetAttacks(); director.Step(director.CurrentInterval + .01f);
            director.CurrentMissile.Cancel(); director.Step(.001f); director.MissilePrefab = null;
            orb.ResetOrb(player.Position + Vector2.right * 3f, Vector2.left); director.Step(director.CurrentInterval + .01f);
            Check(director.LaserSpawnCount == 0 && director.PendingWarningCount == 0, "Actual projected path did not postpone laser.");
            orb.ResetOrb(player.Position + new Vector2(3f, 4f), Vector2.left); director.Step(.11f);
            Check(director.LaserSpawnCount == 1 && director.PendingWarningCount == 1, "A vertical near miss starved the laser.");
        }

        private static void OrbWallPrediction()
        {
            director.Step(director.CurrentInterval + .01f);
            director.CurrentMissile.Cancel(); director.Step(.001f); director.MissilePrefab = null;
            var wall = Child("Prediction wall"); wall.layer = 9;
            wall.transform.position = player.Position + new Vector2(12.5f, 0f);
            wall.AddComponent<BoxCollider2D>().size = new Vector2(.5f, 12f);
            orb.CollisionMask = 1 << 9; director.Orb = orb;
            orb.ResetOrb(player.Position + Vector2.right * 8f, Vector2.right);
            Physics2D.SyncTransforms(); director.Step(director.CurrentInterval + .01f);
            Check(director.LaserSpawnCount == 0, "Outgoing orb was ignored despite its imminent wall-return path.");
            orb.ResetOrb(player.Position + new Vector2(8f, 5f), Vector2.right);
            director.Step(.11f);
            Check(director.LaserSpawnCount == 1, "A reflected path five units above the body wrongly starved the laser.");
            wall.SetActive(false);
        }

        private static void OneEpisode()
        {
            director.LaserPrefab = null; director.InitialInterval = director.MinimumInterval = .2f; director.ResetAttacks();
            director.Step(.2f); float first = director.LastSpawnTime; director.Step(.2f); float second = director.LastSpawnTime;
            director.Step(.2f); float third = director.LastSpawnTime;
            Check(director.ActiveMissileCount == 3 && director.SpawnCount == 3, "Living missiles blocked independent production.");
            Near(second - first, .2f, .001f, "First actual spawn gap"); Near(third - second, .2f, .001f, "Second actual spawn gap");
            director.Step(.2f); Check(director.ActiveMissileCount == 3 && director.SpawnCount == 3, "Overlap exceeded three.");
            director.TriggerEnergyWave(); Check(director.ActiveMissileCount == 0 && director.Missiles.Count == 0 && director.CancelledCount == 3,
                "Wave failed to clear every missile.");
        }

        private static void OppositeParryFairness()
        {
            director.Orb = orb;
            director.Step(director.CurrentInterval + .01f);
            DMissile current = director.CurrentMissile;
            Check(current, "Expected current missile reference.");
            current.TelegraphSeconds = 0f;
            current.Initialize(director, player.Position + Vector2.left * 2f, player.Position, 5f);
            orb.ResetOrb(player.Position + Vector2.right * 2.5f, Vector2.left);
            director.Step(.01f);
            Check(!current.Active && director.ActiveMissileCount == 0 && player.Alive,
                "Opposite imminent parries were not resolved before the missile contact.");

            director.ResetAttacks();
            director.Step(director.CurrentInterval + .01f);
            current = director.CurrentMissile;
            current.TelegraphSeconds = 0f;
            current.Initialize(director, player.Position + Vector2.left * 2f, player.Position, 5f);
            orb.ResetOrb(player.Position + Vector2.left * 2.5f, Vector2.right);
            director.Step(.01f);
            Check(current.Active && director.ActiveMissileCount == 1,
                "Same-side pressure was incorrectly treated as contradictory defense.");
        }

        private static void DifficultyBounds()
        {
            director.Player = null; director.ResetAttacks();
            Near(director.CurrentMissileSpeed, 9f, .001f, "Initial speed"); Near(director.CurrentInterval, 2f, .001f, "Initial interval");
            director.Step(100000f); Near(director.CurrentMissileSpeed, 14f, .001f, "Maximum speed"); Near(director.CurrentInterval, 1f, .001f, "Minimum interval");
        }

        private static void MissileTelegraph()
        {
            director.Step(120f); DMissile value = director.CurrentMissile; Check(value, "Expected high-difficulty missile.");
            float clearance = player.BodyCollider.bounds.extents.magnitude + .2f;
            float reaction = value.LaunchDelayRemaining + (Vector2.Distance(value.Position, player.Position) - clearance)
                / (director.CurrentMissileSpeed + player.MaxSpeed);
            Check(reaction >= .75f, "Charge/source reaction is below .75 seconds at maximum closing speed.");
            Vector2 initial = value.Position; value.Step(.29f); Near(Vector2.Distance(initial, value.Position), 0f, .0001f, "Telegraph position");
            value.Step(.02f); Check(Vector2.Distance(initial, value.Position) > .01f, "Missile failed to launch after pulse.");
        }

        private static void WaveAblation()
        {
            director.Step(director.CurrentInterval + .01f);
            director.WaveEnabled = false;
            game.OnOrbParry(player.Position);
            Check(game.OrbParries == 1 && director.ActiveMissileCount == 1 && director.PauseRemaining == 0f,
                "Ablation failed to preserve the orb callback while disabling the attack-wave connection.");
            director.WaveEnabled = true;
        }

        private static void GroundSupportPermission()
        {
            Collider2D floor = root.transform.Find("Fixture floor").GetComponent<Collider2D>();
            var pad = Child("Short stopping support"); pad.layer = 8;
            pad.transform.position = new Vector3(0f, -.25f, 0f);
            pad.AddComponent<BoxCollider2D>().size = new Vector2(2.4f, .5f);
            try
            {
                floor.enabled = false; player.Body.linearVelocity = Vector2.right * 6.8f;
                director.MissilePrefab = null; Physics2D.SyncTransforms();
                director.Step(director.CurrentInterval + .01f);
                Check(director.LaserSpawnCount == 0 && director.LastLaserBlockReason == "NoSupportedGuardStop",
                    "Laser authorized a braking position beyond the actual pad.");
                player.Body.linearVelocity = Vector2.zero; director.Step(.11f);
                Check(director.LaserSpawnCount == 1, "Supported stationary guard position failed to resume laser permission.");
            }
            finally { floor.enabled = true; pad.SetActive(false); player.Body.linearVelocity = Vector2.zero; }
        }

        private static void AirSupportPermission()
        {
            Collider2D floor = root.transform.Find("Fixture floor").GetComponent<Collider2D>();
            try
            {
                // Source bank is at y=2.8: y=3.5 retains the required .35+ diagonal difference.
                floor.enabled = false; player.ResetAt(new Vector2(0f, 3.5f));
                player.Body.linearVelocity = Vector2.down; director.MissilePrefab = null;
                Physics2D.SyncTransforms(); director.Step(director.CurrentInterval + .01f);
                Check(director.LaserSpawnCount == 0 && director.LastLaserBlockReason == "NoSupportedGuardStop",
                    "Airborne warning authorized a landing without ground geometry.");
                floor.enabled = true; Physics2D.SyncTransforms(); director.Step(.11f);
                Check(director.LaserSpawnCount == 1 && director.CurrentLaser.Warning,
                    "Valid airborne landing was continuously vetoed: " + director.LastLaserBlockReason);
                Near(director.CurrentLaser.Remaining, director.MaximumLaserWarning, .001f, "Airborne warning uses full landing allowance");
            }
            finally { floor.enabled = true; }
        }

        private static void LaserGapKeepsMissiles()
        {
            director.InitialInterval = director.MinimumInterval = .5f;
            DLaser first = StartWarning(); float started = director.LastLaserSpawnTime;
            first.Cancel(); director.Step(.001f);
            director.Step(.5f); director.CurrentMissile.Cancel(); director.Step(.001f);
            int before = director.SpawnCount; float previous = director.LastSpawnTime;
            director.Step(.5f);
            Check(director.LaserSpawnCount == 1 && director.LastLaserBlockReason == "LaserStartGap",
                "Second laser ignored its independent warning-start spacing.");
            Check(director.SpawnCount == before + 1 && director.LastSpawnKind == "Missile",
                "Laser spacing created empty global spawn time.");
            Near(director.LastSpawnTime - previous, .501f, .002f, "Missile spawn gap while laser cooling down");
            Check(director.LastLaserSpawnTime == started, "Missile spawning reset the laser start timestamp.");
        }

        private static void Near(float value, float expected, float tolerance, string label)
            => Check(Mathf.Abs(value - expected) <= tolerance, label + ": expected " + expected + ", got " + value);

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void Finish()
        {
            report.AppendLine("\nResult: " + passed + " passed, " + failed + " failed.");
            report.AppendLine("\nThis harness does not claim full-route playability; route and native-build verification are separate.");
            File.WriteAllText(reportPath, report.ToString());
            File.WriteAllText(statusPath, (failed == 0 ? "PASS" : "FAIL") + ": " + passed + " passed, " + failed + " failed.\n" + reportPath);
            running = false;
        }
    }
}
#endif
