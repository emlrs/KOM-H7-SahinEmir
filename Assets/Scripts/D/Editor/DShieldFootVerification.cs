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
    public static class DShieldFootVerification
    {
        private static Scene scene;
        private static PhysicsScene2D physics;
        private static GameObject root;
        private static DPlayerMotor player;
        private static DAttackDirector director;
        private static readonly StringBuilder report = new StringBuilder();
        private static string statusPath, reportPath;
        private static int passed, failed;
        private static bool running;

        [MenuItem("D/Verify/Shield front foot (Play Mode)")]
        public static void Begin()
        {
            if (running) return;
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            statusPath = Path.Combine(project, "Temp/D_Birlesik/shield-foot-status.txt");
            reportPath = Path.Combine(project, "Belge/D_Duzeltme/D_ShieldFootTests.md");
            Directory.CreateDirectory(Path.GetDirectoryName(statusPath));
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
            if (!Application.isPlaying)
            { File.WriteAllText(statusPath, "BLOCKED: Load the saved D scene in Play Mode."); return; }
            running = true;
            passed = failed = 0;
            report.Clear();
            report.AppendLine("# D front-foot shield verification\n\nUTC: " + DateTime.UtcNow.ToString("O"));
            report.AppendLine("\nIsolated Physics2D scene; clone of the saved scene's actual player, capsule and shield.");
            report.AppendLine("The low missile reproduces the 35.0606-second trace's 8.90-degree downward front trajectory.");
            report.AppendLine("Only the fixture missile's launch delay is disabled; production collision and defense methods are used unchanged.");
            File.WriteAllText(statusPath, "RUNNING");
            EditorApplication.delayCall += Run;
        }

        private static void Run()
        {
            try
            {
                BuildFixture();
                RunCase("Saved collider and visible last arc include the same lower-front extension", SavedShape);
                RunCase("Real left and right shallow front-foot missiles are parried", FrontFoot);
                RunCase("Wrong-facing parry still dies to the same low front trajectory", WrongSide);
                RunCase("The same foot contact without parry remains lethal", NoParry);
                RunCase("Held guard cannot parry a low missile", GuardDoesNotParry);
                RunCase("Overhead crown still parries a descending missile", Crown);
                RunCase("The open underside remains vulnerable during parry", OpenUnderside);
                RunCase("Left/right held guard still stops laser damage", LaserGuard);
                RunCase("Parry still cannot stop laser damage", LaserIgnoresParry);
            }
            catch (Exception ex) { failed++; report.AppendLine("\n- FAIL fixture: " + ex); }
            finally
            {
                if (root) Object.DestroyImmediate(root);
                if (scene.IsValid() && scene.isLoaded)
                {
                    AsyncOperation unload = SceneManager.UnloadSceneAsync(scene);
                    if (unload != null) unload.completed += _ => Finish();
                    else Finish();
                }
                else Finish();
            }
        }

        private static void BuildFixture()
        {
            DPlayerMotor source = Object.FindAnyObjectByType<DPlayerMotor>();
            if (!source) throw new InvalidOperationException("Load the authored D scene first.");
            scene = SceneManager.CreateScene("D_ShieldFoot_" + DateTime.UtcNow.Ticks,
                new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            physics = scene.GetPhysicsScene2D();
            root = new GameObject("D Shield Foot Verification (isolated)");
            SceneManager.MoveGameObjectToScene(root, scene);
            player = Object.Instantiate(source, root.transform);
            player.ManualSimulation = player.UseExternalInput = true;
            player.Game = null;
            player.Body.interpolation = RigidbodyInterpolation2D.None;
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
            camera.orthographicSize = 6f;
            camera.aspect = 16f / 9f;
            camera.transform.position = new Vector3(0f, 2f, -10f);
            director.ViewCamera = camera;
        }

        private static GameObject Child(string name)
        {
            var value = new GameObject(name);
            value.transform.SetParent(root.transform, false);
            return value;
        }

        private static void RunCase(string name, Action action)
        {
            try { Settle(1); action(); passed++; report.AppendLine("\n- PASS: " + name); }
            catch (Exception ex) { failed++; report.AppendLine("\n- FAIL: " + name + " — " + ex.Message); }
            finally
            {
                foreach (DMissile missile in root.GetComponentsInChildren<DMissile>(true))
                    if (missile) Object.DestroyImmediate(missile.gameObject);
                foreach (DLaser laser in root.GetComponentsInChildren<DLaser>(true))
                    if (laser) Object.DestroyImmediate(laser.gameObject);
            }
        }

        private static void Drive(bool press = false, bool held = false, bool release = false, float dt = .02f)
        {
            player.SetInput(new DInputFrame(0f, false, false, false, press, held, release));
            player.Step(dt);
            Physics2D.SyncTransforms();
            physics.Simulate(dt);
            Physics2D.SyncTransforms();
        }

        private static void Settle(int facing)
        {
            player.ResetAt(new Vector2(0f, .9f), facing);
            for (int i = 0; i < 30; i++) Drive();
            Check(player.Grounded, "Real player capsule did not settle on the floor.");
        }

        private static void Parry()
        {
            Drive(true, true);
            Drive(false, true, false, .06f);
            Drive(false, false, true, .001f);
            Check(player.ParryActive && !player.GuardActive, "Short release did not create parry.");
        }

        private static void Guard()
        {
            Drive(true, true);
            for (int i = 0; i < 15; i++) Drive(false, true);
            Check(player.GuardActive && !player.ParryActive, "Stationary held input did not enter guard.");
        }

        private static DMissile Missile(Vector2 offset, Vector2 velocity)
        {
            DMissile missile = Child("Fixture missile").AddComponent<DMissile>();
            missile.CollisionShape = missile.gameObject.AddComponent<CircleCollider2D>();
            missile.CollisionShape.radius = .16f;
            missile.TelegraphSeconds = 0f;
            Vector2 source = player.Position + offset;
            missile.Initialize(director, source, source + velocity, velocity.magnitude);
            return missile;
        }

        private static DMissile FootMissile(int sourceSide)
        {
            // Trace's last pre-contact center was (+/-.4599,-.8153). Start .08 seconds earlier
            // along that exact trajectory, so the real circle sweep begins clear of both shapes.
            return Missile(new Vector2(sourceSide * 1.28234f, -.686476f),
                new Vector2(-sourceSide * 10.2805f, -1.6103f));
        }

        private static DLaser Laser(int sourceSide = 1)
        {
            DLaser laser = Child("Fixture laser").AddComponent<DLaser>();
            laser.Beam = laser.gameObject.AddComponent<LineRenderer>();
            laser.Initialize(director, player.Position + new Vector2(sourceSide * 8f, 2f), player.Position, .02f, 1f);
            return laser;
        }

        private static void SavedShape()
        {
            var polygon = player.ShieldCollider as PolygonCollider2D;
            Check(polygon && polygon.points.Length == 14, "Saved shield polygon is missing.");
            Near(polygon.points[6], new Vector2(.35f, -.78f), "Saved outer tip");
            Near(polygon.points[7], new Vector2(.24f, -.70f), "Saved inner tip");
            Transform visible = polygon.transform.Find("Shield arc 5");
            Check(visible, "Saved lower-front visual arc is missing.");
            Vector2 start = new Vector2(.58f, -.28f), end = new Vector2(.295f, -.74f);
            Near(visible.localPosition, (start + end) * .5f, "Visible arc midpoint");
            Check(Mathf.Abs(visible.localScale.x - ((end - start).magnitude + .035f)) < .0001f,
                "Visible arc length does not match the extended polygon.");
            Vector2 drawnDirection = visible.localRotation * Vector2.right;
            Near(drawnDirection, (end - start).normalized, "Visible arc direction");
            float before = player.Position.y;
            Guard();
            Check(Mathf.Abs(player.Position.y - before) < .01f, "Extended shield lifted the body off the floor.");
        }

        private static void FrontFoot()
        {
            foreach (int side in new[] { -1, 1 })
            {
                Settle(side); Parry();
                DMissile missile = FootMissile(side);
                missile.Step(.10f);
                Check(player.Alive && missile.Deflected, "Correct front-foot parry failed for facing " + side);
                Check(director.PauseRemaining == 0f, "Missile parry incorrectly triggered an energy wave.");
            }
        }

        private static void WrongSide()
        {
            Settle(-1); Parry();
            FootMissile(1).Step(.10f);
            Check(!player.Alive, "Wrong-facing parry protected the lower-front body contact.");
        }

        private static void NoParry()
        {
            FootMissile(1).Step(.10f);
            Check(!player.Alive, "Low body contact became harmless without defense.");
        }

        private static void GuardDoesNotParry()
        {
            Guard();
            FootMissile(1).Step(.10f);
            Check(!player.Alive, "Held guard incorrectly stopped a normal missile.");
        }

        private static void Crown()
        {
            Parry();
            DMissile missile = Missile(Vector2.up * 2f, Vector2.down * 10f);
            missile.Step(.2f);
            Check(player.Alive && missile.Deflected, "Existing overhead crown stopped working.");
        }

        private static void OpenUnderside()
        {
            Parry();
            Missile(Vector2.down * 2f, Vector2.up * 10f).Step(.2f);
            Check(!player.Alive, "Lower-front extension closed the intentionally open underside.");
        }

        private static void LaserGuard()
        {
            foreach (int side in new[] { -1, 1 })
            {
                Settle(side); Guard();
                DLaser laser = Laser(side);
                laser.Step(.02f);
                Check(player.Alive && laser.Active && laser.GuardBlocked, "Correct guard failed against laser.");
                laser.Cancel();
            }
        }

        private static void LaserIgnoresParry()
        {
            Parry(); Laser().Step(.02f);
            Check(!player.Alive, "Parry incorrectly protected the body from laser damage.");
        }

        private static void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
        private static void Near(Vector2 actual, Vector2 expected, string message)
            => Check(Vector2.Distance(actual, expected) < .0001f, message + ": " + actual + " expected " + expected);
        private static void Finish()
        {
            report.AppendLine("\nResult: " + passed + " passed, " + failed + " failed.");
            File.WriteAllText(reportPath, report.ToString());
            File.WriteAllText(statusPath, failed == 0 ? "PASS " + passed : "FAIL " + failed + " / " + (passed + failed));
            Debug.Log("D shield-foot verification: " + passed + " passed, " + failed + " failed. " + reportPath);
            running = false;
        }
    }
}
#endif
