using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Deterministic, chronological attack simulation. Every laser has its own start/end
/// events. Missile impact times are planned after the final laser ends, with a safe gap.
/// </summary>
public sealed class AttackDirector
{
    private enum EventKind { LaserEnd, LaserWarning, MissileSpawn, LaserStart, MissileImpact }

    private sealed class ScheduledEvent
    {
        public float Time;
        public EventKind Kind;
        public AttackPlan Plan;
    }

    private readonly DefenseGame game;
    private readonly List<MissileState> missiles = new List<MissileState>();
    private readonly LaserState[] lasers = new LaserState[5];
    private readonly List<AttackPlan> plans = new List<AttackPlan>();
    private readonly List<ScheduledEvent> events = new List<ScheduledEvent>();
    private bool running;
    private int nextId;

    public IReadOnlyList<MissileState> Missiles => missiles;
    public IReadOnlyList<LaserState> Lasers => lasers;
    public IReadOnlyList<AttackPlan> PlannedAttacks => plans;
    public int ActiveLaserCount => CountLasers(LaserPhase.Firing);
    public int WarningLaserCount => CountLasers(LaserPhase.Warning);

    public AttackDirector(DefenseGame owner)
    {
        game = owner;
        for (int i = 0; i < lasers.Length; i++)
            lasers[i] = new LaserState { Slot = i, OriginY = LaserOrigin(i), Phase = LaserPhase.Idle };
    }

    public void Start(bool automaticSchedule)
    {
        running = true;
        if (automaticSchedule) BuildTimeline();
    }

    public void Stop()
    {
        running = false;
        ClearAttacks();
    }

    /// <summary>Remove all pending/current attacks; keep the run clock for controlled scenarios.</summary>
    public void ClearAttacks()
    {
        events.Clear();
        plans.Clear();
        for (int i = missiles.Count - 1; i >= 0; i--) game.NotifyMissileRemoved(missiles[i].Id);
        missiles.Clear();
        foreach (LaserState laser in lasers)
        {
            laser.Phase = LaserPhase.Idle;
            laser.OriginY = LaserOrigin(laser.Slot);
            laser.WarningRemaining = 0f;
            laser.WarningDuration = 0f;
            laser.ActiveRemaining = 0f;
            laser.ActiveDuration = 0f;
            laser.StartTime = 0f;
            laser.EndTime = 0f;
            game.NotifyLaserChanged(laser);
        }
        nextId = 0;
    }

    /// <summary>Times are absolute seconds since BeginRun. Useful for authoring and deterministic tests.</summary>
    public int ScheduleMissile(float launchTime, float impactTime, float spawnY)
    {
        launchTime = Mathf.Max(game.Elapsed, launchTime);
        impactTime = Mathf.Max(launchTime + 0.05f, impactTime);
        AttackPlan plan = new AttackPlan
        {
            Kind = AttackKind.Missile,
            Id = ++nextId,
            LaunchTime = launchTime,
            StartTime = launchTime,
            ImpactTime = impactTime,
            EndTime = impactTime,
            OriginY = spawnY
        };
        plans.Add(plan);
        AddEvent(launchTime, EventKind.MissileSpawn, plan);
        AddEvent(impactTime, EventKind.MissileImpact, plan);
        SortEvents();
        return plan.Id;
    }

    /// <summary>Each of the five slots has independent warning, firing, and expiration times.</summary>
    public void ScheduleLaser(int slot, float warningStart, float firingStart, float duration, float originY)
    {
        if (slot < 0 || slot >= lasers.Length) throw new ArgumentOutOfRangeException(nameof(slot));
        warningStart = Mathf.Max(game.Elapsed, warningStart);
        firingStart = Mathf.Max(warningStart, firingStart);
        duration = Mathf.Max(0.01f, duration);
        AttackPlan plan = new AttackPlan
        {
            Kind = AttackKind.Laser,
            Id = ++nextId,
            Slot = slot,
            WarningTime = warningStart,
            StartTime = firingStart,
            EndTime = firingStart + duration,
            OriginY = originY
        };
        plans.Add(plan);
        AddEvent(warningStart, EventKind.LaserWarning, plan);
        AddEvent(firingStart, EventKind.LaserStart, plan);
        AddEvent(plan.EndTime, EventKind.LaserEnd, plan);
        SortEvents();
    }

    private void AddEvent(float time, EventKind kind, AttackPlan plan)
    {
        events.Add(new ScheduledEvent { Time = time, Kind = kind, Plan = plan });
    }

    private void SortEvents()
    {
        events.Sort((a, b) =>
        {
            int timeOrder = a.Time.CompareTo(b.Time);
            return timeOrder != 0 ? timeOrder : a.Kind.CompareTo(b.Kind);
        });
    }

