#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CombinedD
{
    // Verification only: ordinary direction/jump/E edges. No pose changes during a route.
    public static class DRouteVerification
    {
        [Serializable] public class Options
        {
            public bool Threats = true;
            public int Seed = 3;
            public float LaunchInset = .32f, LandingInset = 1.1f;
            public float LavaSpeedOverride; // Zero uses the saved scene. Nonzero is an explicitly reported parameter trial.
        }
        [Serializable] public class Frame { public float Move; public bool JumpPress, JumpHeld, JumpRelease, EPress, EHeld, ERelease; }
        [Serializable] public class Visit { public int Index; public float Time, LavaGap, X, Y; }
        [Serializable] public class Record
        {
            public Options Options; public bool Passed; public float Time; public int OrbParries, MissileParries, GuardFrames;
            public string Detail; public List<Frame> Frames = new List<Frame>(); public List<Visit> Visits = new List<Visit>();
            public float MaxCameraLead, MaxCameraSize, MaxOrbSpeedError, LavaSpeed;
            public int OutsideViewFrames, OrbOutsideViewFrames, LaserWarnings, LaserFires, ActiveLaserFrames, PeakMissiles;
        }
        static DGameController game;
        static SimulationMode2D oldSimulation;
        static bool oldBackground, replaying;
        static float oldLavaSpeed;
        static Record tape;
        static int replayIndex, videoIndex;
        static double previousWall, wallStart;
        static float accumulator, nextVideoAt;
        static readonly List<string> errors = new List<string>();
        static readonly List<float> videoTimes = new List<float>();
        static readonly StringBuilder samples = new StringBuilder();
        const float Dt = .02f;
        const string Folder = "Temp/D_Birlesik/";

        public static void Begin()
        {
            Bind();
            var options = File.Exists(Folder + "route-options.json") ? JsonUtility.FromJson<Options>(File.ReadAllText(Folder + "route-options.json")) : new Options();
            File.WriteAllText(Folder + "route-status.txt", "RUNNING");
            var record = new Record { Options = options };
            try
            {
                Prepare(options);
                record.LavaSpeed = game.Lava.RiseSpeed;
                DFixTelemetry.Begin(game, "updated");
                samples.Clear(); samples.AppendLine("time,index,x,y,vx,vy,feet,lava,orb_x,orb_y,orb_vx,orb_vy,laser_warning,laser_active,laser_remaining,guard,move,jump,eheld,wave,facing,parry,missiles");
                var pilot = new Pilot(options);
                for (int i = 0; i < 19000 && game.IsPlaying; i++)
                {
                    Frame frame = pilot.Input(record.Visits);
                    record.Frames.Add(frame); Step(frame); DFixTelemetry.Sample(Dt);
                    SampleStep(pilot.Index, frame);
                    if (game.Player.GuardActive) record.GuardFrames++;
                    SampleRecord(record);
                    if (!options.Threats && pilot.Index >= game.Level.Platforms.Length - 1) break;
                    if (pilot.Failed) { record.Detail = pilot.Failure; break; }
                }
                record.Passed = options.Threats ? game.Phase == DGamePhase.Won : pilot.Index >= game.Level.Platforms.Length - 1;
                record.Time = game.Elapsed; record.OrbParries = game.OrbParries; record.MissileParries = game.MissileParries;
                record.Detail = (record.Detail ?? "") + " phase=" + game.Phase + "; index=" + pilot.Index + "; player=" + game.Player.Position + "; velocity=" + game.Player.Velocity + "; lava=" + game.LavaHeight + "; lavaSpeed=" + game.Lava.RiseSpeed + "; orb=" + game.Orb.Position + "; orbVelocity=" + game.Orb.Velocity + "; cameraY=" + game.CameraRig.ViewCamera.transform.position.y + "; maxCameraLead=" + record.MaxCameraLead + "; outsideViewFrames=" + record.OutsideViewFrames + "; solverLimits=" + game.Orb.SolverLimitCount;
                File.WriteAllText(Folder + "route-samples.csv", samples.ToString());
                File.WriteAllText(Folder + "route-last.json", JsonUtility.ToJson(record));
                if (record.Passed && options.Threats) File.WriteAllText(Folder + "route-tape.json", JsonUtility.ToJson(record));
                WriteReport(record); DFixTelemetry.End();
            }
            finally { Restore(); }
        }
        internal static void Bind()
        {
            if (!EditorApplication.isPlaying || replaying) throw new InvalidOperationException("Idle Play Mode required.");
            game = UnityEngine.Object.FindAnyObjectByType<DGameController>();
            if (!game) throw new InvalidOperationException("Open saved D scene.");
            Directory.CreateDirectory(Folder); Directory.CreateDirectory("Belge/D_TestKaniti");
            oldSimulation = Physics2D.simulationMode; oldBackground = Application.runInBackground; oldLavaSpeed = game.Lava.RiseSpeed;
            Application.runInBackground = true; errors.Clear(); Application.logMessageReceived += Log;
            game.Player.Died += RecordDeath;
        }
        internal static void Prepare(Options options)
        {
            Physics2D.simulationMode = SimulationMode2D.Script;
            game.ManualSimulation = game.Player.ManualSimulation = game.Orb.ManualSimulation = game.Attacks.ManualSimulation = game.CameraRig.ManualSimulation = true;
            game.Player.UseExternalInput = true;
            game.Player.UseAcceleration = game.Player.RequireStoppedGuard = game.Player.GuardLocksMovement = game.Attacks.WaveEnabled = game.Orb.DirectedParry = true;
            if (options.LavaSpeedOverride > 0f) game.Lava.RiseSpeed = options.LavaSpeedOverride;
            UnityEngine.Random.InitState(options.Seed); game.BeginRun(); game.Orb.SetRandomSeed(options.Seed);
            game.Player.Body.interpolation = RigidbodyInterpolation2D.None;
            if (!options.Threats) { game.Orb.Freeze(); game.Attacks.StopAll(); }
            Physics2D.SyncTransforms();
        }
        internal static void Step(Frame frame)
        {
            game.Tick(Dt);
            game.Player.SetInput(new DInputFrame(frame.Move, frame.JumpPress, frame.JumpHeld, frame.JumpRelease, frame.EPress, frame.EHeld, frame.ERelease));
            game.Player.Step(Dt); Physics2D.SyncTransforms();
            game.Orb.Step(Dt); game.CameraRig.FollowStep(Dt); game.Attacks.Step(Dt);
            Physics2D.SyncTransforms(); Physics2D.Simulate(Dt); Physics2D.SyncTransforms();
        }
        internal static void Restore()
        {
            Application.logMessageReceived -= Log;
            Physics2D.simulationMode = oldSimulation; Application.runInBackground = oldBackground;
            if (!game) return;
            game.Lava.RiseSpeed = oldLavaSpeed;
            game.Player.Died -= RecordDeath;
            game.ManualSimulation = game.Player.ManualSimulation = game.Orb.ManualSimulation = game.Attacks.ManualSimulation = game.CameraRig.ManualSimulation = false;
            game.Player.UseExternalInput = false; game.Player.Body.interpolation = RigidbodyInterpolation2D.Interpolate;
        }
        static void Log(string text, string stack, LogType type)
        { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(text); }
        static void RecordDeath()
        {
            // Read-only diagnostics through the public event; the runtime game needs no debug edits.
            File.WriteAllText(Folder + "route-death.txt", "time=" + F(game.Elapsed) + "\n" + new System.Diagnostics.StackTrace(true));
        }
        static void SampleStep(int index, Frame frame)
        {
            var p = game.Player; var o = game.Orb; var laser = game.Attacks.CurrentLaser;
            var missiles = new StringBuilder();
            foreach (DMissile value in game.Attacks.Missiles)
            {
                if (!value || !value.Active || value.Deflected) continue;
                if (missiles.Length > 0) missiles.Append(';');
                missiles.Append(F(value.Position.x)).Append('|').Append(F(value.Position.y)).Append('|')
                    .Append(F(value.FlightVelocity.x)).Append('|').Append(F(value.FlightVelocity.y)).Append('|').Append(F(value.LaunchDelayRemaining));
            }
            samples.AppendLine(string.Join(",", new[] { F(game.Elapsed), index.ToString(), F(p.Position.x), F(p.Position.y), F(p.Velocity.x), F(p.Velocity.y),
                F(p.BodyCollider.bounds.min.y), F(game.LavaHeight), F(o.Position.x), F(o.Position.y), F(o.Velocity.x), F(o.Velocity.y),
                laser && laser.Warning ? "1" : "0", laser && laser.Active ? "1" : "0", F(laser ? laser.Remaining : 0f),
                p.GuardActive ? "1" : "0", F(frame.Move), frame.JumpPress ? "1" : "0", frame.EHeld ? "1" : "0", F(game.Attacks.PauseRemaining),
                p.Facing.ToString(), p.ParryActive ? "1" : "0", missiles.ToString() }));
        }
        static string F(float value) => value.ToString("F4", System.Globalization.CultureInfo.InvariantCulture);
        internal static void SampleRecord(Record record)
        {
            var camera = game.CameraRig.ViewCamera;
            Rect visible = game.CameraRig.VisiblePlayBounds;
            Bounds body = game.Player.BodyCollider.bounds;
            record.MaxCameraLead = Mathf.Max(record.MaxCameraLead, game.Player.Position.y - camera.transform.position.y);
            record.MaxCameraSize = Mathf.Max(record.MaxCameraSize, camera.orthographicSize);
            if (body.min.x < visible.xMin - .02f || body.max.x > visible.xMax + .02f ||
                body.min.y < visible.yMin - .02f || body.max.y > visible.yMax + .02f) record.OutsideViewFrames++;
            if (game.Orb.Active)
            {
                Vector2 orb = game.Orb.Position; float radius = game.Orb.Radius;
                if (orb.x - radius < visible.xMin - .02f || orb.x + radius > visible.xMax + .02f ||
                    orb.y - radius < visible.yMin - .02f || orb.y + radius > visible.yMax + .02f) record.OrbOutsideViewFrames++;
                record.MaxOrbSpeedError = Mathf.Max(record.MaxOrbSpeedError, Mathf.Abs(game.Orb.Velocity.magnitude - game.Orb.Speed));
            }
            record.LaserWarnings = game.Attacks.LaserSpawnCount; record.LaserFires = game.Attacks.LaserFireCount;
            record.ActiveLaserFrames += game.Attacks.ActiveLaserCount;
            record.PeakMissiles = Mathf.Max(record.PeakMissiles, game.Attacks.ActiveMissileCount);
        }
        static void WriteReport(Record r)
        {
            string label = r.Options.Threats ? "Tam oyun" : "Yalnız parkur ulaşılabilirliği";
            var text = new StringBuilder("# D — " + label + "\n\n");
            text.AppendLine(DateTimeOffset.Now.ToString("O") + "; Unity " + Application.unityVersion + ".\n");
            text.AppendLine("Gerçek Play Mode, gerçek Physics2D.Simulate(.02), kayıtlı sahne. Yalnız −1/0/+1 yatay girdi ve Space/E kenarları; koşu sırasında pozisyon değiştirilmez. Otomatik sürücü insan oyun testi değildir.\n");
            text.AppendLine("Saldırılar ve top etkin: " + r.Options.Threats + ". Lav her iki testte de etkin; ölçülen hız=" + F(r.LavaSpeed)
                + (r.Options.LavaSpeedOverride > 0 ? " (açıkça seçilen geçici parametre denemesi; test sonunda sahne değeri geri yüklenir)." : " (kayıtlı sahne değeri).")
                + " İlk koşu sıfırlamasından sonra konumlar değiştirilmez.\n");
            text.AppendLine((r.Passed && errors.Count == 0 ? "PASS" : "INCOMPLETE") + "; " + r.Time.ToString("F2") + " sn; orb parry=" + r.OrbParries + "; missile parry=" + r.MissileParries + "; guard=" + (r.GuardFrames * Dt).ToString("F2") + " sn; errors=" + errors.Count + ".\n" + r.Detail + "\n");
            text.AppendLine("Gerçek yönetici: lazer uyarısı=" + r.LaserWarnings + "; lazer ateşi=" + r.LaserFires
                + "; etkin lazer süresi=" + (r.ActiveLaserFrames * Dt).ToString("F2") + " sn; aynı anda en çok füze=" + r.PeakMissiles
                + "; oyuncu/küre görünür alan dışı kare=" + r.OutsideViewFrames + "/" + r.OrbOutsideViewFrames
                + "; en büyük kamera yarı yüksekliği=" + r.MaxCameraSize.ToString("F2")
                + "; en büyük küre hız hatası=" + r.MaxOrbSpeedError.ToString("F6") + ".\n");
            text.AppendLine("| Platform | Süre | Lav payı | X | Ayak Y |\n|---:|---:|---:|---:|---:|");
            foreach (Visit v in r.Visits) text.AppendLine("| " + v.Index + " | " + v.Time.ToString("F2") + " | " + v.LavaGap.ToString("F2") + " | " + v.X.ToString("F2") + " | " + v.Y.ToString("F2") + " |");
            foreach (string error in errors) text.AppendLine(error);
            File.WriteAllText(r.Options.Threats ? "Belge/D_TamKosu_Test.md" : "Belge/D_Parkur_Test.md", text.ToString());
            File.WriteAllText(Folder + "route-status.txt", (r.Passed && errors.Count == 0 ? "PASS" : "INCOMPLETE") + " " + label + " time=" + r.Time.ToString("F2") + " visits=" + r.Visits.Count + " errors=" + errors.Count + "\n" + r.Detail);
        }

        public static void Replay()
        {
            Bind(); tape = JsonUtility.FromJson<Record>(File.ReadAllText(Folder + "route-tape.json")); Prepare(tape.Options);
            replayIndex = videoIndex = 0; accumulator = nextVideoAt = 0; videoTimes.Clear();
            Directory.CreateDirectory(Folder + "video"); previousWall = wallStart = EditorApplication.timeSinceStartup;
            replaying = true; File.WriteAllText(Folder + "replay-status.txt", "RUNNING"); EditorApplication.update += Pump;
        }
        static void Pump()
        {
            try
            {
                if (!EditorApplication.isPlaying) throw new InvalidOperationException("Play Mode ended.");
                double now = EditorApplication.timeSinceStartup;
                accumulator += Mathf.Min(.1f, (float)(now - previousWall)); previousWall = now;
                while (accumulator >= Dt && replayIndex < tape.Frames.Count && game.IsPlaying)
                { Step(tape.Frames[replayIndex++]); accumulator -= Dt; }
                if (game.Elapsed <= 55f && game.Elapsed >= nextVideoAt)
                {
                    videoTimes.Add(game.Elapsed);
                    ScreenCapture.CaptureScreenshot(Path.GetFullPath(Folder + "video/frame" + (videoIndex++).ToString("D5") + ".png"));
                    nextVideoAt = game.Elapsed + .05f;
                }
                if (replayIndex % 100 < 5) File.WriteAllText(Folder + "replay-status.txt", "RUNNING game=" + game.Elapsed.ToString("F2") + " height=" + game.Player.Position.y.ToString("F1") + " parries=" + game.OrbParries);
                if (replayIndex < tape.Frames.Count && game.IsPlaying) return;
                string result = (game.Phase == DGamePhase.Won && errors.Count == 0 ? "PASS" : "FAIL") + " game=" + game.Elapsed.ToString("F2") + " wall=" + (now - wallStart).ToString("F2") + " phase=" + game.Phase + " parry=" + game.OrbParries + " errors=" + errors.Count;
                File.WriteAllText(Folder + "replay-status.txt", result); File.WriteAllText("Belge/D_GercekZaman_Kosusu.txt", result + "\nOtomatik giriş dizisi gerçek zaman hızında, gerçek fizik ve görüntü kareleriyle yeniden oynatıldı. İnsan testi değildir.");
                var frames = new StringBuilder();
                for (int i = 0; i < videoTimes.Count; i++)
                {
                    frames.AppendLine("file 'frame" + i.ToString("D5") + ".png'");
                    float duration = i + 1 < videoTimes.Count ? videoTimes[i + 1] - videoTimes[i] : .05f;
                    frames.AppendLine("duration " + duration.ToString("F4", System.Globalization.CultureInfo.InvariantCulture));
                }
                File.WriteAllText(Folder + "video/frames.txt", frames.ToString());
                ScreenCapture.CaptureScreenshot(Path.GetFullPath("Belge/D_TestKaniti/tam-kosu-sonuc.png"));
                EditorApplication.update -= Pump; replaying = false; Restore();
            }
            catch (Exception e)
            { File.WriteAllText(Folder + "replay-status.txt", "FAIL " + e); EditorApplication.update -= Pump; replaying = false; Restore(); }
        }

        internal sealed class Pilot
        {
            readonly Options options;
            bool launched, settling, oldJumpHeld, oldEHeld;
            float airAge, edgeAge, tapCooldown;
            int tapStage, tapFacing;
            DLaser preparingLaser;
            public int Index { get; private set; }
            public bool Failed { get; private set; }
            public string Failure { get; private set; }
            public Pilot(Options options) { this.options = options; }
            public Frame Input(List<Visit> visits)
            {
                DPlayerMotor p = game.Player;
                var f = new Frame();
                DPlatform[] route = game.Level.Platforms;
                Vector2 pos = p.Position, velocity = p.Velocity;
                float feet = p.BodyCollider.bounds.min.y;
                edgeAge += Dt;
                if (Index >= route.Length - 1)
                {
                    f.Move = Steer(game.DoorOpen ? game.Level.Exit.Zone.bounds.center.x : game.Level.Receiver.Zone.bounds.center.x - 3f, true);
                    if (!game.DoorOpen && game.Orb.Active && game.Orb.Position.y < pos.y + 3f)
                    { float target = game.Orb.Position.x - Mathf.Sign(game.Orb.Position.x - pos.x) * 1.1f; f.Move = Steer(target, true); }
                }
                else
                {
                    Bounds from = route[Index].Surface.bounds, to = route[Index + 1].Surface.bounds;
                    float direction = Mathf.Sign(to.center.x - from.center.x);
                    bool continues = ContinuesInSameDirection(Index);
                    float landing = LandingTarget(Index);
                    bool onTarget = p.Grounded && Mathf.Abs(feet - to.max.y) < .18f && pos.x >= to.min.x - .1f && pos.x <= to.max.x + .1f;
                    if (launched && onTarget) settling = true;
                    if (settling)
                    {
                        if (onTarget && (Mathf.Abs(velocity.x) < .4f || continues && velocity.x * direction > 0f))
                        {
                            Index++; launched = settling = false; airAge = edgeAge = 0;
                            visits.Add(new Visit { Index = Index, Time = game.Elapsed, LavaGap = feet - game.LavaHeight, X = pos.x, Y = feet });
                            if (continues) f.Move = direction;
                        }
                        else f.Move = Steer(landing, true);
                    }
                    else if (!launched)
                    {
                        float launch = direction > 0
                            ? Mathf.Min(from.max.x - options.LaunchInset, to.center.x - 3.2f)
                            : Mathf.Max(from.min.x + options.LaunchInset, to.center.x + 3.2f);
                        f.Move = direction;
                        if (direction * (pos.x - launch) >= -.02f && p.Grounded)
                        { launched = true; airAge = 0; f.JumpPress = f.JumpHeld = true; }
                    }
                    else
                    {
                        airAge += Dt; f.JumpHeld = airAge < .45f; f.Move = Steer(landing, false);
                    }
                    if (feet < Mathf.Min(from.max.y, to.max.y) - 2.5f || edgeAge > 28f)
                    { Failed = true; Failure = "fell/stuck on edge " + Index + "->" + (Index + 1); }
                }
                bool plannedJump = f.JumpPress;
                if (options.Threats)
                {
                    // The real shield deliberately rejects an orb already moving away in world
                    // space. Do not jump up into its underside and assume a parry can save that.
                    if (p.Grounded && (!launched || f.JumpPress) && !settling && UnsafeUpcomingJump())
                    {
                        f.JumpPress = f.JumpHeld = false;
                        f.Move = CanStopOnSupport() ? 0 : -Mathf.Sign(p.Velocity.x);
                    }
                    Defense(f);
                }
                if (plannedJump && !f.JumpPress) { launched = false; airAge = 0; }
                if (!plannedJump && f.JumpPress) { launched = true; airAge = 0; }
                return FinishInput(f);
            }
            internal void ResumeAt(int index)
            { Index = index; launched = settling = Failed = false; Failure = null; airAge = edgeAge = 0; }
            internal Frame DefendInput(Frame frame, bool traverseWarning = false)
            { if (options.Threats) Defense(frame, traverseWarning); return FinishInput(frame); }
            Frame FinishInput(Frame frame)
            {
                frame.JumpRelease = oldJumpHeld && !frame.JumpHeld;
                frame.EPress = frame.EHeld && !oldEHeld; frame.ERelease = oldEHeld && !frame.EHeld;
                oldJumpHeld = frame.JumpHeld; oldEHeld = frame.EHeld;
                return frame;
            }
            internal float Steer(float target, bool ground)
            {
                var p = game.Player; float error = target - p.Position.x, speed = p.Velocity.x;
                float rate = ground ? p.ReverseBrake : p.AirAcceleration;
                float stopping = speed * speed / (2 * Mathf.Max(.1f, rate));
                if (Mathf.Abs(speed) > .25f && Mathf.Sign(speed) == Mathf.Sign(error) && Mathf.Abs(error) <= stopping + .10f) return -Mathf.Sign(speed);
                if (Mathf.Abs(error) < .12f) return Mathf.Abs(speed) > .3f ? -Mathf.Sign(speed) : 0;
                return Mathf.Sign(error);
            }
            bool ContinuesInSameDirection(int index)
            {
                var route = game.Level.Platforms;
                if (index + 2 >= route.Length) return false;
                float first = route[index + 1].Surface.bounds.center.x - route[index].Surface.bounds.center.x;
                float second = route[index + 2].Surface.bounds.center.x - route[index + 1].Surface.bounds.center.x;
                return first * second > .1f;
            }
            float LandingTarget(int index)
            {
                Bounds from = game.Level.Platforms[index].Surface.bounds, to = game.Level.Platforms[index + 1].Surface.bounds;
                if (!ContinuesInSameDirection(index)) return to.center.x;
                return to.center.x + Mathf.Sign(to.center.x - from.center.x) * Mathf.Max(0f, to.extents.x - .55f);
            }
            bool UnsafeUpcomingJump()
            {
                var p = game.Player; var orb = game.Orb;
                if (Index >= game.Level.Platforms.Length - 1) return false;
                bool watchOrb = orb.Active && Vector2.Distance(orb.Position, p.Position) <= 12f;
                if (!watchOrb && game.Attacks.ActiveMissileCount == 0) return false;
                Bounds from = game.Level.Platforms[Index].Surface.bounds, to = game.Level.Platforms[Index + 1].Surface.bounds;
                float direction = Mathf.Sign(to.center.x - from.center.x);
                float launch = direction > 0 ? Mathf.Min(from.max.x - options.LaunchInset, to.center.x - 3.2f)
                    : Mathf.Max(from.min.x + options.LaunchInset, to.center.x + 3.2f);
                float target = LandingTarget(Index), age = 0f;
                Vector2 position = p.Position, velocity = p.Velocity;
                bool jumped = false, cut = false, landed = false;
                float landingAge = 0f;
                // Predict only this next ordinary jump. No scene object or gameplay clock is moved.
                for (float time = Dt; time <= 1.6f; time += Dt)
                {
                    float move = direction;
                    if (!jumped && direction * (position.x - launch) >= -.02f)
                    { jumped = true; velocity.y = p.JumpSpeed; }
                    else if (jumped && !landed)
                    {
                        age += Dt;
                        float error = target - position.x;
                        float stopping = velocity.x * velocity.x / (2f * Mathf.Max(.1f, p.AirAcceleration));
                        move = Mathf.Abs(velocity.x) > .25f && Mathf.Sign(velocity.x) == Mathf.Sign(error) && Mathf.Abs(error) <= stopping + .10f
                            ? -Mathf.Sign(velocity.x) : Mathf.Abs(error) < .12f
                                ? Mathf.Abs(velocity.x) > .3f ? -Mathf.Sign(velocity.x) : 0 : Mathf.Sign(error);
                    }
                    float rate = jumped && !landed ? Mathf.Abs(move) < .001f ? p.AirBrake : p.AirAcceleration
                        : velocity.x * move < 0f ? p.ReverseBrake : p.GroundAcceleration;
                    velocity.x = Mathf.MoveTowards(velocity.x, move * p.MaxSpeed, rate * Dt);
                    if (jumped && !landed)
                    {
                        if (!cut && age >= .45f) { if (velocity.y > 0) velocity.y *= p.JumpCut; cut = true; }
                        velocity.y = Mathf.Max(-p.MaxFallSpeed, velocity.y - (velocity.y < 0 ? p.FallGravity : p.Gravity) * Dt);
                    }
                    else velocity.y = 0;
                    position += velocity * Dt;
                    if (!jumped) continue;
                    if (!landed && velocity.y <= 0 && position.y - p.BodyCollider.bounds.extents.y <= to.max.y &&
                        position.x >= to.min.x && position.x <= to.max.x)
                    { landed = true; position.y = to.max.y + p.BodyCollider.bounds.extents.y; velocity.y = 0; }
                    if (watchOrb && UncoveredContact(orb.Position + orb.Velocity * time, orb.Velocity, orb.Radius, position)) return true;
                    foreach (DMissile missile in game.Attacks.Missiles)
                    {
                        if (!missile || !missile.Active || missile.Deflected) continue;
                        float flightTime = Mathf.Max(0f, time - missile.LaunchDelayRemaining);
                        if (UncoveredContact(missile.Position + missile.FlightVelocity * flightTime,
                            missile.FlightVelocity, .16f, position)) return true;
                    }
                    // Include the short landing interval: a horizontal shot at the platform's
                    // surface can hit the open underside before another grounded jump is available.
                    if (landed) { landingAge += Dt; if (landingAge >= .20f) break; }
                }
                return false;
            }
            bool UncoveredContact(Vector2 projectile, Vector2 velocity, float radius, Vector2 predictedPlayer)
            {
                var p = game.Player;
                Vector2 probe = projectile - (predictedPlayer - p.Position);
                if (Vector2.Distance(probe, p.BodyCollider.ClosestPoint(probe)) > radius + .12f) return false;
                Vector2 delta = probe - p.Position;
                return Vector2.Dot(velocity, delta) > .001f || delta.y < -.62f;
            }
            void Defense(Frame f, bool traverseWarning = false)
            {
                var p = game.Player; var orb = game.Orb;
                DLaser laser = game.Attacks.CurrentLaser;
                tapCooldown = Mathf.Max(0, tapCooldown - Dt);
                if (laser && laser.Active)
                {
                    // A parry release cannot block the sustained beam. Keep the real grounded
                    // guard held until this episode ends; the director separates contact windows.
                    tapStage = 0; HoldForLaser(f, laser); return;
                }
                if (!traverseWarning && p.Grounded && (!laser || laser.Warning && laser.Remaining >= 1.15f) &&
                    CanJumpToNextPlatform() && LowContactSoon() && !UnsafeUpcomingJump())
                {
                    // The shield has an intentionally open underside. An incoming foot-height
                    // shot is avoided with an early ordinary jump when the next landing is reachable.
                    f.Move = Mathf.Sign(game.Level.Platforms[Index + 1].Surface.bounds.center.x - p.Position.x);
                    f.JumpPress = f.JumpHeld = true; f.EHeld = false; tapStage = 0; return;
                }
                bool imminent = false; float side = 0;
                if (orb.Active)
                    imminent = Incoming(orb.Position, orb.Velocity, orb.Radius, f, out side);
                if (!imminent)
                    foreach (DMissile missile in game.Attacks.Missiles)
                        if (missile && missile.Active && !missile.Deflected && missile.LaunchDelayRemaining <= Dt &&
                            Incoming(missile.Position, missile.FlightVelocity, .16f, f, out side))
                        { imminent = true; break; }
                if (imminent)
                {
                    // Preserve useful momentum if the route already faces the incoming contact.
                    if (p.Facing != (int)side) f.Move = side;
                    else if (f.Move * side < 0) f.Move = 0;
                    if (tapStage == 0 && tapCooldown <= 0)
                    {
                        if (oldEHeld) { f.EHeld = false; return; }
                        tapStage = 1; tapFacing = (int)side;
                    }
                }
                if (tapStage == 1)
                {
                    if (p.Facing != tapFacing) f.Move = tapFacing;
                    else if (f.Move * tapFacing < 0) f.Move = 0;
                    f.EHeld = !oldEHeld; // One real press frame, then release; the window itself lasts .20s.
                    if (!f.EHeld) { tapStage = 0; tapCooldown = .12f; }
                    return;
                }
                if (laser && !laser.Finished && !imminent && !(traverseWarning && laser.Warning)) HoldForLaser(f, laser);
            }
            bool CanJumpToNextPlatform()
            {
                if (Index >= game.Level.Platforms.Length - 1) return false;
                var p = game.Player;
                Bounds target = game.Level.Platforms[Index + 1].Surface.bounds;
                float direction = Mathf.Sign(target.center.x - p.Position.x);
                return p.Velocity.x * direction >= 2f &&
                    Mathf.Abs(target.center.x - p.Position.x) <= p.MaxSpeed * .70f &&
                    target.max.y - p.BodyCollider.bounds.min.y <= 2.3f;
            }
            bool LowContactSoon()
            {
                var orb = game.Orb;
                if (orb.Active && LowContactSoon(orb.Position, orb.Velocity, orb.Radius)) return true;
                foreach (DMissile missile in game.Attacks.Missiles)
                    if (missile && missile.Active && !missile.Deflected && missile.LaunchDelayRemaining <= Dt &&
                        LowContactSoon(missile.Position, missile.FlightVelocity, .16f)) return true;
                return false;
            }
            bool LowContactSoon(Vector2 position, Vector2 velocity, float radius)
            {
                var p = game.Player;
                for (float time = Dt; time <= .28f; time += Dt)
                {
                    Vector2 probe = position + (velocity - p.Velocity) * time;
                    if (probe.y - p.Position.y >= -.62f) continue;
                    if (Vector2.Distance(probe, p.BodyCollider.ClosestPoint(probe)) <= radius + .10f) return true;
                }
                return false;
            }
            void HoldForLaser(Frame f, DLaser laser)
            {
                var p = game.Player;
                bool mayTraverse = laser.Warning && preparingLaser != laser && !p.GuardActive;
                if (f.JumpPress && !(mayTraverse && laser.Remaining >= 1.15f))
                    f.JumpPress = f.JumpHeld = false;
                if (mayTraverse)
                {
                    // Use the telegraph for ordinary traversal, while reserving real braking time.
                    // A new course jump needs its flight/landing time before the sustained beam.
                    if (f.JumpPress && laser.Remaining >= 1.15f) { f.EHeld = false; return; }
                    if (f.JumpPress) f.JumpPress = f.JumpHeld = false;
                    float preparation = Mathf.Abs(p.Velocity.x) / Mathf.Max(1f, p.GroundBrake) + .14f;
                    preparation = Mathf.Max(.30f, preparation);
                    if (laser.Remaining > preparation && !p.Grounded) { f.EHeld = false; return; }
                    if (laser.Remaining > preparation && p.Grounded && !f.JumpPress &&
                        (SafeWarningAdvance(f.Move) || CanReachJumpBefore(laser.Remaining)))
                    { f.EHeld = false; return; }
                }
                preparingLaser = laser;
                // Brake by normal zero movement; the motor itself decides when stopped guard is legal.
                if (p.Grounded && (p.GuardActive || CanStopOnSupport()))
                {
                    f.Move = 0; f.JumpPress = f.JumpHeld = false;
                    int facing = laser.Source.x > p.Position.x ? 1 : -1;
                    if (p.Facing != facing && (p.GuardActive || Mathf.Abs(p.Velocity.x) <= p.GuardStopSpeed)) f.Move = facing;
                }
                f.EHeld = true;
            }
            bool SafeWarningAdvance(float move)
            {
                var p = game.Player;
                float feet = p.BodyCollider.bounds.min.y;
                float rate = move * p.Velocity.x < 0f ? p.ReverseBrake : p.GroundAcceleration;
                float nextSpeed = Mathf.MoveTowards(p.Velocity.x, move * p.MaxSpeed, rate * .08f);
                float stop = p.Position.x + nextSpeed * .08f + nextSpeed * Mathf.Abs(nextSpeed) / (2f * Mathf.Max(1f, p.GroundBrake));
                foreach (var platform in game.Level.Platforms)
                {
                    Bounds bounds = platform.Surface.bounds;
                    if (Mathf.Abs(feet - bounds.max.y) > .18f || p.Position.x < bounds.min.x - .1f || p.Position.x > bounds.max.x + .1f) continue;
                    return stop > bounds.min.x + .32f && stop < bounds.max.x - .32f;
                }
                return false;
            }
            bool CanReachJumpBefore(float warning)
            {
                if (launched || settling || Index >= game.Level.Platforms.Length - 1) return false;
                var p = game.Player;
                Bounds from = game.Level.Platforms[Index].Surface.bounds, to = game.Level.Platforms[Index + 1].Surface.bounds;
                float direction = Mathf.Sign(to.center.x - from.center.x);
                float launch = direction > 0 ? Mathf.Min(from.max.x - options.LaunchInset, to.center.x - 3.2f)
                    : Mathf.Max(from.min.x + options.LaunchInset, to.center.x + 3.2f);
                float distance = Mathf.Max(0, direction * (launch - p.Position.x));
                float speed = direction * p.Velocity.x, acceleration = Mathf.Max(1f, p.GroundAcceleration);
                float accelerationTime = Mathf.Max(0, (p.MaxSpeed - speed) / acceleration);
                float accelerationDistance = speed * accelerationTime + .5f * acceleration * accelerationTime * accelerationTime;
                float approach = distance <= accelerationDistance
                    ? (-speed + Mathf.Sqrt(speed * speed + 2f * acceleration * distance)) / acceleration
                    : accelerationTime + (distance - accelerationDistance) / Mathf.Max(.1f, p.MaxSpeed);
                return warning >= approach + 1.19f;
            }
            bool CanStopOnSupport()
            {
                var p = game.Player;
                if (!p.Grounded) return false;
                float feet = p.BodyCollider.bounds.min.y;
                float stoppedX = p.Position.x + p.Velocity.x * Mathf.Abs(p.Velocity.x) / (2 * Mathf.Max(.1f, p.GroundBrake));
                foreach (var platform in game.Level.Platforms)
                {
                    Bounds b = platform.Surface.bounds;
                    if (Mathf.Abs(feet - b.max.y) > .18f || p.Position.x < b.min.x - .1f || p.Position.x > b.max.x + .1f) continue;
                    return stoppedX > b.min.x + .32f && stoppedX < b.max.x - .32f;
                }
                return false;
            }
            bool Incoming(Vector2 position, Vector2 velocity, float radius, Frame planned, out float side)
            {
                var p = game.Player;
                side = position.x >= p.Position.x ? 1 : -1;
                if (Vector2.Distance(position, p.Position) > 4f) return false;
                Vector2 playerVelocity = p.Velocity;
                if (planned.JumpPress) playerVelocity.y = p.JumpSpeed;
                float gravity = !p.Grounded || planned.JumpPress ? (playerVelocity.y < 0 ? p.FallGravity : p.Gravity) : 0;
                // Predict overlap with the actual capsule, translated by expected motion.
                for (float t = 0; t <= .16f; t += Dt)
                {
                    Vector2 shift = playerVelocity * t + Vector2.down * (.5f * gravity * t * t);
                    Vector2 probe = position + velocity * t - shift;
                    if (Vector2.Distance(probe, p.BodyCollider.ClosestPoint(probe)) > radius + .10f) continue;
                    Vector2 delta = probe - p.Position;
                    if (Mathf.Abs(delta.x) > .04f) side = Mathf.Sign(delta.x);
                    return true;
                }
                return false;
            }
        }
    }
}
#endif
