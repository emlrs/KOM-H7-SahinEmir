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

namespace CombinedD.Editor
{
    /// <summary>Isolated local physics, real Update keyboard edges, and explicit deterministic steps.</summary>
    public static class DPlayerVerification
    {
        public const string StatusPath = "Temp/D_Birlesik/player-status.txt";
        private static readonly List<string> results = new List<string>(), errors = new List<string>();
        private static readonly List<GameObject> originalRoots = new List<GameObject>();
        private static readonly List<bool> originalStates = new List<bool>();
        private static Scene originalScene, testScene;
        private static PhysicsScene2D physics;
        private static DPlayerMotor player;
        private static BoxCollider2D floor;
        private static IEnumerator sequence;
        private static double nextStep;
        private static int failures;
        private static float oldTimeScale;
        public static bool IsRunning => sequence != null;
        public static int FailureCount => failures;

        [MenuItem("Combined D/Verify player in Play Mode")]
        public static void Begin()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Player verification requires Play Mode.");
            if (sequence != null) throw new InvalidOperationException("Player verification already running.");
            if (Keyboard.current == null) throw new InvalidOperationException("Input System keyboard required.");
            results.Clear(); errors.Clear(); originalRoots.Clear(); originalStates.Clear(); failures = 0;
            originalScene = SceneManager.GetActiveScene(); testScene = default; player = null;
            oldTimeScale = Time.timeScale; Time.timeScale = 1;
            Directory.CreateDirectory("Temp/D_Birlesik"); Directory.CreateDirectory("Belge");
            File.WriteAllText(StatusPath, "RUNNING");
            Application.logMessageReceived += OnLog;
            sequence = Run(); nextStep = 0; EditorApplication.update += Advance;
        }
        private static void Advance()
        {
            if (EditorApplication.timeSinceStartup < nextStep) return;
            try
            {
                if (!EditorApplication.isPlaying) throw new InvalidOperationException("Play Mode ended during verification.");
                if (sequence.MoveNext())
                {
                    nextStep = EditorApplication.timeSinceStartup + Convert.ToDouble(sequence.Current ?? 0f, CultureInfo.InvariantCulture);
                    return;
                }
                Finish();
            }
            catch (Exception e) { Check(false, "Verification exception: " + e); Finish(); }
        }
        private static void Finish()
        {
            EditorApplication.update -= Advance; (sequence as IDisposable)?.Dispose(); sequence = null;
            Key();
            if (testScene.IsValid() && testScene.isLoaded)
            {
                foreach (var root in testScene.GetRootGameObjects()) root.SetActive(false);
                SceneManager.UnloadSceneAsync(testScene);
            }
            if (originalScene.IsValid() && originalScene.isLoaded) SceneManager.SetActiveScene(originalScene);
            for (int i = 0; i < originalRoots.Count; i++) if (originalRoots[i]) originalRoots[i].SetActive(originalStates[i]);
            Time.timeScale = oldTimeScale; Application.logMessageReceived -= OnLog;
            Check(errors.Count == 0, "Runtime Error / Exception / Assert: " + errors.Count);
            string report = "# D — Player verification\n\nExecuted: " + DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz")
                + "; Unity " + Application.unityVersion + ".\n\n"
                + "Local PhysicsScene2D; the capsule is actually simulated against a BoxCollider2D floor. "
                + "KeyboardState events pass through normal Update; other checks drive the public input/step API. "
                + "This verifies movement and defense in isolation, not the complete course or orb collision order.\n\n"
                + string.Join("\n", results) + "\n\nFailures: " + failures + "\n";
            if (errors.Count > 0) report += "\n" + string.Join("\n", errors);
            File.WriteAllText("Belge/D_PlayerTest.md", report);
            File.WriteAllText(StatusPath, "DONE failures=" + failures);
            Debug.Log("D player verification finished. failures=" + failures);
        }
        private static void OnLog(string message, string stack, LogType type)
        { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); }
        private static void Check(bool passed, string label)
        { results.Add("- " + (passed ? "PASS" : "FAIL") + " — " + label); if (!passed) failures++; }
        private static bool Near(float a, float b, float tolerance = .001f) => Mathf.Abs(a - b) <= tolerance;
        private static void Key(params Key[] keys)
        { if (Keyboard.current != null) InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(keys)); }
        private static DInputFrame Input(float move = 0, bool jumpPress = false, bool jumpHeld = false,
            bool jumpRelease = false, bool defendPress = false, bool defendHeld = false, bool defendRelease = false)
            => new DInputFrame(move, jumpPress, jumpHeld, jumpRelease, defendPress, defendHeld, defendRelease);
        private static void Tick(float dt = .02f)
        { player.Step(dt); physics.Simulate(dt); Physics2D.SyncTransforms(); }
        private static void Drive(DInputFrame input, int count)
        { player.SetInput(input); for (int i = 0; i < count; i++) Tick(); }
        private static void Reset(float y = .85f, int facing = 1)
        {
            floor.enabled = true; player.ResetAt(new Vector2(0, y), facing);
            player.ManualSimulation = true; player.UseExternalInput = true;
            player.UseAcceleration = player.GuardLocksMovement = player.RequireStoppedGuard = true;
            player.Body.interpolation = RigidbodyInterpolation2D.None;
            Physics2D.SyncTransforms();
            if (y < 1f) Drive(Input(), 10);
        }
        private static void Tap(float duration = .1f)
        {
            player.SetInput(Input(defendPress: true, defendHeld: true)); player.Step(duration);
            player.SetInput(Input(defendRelease: true)); player.Step(.02f); Physics2D.SyncTransforms();
        }
        private static float JumpApex(bool cut)
        {
            Reset(); float start = player.Position.y, apex = start;
            player.SetInput(Input(jumpPress: true, jumpHeld: true)); Tick();
            for (int i = 0; i < 65; i++)
            {
                player.SetInput(Input(jumpHeld: !cut || i < 3, jumpRelease: cut && i == 3)); Tick();
                apex = Mathf.Max(apex, player.Position.y);
            }
            return apex - start;
        }

        private static IEnumerator Run()
        {
            for (int s = 0; s < SceneManager.sceneCount; s++)
                foreach (var root in SceneManager.GetSceneAt(s).GetRootGameObjects())
                { originalRoots.Add(root); originalStates.Add(root.activeSelf); root.SetActive(false); }
            testScene = SceneManager.CreateScene("D Player Verification " + DateTime.UtcNow.Ticks,
                new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            SceneManager.SetActiveScene(testScene); physics = testScene.GetPhysicsScene2D();
            var floorObject = new GameObject("Verification floor"); floorObject.layer = 8;
            floorObject.transform.position = new Vector2(0, -1);
            floor = floorObject.AddComponent<BoxCollider2D>(); floor.size = new Vector2(80, 2);
            player = DTestPlayerFactory.Create(new Vector2(0, .85f)); Reset();
            Check(physics.IsValid() && player.Body.bodyType == RigidbodyType2D.Dynamic && player.Grounded,
                "Separate PhysicsScene2D and actual dynamic capsule resting on the floor");
            Check(player.BodyCollider.sharedMaterial && Near(player.BodyCollider.sharedMaterial.friction, 0),
                "Capsule uses a persistent zero-friction material");
            Check(!player.ShieldCollider.isTrigger && player.ShieldCollider.gameObject.layer == 10,
                "Real nontrigger shield polygon uses separately isolated layer 10");

            // FixedUpdate is manual here; Update still consumes actual Input System state events.
            player.UseExternalInput = false; Key(); yield return .05f;
            Key(UnityEngine.InputSystem.Key.D); yield return .06f; Tick();
            Check(player.Velocity.x > 0 && player.Velocity.x < player.MaxSpeed && player.Facing == 1,
                "Real D input accelerates and turns right through normal Update");
            Key(); yield return .04f; Tick();
            Key(UnityEngine.InputSystem.Key.Space); yield return .04f; Tick();
            Check(player.Velocity.y > 10f && !player.Grounded, "Real Space edge jumps through normal Update");
            Key(); yield return .04f; float beforeCut = player.Velocity.y; Tick();
            Check(player.Velocity.y < beforeCut * .6f, "Real Space release cuts the rising jump");
            int serial = player.ParrySerial;
            Key(UnityEngine.InputSystem.Key.E); yield return .035f;
            Key(); yield return .035f;
            Check(player.ParrySerial == serial, "Update retains press/release edges until a physics step consumes them");
            Tick();
            Check(player.ParrySerial == serial + 1 && Near(player.ParryRemaining, .20f),
                "Both real E edges queued between steps create one full .20-second release parry");
            Key(); yield return .04f; Reset();

            Drive(Input(1), 1); float firstSpeed = player.Velocity.x;
            Check(Near(firstSpeed, .4f, .015f), "First ground acceleration step is 20 × .02 = .4 units/sec");
            Drive(Input(1), 30); Check(Near(player.Velocity.x, 6.8f), "Held movement reaches capped 6.8 speed");
            float oldSpeed = player.Velocity.x;
            Drive(Input(), 1); float ordinaryBrake = player.Velocity.x;
            Check(ordinaryBrake > 6f && Near(oldSpeed - ordinaryBrake, .24f, .015f),
                "Neutral ground braking preserves inertia, decreasing speed by .24 per step");
            Reset(); Drive(Input(1), 30);
            Drive(Input(defendPress: true, defendHeld: true), 1);
            Check(Near(player.Velocity.x, ordinaryBrake, .015f) && !player.GuardActive,
                "Pressing E while moving applies exactly ordinary braking, never an instant stop");
            Drive(Input(1, defendHeld: true), 35);
            Check(!player.GuardActive && Near(player.Velocity.x, 6.8f), "Holding E plus movement cannot auto-lock into guard");
            Drive(Input(-1, defendHeld: true), 15);
            Check(!player.GuardActive && player.Velocity.x < 0 && player.Facing == -1,
                "Reversal crosses zero without accidental guard while movement is requested");

            Reset(); Drive(Input(defendPress: true, defendHeld: true), 12);
            Check(player.GuardActive && !player.ParryActive, "Stopped grounded hold reaches guard after .22 seconds");
            Drive(Input(-1, jumpPress: true, jumpHeld: true, defendHeld: true), 1);
            Check(player.GuardActive && Near(player.Velocity.x, 0) && player.Velocity.y <= 0 && player.Facing == -1,
                "Active guard blocks walking/jumping while direction keys still turn the real shield");
            Check(player.CanGuardFrom(player.Position + new Vector2(-6, 1))
                && !player.CanGuardFrom(player.Position + new Vector2(6, 1)),
                "Guard accepts the facing attack source and rejects the opposite side");
            serial = player.ParrySerial; Drive(Input(defendRelease: true), 1);
            Check(!player.GuardActive && !player.ParryActive && player.ParrySerial == serial,
                "Long hold release disables guard and never creates a parry");
            Reset(3f); Drive(Input(defendPress: true, defendHeld: true), 12);
            Check(!player.GuardActive && !player.Grounded, "Airborne holding cannot activate guard");

            Reset(); Drive(Input(1), 30); float speed = player.Velocity.x;
            player.SetInput(Input(1, defendPress: true, defendHeld: true)); player.Step(.1f);
            player.SetInput(Input(1, defendRelease: true)); player.Step(.02f); Physics2D.SyncTransforms();
            Check(player.ParryActive && Near(player.Velocity.x, speed), "Running release parry preserves movement speed");
            Reset(3); Tap(); Check(player.ParryActive && !player.Grounded, "Airborne short release also parries");
            player.Step(.199f); Check(player.ParryActive, "Parry still active at .199 seconds");
            player.Step(.0011f); Check(!player.ParryActive, "Parry expires after exactly .20 seconds");
            Reset(); Tap(.22f); Check(player.ParryActive, "Tap duration exactly .22 is accepted");
            Reset(); Tap(.2202f); Check(!player.ParryActive, "Tap duration .2202 is rejected");

            Reset(); Tap(); Vector2 center = player.Position;
            Check(player.CanParryAt(center + new Vector2(.83f, .1f), .2f, Vector2.left), "Right shield ClosestPoint accepts front contact");
            Check(!player.CanParryAt(center + new Vector2(-.83f, .1f), .2f, Vector2.right), "Right shield rejects a back contact");
            Check(!player.CanParryAt(center + new Vector2(1.8f, .1f), .2f, Vector2.left), "Front direction alone cannot parry a distant orb");
            Check(player.CanParryAt(center + new Vector2(0, 1.06f), .2f, Vector2.down), "Visible crown accepts overhead contact");
            Check(!player.CanParryAt(center + new Vector2(-.081f, 1.02f), .2f, Vector2.down), "Overhead wrong-side tolerance is limited to .08 units");
            Check(!player.CanParryAt(center + new Vector2(.83f, .1f), .2f, Vector2.right), "Outgoing contact cannot count as another parry");
            Reset(facing: -1); Tap(); center = player.Position;
            Check(player.CanParryAt(center + new Vector2(-.83f, .1f), .2f, Vector2.right), "Mirrored physical shield accepts left contact");

            float full = JumpApex(false), shortJump = JumpApex(true);
            Check(full > 2.8f && full < 3.15f && shortJump < full - 1f,
                "Actual held/short jump apex: " + full.ToString("F3") + " / " + shortJump.ToString("F3"));
            Reset(); Drive(Input(jumpPress: true, jumpHeld: true), 1); Drive(Input(jumpHeld: true), 8);
            float beforeSecond = player.Velocity.y; Drive(Input(jumpPress: true, jumpHeld: true), 1);
            Check(player.Velocity.y < beforeSecond, "Second airborne press cannot reset vertical speed or double-jump");
            Reset(); floor.enabled = false; Drive(Input(), 3); Drive(Input(jumpPress: true, jumpHeld: true), 1);
            Check(player.Velocity.y > 10f, "Jump within .08 seconds of leaving support uses coyote time");
            Reset(); floor.enabled = false; Drive(Input(), 8); Drive(Input(jumpPress: true, jumpHeld: true), 1);
            Check(player.Velocity.y < 0f, "Jump after .12-second coyote expiry cannot launch in air");
            Reset(1.1f); player.Body.linearVelocity = Vector2.down * 2f;
            Drive(Input(jumpPress: true, jumpHeld: true), 1); bool buffered = false;
            for (int i = 0; i < 7; i++) { Drive(Input(jumpHeld: true), 1); if (player.Velocity.y > 10f) buffered = true; }
            Check(buffered, "Jump buffered shortly before physical landing launches on support");

            Reset(); player.UseAcceleration = false; Drive(Input(1), 1);
            Check(Near(player.Velocity.x, player.MaxSpeed) && firstSpeed < player.Velocity.x,
                "Acceleration ablation changes first-step speed from .4 to 6.8");
            Reset(); Drive(Input(1), 30); player.RequireStoppedGuard = false;
            Drive(Input(1, defendPress: true, defendHeld: true), 12);
            Check(player.GuardActive && Near(player.Velocity.x, 0), "Stopped-guard ablation permits the otherwise forbidden moving lock");
            Reset(); Drive(Input(defendPress: true, defendHeld: true), 12); player.GuardLocksMovement = false;
            Drive(Input(1, jumpPress: true, jumpHeld: true, defendHeld: true), 1);
            Check(player.Velocity.x > 0 && player.Velocity.y > 10f, "Guard-lock ablation allows movement and jumping from held guard");
            Reset(); Check(player.UseAcceleration && player.RequireStoppedGuard && player.GuardLocksMovement,
                "All delivery behavior flags restored true");
            player.SetInput(Input(defendPress: true, defendHeld: true)); player.Step(.05f);
            player.InputEnabled = false; player.InputEnabled = true; Drive(Input(defendRelease: true), 1);
            Check(!player.ParryActive, "Disabling input clears old press edges and prevents a stale release parry");
            int deaths = 0; player.Died += () => deaths++; player.Kill(); player.Kill(); player.View.TickVisual(.3f);
            Check(deaths == 1 && !player.Alive && !player.Body.simulated && !player.ShieldCollider.enabled
                && player.View.AnimationState == "Death", "Idempotent death disables physics and defense and animates persistent pieces");
            Reset(); player.View.TickVisual(.3f);
            Check(player.Alive && player.Body.simulated && !player.View.ShieldVisible, "Reset restores body and clears death/parry visuals");
            bool localArt = true, noArms = true;
            foreach (var r in player.GetComponentsInChildren<SpriteRenderer>(true))
            {
                localArt &= AssetDatabase.GetAssetPath(r.sprite).StartsWith("Assets/Art/D/", StringComparison.Ordinal)
                    && AssetDatabase.GetAssetPath(r.sharedMaterial).StartsWith("Assets/Art/D/", StringComparison.Ordinal);
                noArms &= r.name.IndexOf("arm", StringComparison.OrdinalIgnoreCase) < 0;
            }
            Check(localArt && noArms, "Armless persistent sprites/materials belong only to Assets/Art/D");
            Key(); yield return .04f;
        }
    }
}
#endif
