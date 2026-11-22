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
using Object = UnityEngine.Object;

namespace LavaRiseB
{
    /// <summary>Play Mode tests with a real isolated PhysicsScene2D. No synthetic collision or position integration.</summary>
    public static class BMovementVerification
    {
        private const float Dt = .02f;
        private static readonly List<string> results = new List<string>();
        private static readonly List<string> errors = new List<string>();
        private static IEnumerator sequence;
        private static double nextStep;
        private static int failures;
        private static Scene testScene;
        private static PhysicsScene2D physics;
        private static BPlayerMotor motor;
        private static BoxCollider2D floor, wall;
        public static bool IsRunning => sequence != null;
        public static int FailureCount => failures;

        [MenuItem("Lava Rise B/Verify movement in Play Mode")]
        public static void Begin()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("B movement verification requires Play Mode.");
            if (sequence != null) throw new InvalidOperationException("B movement verification is already running.");
            results.Clear(); errors.Clear(); failures = 0;
            Directory.CreateDirectory("Belge"); Directory.CreateDirectory("Temp/B_LavaRise");
            File.WriteAllText("Temp/B_LavaRise/movement-status.txt", "RUNNING");
            Application.logMessageReceived += Log;
            sequence = Run(); nextStep = 0;
            EditorApplication.update += Advance;
        }

