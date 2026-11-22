using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Single-hit survival state machine. Visuals and keyboard sampling are separate.</summary>
[DisallowMultipleComponent]
public sealed class DefenseGame : MonoBehaviour
{
    public DefenseSettings Settings = new DefenseSettings();
    [Tooltip("Disable to inject a controlled attack timeline for testing.")]
    public bool AutoSchedule = true;

    public GamePhase Phase { get; private set; } = GamePhase.Ready;
    public DefenseMode Mode { get; private set; } = DefenseMode.None;
    public float Elapsed { get; private set; }
    public float Remaining => Mathf.Max(0f, Settings.RunDuration - Elapsed);
    public float Progress => Mathf.Clamp01(Elapsed / Mathf.Max(1f, Settings.RunDuration));
    public float BestSeconds { get; private set; }
    public int ParryCount { get; private set; }
    public int BlockCount { get; private set; }
    public bool LastHitWasLaser { get; private set; }
    public bool IsInputReleaseRequired => releaseRequired;
    public float ModeAge => Mathf.Max(0f, Elapsed - modeStartedAt);
    public float DeathProgress => Phase == GamePhase.Lost ? 1f : Mathf.Clamp01(deathElapsed / DeathDuration);
    public float DeathDuration => LastHitWasLaser ? Settings.LaserDeathDuration : Settings.MissileDeathDuration;
    public int MissileCount => Director.Missiles.Count;
    public int ActiveLaserCount => Director.ActiveLaserCount;
    public int WarningLaserCount => Director.WarningLaserCount;
    public IReadOnlyList<MissileState> Missiles => Director.Missiles;
    public IReadOnlyList<LaserState> Lasers => Director.Lasers;
    public AttackDirector Director { get { EnsureInitialized(); return director; } }

    public event Action StateChanged;
    public event Action<DefenseMode> DefenseChanged;
    public event Action<MissileState> MissileSpawned;
    public event Action<int> MissileRemoved;
    public event Action<LaserState> LaserChanged;
    public event Action<bool> Hit;
    public event Action ParrySucceeded;
    public event Action GuardBlocked;
    public event Action RunStarted;
    public event Action<bool> RunEnded;

    private AttackDirector director;
    private bool releaseRequired;
    private bool pressing;
    private float pressStartedAt;
    private float parryUntil;
    private float modeStartedAt;
    private float deathElapsed;

    private void Awake() => EnsureInitialized();
    private void OnValidate() { if (Settings != null) Settings.Validate(); }

    private void EnsureInitialized()
    {
        if (Settings == null) Settings = new DefenseSettings();
        if (director == null) director = new AttackDirector(this);
    }

    /// <summary>Called by DefenseInput.Update, or directly by deterministic tests.</summary>
    public void Tick(float deltaTime, bool down, bool held, bool up)
    {
        EnsureInitialized();
        float dt = Mathf.Max(0f, deltaTime);

        if (Phase != GamePhase.Playing)
        {
            if (!held || up) releaseRequired = false;
            if (Phase == GamePhase.Dying)
            {
                AdvanceDeath(dt);
                return; // A key edge during the death animation is never queued for restart.
            }
            if (down && !releaseRequired) BeginRun();
            return; // The start/restart press is consumed; it cannot also defend.
        }

        if (releaseRequired)
        {
            if (!held || up) releaseRequired = false;
        }
        else
        {
            if (down)
            {
                pressing = true;
                pressStartedAt = Elapsed;
                SetMode(DefenseMode.Guard); // Guard begins on the key-down frame.
            }
            if (pressing && (up || !held))
            {
                pressing = false;
                float heldFor = Mathf.Max(0f, Elapsed - pressStartedAt);
                if (heldFor <= Settings.TapThreshold + 0.00001f)
                {
                    parryUntil = Elapsed + Settings.ParryDuration;
                    SetMode(DefenseMode.Parry); // Exactly one release opens exactly one window.
                }
                else
                {
                    SetMode(DefenseMode.None); // A long guard release NEVER becomes a parry.
                }
            }
        }

        float from = Elapsed;
        float to = Mathf.Min(Settings.RunDuration, from + dt);
        director.Tick(from, to);
        if (Phase == GamePhase.Dying)
        {
            AdvanceDeath(Mathf.Max(0f, to - Elapsed));
            return;
        }

        Elapsed = to;
        if (Mode == DefenseMode.Parry && Elapsed >= parryUntil) SetMode(DefenseMode.None);
        if (Elapsed >= Settings.RunDuration) Win();
    }

    public void BeginRun()
    {
        EnsureInitialized();
        Settings.Validate();
        director.ClearAttacks();
        Elapsed = 0f;
        deathElapsed = 0f;
        pressing = false;
        releaseRequired = true;
        parryUntil = 0f;
        modeStartedAt = 0f;
        ParryCount = 0;
        BlockCount = 0;
        LastHitWasLaser = false;
        SetMode(DefenseMode.None);
        Phase = GamePhase.Playing;
        director.Start(AutoSchedule);
        RunStarted?.Invoke();
        StateChanged?.Invoke();
    }

    /// <summary>Focus loss cancels defense without granting a free release parry.</summary>
    public void CancelDefense()
    {
        pressing = false;
        releaseRequired = true;
        SetMode(DefenseMode.None);
    }

    private void SetMode(DefenseMode value)
    {
        if (Mode == value) return;
        Mode = value;
        modeStartedAt = Elapsed;
        DefenseChanged?.Invoke(value);
    }

    internal bool IsParryingAt(float time) => Mode == DefenseMode.Parry && time < parryUntil;
    internal bool IsGuarding => Mode == DefenseMode.Guard;
    internal void NotifyMissileSpawned(MissileState missile) => MissileSpawned?.Invoke(missile);
    internal void NotifyMissileRemoved(int id) => MissileRemoved?.Invoke(id);
    internal void NotifyLaserChanged(LaserState laser) => LaserChanged?.Invoke(laser);
    internal void NotifyParry() { ParryCount++; ParrySucceeded?.Invoke(); }
    internal void NotifyBlock() { BlockCount++; GuardBlocked?.Invoke(); }

    internal void TakeHit(bool laser, float hitTime)
    {
        if (Phase != GamePhase.Playing) return;
        Elapsed = hitTime;
        BestSeconds = Mathf.Max(BestSeconds, Elapsed);
        LastHitWasLaser = laser;
        deathElapsed = 0f;
        pressing = false;
        releaseRequired = true;
        Phase = GamePhase.Dying;
        SetMode(DefenseMode.None);
        director.Stop(); // Suppress every attack immediately, before rendering the death.
        Hit?.Invoke(laser);
        RunEnded?.Invoke(false);
        StateChanged?.Invoke();
    }

    private void AdvanceDeath(float dt)
    {
        deathElapsed += dt;
        if (deathElapsed < DeathDuration) return;
        Phase = GamePhase.Lost;
        StateChanged?.Invoke();
    }

    private void Win()
    {
        BestSeconds = Mathf.Max(BestSeconds, Elapsed);
        Phase = GamePhase.Won;
        pressing = false;
        releaseRequired = true;
        SetMode(DefenseMode.None);
        director.Stop();
        RunEnded?.Invoke(true);
        StateChanged?.Invoke();
    }
}
