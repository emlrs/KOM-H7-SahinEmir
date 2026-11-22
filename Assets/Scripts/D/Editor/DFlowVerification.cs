using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

namespace CombinedD
{
    // Run in Play Mode. Loads a separate D scene with its own 2D physics world.
    public static class DFlowVerification
    {
        const string StatusPath = "Temp/D_Birlesik/flow-status.txt";
        const string ReportPath = "Belge/D_FlowTest.md";
        static readonly List<string> checks = new List<string>();
        static readonly List<GameObject> suspendedRoots = new List<GameObject>();
        static AsyncOperation operation;
        static Scene scene;
        static DGameController game;
        static Keyboard testKeyboard, originalKeyboard;
        static int state, frameDue, deathSerial;
        static double deadline;
        static string failure;
        static bool running;

        [MenuItem("D/Verify flow in Play Mode")]
        public static void Begin()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("DFlowVerification.Begin requires Play Mode.");
            if (running) throw new InvalidOperationException("Flow verification is already running.");
            running = true; checks.Clear(); suspendedRoots.Clear(); failure = null; game = null; scene = default;
            Directory.CreateDirectory("Temp/D_Birlesik"); Directory.CreateDirectory("Belge");
            File.WriteAllText(StatusPath, "RUNNING");
            deadline = EditorApplication.timeSinceStartup + 45;
            operation = SceneManager.LoadSceneAsync(DSceneTools.ScenePath,
                new LoadSceneParameters(LoadSceneMode.Additive, LocalPhysicsMode.Physics2D));
            state = 0; frameDue = 0;
            EditorApplication.update += Update;
        }

        static void Update()
        {
            if (!running) return;
            try
            {
                if (!EditorApplication.isPlaying) throw new Exception("Play Mode ended while flow verification was running.");
                if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Flow verification timed out.");
                if (Time.frameCount < frameDue) return;
                switch (state)
                {
                    case 0:
                        if (operation != null && !operation.isDone) return;
                        FindLoadedCopy();
                        SuspendOtherScenes();
                        SetManual(true);
                        game.ResetRun(false);
                        VerifyAuthoredScene();
                        originalKeyboard = Keyboard.current;
                        testKeyboard = InputSystem.AddDevice<Keyboard>("D Flow Test Keyboard");
                        testKeyboard.MakeCurrent();
                        game.ManualSimulation = false;
                        game.Player.ManualSimulation = false;
                        game.Player.UseExternalInput = false;
                        InputSystem.QueueStateEvent(testKeyboard, new KeyboardState(Key.Space));
                        Next(1);
                        break;
                    case 1:
                        Check(game.Phase == DGamePhase.Playing, "Real Space edge starts the instruction screen.");
                        Check(!game.Player.InputEnabled, "Held start Space remains consumed until release.");
                        Check(game.Player.Velocity.y < .5f, "Start Space did not create an accidental jump.");
                        InputSystem.QueueStateEvent(testKeyboard, new KeyboardState());
                        Next(2);
                        break;
                    case 2:
                        Check(game.Player.InputEnabled, "Space release enables gameplay input.");
                        Check(game.Player.Velocity.y < .5f, "Release of start Space did not create a jump.");
                        SetManual(true);
                        VerifyManualFlow();
                        game.BeginRun(); game.Die(); deathSerial = game.RunSerial;
                        game.ManualSimulation = false;
                        InputSystem.QueueStateEvent(testKeyboard, new KeyboardState(Key.R));
                        Next(3);
                        break;
                    case 3:
                        Check(game.Phase == DGamePhase.Dying, "R pressed during death does not restart the run.");
                        game.Tick(game.DeathDuration + .01f);
                        Next(4);
                        break;
                    case 4:
                        Check(game.Phase == DGamePhase.Lost && game.RunSerial == deathSerial,
                            "R held through death-to-loss requires a fresh terminal press.");
                        InputSystem.QueueStateEvent(testKeyboard, new KeyboardState());
                        Next(5);
                        break;
                    case 5:
                        InputSystem.QueueStateEvent(testKeyboard, new KeyboardState(Key.R));
                        Next(6);
                        break;
                    case 6:
                        Check(game.IsPlaying && game.RunSerial == deathSerial + 1, "Fresh R edge on the loss screen restarts exactly once.");
                        StartCleanup();
                        break;
                    case 7:
                        if (operation != null && !operation.isDone) return;
                        Finish();
                        break;
                }
            }
            catch (Exception error)
            {
                failure = error.ToString();
                if (state == 7) Finish(); else StartCleanup();
            }
        }

