#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CollisionC
{
    // Only feeds ordinary horizontal and E-edge commands. Never changes a pose during a run.
    public static class CRunVerification
    {
        [Serializable] public class Frame { public float Move; public bool Press, Held, Release; }
        [Serializable] public class Record
        {
            public int Seed, Parries, Bounces; public float Time; public bool Passed;
            public string Detail; public List<Frame> Frames = new List<Frame>();
        }
        static CGameController game;
        static SimulationMode2D oldSimulation;
        static bool oldBackground, replaying;
        static Record replay;
        static int replayIndex;
        static double previousWall, wallStart;
        static float accumulator;
        static readonly List<string> errors = new List<string>();
        const float Dt = .02f;
        const string Folder = "Temp/C_Carpisma/";
        const string Evidence = "Belge/C_TestKaniti/";
        static int visualStage;
        static double visualAt;

        public static void VisualTest()
        {
            Bind(); game.BeginRun(); game.Player.UseExternalInput = true;
            game.Orb.ResetOrb(game.Player.Position + new Vector2(1.6f, .15f), Vector2.left);
            game.Player.SetInput(new CInputFrame(0, true, true, false));
            visualStage = 0; visualAt = EditorApplication.timeSinceStartup + .06;
            File.WriteAllText(Folder + "visual-status.txt", "RUNNING");
            EditorApplication.update += VisualUpdate;
        }
        static void VisualUpdate()
        {
            if (EditorApplication.timeSinceStartup < visualAt) return;
            try
            {
                if (!EditorApplication.isPlaying) throw new InvalidOperationException("Play Mode stopped.");
                switch (visualStage++)
                {
                    case 0:
                        game.Player.SetInput(new CInputFrame(0, false, false, true));
                        visualAt += .10; break;
                    case 1:
                        if (game.ParryCount != 1 || !game.Player.Alive) throw new InvalidOperationException("Visual real-time parry did not resolve.");
                        ScreenCapture.CaptureScreenshot(Path.GetFullPath(Evidence + "04-parry.png"));
                        visualAt += .35; break;
                    case 2:
                        game.BeginRun(); game.Player.UseExternalInput = true;
                        game.Orb.ResetOrb(game.Player.Position + new Vector2(1.15f, .1f), Vector2.left);
                        visualAt += .25; break;
                    case 3:
                        if (game.Phase != CGamePhase.Dying || game.Player.Alive) throw new InvalidOperationException("Actual body contact did not kill.");
                        ScreenCapture.CaptureScreenshot(Path.GetFullPath(Evidence + "05-olum-patlama.png"));
                        visualAt += .4; break;
                    case 4:
                        ScreenCapture.CaptureScreenshot(Path.GetFullPath(Evidence + "06-olum-parcalar.png"));
                        visualAt += 1.1; break;
                    default:
                        if (game.Phase != CGamePhase.Lost) throw new InvalidOperationException("Death did not reach results.");
                        ScreenCapture.CaptureScreenshot(Path.GetFullPath(Evidence + "07-olum-sonuc.png"));
                        string result = "PASS real-time parry, actual body death, fragments, Lost after animation; errors=" + errors.Count;
                        File.WriteAllText(Folder + "visual-status.txt", result);
                        File.WriteAllText("Belge/C_Gorsel_Test.txt", result + "\nBağımsız görsel fixture: top başlangıcı kasıtlı yerleştirildi; bu test tam koşu sayılmaz. Sonraki hareket ve ölüm normal FixedUpdate/LateUpdate ile çalıştı.");
                        EditorApplication.update -= VisualUpdate; Restore(false); break;
                }
            }
            catch (Exception e)
            {
                File.WriteAllText(Folder + "visual-status.txt", "FAIL " + e);
                EditorApplication.update -= VisualUpdate; Restore();
            }
        }

        public static void Begin()
        {
            Bind();
            File.WriteAllText(Folder + "run-status.txt", "RUNNING");
            var report = new StringBuilder("# C — Tam koşu entegrasyonu\n\nGerçek Play Mode; kayıtlı C sahnesi, .02 s adım. Test sürücüsü yalnız hareket ve E bas/bırak girdisi verir. Koşu sırasında konum, sayaç, şarj veya kapı durumu değiştirilmez. Tohum yalnız tekrar üretilebilir rastgele açı içindir. Bu otomatik test insan beceri testi değildir.\n\n");
            int passed = 0;
            try
            {
                for (int seed = 1; seed <= 5; seed++)
                {
                    Prepare(seed);
                    var pilot = new Pilot();
                    var record = new Record { Seed = seed };
                    for (int step = 0; step < 6500 && game.IsPlaying; step++)
                    {
                        Frame input = pilot.Input(game);
                        record.Frames.Add(input); Step(input);
                    }
                    record.Passed = game.Phase == CGamePhase.Won;
                    record.Time = game.Elapsed; record.Parries = game.ParryCount; record.Bounces = game.Orb.WallBounceCount;
                    record.Detail = "phase=" + game.Phase + ", player=" + game.Player.Position + ", orb=" + game.Orb.Position + ", velocity=" + game.Orb.Velocity + ", solver limits=" + game.Orb.SolverLimitCount;
                    File.WriteAllText(Folder + "run-last-" + seed + ".json", JsonUtility.ToJson(record));
                    if (record.Passed)
                    {
                        passed++;
                        if (passed == 1) File.WriteAllText(Folder + "run-tape.json", JsonUtility.ToJson(record));
                    }
                    report.AppendLine("- " + (record.Passed ? "PASS" : "INCOMPLETE") + " — seed " + seed + "; " + record.Time.ToString("F2") + " s; parry=" + record.Parries + "; wall=" + record.Bounces + "; " + record.Detail);
                }
                report.AppendLine("\nTamamlanan koşu: " + passed + "/5. Runtime errors=" + errors.Count + ".");
                File.WriteAllText("Belge/C_Tam_Kosu_Raporu.md", report.ToString());
                File.WriteAllText(Folder + "run-status.txt", "DONE complete=" + passed + "/5 errors=" + errors.Count);
            }
            finally { Restore(); }
        }

        public static void Replay()
        {
            Bind();
            replay = JsonUtility.FromJson<Record>(File.ReadAllText(Folder + "run-tape.json"));
            Prepare(replay.Seed);
            replayIndex = 0; accumulator = 0; previousWall = wallStart = EditorApplication.timeSinceStartup;
            replaying = true; File.WriteAllText(Folder + "replay-status.txt", "RUNNING");
            EditorApplication.update += ReplayUpdate;
        }
        static void ReplayUpdate()
        {
            try
            {
                if (!EditorApplication.isPlaying) throw new InvalidOperationException("Play Mode stopped.");
                double now = EditorApplication.timeSinceStartup;
                accumulator += (float)Math.Min(.1, now - previousWall); previousWall = now;
                while (accumulator >= Dt && replayIndex < replay.Frames.Count)
                {
                    Step(replay.Frames[replayIndex++]); accumulator -= Dt;
                    if (replayIndex == 120) ScreenCapture.CaptureScreenshot(Path.GetFullPath(Evidence + "01-arena.png"));
                    if (replayIndex == 2255) ScreenCapture.CaptureScreenshot(Path.GetFullPath(Evidence + "02-alici-acik.png"));
                }
                if (replayIndex < replay.Frames.Count) return;
                bool won = game.Phase == CGamePhase.Won;
                string result = (won && errors.Count == 0 ? "PASS" : "FAIL") + " phase=" + game.Phase + " game=" + game.Elapsed.ToString("F2") + " wall=" + (now - wallStart).ToString("F2") + " parries=" + game.ParryCount + " errors=" + errors.Count;
                File.WriteAllText(Folder + "replay-status.txt", result);
                File.WriteAllText("Belge/C_Gercek_Zaman_Kosusu.txt", result + "\nNormal görüntü karelerinde, gerçek zaman hızında .02 s fizik ve aynı girdiler. İnsan oyunu değildir.\n");
                ScreenCapture.CaptureScreenshot(Path.GetFullPath(Evidence + "03-basari.png"));
                EditorApplication.update -= ReplayUpdate; replaying = false; Restore(false);
            }
            catch (Exception e)
            {
                EditorApplication.update -= ReplayUpdate; replaying = false;
                File.WriteAllText(Folder + "replay-status.txt", "FAIL " + e); Restore();
            }
        }
        static void Bind()
        {
            if (!EditorApplication.isPlaying || replaying) throw new InvalidOperationException("Idle Play Mode required.");
            game = UnityEngine.Object.FindAnyObjectByType<CGameController>();
            if (!game) throw new InvalidOperationException("Open the saved C scene.");
            Directory.CreateDirectory(Folder); Directory.CreateDirectory(Evidence);
            oldSimulation = Physics2D.simulationMode; oldBackground = Application.runInBackground;
            Application.runInBackground = true; errors.Clear(); Application.logMessageReceived += Log;
        }
        static void Prepare(int seed)
        {
            Physics2D.simulationMode = SimulationMode2D.Script;
            game.ManualSimulation = game.Player.ManualSimulation = game.Orb.ManualSimulation = true;
            game.Player.UseExternalInput = true; game.BeginRun(); game.Orb.SetRandomSeed(seed);
            game.Player.Body.interpolation = game.Orb.Body.interpolation = RigidbodyInterpolation2D.None;
            Physics2D.SyncTransforms();
        }
        static void Step(Frame input)
        {
            game.Tick(Dt);
            game.Player.SetInput(new CInputFrame(input.Move, input.Press, input.Held, input.Release));
            game.Player.Step(Dt); Physics2D.SyncTransforms();
            game.Orb.Step(Dt); Physics2D.SyncTransforms(); Physics2D.Simulate(Dt);
        }
        static void Restore(bool reset = true)
        {
            Application.logMessageReceived -= Log;
            Physics2D.simulationMode = oldSimulation; Application.runInBackground = oldBackground;
            if (!game) return;
            game.ManualSimulation = game.Player.ManualSimulation = game.Orb.ManualSimulation = false;
            game.Player.UseExternalInput = false;
            game.Player.Body.interpolation = game.Orb.Body.interpolation = RigidbodyInterpolation2D.Interpolate;
            if (reset) game.BeginRun();
        }
        static void Log(string msg, string stack, LogType type)
        { if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert) errors.Add(msg); }

        sealed class Pilot
        {
            int heldFrames;
            float cooldown;
            public Frame Input(CGameController g)
            {
                var f = new Frame();
                var p = g.Player; var orb = g.Orb;
                if (g.Phase == CGamePhase.DoorOpen)
                { f.Move = Mathf.Abs(p.Position.x) > .05f ? -Mathf.Sign(p.Position.x) : 0; return f; }
                Vector2 b = orb.Position, v = orb.Velocity;
                float targetY = -2.4f;
                float time = v.y < -.01f ? Mathf.Max(0, (targetY - b.y) / v.y) : (4.6f - b.y + 4.6f - targetY) / Mathf.Max(.01f, v.y);
                float raw = b.x + v.x * time;
                float width = 19.6f, phase = Mathf.Repeat(raw + 9.8f, width * 2f);
                float predicted = phase <= width ? phase - 9.8f : width * 2f - phase - 9.8f;
                float futureVx = (phase <= width ? 1 : -1) * v.x;
                int side = futureVx < 0 ? 1 : -1;
                float target = Mathf.Clamp(predicted - side * .48f, p.MinimumX, p.MaximumX);
                float dx = target - p.Position.x;
                f.Move = Mathf.Abs(dx) > .13f ? Mathf.Sign(dx) : (p.Facing != side ? side : 0);
                bool departing = Vector2.Dot(v, b - p.Position) > 0;
                // Do not run under a freshly deflected, low outgoing ball.
                if (b.y < -1.6f && departing && Mathf.Abs(b.x - p.Position.x) < 3f) f.Move = 0;
                // Briefly hold before release; never creates a parry while E is held.
                cooldown = Mathf.Max(0, cooldown - Dt);
                if (heldFrames > 0)
                {
                    heldFrames--;
                    if (heldFrames == 0) { f.Release = true; cooldown = .24f; }
                    else f.Held = true;
                }
                else if (cooldown <= 0 && !departing && v.y < 0 && (b.y + 2.15f) / -v.y < .14f && Mathf.Abs(b.x - p.Position.x) < 1.6f)
                { f.Press = f.Held = true; heldFrames = 2; }
                return f;
            }
        }
    }
}
#endif
