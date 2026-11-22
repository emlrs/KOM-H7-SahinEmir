using System;
using UnityEngine;

public enum GamePhase { Ready, Playing, Dying, Lost, Won }
public enum DefenseMode { None, Guard, Parry }
public enum LaserPhase { Idle, Warning, Firing }
public enum AttackKind { Missile, Laser }

/// <summary>All gameplay tuning is collected here and exposed in the Inspector.</summary>
[Serializable]
public sealed class DefenseSettings
{
    [Header("Süre ve tek tuş savunması")]
    [Min(1f)] public float RunDuration = 90f;
    [Range(0.05f, 0.4f)] public float TapThreshold = 0.20f;
    [Range(0.04f, 0.4f)] public float ParryDuration = 0.18f;

    [Header("Roket ve zorluk")]
    [Min(1f)] public float StartingMissileSpeed = 7.4f;
    [Min(1f)] public float FinalMissileSpeed = 14.2f;
    [Min(0.3f)] public float StartingMissileSpacing = 1.7f;
    [Min(0.3f)] public float FinalMissileSpacing = 0.72f;
    [Min(0.42f)] public float LaserToMissileSafetyGap = 0.42f;
    [Range(1, 5)] public int MaxConcurrentLasers = 5;
    public int PatternSeed = 5731;

    [Header("Bağımsız lazer zamanlayıcıları")]
    [Min(0.3f)] public float StartingLaserWarning = 1.2f;
    [Min(0.3f)] public float FinalLaserWarning = 0.82f;
    [Min(0.1f)] public float StartingLaserDuration = 1.3f;
    [Min(0.1f)] public float FinalLaserDuration = 2.1f;

    [Header("Sahne koordinatları")]
    public float HeroX = -5.4f;
    public float HeroCenterY = -0.7f;
    public float ShieldX = -4.6f;
    public float MissileSpawnX = 10.6f;
    public float LaserSourceX = 8.7f;

    [Header("Yansıma ve ölüm")]
    [Min(1f)] public float ReflectedMissileSpeed = 12.8f;
    [Min(0f)] public float ReflectedWobble = 0.58f;
    [Min(0.2f)] public float MissileDeathDuration = 1.65f;
    [Min(0.2f)] public float LaserDeathDuration = 2.4f;

    public Vector2 ShieldPosition => new Vector2(ShieldX, HeroCenterY);

    public void Validate()
    {
        RunDuration = Mathf.Max(1f, RunDuration);
        TapThreshold = Mathf.Max(0.01f, TapThreshold);
        ParryDuration = Mathf.Max(0.01f, ParryDuration);
        StartingMissileSpeed = Mathf.Max(1f, StartingMissileSpeed);
        FinalMissileSpeed = Mathf.Max(1f, FinalMissileSpeed);
        StartingMissileSpacing = Mathf.Max(0.3f, StartingMissileSpacing);
        FinalMissileSpacing = Mathf.Max(0.3f, FinalMissileSpacing);
        LaserToMissileSafetyGap = Mathf.Max(0.42f, LaserToMissileSafetyGap);
        MaxConcurrentLasers = Mathf.Clamp(MaxConcurrentLasers, 1, 5);
        StartingLaserWarning = Mathf.Max(0.3f, StartingLaserWarning);
        FinalLaserWarning = Mathf.Max(0.3f, FinalLaserWarning);
        StartingLaserDuration = Mathf.Max(0.1f, StartingLaserDuration);
        FinalLaserDuration = Mathf.Max(0.1f, FinalLaserDuration);
        ReflectedMissileSpeed = Mathf.Max(1f, ReflectedMissileSpeed);
        MissileDeathDuration = Mathf.Max(0.2f, MissileDeathDuration);
        LaserDeathDuration = Mathf.Max(0.2f, LaserDeathDuration);
    }
}

/// <summary>Rendering data only; combat is resolved by the director, never by a visual collider.</summary>
public sealed class MissileState
{
    public int Id { get; internal set; }
    public Vector2 Position { get; internal set; }
    public float Rotation { get; internal set; }
    public bool IsReflected { get; internal set; }
    public float Speed { get; internal set; }
    public float LaunchTime { get; internal set; }
    public float ImpactTime { get; internal set; }
    internal Vector2 LaunchPosition;
    internal float ReflectedAt;
    internal float ReflectedDirection;
}

/// <summary>There are exactly five independently timed laser slots.</summary>
public sealed class LaserState
{
    public int Slot { get; internal set; }
    public float OriginY { get; internal set; }
    public LaserPhase Phase { get; internal set; }
    public float WarningRemaining { get; internal set; }
    public float WarningDuration { get; internal set; }
    public float ActiveRemaining { get; internal set; }
    public float ActiveDuration { get; internal set; }
    public float StartTime { get; internal set; }
    public float EndTime { get; internal set; }
}

/// <summary>Inspectable timeline metadata, also used by deterministic fairness tests.</summary>
public sealed class AttackPlan
{
    public AttackKind Kind { get; internal set; }
    public int Id { get; internal set; }
    public int Slot { get; internal set; }
    public float LaunchTime { get; internal set; }
    public float WarningTime { get; internal set; }
    public float StartTime { get; internal set; }
    public float EndTime { get; internal set; }
    public float ImpactTime { get; internal set; }
    public float OriginY { get; internal set; }
}