        static void FindLoadedCopy()
        {
            // The newest loaded scene is the copy requested by Begin, never the original.
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene candidate = SceneManager.GetSceneAt(i);
                if (!candidate.isLoaded || candidate.path != DSceneTools.ScenePath) continue;
                foreach (var root in candidate.GetRootGameObjects())
                {
                    var found = root.GetComponentInChildren<DGameController>(true);
                    if (!found) continue;
                    scene = candidate; game = found; return;
                }
            }
            throw new Exception("The additive D verification scene failed to load.");
        }

        static void SuspendOtherScenes()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene other = SceneManager.GetSceneAt(i);
                if (other == scene || !other.isLoaded) continue;
                foreach (GameObject root in other.GetRootGameObjects())
                {
                    if (!root.activeSelf) continue;
                    suspendedRoots.Add(root); root.SetActive(false);
                }
            }
        }

        static void SetManual(bool manual)
        {
            if (!game) return;
            game.ManualSimulation = manual;
            if (game.Player) game.Player.ManualSimulation = manual;
            if (game.Orb) game.Orb.ManualSimulation = manual;
            if (game.Attacks) game.Attacks.ManualSimulation = manual;
            if (game.CameraRig) game.CameraRig.ManualSimulation = manual;
        }

        static void VerifyAuthoredScene()
        {
            Check(game.Player && game.Orb && game.CameraRig && game.Attacks && game.Level && game.Lava && game.Effects && game.HUD && game.Level.DoorView && game.Level.Receiver && game.Level.Exit, "All scene gameplay references are assigned.");
            Check(game.Phase == DGamePhase.Instruction && !game.Player.InputEnabled && !game.DoorOpen,
                "Initial instruction phase has disabled input and a closed exit.");
            Check(game.Level.Receiver.Zone && game.Level.Receiver.Zone.enabled && game.Level.Receiver.Zone.gameObject.activeInHierarchy,
                "Receiver exists and is enabled from the initial frame; there is no timed activation.");
            var route = game.Level.Platforms;
            Check(route != null && route.Length == 145, "145 persistent platform objects provide 144 linked jumps.");
            int longCrossings = 0;
            for (int i = 0; i < route.Length; i++)
            {
                Check(route[i] && route[i].Surface && route[i].Index == i &&
                    (i == route.Length - 1 ? !route[i].Next : route[i].Next == route[i + 1]),
                    "Platform " + i.ToString("000") + " has its authored collider and expected Next link.");
                if (i > 0 && route[i].LongCrossing)
                {
                    longCrossings++;
                    Bounds from = route[i - 1].Surface.bounds, to = route[i].Surface.bounds;
                    float gap = Mathf.Abs(to.center.x - from.center.x) - from.extents.x - to.extents.x;
                    Check(gap >= 3.9f && gap <= 4.1f && to.max.y - from.max.y < 1.4f,
                        "Long crossing " + i + " has a real 4m edge gap at a reduced rise.");
                }
            }
            Check(longCrossings == 8, "Eight authored long crossings vary the route.");
            float height = route[route.Length - 1].Surface.bounds.max.y;
            Check(height >= 290 && height <= 300, "Final route height is within the measured 290–300m course range.");
            Check(route[route.Length - 1].Surface.size.x >= 12, "Final landing is broad enough to stand and redirect the orb.");
            Check(game.Attacks.MissilePrefab && game.Attacks.LaserPrefab && game.Attacks.Points.Length > 50,
                "Real projectile/laser prefab references and authored source points are present.");
            Check(Mathf.Abs(game.Lava.RiseSpeed - 1.25f) < .001f &&
                Mathf.Abs(game.Attacks.InitialMissileSpeed - 9f) < .001f && Mathf.Abs(game.Attacks.MaximumMissileSpeed - 14f) < .001f &&
                Mathf.Abs(game.Attacks.InitialInterval - 2f) < .001f && Mathf.Abs(game.Attacks.MinimumInterval - 1f) < .001f &&
                Mathf.Abs(game.Attacks.DifficultyDuration - 120f) < .001f,
                "Saved scene uses the measured final lava 1.25 (initial trial 1.5), missile speed 9 to 14 and interval 2 to 1 over 120 seconds.");
            Check(Mathf.Abs(game.Attacks.MinimumLaserWarning - 1.3f) < .001f && Mathf.Abs(game.Attacks.MaximumLaserWarning - 1.7f) < .001f &&
                Mathf.Abs(game.Attacks.MinimumLaserDuration - 1f) < .001f && Mathf.Abs(game.Attacks.MaximumLaserDuration - 1.8f) < .001f &&
                Mathf.Abs(game.Attacks.MinimumLaserStartGap - 8.5f) < .001f,
                "Saved scene uses warning 1.3 to 1.7 seconds, sustained laser 1 to 1.8 seconds and independent 8.5-second laser-start spacing.");
        }

        static void VerifyManualFlow()
        {
            game.BeginRun();
            int serial = game.RunSerial;
            Check(game.IsPlaying && game.Elapsed == 0, "BeginRun enters Playing immediately, with zero elapsed time.");
            Check(Mathf.Abs(game.LavaHeight - game.Lava.StartMarker.position.y) < .001f, "Lava resets from its scene marker.");
            game.Tick(2);
            Check(Mathf.Abs(game.LavaHeight - game.Lava.StartMarker.position.y - 2 * game.Lava.RiseSpeed) < .001f,
                "Lava rises at the configured constant world speed.");
            game.OnOrbParry(game.Player.Position);
            Check(game.OrbParries == 1 && Mathf.Abs(game.Attacks.PauseRemaining - 5) < .001f,
                "Orb parry invokes the integrated five-second wave.");
            game.Attacks.Step(1.5f);
            game.OnOrbParry(game.Player.Position);
            Check(game.OrbParries == 2 && Mathf.Abs(game.Attacks.PauseRemaining - 5) < .001f,
                "A second orb parry refreshes the pause to five, without stacking.");
            game.OnMissileParry();
            Check(game.MissileParries == 1 && Mathf.Abs(game.Attacks.PauseRemaining - 5) < .001f,
                "Missile parry leaves the wave pause unchanged.");
            game.Die();
            Check(game.Phase == DGamePhase.Dying && !game.Player.Alive && !game.Orb.Active,
                "Death immediately stops player/orb danger and enters the visual death phase.");
            Check(game.Attacks.ActiveMissileCount == 0 && game.Attacks.ActiveLaserCount == 0 && game.Attacks.PendingWarningCount == 0,
                "Death cancels attacks and pending warnings.");
            game.Tick(game.DeathDuration - .01f);
            Check(game.Phase == DGamePhase.Dying, "Death animation remains visible until its duration expires.");
            game.Tick(.02f);
            Check(game.Phase == DGamePhase.Lost, "Loss screen follows the death animation.");
            game.RestartRun();
            Check(game.RunSerial == serial + 1 && game.Elapsed == 0 && game.OrbParries == 0 &&
                game.MissileParries == 0 && !game.DoorOpen && game.Attacks.PauseRemaining == 0,
                "Restart resets serial, time, counters, door and wave.");
            Check((game.Player.Position - (Vector2)game.Level.PlayerSpawn.position).sqrMagnitude < .001f &&
                (game.Orb.Position - (Vector2)game.Level.OrbSpawn.position).sqrMagnitude < .001f,
                "Restart uses both editable spawn markers.");
            foreach (var panel in game.Level.Panels)
                Check(!panel.Broken && panel.RemainingHits == panel.MaximumHits, "Panel resets to its authored durability.");
            Check(Mathf.Abs(game.CameraRig.ViewCamera.transform.position.y - game.CameraRig.InitialCameraY) < .001f,
                "Restart resets camera and moving boundaries.");

            game.Player.ResetAt(new Vector2(-10, 12));
            game.Orb.ResetOrb(new Vector2(8, -5.3f), Vector2.up);
            Vector2 before = game.Orb.Position;
            Vector2 velocityBefore = game.Orb.Velocity;
            float centerBefore = game.CameraRig.ViewCamera.transform.position.y;
            Physics2D.SyncTransforms();
            for (int i = 0; i < 50; i++) game.CameraRig.FollowStep(.02f);
            Check((game.Orb.Position - before).sqrMagnitude < .000001f && (game.Orb.Velocity - velocityBefore).sqrMagnitude < .000001f,
                "Camera follow never changes the orb position or world velocity.");
            Check(game.CameraRig.ViewCamera.transform.position.y > centerBefore + .1f &&
                game.CameraRig.ViewCamera.orthographicSize > game.CameraRig.BaseOrthographicSize,
                "A high player and low orb are framed by upward follow plus temporary zoom.");
            CheckCameraContainsActors();
            float upperCenter = game.CameraRig.ViewCamera.transform.position.y;
            game.Player.ResetAt(new Vector2(-10, -2));
            game.Orb.ResetOrb(new Vector2(8, 8), Vector2.up);
            before = game.Orb.Position; velocityBefore = game.Orb.Velocity;
            Physics2D.SyncTransforms();
            for (int i = 0; i < 75; i++) game.CameraRig.FollowStep(.02f);
            Check(game.CameraRig.ViewCamera.transform.position.y < upperCenter - .5f,
                "Camera also follows a lower player downward; it is not locked to the maximum reached height.");
            Check((game.Orb.Position - before).sqrMagnitude < .000001f && (game.Orb.Velocity - velocityBefore).sqrMagnitude < .000001f,
                "Downward follow and zoom preserve the unstepped orb exactly.");
            CheckCameraContainsActors();
            Check(Mathf.Abs(game.Orb.Velocity.magnitude - game.Orb.Speed) < .001f, "Camera handling preserves constant orb world speed.");

            game.BeginRun();
            game.TryExit(game.Player);
            Check(game.IsPlaying && !game.DoorOpen, "Closed or remote exit cannot win the game.");
            game.Orb.ResetOrb(game.Level.Receiver.Zone.bounds.center, Vector2.right);
            Physics2D.SyncTransforms();
            bool accepted = game.Level.Receiver.TryAccept(game.Orb);
            Check(accepted && game.DoorOpen && game.Elapsed == 0 && !game.Orb.Active,
                "Actual receiver contact opens the door immediately, without time/parry prerequisites.");
            game.TryExit(game.Player);
            Check(game.IsPlaying, "Open door still requires actual player/exit overlap.");
            game.Player.ResetAt(game.Level.Exit.Zone.bounds.center);
            Physics2D.SyncTransforms();
            game.TryExit(game.Player);
            Check(game.Phase == DGamePhase.Won, "An open door plus actual exit overlap produces success.");
            game.RestartRun();
            Check(game.IsPlaying && !game.DoorOpen && game.Orb.Active && game.Level.Receiver.Zone.enabled,
                "Restart after success resets the absorbed orb and keeps receiver available.");
        }

        static void CheckCameraContainsActors()
        {
            Rect visible = game.CameraRig.VisiblePlayBounds;
            Bounds body = game.Player.BodyCollider.bounds;
            Vector2 orb = game.Orb.Position; float radius = game.Orb.Radius;
            Check(body.min.x >= visible.xMin - .02f && body.max.x <= visible.xMax + .02f &&
                body.min.y >= visible.yMin - .02f && body.max.y <= visible.yMax + .02f,
                "Full player collider remains inside all four visible play boundaries.");
            Check(orb.x - radius >= visible.xMin - .02f && orb.x + radius <= visible.xMax + .02f &&
                orb.y - radius >= visible.yMin - .02f && orb.y + radius <= visible.yMax + .02f,
                "Full orb radius remains inside all four visible play boundaries.");
        }

        static void Check(bool condition, string description)
        {
            if (!condition) throw new Exception(description);
            checks.Add(description);
        }
        static void Next(int next) { state = next; frameDue = Time.frameCount + 3; }

        static void StartCleanup()
        {
            SetManual(true);
            if (testKeyboard != null)
            {
                InputSystem.RemoveDevice(testKeyboard); testKeyboard = null;
                if (originalKeyboard != null && originalKeyboard.added) originalKeyboard.MakeCurrent();
            }
            foreach (GameObject root in suspendedRoots) if (root) root.SetActive(true);
            suspendedRoots.Clear();
            operation = scene.IsValid() && scene.isLoaded ? SceneManager.UnloadSceneAsync(scene) : null;
            state = 7; frameDue = 0; deadline = EditorApplication.timeSinceStartup + 10;
        }

        static void Finish()
        {
            EditorApplication.update -= Update; running = false;
            var report = new StringBuilder("# D flow verification\n\n");
            report.AppendLine(failure == null ? "Result: PASS\n" : "Result: FAIL\n");
            report.AppendLine("Scope: isolated additive scene with its own Physics2D world; real keyboard edges for start/restart, deterministic lifecycle checks, scene references, receiver/exit contact gates and camera clearance. Gate tests deliberately position objects and are not a full playable-route completion claim. Original scene roots and keyboard device are restored; no global Physics2D simulation settings are changed.\n");
            foreach (string check in checks) report.AppendLine("- PASS: " + check);
            if (failure != null) report.AppendLine("\n```text\n" + failure + "\n```");
            File.WriteAllText(ReportPath, report.ToString(), Encoding.UTF8);
            File.WriteAllText(StatusPath, failure == null ? "PASS" : "FAIL\n" + failure);
            Debug.Log("D flow verification " + (failure == null ? "PASS" : "FAIL") + "; see " + ReportPath);
        }
    }
}