    private void BuildTimeline()
    {
        DefenseSettings s = game.Settings;
        var random = new System.Random(s.PatternSeed);
        float[] openingImpacts = { 3.1f, 6.2f, 9.2f, 11.7f };
        for (int i = 0; i < openingImpacts.Length; i++)
        {
            if (openingImpacts[i] < s.RunDuration)
                PlanMissile(openingImpacts[i], i, random);
        }

        // Easy missile practice first. Later waves combine independent lasers and missile bursts.
        float cursor = 12.8f;
        int wave = 0;
        while (cursor < s.RunDuration - 1.5f)
        {
            float difficulty = Mathf.Clamp01(cursor / s.RunDuration);
            int count = Mathf.Clamp(1 + Mathf.FloorToInt(difficulty * 5f), 1, s.MaxConcurrentLasers);
            float warning = Mathf.Lerp(s.StartingLaserWarning, s.FinalLaserWarning, difficulty);
            float baseDuration = Mathf.Lerp(s.StartingLaserDuration, s.FinalLaserDuration, difficulty);
            float finalEnd = cursor;
            float[] durationOffsets = { 0.08f, 0.55f, 0.22f, 0.42f, 0f };

            for (int slot = 0; slot < count; slot++)
            {
                float warningStart = cursor + slot * Mathf.Lerp(0.18f, 0.12f, difficulty);
                float firingStart = warningStart + warning + (slot % 2) * 0.09f;
                float duration = baseDuration + durationOffsets[(wave + slot) % durationOffsets.Length]
                    + (float)random.NextDouble() * 0.12f;
                float end = Mathf.Min(s.RunDuration - 0.05f, firingStart + duration);
                if (firingStart >= s.RunDuration - 0.1f) continue;
                ScheduleLaser(slot, warningStart, firingStart, end - firingStart, LaserOrigin(slot));
                finalEnd = Mathf.Max(finalEnd, end);
            }

            // Launches may overlap lasers, but every IMPACT is after the last beam plus the gap.
            float firstImpact = finalEnd + s.LaserToMissileSafetyGap + 0.18f;
            int missileCount = 1 + Mathf.FloorToInt(difficulty * 3f);
            float spacing = Mathf.Lerp(s.StartingMissileSpacing, s.FinalMissileSpacing, difficulty);
            float lastImpact = finalEnd;
            for (int missile = 0; missile < missileCount; missile++)
            {
                float impact = firstImpact + missile * spacing;
                if (impact >= s.RunDuration - 0.05f) break;
                PlanMissile(impact, wave + missile, random);
                lastImpact = impact;
            }

            cursor = lastImpact + Mathf.Lerp(1.55f, 0.95f, difficulty);
            wave++;
        }
    }

    private void PlanMissile(float impact, int patternIndex, System.Random random)
    {
        DefenseSettings s = game.Settings;
        float[] offsets = { 0f, 1.35f, -1.1f, 2.15f, -0.45f };
        float spawnY = s.HeroCenterY + offsets[patternIndex % offsets.Length]
            + ((float)random.NextDouble() - 0.5f) * 0.2f;
        float speed = Mathf.Lerp(s.StartingMissileSpeed, s.FinalMissileSpeed, impact / s.RunDuration);
        float distance = Vector2.Distance(new Vector2(s.MissileSpawnX, spawnY), s.ShieldPosition);
        ScheduleMissile(Mathf.Max(0.25f, impact - distance / speed), impact, spawnY);
    }

    public void Tick(float from, float to)
    {
        if (!running) return;

        // Expire beams at the current instant before evaluating a just-released guard.
        while (running && events.Count > 0 && events[0].Time <= from)
            ProcessNext(from);
        if (!running) return;
        if (!game.IsGuarding && ActiveLaserCount > 0)
        {
            game.TakeHit(true, from);
            return;
        }

        while (running && events.Count > 0 && events[0].Time <= to)
        {
            float at = events[0].Time;
            UpdatePositions(at);
            ProcessNext(at);
        }
        if (!running) return;

        UpdatePositions(to);
        foreach (LaserState laser in lasers)
        {
            laser.WarningRemaining = laser.Phase == LaserPhase.Warning ? Mathf.Max(0f, laser.StartTime - to) : 0f;
            laser.ActiveRemaining = laser.Phase == LaserPhase.Firing ? Mathf.Max(0f, laser.EndTime - to) : 0f;
        }
    }

