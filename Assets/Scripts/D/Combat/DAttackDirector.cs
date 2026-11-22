using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CombinedD
{
    // One spawn clock, up to three missiles, and at most one warning/beam.
    public sealed class DAttackDirector : MonoBehaviour
    {
        public DGameController Game;
        public DPlayerMotor Player;
        public DOrbMotor Orb;
        public Camera ViewCamera;
        public Transform ProjectileRoot;
        public DAttackPoint[] Points;
        public DMissile MissilePrefab;
        public DLaser LaserPrefab;
        public bool ManualSimulation, WaveEnabled = true;
        public float InitialMissileSpeed = 9f, MaximumMissileSpeed = 14f;
        public float InitialInterval = 2f, MinimumInterval = 1f, DifficultyDuration = 120f;
        public float MinimumLaserWarning = 1.3f, MaximumLaserWarning = 1.7f;
        public float MinimumLaserDuration = 1f, MaximumLaserDuration = 1.8f;
        public float MinimumLaserStartGap = 8.5f;
        public float WaveDuration = 5f, MinimumMissileReaction = .75f;
        [Range(1, 3)] public int MaximumMissiles = 3;

        public float PauseRemaining { get; private set; }
        public int SpawnCount { get; private set; }
        public int MissileSpawnCount { get; private set; }
        public int LaserSpawnCount { get; private set; }
        public int LaserFireCount { get; private set; }
        public int CancelledCount { get; private set; }
        public int SuppressedAttemptCount { get; private set; }
        public float LastSpawnTime { get; private set; } = -1f;
        public float LastLaserFireTime { get; private set; } = -1f;
        public float LastLaserSpawnTime { get; private set; } = -1f;
        public string LastSpawnKind { get; private set; } = "None";
        public string LastCancelReason { get; private set; } = "None";
        public string LastBlockedReason { get; private set; } = "None";
        public string LastLaserBlockReason { get; private set; } = "None";
        public IReadOnlyList<DMissile> Missiles => missiles;
        public DLaser CurrentLaser => laser;
        public int ActiveMissileCount
        {
            get { int count = 0; foreach (DMissile value in missiles) if (value && value.Active) count++; return count; }
        }
        public int ActiveLaserCount => laser && laser.Active ? 1 : 0;
        public int PendingWarningCount => laser && laser.Warning ? 1 : 0;
        public float CurrentMissileSpeed => Mathf.Lerp(Mathf.Max(.1f, InitialMissileSpeed),
            Mathf.Max(InitialMissileSpeed, MaximumMissileSpeed), Difficulty);
        public float CurrentInterval => Mathf.Lerp(Mathf.Max(.1f, MinimumInterval, InitialInterval),
            Mathf.Max(.1f, MinimumInterval), Difficulty);
        public DMissile CurrentMissile
        {
            get
            {
                DMissile nearest = null;
                float soonest = float.PositiveInfinity;
                foreach (DMissile value in missiles)
                {
                    if (!value || !value.Active || value.Deflected) continue;
                    float time = value.LaunchDelayRemaining + (Player ? Vector2.Distance(value.Position, Player.Position) / Mathf.Max(.1f, value.FlightVelocity.magnitude) : 0f);
                    if (nearest && time >= soonest) continue;
                    nearest = value; soonest = time;
                }
                return nearest;
            }
        }

        private readonly List<DMissile> missiles = new List<DMissile>(3);
        private readonly RaycastHit2D[] predictionHits = new RaycastHit2D[32];
        private DLaser laser;
        private float elapsed, nextSpawnIn;
        private bool stopped = true, preferLaser, laserFireRecorded;
        private float Difficulty => Mathf.Clamp01(elapsed / Mathf.Max(1f, DifficultyDuration));
        private void FixedUpdate() { if (!ManualSimulation) Step(Time.fixedDeltaTime); }

        public void ResetAttacks()
        {
            ClearAttacks("Reset");
            elapsed = 0f; preferLaser = false; PauseRemaining = 0f; WaveDuration = 5f;
            SpawnCount = MissileSpawnCount = LaserSpawnCount = LaserFireCount = CancelledCount = SuppressedAttemptCount = 0;
            LastSpawnTime = LastLaserFireTime = LastLaserSpawnTime = -1f;
            LastSpawnKind = LastCancelReason = LastBlockedReason = LastLaserBlockReason = "None";
            nextSpawnIn = CurrentInterval; stopped = false;
        }

        public void Step(float dt)
        {
            if (dt <= 0f || stopped || (Game && !Game.IsPlaying)) return;
            elapsed += dt;
            if (PauseRemaining > 0f)
            {
                PauseRemaining = Mathf.Max(0f, PauseRemaining - dt);
                if (PauseRemaining <= .00001f) PauseRemaining = 0f;
                if (PauseRemaining == 0f) { nextSpawnIn = 0f; TrySpawn(); }
                return; // One eligible spawn, never catch up the paused clock.
            }
            Physics2D.SyncTransforms();
            StepAttacks(dt);
            if (stopped || PauseRemaining > 0f) return;
            nextSpawnIn -= dt;
            if (nextSpawnIn <= 0f) TrySpawn();
        }

        private void StepAttacks(float dt)
        {
            for (int i = missiles.Count - 1; i >= 0; i--)
            {
                DMissile current = missiles[i];
                if (current && current.Active && ConflictingParries(current))
                { current.Cancel(); CancelledCount++; LastCancelReason = "OppositeOrbMissile"; }
                else if (current) current.Step(dt);
                if (stopped || PauseRemaining > 0f) return;
                if (!current || !current.Active) missiles.RemoveAt(i);
            }
            if (!laser) return;
            laser.Step(dt); // No orb/range/visibility interruption after permission to start.
            if (laser && laser.Finished) laser = null;
        }

        public void NotifyLaserFired(DLaser value)
        {
            if (value != laser || laserFireRecorded) return;
            laserFireRecorded = true; LaserFireCount++; LastLaserFireTime = elapsed;
        }

        public void TriggerEnergyWave()
        {
            if (!WaveEnabled) return;
            ClearAttacks("Wave"); WaveDuration = 5f; PauseRemaining = 5f; nextSpawnIn = 0f;
        }

        public void StopAll()
        {
            stopped = true; ClearAttacks("StopAll"); PauseRemaining = 0f; nextSpawnIn = 0f;
        }

        private void ClearAttacks(string reason)
        {
            foreach (DMissile value in missiles)
                if (value) { if (value.Active) CancelledCount++; value.Cancel(); }
            missiles.Clear();
            if (laser) { if (!laser.Finished) CancelledCount++; laser.Cancel(); laser = null; }
            LastCancelReason = reason;
        }

        private void TrySpawn()
        {
            nextSpawnIn = .1f; // Retry a blocked present opportunity, with no queue or backlog.
            if (!Player || !Player.Alive) return;
            bool spawned = preferLaser ? TryLaser() : TryMissile();
            string firstReason = LastBlockedReason;
            if (!spawned)
            {
                spawned = preferLaser ? TryMissile() : TryLaser();
                if (!spawned && firstReason != LastBlockedReason) LastBlockedReason = firstReason + "+" + LastBlockedReason;
            }
            if (spawned) { SpawnCount++; LastSpawnTime = elapsed; nextSpawnIn = CurrentInterval; LastBlockedReason = "None"; }
            else SuppressedAttemptCount++;
        }

        private bool TryLaser()
        {
            if (laser || !LaserPrefab) { LastLaserBlockReason = LastBlockedReason = "LaserBusy"; return false; }
            if (LastLaserSpawnTime >= 0f && elapsed - LastLaserSpawnTime < Mathf.Max(0f, MinimumLaserStartGap))
            { LastLaserBlockReason = LastBlockedReason = "LaserStartGap"; return false; }
            if (!TryGuardPosition(out Vector2 guard))
            { LastLaserBlockReason = LastBlockedReason = "NoSupportedGuardStop"; return false; }
            float minimumWarning = Mathf.Max(.1f, MinimumLaserWarning);
            float maximumWarning = Mathf.Max(minimumWarning, MaximumLaserWarning);
            float warning = Player.Grounded ? Random.Range(minimumWarning, maximumWarning) : maximumWarning;
            float duration = Random.Range(Mathf.Max(.1f, MinimumLaserDuration), Mathf.Max(MinimumLaserDuration, MaximumLaserDuration));
            if (LaserWindowBlocked(guard, warning, duration))
            {
                duration = Mathf.Max(.1f, MinimumLaserDuration);
                if (LaserWindowBlocked(guard, warning, duration)) { LastLaserBlockReason = LastBlockedReason; return false; }
            }
            DAttackPoint point = ChoosePoint(false, 0f, guard);
            if (!point) { LastLaserBlockReason = LastBlockedReason = "NoVisibleLaserSource"; return false; }
            Vector2 source = point.transform.position;
            laser = Instantiate(LaserPrefab, ProjectileRoot ? ProjectileRoot : transform); laser.gameObject.SetActive(true);
            laserFireRecorded = false;
            laser.Initialize(this, source, Player.Position, warning, duration);
            LaserSpawnCount++; LastLaserSpawnTime = elapsed;
            LastSpawnKind = "Laser"; LastLaserBlockReason = "None"; preferLaser = false;
            return true;
        }

        private bool TryMissile()
        {
            if (!MissilePrefab || missiles.Count >= Mathf.Clamp(MaximumMissiles, 1, 3))
            { LastBlockedReason = "MissileLimit"; return false; }
            float earliest = MinimumMissileReaction;
            if (laser) earliest = Mathf.Max(earliest, laser.Remaining + (laser.Warning ? laser.ActiveDuration : 0f) + .25f);
            DAttackPoint point = ChoosePoint(true, earliest);
            if (!point) { LastBlockedReason = laser ? "MissileArrivalDuringLaser" : "NoVisibleReactionSafeSource"; return false; }
            var value = Instantiate(MissilePrefab, ProjectileRoot ? ProjectileRoot : transform); value.gameObject.SetActive(true);
            value.Initialize(this, point.transform.position, Player.Position, CurrentMissileSpeed);
            missiles.Add(value); MissileSpawnCount++; LastSpawnKind = "Missile"; preferLaser = true;
            return true;
        }

        private DAttackPoint ChoosePoint(bool missile, float earliest, Vector2 guard = default)
        {
            if (Points == null || !ViewCamera) return null;
            DAttackPoint best = null; float bestScore = float.PositiveInfinity;
            int preferredSide = SpawnCount % 4 == 0 || SpawnCount % 4 == 3 ? -1 : 1;
            foreach (DAttackPoint point in Points)
            {
                if (!point || !point.gameObject.activeInHierarchy) continue;
                Vector2 delta = (Vector2)point.transform.position - Player.Position;
                if (Mathf.Sign(delta.x) != point.Side || Mathf.Abs(delta.x) < 3f ||
                    Mathf.Abs(delta.y) < .35f || Mathf.Abs(delta.y) > .65f * Mathf.Abs(delta.x)) continue;
                Vector3 view = ViewCamera.WorldToViewportPoint(point.transform.position);
                if (view.z <= 0f || view.x < .04f || view.x > .96f || view.y < .06f || view.y > .88f) continue;
                Vector2 guardDelta = (Vector2)point.transform.position - guard;
                if (!missile && (Mathf.Sign(guardDelta.x) != point.Side || Mathf.Abs(guardDelta.x) < 3f ||
                    Mathf.Abs(guardDelta.y) > .65f * Mathf.Abs(guardDelta.x))) continue;
                if (missile && MissilePrefab.TelegraphSeconds + Mathf.Max(0f, delta.magnitude - PlayerClearance()) /
                    (CurrentMissileSpeed + Mathf.Max(0f, Player.MaxSpeed)) < earliest) continue;
                if (missile && !MissilePairSafe(point.transform.position)) continue;
                float score = Mathf.Abs(Mathf.Abs(delta.y) - 2f) + Mathf.Abs(delta.x) * .015f + (point.Side == preferredSide ? 0f : 1f);
                if (score < bestScore) { best = point; bestScore = score; }
            }
            return best;
        }

        private float PlayerClearance() => (Player && Player.BodyCollider ? Player.BodyCollider.bounds.extents.magnitude : .9f) + .2f;

        private bool TryGuardPosition(out Vector2 point)
        {
            point = Player.Position;
            Vector2 half = Player.BodyCollider ? (Vector2)Player.BodyCollider.bounds.extents : new Vector2(.35f, .8f);
            float vx = Player.Velocity.x, brake = Mathf.Max(1f, Player.GroundBrake);
            point.x += vx * Mathf.Abs(vx) / (2f * brake);
            float top = point.y - half.y;
            if (!Player.Grounded)
            {
                float vy = Player.Velocity.y, gravity = Mathf.Max(1f, Player.Gravity);
                float apex = top + Mathf.Pow(Mathf.Max(0f, vy), 2f) / (2f * gravity);
                for (int pass = 0; pass < 2; pass++)
                {
                    RaycastHit2D landing = GroundBelow(new Vector2(point.x, apex + .02f), 12f);
                    if (!landing.collider) return false;
                    top = landing.point.y;
                    float fall = Mathf.Max(1f, Player.FallGravity);
                    float time = vy > 0f ? vy / gravity + Mathf.Sqrt(2f * Mathf.Max(0f, apex - top) / fall)
                        : (vy + Mathf.Sqrt(vy * vy + 2f * fall * Mathf.Max(0f, Player.Position.y - half.y - top))) / fall;
                    float airBrake = Mathf.Max(.01f, Player.AirBrake);
                    float slowing = Mathf.Min(time, Mathf.Abs(vx) / airBrake);
                    float landingVx = Mathf.MoveTowards(vx, 0f, airBrake * time);
                    point.x = Player.Position.x + vx * slowing - Mathf.Sign(vx) * airBrake * slowing * slowing * .5f
                        + landingVx * Mathf.Abs(landingVx) / (2f * brake);
                }
            }
            point.y = top + half.y;
            // The whole stopped capsule footprint must rest on real support at this height.
            for (int side = -1; side <= 1; side++)
            {
                RaycastHit2D support = GroundBelow(new Vector2(point.x + side * (half.x + .03f), top + .08f), .18f);
                if (!support.collider || support.normal.y < .65f || Mathf.Abs(support.point.y - top) > .06f) return false;
            }
            return true;
        }

        private RaycastHit2D GroundBelow(Vector2 origin, float distance)
        {
            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(Player.GroundMask);
            return gameObject.scene.GetPhysicsScene2D().Raycast(origin, Vector2.down, distance, filter);
        }

        private bool MissilePairSafe(Vector2 source)
        {
            float arrival = MissilePrefab.TelegraphSeconds + PathContactTime(source,
                (Player.Position - source).normalized * CurrentMissileSpeed, Player.Position, .16f);
            foreach (DMissile other in missiles)
            {
                if (!other || !other.Active || other.Deflected ||
                    (source.x - Player.Position.x) * (other.Position.x - Player.Position.x) >= 0f) continue;
                float otherArrival = other.LaunchDelayRemaining + PathContactTime(other.Position,
                    other.FlightVelocity, Player.Position, .16f);
                if (Mathf.Abs(arrival - otherArrival) < .35f) return false;
            }
            return true;
        }

        private bool LaserWindowBlocked(Vector2 guard, float warning, float duration)
        {
            foreach (DMissile value in missiles)
            {
                if (!value || !value.Active || value.Deflected) continue;
                float contact = PathContactTime(value.Position, value.FlightVelocity, guard, .16f);
                contact += value.LaunchDelayRemaining;
                if (contact >= Mathf.Max(0f, warning - .30f) && contact <= warning + duration + .25f)
                { LastBlockedReason = "ExistingMissileContact"; return true; }
            }
            if (OrbPathHitsGuard(guard, warning + duration + .25f))
            { LastBlockedReason = "PredictedOrbPath"; return true; }
            return false;
        }

        // Project the actual current flight through wall/panel reflections, without moving any scene object.
        private bool OrbPathHitsGuard(Vector2 guard, float horizon)
        {
            if (!Orb || !Orb.Active) return false;
            Vector2 position = Orb.Position, velocity = Orb.Velocity;
            float time = 0f, speed = velocity.magnitude;
            if (speed < .01f) return false;
            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(Orb.CollisionMask.value & ((1 << 9) | (1 << 12)));
            for (int bounce = 0; bounce < 12 && time < horizon; bounce++)
            {
                float segmentTime = horizon - time;
                int count = gameObject.scene.GetPhysicsScene2D().CircleCast(position, Orb.Radius,
                    velocity / speed, speed * segmentTime, filter, predictionHits);
                RaycastHit2D wall = default;
                for (int i = 0; i < count; i++)
                    if (predictionHits[i].collider && (!wall.collider || predictionHits[i].distance < wall.distance)) wall = predictionHits[i];
                if (wall.collider) segmentTime = Mathf.Min(segmentTime, wall.distance / speed);
                float contact = PathContactTime(position, velocity, guard, Orb.Radius);
                if (contact <= segmentTime) return true;
                time += segmentTime;
                if (!wall.collider) return false;
                position += velocity * segmentTime;
                velocity = Vector2.Reflect(velocity, wall.normal);
                position += velocity.normalized * .01f;
                time += .01f / speed;
            }
            return time < horizon; // Pathological repeated corner contacts defer just this attempt.
        }

        // A real body's bounds expanded by the projectile radius: conservative only at rounded corners.
        private float PathContactTime(Vector2 origin, Vector2 velocity, Vector2 center, float radius)
        {
            Vector2 half = Player.BodyCollider ? (Vector2)Player.BodyCollider.bounds.extents : new Vector2(.35f, .8f);
            half += Vector2.one * radius;
            Vector2 delta = center - origin;
            float enter = 0f, exit = float.PositiveInfinity;
            for (int axis = 0; axis < 2; axis++)
            {
                if (Mathf.Abs(velocity[axis]) < .0001f) { if (Mathf.Abs(delta[axis]) > half[axis]) return float.PositiveInfinity; continue; }
                float a = (delta[axis] - half[axis]) / velocity[axis], b = (delta[axis] + half[axis]) / velocity[axis];
                enter = Mathf.Max(enter, Mathf.Min(a, b)); exit = Mathf.Min(exit, Mathf.Max(a, b));
                if (exit < enter) return float.PositiveInfinity;
            }
            return enter;
        }

        private bool ConflictingParries(DMissile value)
        {
            if (!Orb || !Orb.Active || value.Deflected || Orb.ThreatTimeToPlayer() > .6f) return false;
            if ((value.Position.x - Player.Position.x) * (Orb.Position.x - Player.Position.x) >= -.05f) return false;
            float contact = PathContactTime(value.Position, value.FlightVelocity - Player.Velocity, Player.Position, .16f);
            return contact + value.LaunchDelayRemaining <= .5f;
        }
    }
}

