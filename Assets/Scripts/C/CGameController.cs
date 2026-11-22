using UnityEngine;
using UnityEngine.InputSystem;

namespace CollisionC
{
    public enum CGamePhase { Instruction, Playing, ReceiverReady, DoorOpen, Dying, Lost, Won }

    /// <summary>Owns run state; neither receiver readiness nor door opening teleports the player.</summary>
    [DefaultExecutionOrder(-300)]
    [DisallowMultipleComponent]
    public sealed class CGameController : MonoBehaviour
    {
        public CPlayerMotor Player;
        public COrbMotor Orb;
        public CEnvironmentView Environment;
        public CReceiver Receiver;
        public CExitTrigger Exit;
        public CPlate[] Plates;
        [Min(0f)] public float CountdownDuration = 45f;
        [Tooltip("Editor verification advances Tick explicitly; normal keyboard edges still use Update.")]
        public bool ManualSimulation;

        public CGamePhase Phase { get; private set; } = CGamePhase.Instruction;
        public float Elapsed { get; private set; }
        public float Remaining => Mathf.Max(0f, CountdownDuration - Elapsed);
        public int ParryCount { get; private set; }
        public float DeathElapsed { get; private set; }
        public int RunSerial { get; private set; }
        public bool IsPlaying => Phase == CGamePhase.Playing || Phase == CGamePhase.ReceiverReady || Phase == CGamePhase.DoorOpen;
        public bool ThreatActive => Phase == CGamePhase.Playing || Phase == CGamePhase.ReceiverReady;
        public bool ReceiverReady => Phase == CGamePhase.ReceiverReady;
        public const float DeathDuration = 1.4f;
        public static readonly Vector2 PlayerStart = new Vector2(-5.8f, -3.24f);
        public static readonly Vector2 OrbStart = new Vector2(6.7f, 2f);
        public static readonly Vector2 OrbDirection = new Vector2(-.86f, -.5f).normalized;

        private bool initialized, waitingForStartRelease, enableInputNextUpdate;
        private CPlayerMotor subscribedPlayer;

        private void Awake() => BindPlayer();
        private void OnEnable() => BindPlayer();
        private void Start()
        {
            BindPlayer();
            if (!initialized) ResetState(false);
        }
        private void OnDisable() => UnbindPlayer();
        private void BindPlayer()
        {
            if (subscribedPlayer == Player) return;
            UnbindPlayer();
            subscribedPlayer = Player;
            if (subscribedPlayer) subscribedPlayer.Died += Die;
        }
        private void UnbindPlayer()
        {
            if (subscribedPlayer) subscribedPlayer.Died -= Die;
            subscribedPlayer = null;
        }

        public void Configure(CPlayerMotor player, COrbMotor orb, CEnvironmentView environment,
            CReceiver receiver, CExitTrigger exit, CPlate[] plates)
        {
            Player = player; Orb = orb; Environment = environment; Receiver = receiver; Exit = exit; Plates = plates;
            if (Player) Player.Game = this;
            if (Orb) { Orb.Game = this; Orb.Player = Player; }
            if (Receiver) Receiver.Game = this;
            if (Exit) Exit.Game = this;
            BindPlayer();
            ResetState(false);
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (Phase == CGamePhase.Instruction)
            {
                if (keyboard.eKey.wasPressedThisFrame) BeginRun();
                return;
            }
            if (Phase == CGamePhase.Lost || Phase == CGamePhase.Won)
            {
                // An R held during Dying has no new edge here and cannot skip the animation.
                if (keyboard.rKey.wasPressedThisFrame) RestartRun();
                return;
            }
            if (!IsPlaying) return;
            if (enableInputNextUpdate)
            {
                enableInputNextUpdate = false;
                if (Player) Player.InputEnabled = true;
            }
            if (waitingForStartRelease && !keyboard.eKey.isPressed)
            {
                // Player.Update runs later. Leave it disabled for this release frame so the E
                // which dismissed the instructions can never become the first parry.
                waitingForStartRelease = false;
                enableInputNextUpdate = true;
            }
        }
        private void FixedUpdate() { if (!ManualSimulation) Tick(Time.fixedDeltaTime); }

        public void BeginRun()
        {
            ResetState(true);
            RunSerial++;
        }
        public void RestartRun()
        {
            if (Phase != CGamePhase.Lost && Phase != CGamePhase.Won) return;
            BeginRun();
        }
        private void ResetState(bool playing)
        {
            initialized = true;
            Phase = playing ? CGamePhase.Playing : CGamePhase.Instruction;
            Elapsed = 0f; ParryCount = 0; DeathElapsed = 0f;
            waitingForStartRelease = playing && Keyboard.current != null && Keyboard.current.eKey.isPressed;
            enableInputNextUpdate = false;
            if (Plates != null) foreach (CPlate plate in Plates) if (plate) plate.ResetPlate();
            if (Environment) Environment.ResetWorld();
            if (Player)
            {
                Player.ResetAt(PlayerStart, 1);
                Player.InputEnabled = playing && !waitingForStartRelease;
            }
            if (Orb)
            {
                Orb.ResetOrb(OrbStart, OrbDirection);
                if (!playing) Orb.Freeze();
            }
            Physics2D.SyncTransforms();
            RefreshHUD();
        }

        public void Tick(float dt)
        {
            if (dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt)) return;
            if (IsPlaying)
            {
                Elapsed += dt;
                if (Phase == CGamePhase.Playing && Remaining <= 0f)
                {
                    Phase = CGamePhase.ReceiverReady;
                    if (Environment) Environment.SetReceiverOpen(true);
                }
            }
            else if (Phase == CGamePhase.Dying)
            {
                DeathElapsed = Mathf.Min(DeathDuration, DeathElapsed + dt);
                if (DeathElapsed >= DeathDuration) Phase = CGamePhase.Lost;
            }
            if (Environment) Environment.TickVisual(dt);
            RefreshHUD();
        }
        public void OnParry()
        {
            if (!ThreatActive) return;
            ParryCount++;
            RefreshHUD();
        }
        public void Die()
        {
            if (!ThreatActive) return;
            Phase = CGamePhase.Dying; DeathElapsed = 0f;
            waitingForStartRelease = enableInputNextUpdate = false;
            if (Orb) Orb.Freeze();
            if (Player) { Player.InputEnabled = false; Player.Kill(); }
            if (Environment) Environment.PlayDeath(Player ? Player.Position : PlayerStart);
            RefreshHUD();
        }
        public bool AcceptOrb(COrbMotor candidate)
        {
            if (!ReceiverReady || !candidate || candidate != Orb || !candidate.Active || !candidate.ReceiverCharged
                || !Receiver || !Receiver.TouchesOrb(candidate)) return false;
            Phase = CGamePhase.DoorOpen;
            candidate.Absorb();
            if (Environment) Environment.SetDoorOpen(true);
            RefreshHUD();
            return true;
        }
        public bool TryExit(CPlayerMotor candidate)
        {
            if (Phase != CGamePhase.DoorOpen || !candidate || candidate != Player || !candidate.Alive
                || !Exit || !Exit.TouchesPlayer(candidate)) return false;
            Phase = CGamePhase.Won;
            waitingForStartRelease = enableInputNextUpdate = false;
            candidate.InputEnabled = false;
            RefreshHUD();
            return true;
        }
        private void RefreshHUD() { if (Environment) Environment.UpdateHUD(this); }
    }
}
