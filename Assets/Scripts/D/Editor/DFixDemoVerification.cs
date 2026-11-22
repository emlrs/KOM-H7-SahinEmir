#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CombinedD
{
    // A normal saved-scene run. The only actions after BeginRun are direction/Space/E inputs.
    // Begin measures and records those inputs; Replay captures the real Game View at wall-clock speed.
    public static class DFixDemoVerification
    {
        [Serializable] public class DemoRecord
        {
            public DRouteVerification.Options Options;
            public List<DRouteVerification.Frame> Frames = new List<DRouteVerification.Frame>();
            public List<string> Stages = new List<string>();
            public bool Passed, CameraPassed, LaserPassed, Recovered;
            public float Time, DropStartedAt, DropHeight, CameraDown, MaxCameraSize, MaxOrbSpeedError, LavaSpeed;
            public int DropFrom = -1, LandedOn = -1, LaserWarnings, LaserFires, GuardedLaserFrames, OutsidePlayerFrames, OutsideOrbFrames;
            public string Detail;
        }
        const float Dt = .02f, Duration = 55f;
        const string Folder = "Temp/D_Birlesik/fix-demo/";
        const string Evidence = "Belge/D_Duzeltme/";
        static DGameController game;
        static DemoRecord record;
        static bool replaying;
        static int replayIndex, videoIndex;
        static float accumulator, nextCapture;
        static double lastWall, startWall;
        static readonly List<float> captureTimes = new List<float>();
        static readonly StringBuilder rows = new StringBuilder();
        static readonly List<string> errors = new List<string>();
        static float dropFeet, dropCamera;

        [MenuItem("D/Fix/Measure normal gameplay and multi-platform fall")]
        public static void Begin()
        {
            if (replaying) throw new InvalidOperationException("Demo replay is running.");
            DRouteVerification.Bind();
            try
            {
                game = UnityEngine.Object.FindAnyObjectByType<DGameController>();
                var options = new DRouteVerification.Options();
                if (File.Exists("Temp/D_Birlesik/route-options.json"))
                    options = JsonUtility.FromJson<DRouteVerification.Options>(File.ReadAllText("Temp/D_Birlesik/route-options.json"));
                options.Threats = true;
                DRouteVerification.Prepare(options);
                Directory.CreateDirectory(Folder); Directory.CreateDirectory(Evidence);
                record = new DemoRecord { Options = options, LavaSpeed = game.Lava.RiseSpeed }; errors.Clear();
                Application.logMessageReceived += Log;
                File.WriteAllText(Folder + "status.txt", "RUNNING");
                StartRows();
                var pilot = new DRouteVerification.Pilot(options);
                var visits = new List<DRouteVerification.Visit>();
                string phase = "climb";
                for (int i = 0; i < Mathf.RoundToInt(Duration / Dt) && game.IsPlaying; i++)
                {
                    DRouteVerification.Frame frame;
                    DLaser laser = game.Attacks.CurrentLaser;
                    bool fallWindow = !laser || laser.Warning && laser.Remaining >= 1.25f;
                    if (phase == "climb" && pilot.Index >= 5 && pilot.Index % 8 == 5 &&
                        !game.Level.Platforms[pilot.Index].LongCrossing && game.Player.Grounded &&
                        fallWindow && game.Player.BodyCollider.bounds.min.y - game.LavaHeight > 8f)
                    {
                        phase = "intentional_fall"; record.DropFrom = pilot.Index; record.DropStartedAt = game.Elapsed;
                        dropFeet = game.Player.BodyCollider.bounds.min.y; dropCamera = game.CameraRig.ViewCamera.transform.position.y;
                    }
                    if (phase == "intentional_fall")
                    {
                        // A normal section's fifth platform is above the open x=0 corridor.
                        // Walking left lands three levels lower through ordinary gravity/collisions.
                        frame = pilot.DefendInput(new DRouteVerification.Frame { Move = pilot.Steer(0f, game.Player.Grounded) }, true);
                    }
                    else frame = pilot.Input(visits);
                    record.Frames.Add(frame); record.Stages.Add(phase);
                    DRouteVerification.Step(frame); Sample(phase);
                    if (phase == "intentional_fall")
                    {
                        record.DropHeight = Mathf.Max(record.DropHeight, dropFeet - game.Player.BodyCollider.bounds.min.y);
                        record.CameraDown = Mathf.Max(record.CameraDown, dropCamera - game.CameraRig.ViewCamera.transform.position.y);
                        int support = SupportIndex();
                        if (support >= 0 && support <= record.DropFrom - 3)
                        {
                            record.LandedOn = support; record.Recovered = true;
                            pilot.ResumeAt(support); phase = "recovered_climb";
                        }
                    }
                    if (phase != "intentional_fall" && pilot.Failed)
                    { record.Detail = pilot.Failure; break; }
                }
                record.Time = game.Elapsed;
                record.CameraPassed = record.Recovered && record.DropHeight >= 5f && record.CameraDown > .5f && record.OutsidePlayerFrames == 0 &&
                    record.OutsideOrbFrames == 0 && record.MaxOrbSpeedError < .01f && errors.Count == 0;
                record.LaserPassed = record.LaserFires >= 2 && record.GuardedLaserFrames > 0 && errors.Count == 0;
                record.Passed = record.CameraPassed && record.LaserPassed;
                record.Detail = (record.Detail ?? "") + " phase=" + game.Phase + "; errors=" + errors.Count;
                File.WriteAllText(Folder + "tape.json", JsonUtility.ToJson(record));
                File.WriteAllText(Evidence + "normal-gameplay-demo.csv", rows.ToString());
                WriteResult();
            }
            finally { Application.logMessageReceived -= Log; DRouteVerification.Restore(); }
        }

        static int SupportIndex()
        {
            if (!game.Player.Grounded) return -1;
            float feet = game.Player.BodyCollider.bounds.min.y;
            foreach (var platform in game.Level.Platforms)
            {
                Bounds bounds = platform.Surface.bounds;
                if (Mathf.Abs(feet - bounds.max.y) < .18f && game.Player.Position.x >= bounds.min.x - .1f &&
                    game.Player.Position.x <= bounds.max.x + .1f) return platform.Index;
            }
            return -1;
        }

        static void StartRows()
        {
            rows.Clear();
            rows.AppendLine("time,stage,player_x,player_y,feet,player_vy,camera_y,camera_size,play_bottom,play_top,lava,orb_x,orb_y,orb_speed,spawn_count,laser_warnings,laser_fires,laser_active,guard,player_visible,orb_visible");
        }
        static void Sample(string phase)
        {
            Rect visible = game.CameraRig.VisiblePlayBounds;
            Bounds body = game.Player.BodyCollider.bounds;
            Vector2 orb = game.Orb.Position; float radius = game.Orb.Radius;
            bool playerVisible = body.min.x >= visible.xMin - .02f && body.max.x <= visible.xMax + .02f &&
                body.min.y >= visible.yMin - .02f && body.max.y <= visible.yMax + .02f;
            bool orbVisible = !game.Orb.Active || orb.x - radius >= visible.xMin - .02f && orb.x + radius <= visible.xMax + .02f &&
                orb.y - radius >= visible.yMin - .02f && orb.y + radius <= visible.yMax + .02f;
            if (!playerVisible) record.OutsidePlayerFrames++;
            if (!orbVisible) record.OutsideOrbFrames++;
            float size = game.CameraRig.ViewCamera.orthographicSize;
            record.MaxCameraSize = Mathf.Max(record.MaxCameraSize, size);
            if (game.Orb.Active) record.MaxOrbSpeedError = Mathf.Max(record.MaxOrbSpeedError, Mathf.Abs(game.Orb.Velocity.magnitude - game.Orb.Speed));
            record.LaserWarnings = game.Attacks.LaserSpawnCount; record.LaserFires = game.Attacks.LaserFireCount;
            if (game.Attacks.CurrentLaser && game.Attacks.CurrentLaser.Active && game.Attacks.CurrentLaser.GuardBlocked) record.GuardedLaserFrames++;
            rows.AppendLine(F(game.Elapsed) + "," + phase + "," + F(game.Player.Position.x) + "," + F(game.Player.Position.y)
                + "," + F(body.min.y) + "," + F(game.Player.Velocity.y) + "," + F(game.CameraRig.ViewCamera.transform.position.y)
                + "," + F(size) + "," + F(visible.yMin) + "," + F(visible.yMax) + "," + F(game.LavaHeight)
                + "," + F(orb.x) + "," + F(orb.y) + "," + F(game.Orb.Velocity.magnitude) + "," + game.Attacks.SpawnCount
                + "," + record.LaserWarnings + "," + record.LaserFires + "," + game.Attacks.ActiveLaserCount
                + "," + (game.Player.GuardActive ? 1 : 0) + "," + (playerVisible ? 1 : 0) + "," + (orbVisible ? 1 : 0));
        }

        static void WriteResult()
        {
            string result = (record.Passed ? "PASS" : "INCOMPLETE") + " time=" + F(record.Time)
                + " camera=" + (record.CameraPassed ? "PASS" : "INCOMPLETE") + " regularLaser=" + (record.LaserPassed ? "PASS" : "INCOMPLETE")
                + " fall=" + record.DropFrom + "->" + record.LandedOn + " drop=" + F(record.DropHeight)
                + " cameraDown=" + F(record.CameraDown) + " lavaSpeed=" + F(record.LavaSpeed)
                + " laserWarnings=" + record.LaserWarnings + " laserFires=" + record.LaserFires
                + " guardBeamSeconds=" + F(record.GuardedLaserFrames * Dt) + " offscreenPlayer=" + record.OutsidePlayerFrames
                + " offscreenOrb=" + record.OutsideOrbFrames + " " + record.Detail;
            File.WriteAllText(Folder + "status.txt", result);
            File.WriteAllText(Evidence + "normal-gameplay-demo.txt", result + "\n"
                + "Saved D scene; all game mechanics and lava remain enabled. Lava speed=" + F(record.LavaSpeed)
                + (record.Options.LavaSpeedOverride > 0 ? " (explicit temporary parameter trial, restored at the end). " : " (saved scene value). ")
                + "One ordinary BeginRun reset; thereafter only -1/0/+1 movement and Space/E edges. "
                + "The pilot climbs from spawn, walks off a normal section's fifth platform toward x=0 when lava clearance permits, falls onto a lower existing platform and resumes climbing. "
                + "No pose writes, injected attacks, frozen orb, modified camera targets or invulnerability. The camera follows its normal runtime code. "
                + "Laser counts come from the normal attack director. Automated Play Mode test; not a human/friend test. A terminal loss is recorded honestly and is not a full-route win claim.\n"
                + "Camera and repeated-laser criteria are reported separately. A camera-only clip can be shown alongside a separately labeled normal-route clip demonstrating repeated laser fire.\n"
                + string.Join("\n", errors));
        }

        [MenuItem("D/Fix/Replay and capture normal gameplay demo")]
        public static void Replay()
        {
            if (replaying) throw new InvalidOperationException("Demo replay is running.");
            DRouteVerification.Bind();
            try
            {
                game = UnityEngine.Object.FindAnyObjectByType<DGameController>();
                record = JsonUtility.FromJson<DemoRecord>(File.ReadAllText(Folder + "tape.json"));
                DRouteVerification.Prepare(record.Options);
                replayIndex = videoIndex = 0; accumulator = nextCapture = 0; captureTimes.Clear(); errors.Clear();
                Directory.CreateDirectory(Folder + "video");
                lastWall = startWall = EditorApplication.timeSinceStartup; replaying = true;
                File.WriteAllText(Folder + "replay-status.txt", "RUNNING");
                Application.logMessageReceived += Log; EditorApplication.update += Pump;
            }
            catch { DRouteVerification.Restore(); throw; }
        }
        static void Pump()
        {
            try
            {
                if (!EditorApplication.isPlaying) throw new InvalidOperationException("Play Mode ended.");
                double now = EditorApplication.timeSinceStartup;
                accumulator += Mathf.Min(.1f, (float)(now - lastWall)); lastWall = now;
                while (accumulator >= Dt && replayIndex < record.Frames.Count && game.IsPlaying)
                { DRouteVerification.Step(record.Frames[replayIndex++]); accumulator -= Dt; }
                if (game.Elapsed >= nextCapture && game.Elapsed <= Duration + .01f)
                {
                    ScreenCapture.CaptureScreenshot(Path.GetFullPath(Folder + "video/frame" + (videoIndex++).ToString("D5") + ".png"));
                    captureTimes.Add(game.Elapsed); nextCapture = game.Elapsed + .05f;
                }
                if (replayIndex % 100 < 5)
                    File.WriteAllText(Folder + "replay-status.txt", "RUNNING time=" + F(game.Elapsed) + " frames=" + videoIndex + " fires=" + game.Attacks.LaserFireCount);
                if (replayIndex < record.Frames.Count && game.IsPlaying) return;
                bool sameResult = Mathf.Abs(game.Elapsed - record.Time) <= .04f && game.Attacks.LaserFireCount == record.LaserFires;
                string result = (sameResult && errors.Count == 0 ? "PASS" : "FAIL") + " time=" + F(game.Elapsed)
                    + " wall=" + (now - startWall).ToString("F3", CultureInfo.InvariantCulture) + " phase=" + game.Phase
                    + " laserFires=" + game.Attacks.LaserFireCount + " frames=" + videoIndex + " errors=" + errors.Count;
                var manifest = new StringBuilder();
                for (int i = 0; i < captureTimes.Count; i++)
                {
                    manifest.AppendLine("file 'frame" + i.ToString("D5") + ".png'");
                    manifest.AppendLine("duration " + F(i + 1 < captureTimes.Count ? captureTimes[i + 1] - captureTimes[i] : .05f));
                }
                File.WriteAllText(Folder + "video/frames.txt", manifest.ToString());
                File.WriteAllText(Folder + "replay-status.txt", result);
                File.WriteAllText(Evidence + "normal-gameplay-replay.txt", result + "\nReal Game View ScreenCapture frames; automated input replay at wall-clock speed.\n" + string.Join("\n", errors));
                FinishReplay();
            }
            catch (Exception error)
            { File.WriteAllText(Folder + "replay-status.txt", "FAIL " + error); FinishReplay(); }
        }
        static void FinishReplay()
        {
            EditorApplication.update -= Pump; Application.logMessageReceived -= Log;
            replaying = false; DRouteVerification.Restore();
        }
        static void Log(string message, string stack, LogType type)
        { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); }
        static string F(float value) => value.ToString("F4", CultureInfo.InvariantCulture);
    }
}
#endif