    private void ProcessNext(float at)
    {
        ScheduledEvent next = events[0];
        events.RemoveAt(0);
        AttackPlan plan = next.Plan;
        switch (next.Kind)
        {
            case EventKind.LaserWarning:
                LaserState warning = lasers[plan.Slot];
                warning.OriginY = plan.OriginY;
                warning.Phase = LaserPhase.Warning;
                warning.StartTime = plan.StartTime;
                warning.EndTime = plan.EndTime;
                warning.WarningDuration = plan.StartTime - plan.WarningTime;
                warning.WarningRemaining = Mathf.Max(0f, plan.StartTime - at);
                warning.ActiveDuration = plan.EndTime - plan.StartTime;
                warning.ActiveRemaining = 0f;
                game.NotifyLaserChanged(warning);
                break;
            case EventKind.LaserStart:
                LaserState active = lasers[plan.Slot];
                active.Phase = LaserPhase.Firing;
                active.WarningRemaining = 0f;
                active.ActiveRemaining = Mathf.Max(0f, plan.EndTime - at);
                game.NotifyLaserChanged(active);
                if (game.IsGuarding) game.NotifyBlock();
                else game.TakeHit(true, at);
                break;
            case EventKind.LaserEnd:
                LaserState ended = lasers[plan.Slot];
                ended.Phase = LaserPhase.Idle;
                ended.WarningRemaining = 0f;
                ended.ActiveRemaining = 0f;
                game.NotifyLaserChanged(ended);
                break;
            case EventKind.MissileSpawn:
                SpawnMissile(plan, at);
                break;
            case EventKind.MissileImpact:
                MissileState missile = missiles.Find(item => item.Id == plan.Id);
                if (missile == null || missile.IsReflected) break;
                if (game.IsParryingAt(at))
                {
                    missile.IsReflected = true;
                    missile.ReflectedAt = at;
                    missile.ReflectedDirection = missile.Id % 2 == 0 ? 1f : -1f;
                    missile.Position = game.Settings.ShieldPosition;
                    missile.Rotation = 0f;
                    missile.Speed = game.Settings.ReflectedMissileSpeed;
                    game.NotifyParry();
                }
                else game.TakeHit(false, at); // Guard offers no protection against missiles.
                break;
        }
    }

    private void SpawnMissile(AttackPlan plan, float at)
    {
        DefenseSettings s = game.Settings;
        Vector2 launch = new Vector2(s.MissileSpawnX, plan.OriginY);
        Vector2 travel = s.ShieldPosition - launch;
        MissileState missile = new MissileState
        {
            Id = plan.Id,
            Position = launch,
            LaunchPosition = launch,
            LaunchTime = plan.LaunchTime,
            ImpactTime = plan.ImpactTime,
            Speed = travel.magnitude / (plan.ImpactTime - plan.LaunchTime),
            Rotation = Mathf.Atan2(travel.y, travel.x) * Mathf.Rad2Deg
        };
        missiles.Add(missile);
        UpdateMissilePosition(missile, at);
        game.NotifyMissileSpawned(missile);
    }

    private void UpdatePositions(float at)
    {
        for (int i = missiles.Count - 1; i >= 0; i--)
        {
            MissileState missile = missiles[i];
            UpdateMissilePosition(missile, at);
            if (missile.IsReflected && (missile.Position.x > 12.5f || Mathf.Abs(missile.Position.y) > 7.5f))
            {
                missiles.RemoveAt(i);
                game.NotifyMissileRemoved(missile.Id);
            }
        }
    }

    private void UpdateMissilePosition(MissileState missile, float at)
    {
        DefenseSettings s = game.Settings;
        if (!missile.IsReflected)
        {
            float t = Mathf.InverseLerp(missile.LaunchTime, missile.ImpactTime, at);
            missile.Position = Vector2.Lerp(missile.LaunchPosition, s.ShieldPosition, t);
            return;
        }

        // Reflected missiles remain visible, spin and wobble, and never enter hit detection again.
        float age = Mathf.Max(0f, at - missile.ReflectedAt);
        float bend = Mathf.Sin(age * 9f) * s.ReflectedWobble;
        missile.Position = s.ShieldPosition + new Vector2(age * s.ReflectedMissileSpeed,
            (age * 0.9f + bend) * missile.ReflectedDirection);
        missile.Rotation = age * 620f * missile.ReflectedDirection;
    }

    private int CountLasers(LaserPhase phase)
    {
        int count = 0;
        foreach (LaserState laser in lasers) if (laser.Phase == phase) count++;
        return count;
    }

    private static float LaserOrigin(int slot)
    {
        float[] origins = { 2.1f, 1.1f, 0.1f, -0.9f, -1.9f };
        return origins[slot];
    }
}