        private static void Log(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message);
        }
        private static void Advance()
        {
            if (EditorApplication.timeSinceStartup < nextStep) return;
            try
            {
                if (sequence.MoveNext())
                {
                    nextStep = EditorApplication.timeSinceStartup + Convert.ToDouble(sequence.Current ?? 0f, CultureInfo.InvariantCulture);
                    return;
                }
                Finish();
            }
            catch (Exception exception) { Check(false, "Verification exception: " + exception); Finish(); }
        }
        private static void Finish()
        {
            EditorApplication.update -= Advance;
            (sequence as IDisposable)?.Dispose(); sequence = null;
            Application.logMessageReceived -= Log;
            Key();
            if (testScene.IsValid() && testScene.isLoaded) SceneManager.UnloadSceneAsync(testScene);
            Check(errors.Count == 0, "Runtime Error / Exception / Assert: " + errors.Count);
            string report = "# B — Gerçek fizik hareket doğrulaması\n\n"
                + "Çalıştırma: " + DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz") + "; Unity " + Application.unityVersion + ".\n\n"
                + "Editor Play Mode içinde bağımsız yerel PhysicsScene2D kullanıldı. Her adımda gerçek BPlayerMotor.ManualFixedStep(0.02) ve PhysicsScene2D.Simulate(0.02) çağrıldı; konum, yerçekimi veya çarpışma sonucu test koduyla taklit edilmedi. Klavye kontrolleri Input System olayları ve gerçek Update üzerinden geçti. Parkurun tamamı ve insan eşli oynanabilirlik bu hareket birim testinin kapsamı dışındadır.\n\n"
                + string.Join("\n", results) + "\n\nBaşarısız kontrol sayısı: " + failures + "\n";
            if (errors.Count > 0) report += "\n" + string.Join("\n", errors);
            File.WriteAllText("Belge/B_Hareket_Test_Raporu.md", report);
            File.WriteAllText("Temp/B_LavaRise/movement-status.txt", "DONE failures=" + failures);
            Debug.Log("B movement verification finished. failures=" + failures);
        }
        private static void Check(bool value, string label)
        {
            results.Add("- " + (value ? "PASS" : "FAIL") + " — " + label);
            if (!value) failures++;
        }
        private static string F(float value) => value.ToString("0.000", CultureInfo.InvariantCulture);
        private static BInputFrame Frame(float axis = 0f, bool pressed = false, bool held = false, bool released = false, bool change = false)
            => new BInputFrame(axis, pressed, held, released, change);
        private static void Step(BInputFrame frame, int count = 1)
        {
            for (int i = 0; i < count; i++)
            {
                motor.SetInput(frame); motor.ManualFixedStep(Dt); physics.Simulate(Dt);
            }
        }
        private static BoxCollider2D Solid(string name, Vector2 position, Vector2 size)
        {
            var root = new GameObject(name); SceneManager.MoveGameObjectToScene(root, testScene);
            root.transform.position = position;
            var shape = root.AddComponent<BoxCollider2D>(); shape.size = size;
            shape.sharedMaterial = motor.GetComponent<CapsuleCollider2D>().sharedMaterial;
            return shape;
        }
        private static void Reset(int profile = 0)
        {
            floor.enabled = true; floor.size = new Vector2(80, .6f); wall.enabled = false;
            motor.UseExternalInput = true; motor.ManualSimulation = true;
            motor.ResetAt(new Vector2(0, .74f), profile);
            Physics2D.SyncTransforms(); Step(Frame(), 25);
        }
        private static void Key(params Key[] keys)
        {
            if (Keyboard.current != null) InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(keys));
        }

        /// <summary>Public sequence for another Editor harness; yielded values are realtime delays in seconds.</summary>
        public static IEnumerator Run()
        {
            testScene = SceneManager.CreateScene("B movement QA " + DateTime.Now.Ticks, new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            physics = testScene.GetPhysicsScene2D();
            var root = new GameObject("B isolated real-physics test capsule");
            SceneManager.MoveGameObjectToScene(root, testScene);
            motor = root.AddComponent<BPlayerMotor>(); motor.ManualSimulation = true; motor.UseExternalInput = true;
            floor = Solid("QA stone and metal equivalent frictionless floor", new Vector2(0, -.3f), new Vector2(80, .6f));
            wall = Solid("QA vertical wall", new Vector2(1.7f, 1.6f), new Vector2(.3f, 5f)); wall.enabled = false;
            Reset();
            Check(physics.IsValid() && motor.Grounded, "Bağımsız PhysicsScene2D içinde gerçek kapsül zemin üzerinde duruyor");
            Check(motor.Body.bodyType == RigidbodyType2D.Dynamic && motor.Body.constraints == RigidbodyConstraints2D.FreezeRotation
                && motor.GetComponent<CapsuleCollider2D>().sharedMaterial.friction == 0f,
                "Dynamic Rigidbody2D, FreezeRotation ve friction=0");
            var coastDistances = new float[2];
            var apexes = new float[2];
            var accelerationSamples = new float[2];
            for (int profile = 0; profile < 2; profile++)
            {
                Reset(profile); string name = motor.CurrentProfile.Name;
                Step(Frame(1), 10); accelerationSamples[profile] = motor.Velocity.x;
                Check(motor.Velocity.x > .5f && motor.Velocity.x < motor.CurrentProfile.MaxSpeed - .5f,
                    name + " ivmelenir, ilk 0.20s hız=" + F(motor.Velocity.x));
                Step(Frame(1), 65);
                Check(Mathf.Abs(motor.Velocity.x - motor.CurrentProfile.MaxSpeed) < .06f,
                    name + " azami koşu hızı=" + F(motor.Velocity.x));
                float starting = motor.Body.position.x, speed = motor.Velocity.x;
                Step(Frame());
                Check(motor.Velocity.x > speed * .8f, name + " giriş bırakılınca hız bir anda sıfırlanmaz");
                Step(Frame(), 100); coastDistances[profile] = motor.Body.position.x - starting;
                Check(Mathf.Abs(motor.Velocity.x) < .01f, name + " frenle durur; kayma mesafesi=" + F(coastDistances[profile]));
                Reset(profile); Step(Frame(1), 65); Step(Frame(-1));
                Check(motor.Velocity.x > 0f, name + " ters giriş ilk adımda momentumu korur");
                Step(Frame(-1), 60);
                Check(motor.Velocity.x < -motor.CurrentProfile.MaxSpeed + .06f, name + " ters yön ivmesi sonunda yönü değiştirir");
                Reset(profile); float baseY = motor.Body.position.y;
                Step(Frame(0, true, true)); float apex = motor.Body.position.y;
                for (int i = 0; i < 100; i++) { Step(Frame(0, false, true)); apex = Mathf.Max(apex, motor.Body.position.y); }
                apexes[profile] = apex - baseY;
                Check(apexes[profile] > 2.35f && apexes[profile] < 2.9f && motor.Grounded,
                    name + " uzun basılı zıplama sınırlı, tepe=" + F(apexes[profile]) + "; yere döndü");
                Check(motor.JumpCount == 1, name + " Space basılı tutulunca inişte otomatik tekrar zıplamaz");
                Reset(profile); baseY = motor.Body.position.y;
                Step(Frame(0, true, true)); Step(Frame(0, false, true), 2);
                Step(Frame(0, false, false, true)); float tapApex = motor.Body.position.y;
                for (int i = 0; i < 80; i++) { Step(Frame()); tapApex = Mathf.Max(tapApex, motor.Body.position.y); }
                tapApex -= baseY;
                Check(tapApex > .45f && tapApex < apexes[profile] - .7f,
                    name + " kısa basış daha alçak; tepe=" + F(tapApex));
                Reset(profile); Step(Frame(1, true, true)); Step(Frame(1, false, true), 5);
                float airSpeed = motor.Velocity.x;
                Reset(profile); Step(Frame(1), 6);
                Check(airSpeed > 0f && airSpeed < motor.Velocity.x,
                    name + " havada yön kontrolü var, yer ivmesinden düşük (" + F(airSpeed) + " < " + F(motor.Velocity.x) + ")");
                yield return 0f;
            }
            Check(coastDistances[0] > coastDistances[1] * 1.7f && accelerationSamples[1] > accelerationSamples[0],
                "Profil ödünleşimi: Akış daha çok kayar; Denge daha hızlı ivmelenir/frenler");
            Check(Mathf.Abs(apexes[0] - apexes[1]) < .02f, "Profil değişimi ortak zıplama yüksekliğini değiştirmez");
            Reset(); Step(Frame(1), 65); float before = motor.Velocity.x;
            Step(Frame(1, false, false, false, true));
            Check(motor.ProfileIndex == 1 && motor.Velocity.x > motor.CurrentProfile.MaxSpeed && Mathf.Abs(before - motor.Velocity.x) < .4f,
                "E profil değişimi hızı aniden yeni limite kesmez");

            Reset(); motor.SetInput(Frame(0, true, true)); motor.SetInput(Frame(0, false, true));
            motor.ManualFixedStep(Dt); physics.Simulate(Dt);
            Check(motor.JumpCount == 1, "İki Update örneği arasındaki basış, sonraki FixedUpdate için kuyrukta korunur");
            Step(Frame(0, true, true), 90);
            Check(motor.JumpCount == 1, "Aynı basılı girişin tekrar gönderilmesi ikinci zıplama üretmez");
            Step(Frame(0, false, false, true)); Step(Frame(0, true, true));
            Check(motor.JumpCount == 2, "Bırakıp yeniden basmak yeni bir zıplamaya izin verir");

            for (int test = 0; test < 2; test++)
            {
                Reset(); floor.size = new Vector2(4, .6f); motor.ResetAt(new Vector2(1.7f, .74f));
                Physics2D.SyncTransforms(); Step(Frame(), 5); motor.Body.linearVelocity = new Vector2(6.6f, 0);
                int guard = 0;
                while (motor.Grounded && guard++ < 35) Step(Frame(1));
                Step(Frame(1), test == 0 ? 2 : 8);
                Step(Frame(1, true, true));
                Check(test == 0 ? motor.JumpCount == 1 && motor.Velocity.y > 8f : motor.JumpCount == 0,
                    test == 0 ? "Kenardan ayrıldıktan 0.04s sonra coyote jump çalışır" : "0.16s sonra coyote süresi bitmiştir; havada yeni zıplama yok");
            }
            Reset(); motor.ResetAt(new Vector2(0, 2.5f)); Physics2D.SyncTransforms();
            int fallingGuard = 0;
            while (motor.Body.position.y > 1.1f && fallingGuard++ < 80) Step(Frame());
            Step(Frame(0, true, true)); Step(Frame(0, false, true), 8);
            Check(motor.JumpCount == 1 && motor.Velocity.y > 4f, "Yere inmeden basılan Space, 0.14s buffer içinde tek zıplamaya dönüşür");

            Reset(); floor.size = new Vector2(4, .6f); motor.ResetAt(new Vector2(2.025f, .74f));
            Physics2D.SyncTransforms(); Step(Frame(), 3);
            Check(motor.Grounded, "Kapsül merkezinin platform kenarını hafif aşması zemin algısını bozmaz");
            Reset(); floor.enabled = false; wall.enabled = true;
            motor.ResetAt(new Vector2(1.21f, 2f)); Physics2D.SyncTransforms();
            Step(Frame(1), 12); float wallY = motor.Body.position.y;
            Step(Frame(1, true, true), 5);
            Check(!motor.Grounded && motor.JumpCount == 0 && motor.Body.position.y < wallY,
                "Dikey duvar zemin değildir: duvara yapışma, tırmanma ve duvar zıplaması yok");
            yield return 0f;

            Reset(); int deaths = 0; motor.Died += () => deaths++;
            motor.Kill(); motor.Kill(); Vector2 deathAt = motor.Body.position;
            Step(Frame(1, true, true), 20);
            Check(!motor.IsAlive && !motor.Body.simulated && deaths == 1 && motor.Body.position == deathAt,
                "Ölüm bir kez yayınlanır, gövde fiziği durur ve hareket girdisi etkisizdir");
            Reset(1);
            Check(motor.IsAlive && motor.InputEnabled && motor.JumpCount == 0 && motor.ProfileIndex == 1 && motor.Grounded,
                "ResetAt fizik, profil ve sayaçları temiz biçimde yeniden kurar");

            Reset(); motor.UseExternalInput = false;
            if (Keyboard.current == null) Check(false, "Input System klavyesi bulunamadı");
            else
            {
                Key(UnityEngine.InputSystem.Key.D, UnityEngine.InputSystem.Key.Space); yield return .08f;
                motor.ManualFixedStep(Dt); physics.Simulate(Dt);
                Check(motor.Velocity.x > 0f && motor.JumpCount == 1,
                    "Gerçek Input System D + Space olayı Update üzerinden FixedUpdate hareketine ulaştı");
                Key(); yield return .06f; motor.ManualFixedStep(Dt); physics.Simulate(Dt);
                Key(UnityEngine.InputSystem.Key.E); yield return .06f; motor.ManualFixedStep(Dt); physics.Simulate(Dt);
                Check(motor.ProfileIndex == 1, "Gerçek Input System E olayı Update üzerinden profil değiştirdi");
                Key(); yield return .06f;
                motor.UseExternalInput = true; Reset(); motor.UseExternalInput = false;
                Key(UnityEngine.InputSystem.Key.LeftArrow); yield return .06f;
                motor.ManualFixedStep(Dt); physics.Simulate(Dt);
                Check(motor.Velocity.x < 0f, "Gerçek sol ok olayı sola hareket üretti");
                Key(); yield return .06f;
            }
        }
    }
}
#endif
