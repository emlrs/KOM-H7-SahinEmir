#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace CombinedD
{
    // Measures actual runtime transitions. It never changes gameplay or schedules an attack.
    public static class DFixTelemetry
    {
        static readonly List<float> spawns = new List<float>();
        static readonly List<float> ordinaryGaps = new List<float>();
        static readonly HashSet<DMissile> seenMissiles = new HashSet<DMissile>();
        static readonly StringBuilder rows = new StringBuilder();
        static DGameController game;
        static DMissile missile;
        static DLaser laser;
        static string label;
        static int previousCount, fires, warnings, activeSteps;

        static float maxOrbSpeedError, previousLava, lavaSpeedError;
        static float minimumReaction, minimumMissileSpeed, maximumMissileSpeed, maximumZoom;
        static bool pauseSinceSpawn;
        static int previousFireCount, outsidePlayer, outsideOrb;
        public static void Begin(DGameController controller, string name)
        {
            game = controller; label = name; spawns.Clear(); rows.Clear();
            ordinaryGaps.Clear(); seenMissiles.Clear(); pauseSinceSpawn = false;
            minimumReaction = minimumMissileSpeed = float.PositiveInfinity;
            maximumMissileSpeed = maximumZoom = 0; previousFireCount = outsidePlayer = outsideOrb = 0;
            rows.AppendLine("time,event,details"); previousCount = fires = warnings = activeSteps = 0;
            missile = null; laser = null; maxOrbSpeedError = lavaSpeedError = 0;
            previousLava = game.LavaHeight;
        }
        public static void Sample(float dt)
        {
            if (!game) return;
            pauseSinceSpawn |= game.Attacks.PauseRemaining > 0;
            if (game.Attacks.SpawnCount != previousCount)
            {
                if (spawns.Count > 0 && !pauseSinceSpawn) ordinaryGaps.Add(game.Elapsed - spawns[spawns.Count-1]);
                spawns.Add(game.Elapsed); previousCount = game.Attacks.SpawnCount;
                rows.AppendLine(F(game.Elapsed) + ",spawn," + previousCount + ";kind=" + game.Attacks.LastSpawnKind + ";sinceWave=" + pauseSinceSpawn);
                pauseSinceSpawn = false;
            }
            foreach (var value in game.Attacks.Missiles)
            {
                if (!value || !value.Active || value.Deflected) continue;
                if (seenMissiles.Add(value))
                {
                    float clearance = game.Player.BodyCollider.bounds.extents.magnitude + .2f;
                    float reaction = value.LaunchDelayRemaining + Mathf.Max(0, Vector2.Distance(value.Position,game.Player.Position)-clearance)/(value.FlightVelocity.magnitude+game.Player.MaxSpeed);
                    minimumReaction = Mathf.Min(minimumReaction,reaction);
                    rows.AppendLine(F(game.Elapsed)+",missile_reaction,"+F(reaction));
                }
                if (value.LaunchDelayRemaining <= 0) { float speed=value.Velocity.magnitude; minimumMissileSpeed=Mathf.Min(minimumMissileSpeed,speed); maximumMissileSpeed=Mathf.Max(maximumMissileSpeed,speed); }
            }
            if (game.Attacks.CurrentMissile != missile)
            {
                missile = game.Attacks.CurrentMissile;
                if (missile) rows.AppendLine(F(game.Elapsed) + ",missile,speed=" + F(missile.Velocity.magnitude));
            }
            if (game.Attacks.CurrentLaser != laser)
            {
                if (laser) rows.AppendLine(F(game.Elapsed) + ",laser_end,activeSteps=" + activeSteps);
                laser = game.Attacks.CurrentLaser; activeSteps = 0;
                if (laser) { warnings++; rows.AppendLine(F(game.Elapsed) + ",laser_warning,remaining=" + F(laser.Remaining)); }
            }
            if (laser && laser.Active)
            {
                activeSteps++;
            }
            if (game.Attacks.LaserFireCount != previousFireCount)
            {
                previousFireCount=game.Attacks.LaserFireCount; fires=previousFireCount;
                rows.AppendLine(F(game.Elapsed)+",laser_fire,scheduled="+F(game.Attacks.LastLaserFireTime));
            }
            Camera camera=game.CameraRig.ViewCamera;
            maximumZoom=Mathf.Max(maximumZoom,camera.orthographicSize);
            Vector3 playerView=camera.WorldToViewportPoint(game.Player.Position), orbView=camera.WorldToViewportPoint(game.Orb.Position);
            if(playerView.x<0 || playerView.x>1 || playerView.y<0 || playerView.y>1) outsidePlayer++;
            if(game.Orb.Active && (orbView.x<0 || orbView.x>1 || orbView.y<0 || orbView.y>1)) outsideOrb++;
            if (game.Orb.Active) maxOrbSpeedError = Mathf.Max(maxOrbSpeedError, Mathf.Abs(game.Orb.Velocity.magnitude - game.Orb.Speed));
            if (game.IsPlaying) lavaSpeedError = Mathf.Max(lavaSpeedError, Mathf.Abs((game.LavaHeight - previousLava) / dt - game.Lava.RiseSpeed));
            previousLava = game.LavaHeight;
        }
        public static void End()
        {
            if (!game) return;
            Directory.CreateDirectory("Belge/D_Duzeltme");
            File.WriteAllText("Belge/D_Duzeltme/" + label + "-events.csv", rows.ToString());
            float sum = 0, min = float.PositiveInfinity, max = 0;
            for (int i = 1; i < spawns.Count; i++) { float gap = spawns[i] - spawns[i-1]; sum += gap; min = Mathf.Min(min,gap); max = Mathf.Max(max,gap); }
            string report = "Actual Play Mode transitions; " + DateTimeOffset.Now.ToString("O") + "\n"
                + "RunTime=" + F(game.Elapsed) + " Phase=" + game.Phase + "\n"
                + "Spawns=" + spawns.Count + " LaserWarnings=" + warnings + " ObservedLaserFires=" + fires + "\n"
                + "Spawn gaps (including wave pauses/safety deferrals): min=" + F(min) + " mean=" + F(sum/Mathf.Max(1,spawns.Count-1)) + " max=" + F(max) + "\n"
                + "Orb speed max error=" + F(maxOrbSpeedError) + " Lava speed max error=" + F(lavaSpeedError) + "\n";
            float gapSum=0,gapMin=float.PositiveInfinity,gapMax=0;
            foreach(float gap in ordinaryGaps){gapSum+=gap;gapMin=Mathf.Min(gapMin,gap);gapMax=Mathf.Max(gapMax,gap);}
            report += "Gaps without an intervening wave: count="+ordinaryGaps.Count+" min="+F(gapMin)+" mean="+F(gapSum/Mathf.Max(1,ordinaryGaps.Count))+" max="+F(gapMax)+"\n"
                + "Hostile missile moving speed min="+F(minimumMissileSpeed)+" max="+F(maximumMissileSpeed)+" conservative spawn reaction min="+F(minimumReaction)+"\n"
                + "Outside player="+outsidePlayer+" orb="+outsideOrb+" Maximum ortho="+F(maximumZoom)+"\n";
            File.WriteAllText("Belge/D_Duzeltme/" + label + "-metrics.txt", report); game = null;
        }
        static string F(float value) => value.ToString("F4", System.Globalization.CultureInfo.InvariantCulture);
    }
}
#endif
