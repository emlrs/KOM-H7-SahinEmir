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
    public static class DLaserFixVerification
    {
        private static Scene scene;
        private static PhysicsScene2D physics;
        private static GameObject root;
        private static DPlayerMotor player;
        private static DAttackDirector director;
        private static Camera camera;
        private static readonly StringBuilder report = new StringBuilder();
        private static string statusPath, reportPath;
        private static int passed, failed;
        private static bool running;

        [MenuItem("D/Verify/Laser corrections (Play Mode)")]
        public static void Begin()
        {
            if (running) return;
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            statusPath = Path.Combine(project, "Temp/D_Birlesik/laser-fix-status.txt");
            reportPath = Path.Combine(project, "Belge/D_Duzeltme/D_LaserFixTests.md");
            Directory.CreateDirectory(Path.GetDirectoryName(statusPath));
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
            if (!Application.isPlaying)
            {
                File.WriteAllText(statusPath, "BLOCKED: Run in Play Mode with the built D scene loaded.");
                return;
            }
            running = true;
            passed = failed = 0;
            report.Clear();
            report.AppendLine("# D laser correction verification");
            report.AppendLine("\nUTC: " + DateTime.UtcNow.ToString("O"));
            report.AppendLine("\nIsolated local Physics2D scene; authored body/shield colliders and real CircleCast contacts.");
            report.AppendLine("Only the sustained motion test disables GuardLocksMovement on its isolated player copy.");
            File.WriteAllText(statusPath, "RUNNING");
            EditorApplication.delayCall += Run;
        }

        private static void Run()
        {
            try
            {
                BuildFixture();
                RunCase("Configured 1.3 warning / 1.8 active reach exact phase boundaries", ExactTiming);
                RunCase("Repeated .02 fixed steps do not add a stray frame to either phase", FixedStepTiming);
                RunCase("Other authored durations are preserved; no hidden minimum or cap", ConfiguredTiming);
                RunCase("Overshoot is transferred once and cannot extend the episode", Overshoot);
                RunCase("Offscreen source still fires and completes its full active duration", OffscreenSource);
                RunCase("Full ray exits all four visible edges, including outside sources", VisibleBoundaries);
                RunCase("Camera position, zoom and aspect are read again each step", DynamicCamera);
                RunCase("Active beam follows a moving real guarded player every physics step", ActiveTracking);
                RunCase("Correct left/right guard prevents damage without clipping the beam", CorrectGuard);
                RunCase("Steep offscreen diagonals respect the real crown, front and open underside", SteepGuard);
                RunCase("Wrong-direction guard fails on the real body collider", WrongGuard);
                RunCase("Walls do not block laser collision or visible length", WallIgnore);
                RunCase("New aim, drawn segment and lethal damage use the same direction", TrackingDamage);
                RunCase("Explicit cancellation disables the beam and freezes its clocks", ExplicitCancel);
            }
            catch (Exception ex)
            {
                failed++;
                report.AppendLine("\n- FAIL fixture: " + ex);
            }
            finally
            {
                if (root) Object.DestroyImmediate(root);
                if (scene.IsValid() && scene.isLoaded)
                {
                    AsyncOperation unload = SceneManager.UnloadSceneAsync(scene);
                    if (unload != null) { unload.completed += _ => Finish(); }
                    else Finish();
                }
                else Finish();
            }
        }

        private static void BuildFixture()
        {
            DPlayerMotor source = Object.FindAnyObjectByType<DPlayerMotor>();
            if (!source) throw new InvalidOperationException("Load the authored D scene before Begin.");
            scene = SceneManager.CreateScene("D_LaserFixVerification_" + DateTime.UtcNow.Ticks,
                new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            physics = scene.GetPhysicsScene2D();
            root = new GameObject("D Laser Verification (isolated)");
            SceneManager.MoveGameObjectToScene(root, scene);
            player = Object.Instantiate(source, root.transform);
            player.ManualSimulation = player.UseExternalInput = true;
            player.Game = null;
            player.Body.interpolation = RigidbodyInterpolation2D.None;
            var floor = Child("Fixture floor");
            floor.layer = 8;
            floor.transform.position = new Vector3(0f, -.25f, 0f);
            floor.AddComponent<BoxCollider2D>().size = new Vector2(80f, .5f);
            director = Child("Fixture director").AddComponent<DAttackDirector>();
            director.ManualSimulation = true;
            director.Player = player;
            camera = Child("Fixture camera").AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            director.ViewCamera = camera;
            Physics2D.SyncTransforms();
        }

        private static GameObject Child(string name)
        {
            var value = new GameObject(name);
            value.transform.SetParent(root.transform, false);
            return value;
        }

        private static void RunCase(string name, Action action)
        {
            try
            {
                camera.transform.position = new Vector3(0f, 2f, -10f);
                camera.orthographicSize = 6f;
                camera.aspect = 16f / 9f;
                player.GuardLocksMovement = player.RequireStoppedGuard = true;
                Settle(1);
                action();
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
                player.GuardLocksMovement = player.RequireStoppedGuard = true;
                foreach (DLaser laser in root.GetComponentsInChildren<DLaser>(true))
                    if (laser) Object.DestroyImmediate(laser.gameObject);
                Transform wall = root.transform.Find("Fixture laser wall");
                if (wall) Object.DestroyImmediate(wall.gameObject);
            }
        }

        private static DInputFrame Input(bool press = false, bool held = false, bool release = false, float move = 0f)
            => new DInputFrame(move, false, false, false, press, held, release);

        private static void StepPlayer(DInputFrame input, float dt)
        {
            player.SetInput(input);
            player.Step(dt);
            Physics2D.SyncTransforms();
            physics.Simulate(dt);
            Physics2D.SyncTransforms();
        }

        private static void Settle(int facing)
        {
            player.ResetAt(new Vector2(0f, .9f), facing);
            for (int i = 0; i < 30; i++) StepPlayer(Input(), .02f);
            Check(player.Grounded, "Player did not settle on the fixture's ground collider.");
        }

        private static void Guard()
        {
            StepPlayer(Input(true, true), .02f);
            for (int i = 0; i < 15; i++) StepPlayer(Input(false, true), .02f);
            Check(player.GuardActive, "Real held input did not enter stationary grounded guard.");
        }

        private static DLaser Laser(float warning = 1.3f, float duration = 1.8f, Vector2? source = null)
        {
            DLaser laser = Child("Fixture laser").AddComponent<DLaser>();
            laser.Beam = laser.gameObject.AddComponent<LineRenderer>();
            laser.Initialize(director, source ?? player.Position + new Vector2(8f, 2f),
                player.Position, warning, duration);
            return laser;
        }

        private static void ExactTiming()
        {
            Guard();
            DLaser laser = Laser();
            laser.Step(1.2999f);
            Check(laser.Warning && !laser.Active && player.Alive, "Fired before configured warning ended.");
            laser.Step(laser.Remaining);
            Check(laser.Active && !laser.Warning && player.Alive, "Warning failed to become active at its boundary.");
            Near(laser.Remaining, 1.8f, .00001f, "Active budget at transition");
            Near(laser.Elapsed, 1.3f, .00001f, "Warning elapsed");
            laser.Step(1.7999f);
            Check(laser.Active, "Active beam ended before its configured duration.");
            laser.Step(laser.Remaining);
            Check(laser.Finished && !laser.Active && !laser.Beam.enabled, "Beam did not end at its exact active boundary.");
            Near(laser.Elapsed, 3.1f, .00001f, "Total duration");
            Near(laser.ActiveElapsed, 1.8f, .00001f, "Exact active duration");
        }

        private static void ConfiguredTiming()
        {
            Guard();
            DLaser laser = Laser(.4f, 2.3f);
            Near(laser.Remaining, .4f, .00001f, "Configured warning");
            laser.Step(.4f);
            Check(laser.Active && player.Alive, "A hidden warning minimum delayed activation.");
            Near(laser.Remaining, 2.3f, .00001f, "Configured active duration");
            laser.Step(1.81f);
            Check(laser.Active, "A hidden maximum shortened the active phase.");
            laser.Step(laser.Remaining);
            Check(laser.Finished, "Configured duration did not finish.");
        }

        private static void FixedStepTiming()
        {
            Guard();
            DLaser laser = Laser(1.7f, 1.8f);
            for (int i = 0; i < 84; i++) laser.Step(.02f);
            Check(laser.Warning, "Repeated fixed steps shortened the warning.");
            laser.Step(.02f);
            Check(laser.Active, "Floating-point accumulation added an extra warning frame.");
            Near(laser.Remaining, 1.8f, .000001f, "Exact fixed-step transition");
            for (int i = 0; i < 89; i++) laser.Step(.02f);
            Check(laser.Active, "Repeated fixed steps shortened the active phase.");
            laser.Step(.02f);
            Check(laser.Finished, "Floating-point accumulation added an extra active frame.");
            Near(laser.Elapsed, 3.5f, .000001f, "Exact fixed-step episode duration");
        }

        private static void Overshoot()
        {
            Guard();
            DLaser laser = Laser();
            laser.Step(2f);
            Check(laser.Active && player.Alive, "Overshooting the warning skipped active damage/guard.");
            Near(laser.ActiveElapsed, .7f, .00001f, "Transferred warning overshoot");
            Near(laser.Remaining, 1.1f, .00001f, "Remaining active time");
            laser.Step(5f);
            Check(laser.Finished, "Oversized step failed to finish.");
            Near(laser.Elapsed, 3.1f, .00001f, "Elapsed must stop at configured episode end");
        }

        private static void OffscreenSource()
        {
            Guard();
            DLaser laser = Laser();
            Vector2 actualSource = laser.Source;
            camera.aspect = 1f;
            camera.transform.position = new Vector3(-5f, 2f, -10f);
            Check(camera.WorldToViewportPoint(actualSource).x > 1f, "Fixture source should be offscreen.");
            laser.Step(laser.Remaining);
            Check(laser.Active && player.Alive, "Offscreen source cancelled the warning-to-active transition.");
            VerifySegment(laser);
            camera.aspect = .8f;
            camera.transform.position = new Vector3(-4f, 2f, -10f);
            laser.Step(.75f);
            Check(laser.Active && player.Alive, "Offscreen source cancelled an active beam.");
            Near(Vector2.Distance(laser.Source, actualSource), 0f, .00001f, "Real source remains fixed");
            VerifySegment(laser);
            laser.Step(laser.Remaining);
            Check(laser.Finished, "Offscreen source did not complete its duration.");
            Near(laser.ActiveElapsed, 1.8f, .00001f, "Full offscreen active duration");
        }

        private static void VisibleBoundaries()
        {
            Vector2[] sources =
            {
                new Vector2(8f, 3f), new Vector2(-15f, 5f), new Vector2(0f, 12f),
                new Vector2(0f, -10f), new Vector2(18f, 10f), new Vector2(-18f, -10f)
            };
            foreach (Vector2 source in sources)
            {
                DLaser laser = Laser(10f, 1f, source);
                laser.Step(.02f);
                VerifySegment(laser);
                Near(Vector2.Distance(laser.Beam.GetPosition(0), source), 0f, .0001f,
                    "Full visible segment must retain the real source");
                laser.Cancel();
            }
        }

        private static void DynamicCamera()
        {
            DLaser laser = Laser(10f);
            Vector2 oldEnd = laser.Target;
            camera.orthographicSize = 4f;
            camera.aspect = 1f;
            camera.transform.position = new Vector3(-2f, 1.5f, -10f);
            laser.Step(.02f);
            VerifySegment(laser);
            Check(Vector2.Distance(oldEnd, laser.Target) > 1f, "Beam reused stale camera dimensions.");
            player.ResetAt(new Vector2(-1f, 2.5f));
            Physics2D.SyncTransforms();
            laser.Step(.02f);
            VerifySegment(laser);
        }

        private static void ActiveTracking()
        {
            Guard();
            DLaser laser = Laser(.2f);
            laser.Step(.2f);
            Vector2 firstAim = laser.AimPoint;
            Vector2 firstEnd = laser.Target;
            // Controlled test only: the delivered motor still locks movement during guard.
            player.GuardLocksMovement = false;
            for (int i = 0; i < 20; i++)
            {
                StepPlayer(Input(false, true, false, 1f), .02f);
                laser.Step(.02f);
                Check(player.Alive && laser.Active && laser.GuardBlocked, "Moving real guard did not intercept the tracked ray.");
                VerifySegment(laser);
                Near(Vector2.Distance(laser.LastDamageTarget, laser.Target), 0f, .00001f, "Damage and render endpoint");
            }
            Check(Vector2.Distance(firstAim, laser.AimPoint) > 1f, "Fixture did not produce real Rigidbody motion.");
            Check(Vector2.Distance(firstEnd, laser.Target) > .1f, "Active endpoint stayed locked while player moved.");
        }

        private static void CorrectGuard()
        {
            foreach (int side in new[] { -1, 1 })
            {
                Settle(side);
                Guard();
                DLaser laser = Laser(.2f, 1f, player.Position + new Vector2(side * 8f, 2f));
                laser.Step(.2f);
                Check(player.Alive && laser.Active && laser.GuardBlocked, "Correct-facing real shield failed.");
                VerifySegment(laser);
                Check(Vector2.Distance(laser.Target, player.Position) > 2f, "Guard clipped the endpoint to the player/shield.");
                laser.Cancel();
            }
        }

        private static void WrongGuard()
        {
            Settle(-1);
            Guard();
            DLaser laser = Laser(.2f);
            laser.Step(.2f);
            Check(!player.Alive && !laser.GuardBlocked, "Back-facing guard incorrectly protected the real body.");
            laser.Step(.02f);
            Check(laser.Finished, "Player death did not terminate the next laser step.");
        }

        private static void SteepGuard()
        {
            Vector2[] protectedAngles =
            {
                new Vector2(.3f, 20f), new Vector2(5f, 20f), new Vector2(20f, -22f)
            };
            foreach (int side in new[] { -1, 1 })
            {
                foreach (Vector2 offset in protectedAngles)
                {
                    Settle(side);
                    Guard();
                    Vector2 source = player.Position + new Vector2(side * offset.x, offset.y);
                    Check(player.CanGuardFrom(source), "Protected steep angle failed the directional rule: " + offset);
                    DLaser laser = Laser(.2f, 1f, source);
                    laser.Step(.2f);
                    Check(player.Alive && laser.GuardBlocked,
                        "Physical shield missed a permitted steep angle (side " + side + "): " + offset);
                    VerifySegment(laser);
                    laser.Cancel();
                }
            }
            Settle(-1);
            Guard();
            Laser(.2f, 1f, player.Position + new Vector2(5f, 20f)).Step(.2f);
            Check(!player.Alive, "Wrong-facing guard incorrectly blocked the steep offscreen diagonal.");
            Settle(1);
            Guard();
            Vector2 underside = player.Position + new Vector2(2f, -20f);
            Check(!player.CanGuardFrom(underside), "The visibly open underside must remain unprotected.");
            Laser(.2f, 1f, underside).Step(.2f);
            Check(!player.Alive, "A steep underside ray passed through the body without damage.");
        }

        private static void WallIgnore()
        {
            GameObject wall = Child("Fixture laser wall");
            wall.layer = player.BodyCollider.gameObject.layer; // Included in the query, deliberately ignored by identity.
            wall.transform.position = new Vector3(4f, 2f, 0f);
            wall.AddComponent<BoxCollider2D>().size = new Vector2(.5f, 6f);
            Physics2D.SyncTransforms();
            DLaser laser = Laser(.2f);
            laser.Step(.2f);
            Check(!player.Alive, "A world wall blocked damage before it reached the body.");
            VerifySegment(laser);
        }

        private static void TrackingDamage()
        {
            Guard();
            DLaser laser = Laser(.2f);
            laser.Step(.2f);
            Vector2 oldDirection = (laser.Target - laser.Source).normalized;
            player.ResetAt(new Vector2(-3f, 4f), 1);
            Physics2D.SyncTransforms();
            Vector2 relative = player.Position - laser.Source;
            float oldClearance = Mathf.Abs(relative.x * oldDirection.y - relative.y * oldDirection.x);
            Check(oldClearance > 2f, "New position was not safely outside the previous beam direction.");
            laser.Step(.02f);
            Check(!player.Alive, "Active beam failed to hit the new unguarded player position.");
            VerifySegment(laser);
            Near(Vector2.Distance(laser.LastDamageTarget, laser.Beam.GetPosition(1)), 0f, .00001f,
                "Lethal query and visible segment must use the same endpoint");
        }

        private static void ExplicitCancel()
        {
            DLaser laser = Laser();
            laser.Step(.2f);
            float elapsed = laser.Elapsed;
            laser.Cancel();
            laser.Step(10f);
            Check(laser.Finished && !laser.Warning && !laser.Active && !laser.Beam.enabled,
                "Explicit cancellation left a visible or active beam.");
            Near(laser.Elapsed, elapsed, .00001f, "Cancelled clock");
        }

        private static void VerifySegment(DLaser laser)
        {
            Near(Vector2.Distance(laser.AimPoint, player.Position), 0f, .0001f, "Current player aim");
            Near(Vector2.Distance(laser.Beam.GetPosition(0), laser.Source), 0f, .0001f, "Rendered source");
            Near(Vector2.Distance(laser.Beam.GetPosition(1), laser.Target), 0f, .0001f, "Rendered end");
            Vector2 aim = laser.AimPoint - laser.Source;
            Vector2 end = laser.Target - laser.Source;
            Near(Mathf.Abs(aim.x * end.y - aim.y * end.x) / Mathf.Max(1f, aim.magnitude),
                0f, .001f, "Source, player and endpoint collinearity");
            Check(Vector2.Dot(laser.Target - laser.AimPoint, aim) > 0f, "Endpoint must continue beyond the player along the same ray.");
            Vector3 viewport = camera.WorldToViewportPoint(laser.Target);
            Check(viewport.x >= -.0001f && viewport.x <= 1.0001f && viewport.y >= -.0001f && viewport.y <= 1.0001f,
                "Endpoint lies outside the visible rectangle: " + viewport);
            float edge = Mathf.Min(Mathf.Abs(viewport.x), Mathf.Abs(1f - viewport.x),
                Mathf.Abs(viewport.y), Mathf.Abs(1f - viewport.y));
            Near(edge, 0f, .0001f, "Endpoint must touch a visible camera edge");
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void Near(float actual, float expected, float tolerance, string message)
            => Check(Mathf.Abs(actual - expected) <= tolerance, message + ": " + actual + " (expected " + expected + ")");

        private static void Finish()
        {
            report.AppendLine("\nResult: " + passed + " passed, " + failed + " failed.");
            File.WriteAllText(reportPath, report.ToString());
            File.WriteAllText(statusPath, failed == 0 ? "PASS " + passed : "FAIL " + failed + " / " + (passed + failed));
            Debug.Log("D laser correction verification: " + passed + " passed, " + failed + " failed. " + reportPath);
            running = false;
        }
    }
}
#endif
