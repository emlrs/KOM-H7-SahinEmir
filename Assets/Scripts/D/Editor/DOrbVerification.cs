#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CombinedD
{
    /// <summary>Run explicitly in Play Mode. Every test uses a separate local PhysicsScene2D.</summary>
    public static class DOrbVerification
    {
        public const string StatusPath = "Temp/D_Birlesik/orb-status.txt";
        public static bool IsRunning { get; private set; }
        public static int FailureCount { get; private set; }
        private static readonly List<string> lines = new List<string>();
        private static Scene testScene;
        private static GameObject fixture;
        private static DGameController game;
        private static DPlayerMotor player;
        private static DOrbMotor orb;
        private static DAttackDirector attacks;

        [MenuItem("Combined D/Verify orb in Play Mode")]
        public static void Begin()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Orb verification requires Play Mode.");
            if (IsRunning) throw new InvalidOperationException("Orb verification is already running.");
            IsRunning = true; FailureCount = 0; lines.Clear();
            Directory.CreateDirectory("Temp/D_Birlesik"); Directory.CreateDirectory("Belge");
            File.WriteAllText(StatusPath, "RUNNING");
            Scene original = SceneManager.GetActiveScene();
            bool[] orbPairs = new bool[32], shieldPairs = new bool[32];
            for (int layer = 0; layer < 32; layer++)
            {
                orbPairs[layer] = Physics2D.GetIgnoreLayerCollision(14, layer);
                shieldPairs[layer] = Physics2D.GetIgnoreLayerCollision(10, layer);
            }
            try
            {
                for (int layer = 0; layer < 32; layer++)
                { Physics2D.IgnoreLayerCollision(14, layer, true); Physics2D.IgnoreLayerCollision(10, layer, true); }
                testScene = SceneManager.CreateScene("D Orb verification " + Guid.NewGuid().ToString("N"),
                    new CreateSceneParameters(LocalPhysicsMode.Physics2D));
                SceneManager.SetActiveScene(testScene);
                SpeedAndCorners();
                ParryAndDefense();
                Panels();
                Receiver();
                RisingCamera();
            }
            catch (Exception error) { Check(false, "Unexpected exception: " + error); }
            finally
            {
                if (fixture) UnityEngine.Object.DestroyImmediate(fixture);
                if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
                if (testScene.IsValid() && testScene.isLoaded) SceneManager.UnloadSceneAsync(testScene);
                for (int layer = 0; layer < 32; layer++) Physics2D.IgnoreLayerCollision(14, layer, orbPairs[layer]);
                for (int layer = 0; layer < 32; layer++) Physics2D.IgnoreLayerCollision(10, layer, shieldPairs[layer]);
                string report = "# D — Orb verification\n\nExecuted " + DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz")
                    + "; Unity " + Application.unityVersion + ".\n\n"
                    + "Isolated real Collider2D casts and a local PhysicsScene2D. Public manual steps; no active game scene is modified. "
                    + "The 240-second camera test is a controlled rising target, not proof of completing the authored course. "
                    + "The parent route verification is responsible for the actual route and input-driven full-game result.\n\n"
                    + string.Join("\n", lines) + "\n\nFailures: " + FailureCount + "\n";
                File.WriteAllText("Belge/D_OrbTest.md", report);
                File.WriteAllText(StatusPath, "DONE failures=" + FailureCount);
                IsRunning = false;
                Debug.Log("D orb verification finished. failures=" + FailureCount);
            }
        }

        private static void Check(bool ok, string label)
        { lines.Add("- " + (ok ? "PASS" : "FAIL") + " — " + label); if (!ok) FailureCount++; }
        private static bool Near(float a, float b, float tolerance = .003f) => Mathf.Abs(a - b) <= tolerance;
        private static GameObject Child(string name)
        { var item = new GameObject(name); item.transform.SetParent(fixture.transform); return item; }
        private static Transform Marker(string name, Vector2 position)
        { var item = Child(name); item.transform.position = position; return item.transform; }
        private static BoxCollider2D Box(string name, Vector2 position, Vector2 size, int layer = 9)
        {
            var item = Child(name); item.layer = layer; item.transform.position = position;
            var box = item.AddComponent<BoxCollider2D>(); box.size = size; return box;
        }
        private static void NewFixture(Vector2 ball, Vector2 direction, bool playerNear = false)
        {
            if (fixture) UnityEngine.Object.DestroyImmediate(fixture);
            fixture = new GameObject("Orb verification fixture");
            game = Child("Game").AddComponent<DGameController>(); game.ManualSimulation = true;
            game.Level = Child("Level manager").AddComponent<DLevelManager>();
            game.Lava = Child("Lava controller").AddComponent<DLavaController>();
            player = DTestPlayerFactory.Create(new Vector2(playerNear ? 0 : 1000, .85f));
            player.transform.SetParent(fixture.transform); player.ManualSimulation = true; player.UseExternalInput = true;
            player.Body.interpolation = RigidbodyInterpolation2D.None;
            Box("Ground ignored by orb", new Vector2(playerNear ? 0 : 1000, -.25f), new Vector2(20, .5f), 8);
            var orbObject = Child("Orb"); orbObject.layer = 14;
            orb = orbObject.AddComponent<DOrbMotor>(); orb.ManualSimulation = true;
            orb.CollisionShape.radius = .28f;
            attacks = Child("Attacks").AddComponent<DAttackDirector>(); attacks.ManualSimulation = true;
            attacks.Game = game; attacks.Player = player; attacks.Orb = orb;
            game.Player = player; game.Orb = orb; game.Attacks = attacks;
            player.Game = game; orb.Game = game; orb.Player = player;
            game.Level.PlayerSpawn = Marker("Player spawn", player.Position); game.Level.OrbSpawn = Marker("Orb spawn", ball);
            game.Lava.StartMarker = Marker("Lava start", new Vector2(0, -1000)); game.Lava.RiseSpeed = 0;
            game.Level.InitialOrbDirection = direction; game.BeginRun(); Physics2D.SyncTransforms();
        }
        private static void Tap()
        {
            player.SetInput(new DInputFrame(0, false, false, false, true, true, false)); player.Step(.08f);
            player.SetInput(new DInputFrame(0, false, false, false, false, false, true)); player.Step(.02f);
            Physics2D.SyncTransforms();
        }

        private static void SpeedAndCorners()
        {
            NewFixture(Vector2.zero, new Vector2(.8f, .6f));
            bool constant = true; Vector2 start = orb.Position;
            for (int i = 0; i < 1500; i++)
            {
                Vector2 previous = orb.Position; orb.Step(.02f); game.Tick(.02f);
                constant &= Near(orb.Velocity.magnitude, 8f, .0001f)
                    && Near(Vector2.Distance(previous, orb.Position), .16f, .0001f);
            }
            Check(constant && Near(Vector2.Distance(start, orb.Position), 240f, .015f),
                "30 s open travel stays 8 world units/s; no elapsed-time acceleration");
            Vector2 beforePhysics = orb.Position; testScene.GetPhysicsScene2D().Simulate(.02f);
            Check(Vector2.Distance(beforePhysics, orb.Position) < .0001f && orb.Body.linearVelocity == Vector2.zero,
                "Native physics adds no second orb movement");

            NewFixture(Vector2.zero, Vector2.one);
            var left = Box("Left", new Vector2(-4.25f, 0), new Vector2(.5f, 10));
            var right = Box("Right", new Vector2(4.25f, 0), new Vector2(.5f, 10));
            var bottom = Box("Bottom", new Vector2(0, -4.25f), new Vector2(10, .5f));
            var top = Box("Top", new Vector2(0, 4.25f), new Vector2(10, .5f));
            Physics2D.SyncTransforms();
            // Use the same real shape definition as the solver, not nominal transform/size faces.
            // Unity may include its polygon skin even when BoxCollider2D.edgeRadius is zero.
            Vector2 rightSurface = right.ClosestPoint(Vector2.zero), topSurface = top.ClosestPoint(Vector2.zero);
            Vector2 minimumCenter = new Vector2(left.ClosestPoint(Vector2.zero).x + orb.Radius,
                bottom.ClosestPoint(Vector2.zero).y + orb.Radius);
            Vector2 maximumCenter = new Vector2(rightSurface.x - orb.Radius, topSurface.y - orb.Radius);
            orb.Step(1f);
            Vector2 expected = maximumCenter * 2 - Vector2.one * (8f / Mathf.Sqrt(2));
            Check(Near(orb.Position.x, expected.x, .006f) && Near(orb.Position.y, expected.y, .006f)
                && orb.WallBounceCount == 2 && orb.SolverLimitCount == 0 && Near(orb.Velocity.magnitude, 8f),
                "Exact corner reflects both real wall normals; one-second distance is conserved"
                + " [position=" + orb.Position.ToString("F6") + ", expected=" + expected.ToString("F6")
                + ", velocity=" + orb.Velocity.ToString("F6") + ", bounces=" + orb.WallBounceCount + ", limits=" + orb.SolverLimitCount
                + ", rightClosest=" + rightSurface.ToString("F6") + ", topClosest=" + topSurface.ToString("F6")
                + ", edgeRadius=" + right.edgeRadius.ToString("F6") + ", defaultContactOffset=" + Physics2D.defaultContactOffset.ToString("F6") + "]");
            bool staysInside = true;
            float worstCornerPenetration = 0;
            for (int i = 0; i < 1500; i++)
            {
                orb.Step(.02f);
                float penetration = Mathf.Max(minimumCenter.x - orb.Position.x, minimumCenter.y - orb.Position.y,
                    orb.Position.x - maximumCenter.x, orb.Position.y - maximumCenter.y);
                staysInside &= penetration <= .003f && Near(orb.Velocity.magnitude, 8f);
                worstCornerPenetration = Mathf.Max(worstCornerPenetration, penetration);
            }
            Check(staysInside && orb.SolverLimitCount == 0, "Repeated corners remain inside without a solver stall"
                + " [max penetration=" + worstCornerPenetration.ToString("F6") + ", limits=" + orb.SolverLimitCount + "]");

            NewFixture(new Vector2(0, 2), Vector2.down);
            Box("Platform", Vector2.zero, new Vector2(20, .5f), 8);
            Box("Lava shape", new Vector2(0, -2), new Vector2(20, .5f), 0);
            orb.Step(1);
            Check(orb.Active && Near(orb.Position.y, -6f) && orb.WallBounceCount == 0,
                "Explicit cast mask ignores platform and lava geometry");
        }

        private static void ParryAndDefense()
        {
            bool all = true; int upward = 0, downward = 0;
            for (int seed = 0; seed < 32; seed++)
            {
                NewFixture(new Vector2(1.3f, .97f), Vector2.left, true);
                orb.SetRandomSeed(seed); Tap(); orb.Step(.09f);
                float angle = Mathf.Abs(Mathf.Atan2(orb.Velocity.y, orb.Velocity.x) * Mathf.Rad2Deg);
                all &= orb.Active && player.Alive && orb.ParryCount == 1 && game.OrbParries == 1
                    && Near(attacks.PauseRemaining, 5) && angle >= 21.99f && angle <= 62.01f
                    && orb.Velocity.x > 0 && Near(orb.Velocity.magnitude, 8f);
                if (orb.Velocity.y > 0) upward++; else downward++;
                orb.Step(.05f);
                all &= player.Alive && orb.ParryCount == 1 && orb.SolverLimitCount == 0;
            }
            Check(all && upward > 0 && downward > 0,
                "32 seeded right-facing contacts: safe exit, 22–62 degrees, both up/down, one parry/wave (up=" + upward + ", down=" + downward + ")");

            NewFixture(new Vector2(-1.3f, .97f), Vector2.right, true);
            player.ResetAt(game.Level.PlayerSpawn.position, -1); Tap(); orb.Step(.09f);
            float leftAngle = Mathf.Abs(Mathf.Atan2(orb.Velocity.y, -orb.Velocity.x) * Mathf.Rad2Deg);
            Check(player.Alive && orb.ParryCount == 1 && orb.Velocity.x < 0 && leftAngle >= 21.99f && leftAngle <= 62.01f,
                "Mirrored shield exits through the left-facing half-plane");

            NewFixture(new Vector2(.20f, 2.5f), Vector2.down, true); Tap(); orb.Step(.11f);
            Check(player.Alive && orb.ParryCount == 1 && orb.Velocity.y > 0 && orb.SolverLimitCount == 0,
                "Visible shield crown accepts an overhead contact and exits upward");

            NewFixture(new Vector2(1.3f, .97f), Vector2.left, true); Tap(); orb.Step(.09f);
            Box("Return wall", new Vector2(2.1f, .85f), new Vector2(.2f, 12));
            Box("Upper return", new Vector2(0, 2.8f), new Vector2(5, .2f));
            Box("Lower return", new Vector2(0, -.5f), new Vector2(5, .2f));
            orb.Step(1.5f);
            Check(orb.ParryCount == 1 && game.OrbParries == 1,
                "A still-open ParrySerial cannot count or emit the energy wave twice");

            NewFixture(new Vector2(-1.3f, .85f), Vector2.right, true); Tap(); orb.Step(.3f);
            Check(!player.Alive && game.Phase == DGamePhase.Dying && game.OrbParries == 0,
                "Wrong-side orb contact remains lethal during the parry window");

            NewFixture(new Vector2(1.3f, .85f), Vector2.left, true);
            player.SetInput(new DInputFrame(0, false, false, false, true, true, false)); player.Step(.3f);
            bool guarding = player.GuardActive; orb.Step(.3f);
            Check(guarding && !player.Alive && game.OrbParries == 0,
                "Holding the directional guard does not protect from the orb");

            NewFixture(new Vector2(1.3f, .97f), Vector2.left, true); Tap(); orb.Step(.09f);
            attacks.Step(1); player.Step(.25f); Tap(); orb.ResetOrb(new Vector2(1.3f, .97f), Vector2.left); orb.Step(.09f);
            Check(game.OrbParries == 2 && Near(attacks.PauseRemaining, 5),
                "A second real-contact parry refreshes the wave to exactly five seconds");

            bool movingSafe = true;
            for (int facing = -1; facing <= 1; facing += 2)
                for (int jumping = 0; jumping < 2; jumping++)
                    for (int seed = 0; seed < 12; seed++)
                    {
                        NewFixture(new Vector2(facing * 1.3f, .97f), new Vector2(-facing, 0), true);
                        player.ResetAt(game.Level.PlayerSpawn.position, facing); orb.SetRandomSeed(seed); Tap();
                        Vector2 velocity = new Vector2(facing * 6.8f, jumping == 0 ? 0 : 10);
                        player.Body.linearVelocity = velocity;
                        orb.Step(.06f);
                        movingSafe &= orb.ParryCount == 1 && player.Velocity == velocity;
                        for (int frame = 0; frame < 10; frame++)
                        {
                            // Native simulation actually translates the capsule with unchanged input velocity.
                            testScene.GetPhysicsScene2D().Simulate(.02f); Physics2D.SyncTransforms(); orb.Step(.02f);
                            movingSafe &= player.Alive && orb.Active && orb.ParryCount == 1
                                && Near(orb.Velocity.magnitude, 8f) && Near(player.Velocity.x, facing * 6.8f);
                        }
                    }
            Check(movingSafe, "48 running/jumping front parries stay safe for .20 s with actual moving capsules; player horizontal velocity is unchanged");

            bool risingCrownSafe = true;
            for (int facing = -1; facing <= 1; facing += 2)
            {
                NewFixture(new Vector2(facing * .20f, 2.5f), Vector2.down, true);
                player.ResetAt(game.Level.PlayerSpawn.position, facing); Tap();
                Vector2 velocity = new Vector2(facing * 6.8f, 10);
                player.Body.linearVelocity = velocity; orb.Step(.08f);
                risingCrownSafe &= orb.ParryCount == 1 && player.Velocity == velocity;
                for (int frame = 0; frame < 10; frame++)
                {
                    testScene.GetPhysicsScene2D().Simulate(.02f); Physics2D.SyncTransforms(); orb.Step(.02f);
                    risingCrownSafe &= player.Alive && orb.Active && orb.ParryCount == 1;
                }
            }
            Check(risingCrownSafe, "Rising crown parries use bounded geometric front clearance; no immunity or player velocity penalty");
        }

        private static DPanel Panel(int hits)
        {
            var collider = Box("Panel", new Vector2(2, 0), new Vector2(.5f, 4), 12);
            var panel = collider.gameObject.AddComponent<DPanel>(); panel.MaximumHits = hits; panel.Solid = collider; panel.ResetPanel();
            return panel;
        }
        private static void Panels()
        {
            NewFixture(Vector2.zero, Vector2.right); DPanel normal = Panel(1); orb.Step(.4f);
            Check(normal.Broken && !normal.Solid.enabled && orb.Velocity.x < 0,
                "Normal panel breaks after one real impact while the orb reflects");
            NewFixture(Vector2.zero, Vector2.right); DPanel armor = Panel(2);
            Box("Return wall", new Vector2(-2.25f, 0), new Vector2(.5f, 6)); orb.Step(.4f);
            bool first = armor.RemainingHits == 1 && armor.Solid.enabled;
            for (int i = 0; i < 10; i++) orb.Step(.001f);
            first &= armor.RemainingHits == 1;
            orb.Step(1.1f);
            Check(first && armor.Broken && !armor.Solid.enabled && orb.SolverLimitCount == 0,
                "Armor needs two separated collider episodes; small repeated steps do not double-count");
            armor.ResetPanel(); armor.ApplyImpact(42); armor.ApplyImpact(42);
            Check(armor.RemainingHits == 1 && armor.Solid.enabled, "Panel rejects a duplicate contact token");
        }

        private static void Receiver()
        {
            NewFixture(Vector2.zero, Vector2.right);
            var zone = Box("Thin receiver", new Vector2(2, 0), new Vector2(.12f, 4), 13); zone.isTrigger = true;
            var receiver = zone.gameObject.AddComponent<DReceiver>(); receiver.Zone = zone; receiver.Game = game;
            game.Level.Receiver = receiver;
            Check(!receiver.TryAccept(orb) && !game.DoorOpen, "Receiver rejects a remote API call without geometric contact");
            orb.Step(1);
            Check(game.Elapsed == 0 && game.DoorOpen && !orb.Active && !orb.CollisionShape.enabled
                && game.Phase == DGamePhase.Playing && game.OrbParries == 0,
                "Swept thin trigger accepts at elapsed zero without a parry/charge; door opens, exit is still required");
            game.RestartRun(); orb.Step(1);
            Check(game.DoorOpen && !orb.Active && game.RunSerial == 2,
                "Restart re-enables the orb and the stateless receiver accepts a new run");
        }

        private static void RisingCamera()
        {
            NewFixture(new Vector2(8, 5), new Vector2(-.85f, -.52f));
            // Frame the real target inside the view. This camera-only fixture excludes player damage.
            // The former x=1000 hiding trick is invalid for a camera which frames both actors.
            player.ResetAt(new Vector2(-10, .85f));
            orb.CollisionMask = 1 << 9;
            var cameraRig = Child("Camera controller").AddComponent<DCameraController>(); cameraRig.ManualSimulation = true;
            cameraRig.Game = game; cameraRig.Orb = orb; cameraRig.Player = player;
            cameraRig.ViewCamera = Child("Camera").AddComponent<Camera>(); cameraRig.ViewCamera.orthographic = true;
            cameraRig.ViewCamera.orthographicSize = 9.5f;
            cameraRig.CameraBottom = Box("Bottom", Vector2.zero, new Vector2(31, .5f)).transform;
            cameraRig.CameraTop = Box("Top", Vector2.zero, new Vector2(31, .5f)).transform;
            cameraRig.CameraLeft = Box("Left", Vector2.zero, new Vector2(.5f, 19)).transform;
            cameraRig.CameraRight = Box("Right", Vector2.zero, new Vector2(.5f, 19)).transform;
            Collider2D bottomWall = cameraRig.CameraBottom.GetComponent<Collider2D>();
            Collider2D topWall = cameraRig.CameraTop.GetComponent<Collider2D>();
            Collider2D leftWall = cameraRig.CameraLeft.GetComponent<Collider2D>();
            Collider2D rightWall = cameraRig.CameraRight.GetComponent<Collider2D>();
            cameraRig.ResetCamera(); bool safe = true, constant = true, bounded = true, untouched = true;
            float maximumPlayerLead = 0, maximumBoundaryPenetration = 0, maximumCameraStep = 0;
            float maximumZoom = cameraRig.ViewCamera.orthographicSize;
            bool targetVisible = true;
            for (int i = 0; i < 12000; i++)
            {
                // Controlled target progression; the full-course verifier uses real platform inputs.
                player.Body.position = new Vector2(-10, 4 + i * .02f);
                player.transform.position = player.Body.position;
                Vector2 before = orb.Position; int bounces = orb.WallBounceCount;
                orb.Step(.02f);
                if (orb.WallBounceCount == bounces) constant &= Near(Vector2.Distance(before, orb.Position), .16f, .00015f);
                constant &= Near(orb.Velocity.magnitude, 8f, .0001f);
                Vector2 after = orb.Position; float previousY = cameraRig.ViewCamera.transform.position.y;
                cameraRig.FollowStep(.02f); Physics2D.SyncTransforms();
                float center = cameraRig.ViewCamera.transform.position.y;
                Vector2 viewCenter = new Vector2(0, center);
                float bottomFace = bottomWall.ClosestPoint(viewCenter).y, topFace = topWall.ClosestPoint(viewCenter).y;
                float leftFace = leftWall.ClosestPoint(viewCenter).x, rightFace = rightWall.ClosestPoint(viewCenter).x;
                float penetration = Mathf.Max(bottomFace - orb.Position.y + orb.Radius,
                    orb.Position.y + orb.Radius - topFace, leftFace - orb.Position.x + orb.Radius,
                    orb.Position.x + orb.Radius - rightFace);
                maximumCameraStep = Mathf.Max(maximumCameraStep, Mathf.Abs(center - previousY));
                maximumZoom = Mathf.Max(maximumZoom, cameraRig.ViewCamera.orthographicSize);
                maximumBoundaryPenetration = Mathf.Max(maximumBoundaryPenetration, penetration);
                untouched &= orb.Position == after;
                bounded &= Mathf.Abs(center - previousY) <= cameraRig.CameraCatchUpSpeed * .02f + .00002f;
                safe &= penetration <= .003f;
                Rect visible = cameraRig.VisiblePlayBounds;
                Bounds playerBounds = player.BodyCollider.bounds;
                targetVisible &= playerBounds.min.y >= visible.yMin - .002f && playerBounds.max.y <= visible.yMax + .002f
                    && playerBounds.min.x >= visible.xMin - .002f && playerBounds.max.x <= visible.xMax + .002f;
                maximumPlayerLead = Mathf.Max(maximumPlayerLead, player.Position.y - center);
            }
            Check(safe && bounded && untouched && orb.SolverLimitCount == 0,
                "240 s adaptive camera bounds stay outside the ball; bounded camera motion never changes orb position"
                + " [safe=" + safe + ", bounded=" + bounded + ", untouched=" + untouched
                + ", max penetration=" + maximumBoundaryPenetration.ToString("F6")
                + ", max camera step=" + maximumCameraStep.ToString("F6") + ", limits=" + orb.SolverLimitCount + "]");
            Check(constant && orb.Active && targetVisible,
                "Rising-camera motion preserves 8 world speed and the whole player stays inside adaptive bounds; max lead="
                + maximumPlayerLead.ToString("F3") + "; max orthographic size=" + maximumZoom.ToString("F3"));
        }
    }
}
#endif

