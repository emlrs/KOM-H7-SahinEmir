#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CollisionC
{
    /// <summary>Run Begin in Play Mode. Each fixture uses real shapes in an isolated Physics2D scene.</summary>
    public static class COrbVerification
    {
        private static IEnumerator routine;
        private static Scene testScene;
        private static readonly StringBuilder report = new StringBuilder();
        private const string StatusPath = "Temp/C_Carpisma/orb-verification-status.txt";
        private const string ReportPath = "Belge/C_Top_Fizik_Dogrulama.md";

        [MenuItem("Tools/Collision C/Verify Orb (Play Mode)")]
        public static void Begin()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("COrbVerification.Begin requires Play Mode.");
            if (routine != null) throw new InvalidOperationException("Orb verification is already running.");
            Directory.CreateDirectory("Temp/C_Carpisma");
            Directory.CreateDirectory("Belge");
            File.WriteAllText(StatusPath, "RUNNING");
            report.Clear();
            report.AppendLine("# C — Gerçek Collider2D top fiziği doğrulaması\n");
            report.AppendLine("Play Mode; bağımsız Physics2D sahnesi; gerçek CircleCast/ClosestPoint temasları. Kayıtlı sahne değiştirilmez.\n");
            testScene = SceneManager.CreateScene("C_OrbVerification_" + DateTime.Now.Ticks,
                new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            routine = Run();
            EditorApplication.update += Advance;
        }

        private static void Advance()
        {
            try
            {
                if (!EditorApplication.isPlaying) throw new InvalidOperationException("Play Mode ended during verification.");
                if (routine.MoveNext()) return;
                Finish("PASS", null);
            }
            catch (Exception error) { Finish("FAIL", error); }
        }

        private static void Finish(string status, Exception error)
        {
            EditorApplication.update -= Advance;
            routine = null;
            if (error != null) report.AppendLine("\nFAIL: " + error);
            report.AppendLine("\nSonuç: **" + status + "**");
            report.AppendLine("\nAlıcı şarjı, geri sayım, gerçek klavye ve tam oyun akışı ayrıca C akış/entegrasyon doğrulamasında sınanır.");
            File.WriteAllText(ReportPath, report.ToString());
            File.WriteAllText(StatusPath, status + (error == null ? "" : "\n" + error));
            if (testScene.IsValid() && testScene.isLoaded) SceneManager.UnloadSceneAsync(testScene);
            if (error == null) Debug.Log("COrbVerification PASS — " + ReportPath);
            else Debug.LogException(error);
        }

        private static IEnumerator Run()
        {
            yield return null;
            VerifySpeedAndFreeze(); yield return null;
            VerifyThinWallAndCorner(); yield return null;
            VerifyPlateEpisodes(); yield return null;
            VerifyParrySidesAndEgress(); yield return null;
            VerifyDiagonalAndPriority(); yield return null;
            VerifyRandomAngles(); yield return null;
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void Pass(string message) => report.AppendLine("- PASS — " + message);

        private static void VerifySpeedAndFreeze()
        {
            using (var f = new Fixture())
            {
                f.Settings.InitialSpeed = 5.5f; f.Settings.MaximumSpeed = 8f; f.Settings.AccelerationPerSecond = .025f;
                f.Orb.ResetOrb(Vector2.zero, new Vector2(.8f, .6f));
                f.Orb.Step(10f);
                Vector2 oneStep = f.Orb.Position;
                Check(Mathf.Abs(f.Orb.CurrentSpeed - 5.75f) < .002f, "Time-based speed after ten seconds.");
                Check(Mathf.Abs(oneStep.magnitude - 56.25f) < .04f, "Integrated travel with acceleration.");
                f.Orb.ResetOrb(Vector2.zero, new Vector2(.8f, .6f));
                for (int i = 0; i < 1000; i++) f.Orb.Step(.01f);
                Check(Vector2.Distance(oneStep, f.Orb.Position) < .04f, "Travel depends on step size.");
                f.Orb.Step(100f);
                Check(Mathf.Abs(f.Orb.CurrentSpeed - 8f) < .002f, "Speed must be capped at eight.");
                Vector2 frozen = f.Orb.Position; float age = f.Orb.ActiveElapsed;
                f.Orb.Freeze(); f.Orb.Step(5f);
                Check(f.Orb.Position == frozen && f.Orb.ActiveElapsed == age, "Freeze must stop motion and speed clock.");
                Pass("Saniyeye bağlı hız artışı, 8 m/s sınırı, büyük/küçük zaman adımı eşdeğerliği ve donmuş saat.");
            }
        }

        private static void VerifyThinWallAndCorner()
        {
            using (var f = new Fixture())
            {
                f.Box("Thin solid", new Vector2(1, 0), new Vector2(.012f, 8));
                f.Orb.ResetOrb(Vector2.zero, Vector2.right); f.Orb.Step(.35f);
                Check(f.Orb.WallBounceCount == 1 && f.Orb.Velocity.x < 0 && f.Orb.Position.x < .8f,
                    "8 m/s swept motion tunneled through a thin solid.");
                Pass("8 m/s ve .35 saniyelik adımda ince gerçek duvar delinmeden yansıma.");
            }
            using (var f = new Fixture())
            {
                f.Box("Right", new Vector2(2.05f, 0), new Vector2(.1f, 5));
                f.Box("Top", new Vector2(0, 2.05f), new Vector2(5, .1f));
                f.Box("Left", new Vector2(-2.05f, 0), new Vector2(.1f, 5));
                f.Box("Bottom", new Vector2(0, -2.05f), new Vector2(5, .1f));
                f.Orb.ResetOrb(Vector2.zero, Vector2.one); f.Orb.Step(.4f);
                Check(f.Orb.Velocity.x < 0 && f.Orb.Velocity.y < 0 && f.Orb.WallBounceCount >= 2,
                    "A corner must reflect both incoming axes.");
                for (int i = 0; i < 300; i++)
                {
                    f.Orb.Step(.17f);
                    Check(Mathf.Abs(f.Orb.Position.x) <= 1.805f && Mathf.Abs(f.Orb.Position.y) <= 1.805f,
                        "Long corner run escaped its actual colliders.");
                }
                Check(f.Orb.SolverLimitCount == 0, "Ordinary arena unexpectedly exhausted solver budget.");
                Check(Mathf.Abs(f.Orb.Velocity.magnitude - 8f) < .001f, "Bounces altered speed magnitude.");
                Pass("Köşede iki normal, 51 saniye seri çarpışma, arena içinde kalma ve sabit yansıma hızı.");
            }
            using (var f = new Fixture())
            {
                var trigger = f.Box("Uncharged trigger", new Vector2(.6f, 0), new Vector2(.1f, 2));
                trigger.isTrigger = true;
                f.Orb.ResetOrb(Vector2.zero, Vector2.right); f.Orb.Step(.2f);
                Check(f.Orb.WallBounceCount == 0 && f.Orb.Position.x > 1.5f && f.Orb.Velocity.x > 0,
                    "A trigger incorrectly reflected the orb.");
                Pass("Gerçek trigger hacmi çarpışma duvarı gibi yansıtmaz.");
            }
        }

        private static void VerifyPlateEpisodes()
        {
            using (var f = new Fixture())
            {
                var normal = f.Plate(new Vector2(1, 0), 1);
                f.Orb.ResetOrb(Vector2.zero, Vector2.right); f.Orb.Step(.1f);
                Check(normal.Broken && !normal.Solid.enabled && f.Orb.Velocity.x < 0,
                    "Normal plate must break and reflect on the first swept contact.");
                Pass("Normal plaka ilk gerçek temasta kırılır; top yansır.");
            }
            using (var f = new Fixture())
            {
                var armor = f.Plate(new Vector2(1, 0), 2);
                // Only .01 world units of free centre travel: repeated bounces remain one
                // contact episode because the orb never separates by EpisodeSeparation.
                var nearWall = f.Box("Persistent contact test wall", new Vector2(.49f, 0), new Vector2(.1f, 2));
                f.Orb.ResetOrb(new Vector2(.745f, 0), Vector2.right); f.Orb.Step(.05f);
                Check(!armor.Broken && armor.RemainingHits == 1 && f.Orb.WallBounceCount > 2,
                    "Repeated sustained contact incorrectly dealt the second armored hit.");
                nearWall.enabled = false;
                f.Orb.Step(.08f);
                f.Box("Distant return wall", new Vector2(-1, 0), new Vector2(.1f, 2));
                f.Orb.Step(.6f);
                Check(armor.Broken && armor.RemainingHits == 0,
                    "A genuinely separated return contact must break armor.");
                armor.ResetPlate();
                Check(!armor.Broken && armor.RemainingHits == 2 && armor.Solid.enabled, "Armor reset failed.");
                Pass("Zırhlı plaka kesintisiz yakın temaslarda tek hasar alır; ayrılıp geri gelince ikinci hasarla kırılır; sıfırlanır.");
            }
        }

        private static void VerifyParrySidesAndEgress()
        {
            foreach (int facing in new[] { 1, -1 })
            {
                using (var f = new Fixture())
                {
                    var player = f.Player(facing); f.Tap(player);
                    f.Orb.ResetOrb(new Vector2(facing * 1.2f, .15f), new Vector2(-facing, 0));
                    f.Orb.Step(.1f);
                    Check(player.Alive && f.Orb.ParryCount == 1 && f.Orb.LastParryDirection.x * facing > 0,
                        "Correct-side parry failed for facing " + facing);
                    Check(Vector2.Distance(f.Orb.LastParryPosition, player.BodyCollider.ClosestPoint(f.Orb.LastParryPosition)) >= f.Orb.Radius,
                        "Successful parry left the orb inside the lethal body.");
                    Check(Vector2.Distance(f.Orb.LastParryPosition, player.ShieldCollider.ClosestPoint(f.Orb.LastParryPosition)) >= f.Orb.Radius,
                        "Successful parry left the orb inside its shield.");
                    f.Orb.Step(.05f);
                    Check(player.Alive && f.Orb.ParryCount == 1, "The same successful contact repeated or caused instant death.");
                }
                using (var f = new Fixture())
                {
                    var player = f.Player(facing); f.Tap(player);
                    f.Orb.ResetOrb(new Vector2(-facing * .9f, .15f), new Vector2(facing, 0));
                    f.Orb.Step(.15f);
                    Check(!player.Alive && f.Orb.ParryCount == 0, "Wrong-side shield prevented lethal body contact.");
                }
            }
            Pass("Sağ/sol doğru yön savunması, yanlış yönün öldürmesi ve dokunulmazlık kullanmadan gövde/kalkandan güvenli çıkış.");
        }

        private static void VerifyDiagonalAndPriority()
        {
            foreach (int facing in new[] { 1, -1 })
            {
                using (var f = new Fixture())
                {
                    var player = f.Player(facing); f.Tap(player);
                    f.Orb.ResetOrb(new Vector2(facing * .9f, 1.3f), new Vector2(-facing * .65f, -.76f));
                    f.Orb.Step(.18f);
                    Check(player.Alive && f.Orb.ParryCount == 1 && f.Orb.LastParryDirection.x * facing > 0,
                        "Correct upper-diagonal contact was not parried.");
                }
                using (var f = new Fixture())
                {
                    var player = f.Player(facing); f.Tap(player);
                    f.Orb.ResetOrb(new Vector2(-facing * .9f, 1.3f), new Vector2(facing * .65f, -.76f));
                    f.Orb.Step(.25f);
                    Check(!player.Alive && f.Orb.ParryCount == 0, "Wrong upper-diagonal contact was incorrectly protected.");
                }
            }
            using (var f = new Fixture())
            {
                var player = f.Player(1); f.Tap(player);
                // Orb overlaps the crown AND the capsule cap: this reproduces simultaneous
                // shield/body contact caused by player movement at the fixed-step boundary.
                f.Orb.ResetOrb(new Vector2(0, .91f), Vector2.down); f.Orb.Step(.02f);
                Check(player.Alive && f.Orb.ParryCount == 1 && f.Orb.LastParryDirection.x > 0,
                    "Body death won over a valid simultaneous crown/shield parry.");
                Check(f.Orb.LastParryPosition.y > 1.10f, "Crown egress did not geometrically separate.");
                // A fresh real collision remains deadly immediately; there is no immunity clock.
                player.InputEnabled = false;
                f.Orb.Body.position = new Vector2(0, .95f); f.Orb.transform.position = f.Orb.Body.position;
                f.Orb.Step(.02f);
                // Direction after crown reflection points away, so test lethal overlap at torso.
                f.Orb.Body.position = Vector2.zero; f.Orb.transform.position = Vector2.zero;
                f.Orb.Step(.02f);
                Check(!player.Alive, "A successful parry granted blanket immunity to a new body overlap.");
            }
            Pass("İki üst çapraz yön, yanlış üst tarafın tehlikesi, eşzamanlı kalkana öncelik, taç çıkışı ve sonraki gerçek temasta dokunulmazlık olmaması.");
        }

        private static void VerifyRandomAngles()
        {
            using (var f = new Fixture())
            {
                var player = f.Player(1);
                bool up = false, down = false;
                float smallest = 90f, largest = 0f;
                f.Orb.SetRandomSeed(71591);
                for (int i = 0; i < 64; i++)
                {
                    player.ResetAt(Vector2.zero, 1); player.UseExternalInput = true; f.Tap(player);
                    f.Orb.ResetOrb(new Vector2(1.2f, .15f), Vector2.left); f.Orb.Step(.06f);
                    Check(f.Orb.ParryCount == 1, "Random-angle sample did not reach its shield.");
                    Vector2 output = f.Orb.LastParryDirection;
                    float angle = Mathf.Abs(Mathf.Atan2(output.y, output.x) * Mathf.Rad2Deg);
                    Check(output.x > 0 && angle >= 21.99f && angle <= 62.01f, "Outgoing direction escaped its configured half-plane/angle interval.");
                    up |= output.y > 0; down |= output.y < 0;
                    smallest = Mathf.Min(smallest, angle); largest = Mathf.Max(largest, angle);
                }
                Check(up && down && largest - smallest > 20f, "Parries locked to one axis/sign/angle.");
                Pass("64 deterministik örnekte hem yukarı hem aşağı; doğru yarı düzlemde 22–62° rastgele açı çeşitliliği (alıcı konumu kullanılmaz).");
            }
        }

        private sealed class Fixture : IDisposable
        {
            public readonly GameObject Root;
            public readonly COrbMotor Orb;
            public readonly COrbSettings Settings;
            public Fixture()
            {
                Root = new GameObject("Temporary orb physics fixture");
                SceneManager.MoveGameObjectToScene(Root, testScene);
                Settings = ScriptableObject.CreateInstance<COrbSettings>();
                Settings.InitialSpeed = Settings.MaximumSpeed = 8f; Settings.AccelerationPerSecond = 0;
                var obj = Child("Orb", Vector2.zero);
                Orb = obj.AddComponent<COrbMotor>(); Orb.ManualSimulation = true; Orb.Settings = Settings;
                Orb.CollisionShape.radius = .2f; Orb.SetRandomSeed(27183);
            }
            private GameObject Child(string name, Vector2 position)
            {
                var obj = new GameObject(name); obj.transform.SetParent(Root.transform, false); obj.transform.position = position;
                return obj;
            }
            public BoxCollider2D Box(string name, Vector2 position, Vector2 size)
            {
                var box = Child(name, position).AddComponent<BoxCollider2D>(); box.size = size; Physics2D.SyncTransforms(); return box;
            }
            public CPlate Plate(Vector2 position, int hits)
            {
                var box = Box("Plate", position, new Vector2(.1f, 2));
                var plate = box.gameObject.AddComponent<CPlate>(); plate.Solid = box; plate.MaximumHits = hits; plate.ResetPlate(); return plate;
            }
            public CPlayerMotor Player(int facing)
            {
                var player = Child("Player", Vector2.zero).AddComponent<CPlayerMotor>();
                player.ManualSimulation = true; player.UseExternalInput = true;
                var shield = new GameObject("Real physical shield"); shield.transform.SetParent(player.transform, false);
                var shape = shield.AddComponent<PolygonCollider2D>(); shape.points = CPlayerMotor.ShieldPoints();
                shape.isTrigger = false; shield.transform.localScale = new Vector3(facing, 1, 1);
                player.ShieldCollider = shape; player.ResetAt(Vector2.zero, facing); Orb.Player = player;
                Physics2D.SyncTransforms(); return player;
            }
            public void Tap(CPlayerMotor player)
            {
                player.SetInput(new CInputFrame(0, true, true, false)); player.Step(.02f);
                player.SetInput(new CInputFrame(0, false, false, true)); player.Step(.02f);
                Check(player.ParryActive, "Test fixture failed to trigger a real tap-release parry.");
                Physics2D.SyncTransforms();
            }
            public void Dispose()
            {
                UnityEngine.Object.DestroyImmediate(Root);
                UnityEngine.Object.DestroyImmediate(Settings);
            }
        }
    }
}
#endif
