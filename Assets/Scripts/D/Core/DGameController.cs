using UnityEngine;
using UnityEngine.InputSystem;

namespace CombinedD
{
    public enum DGamePhase { Instruction, Playing, Dying, Lost, Won }

    [DefaultExecutionOrder(-300)]
    public class DGameController : MonoBehaviour
    {
        public DPlayerMotor Player;
        public DOrbMotor Orb;
        public DAttackDirector Attacks;
        public DLevelManager Level;
        public DCameraController CameraRig;
        public DLavaController Lava;
        public DEffectsController Effects;
        public DHUDController HUD;
        public bool ManualSimulation;
        public float DeathDuration = 1.3f;

        public DGamePhase Phase { get; private set; }
        public float Elapsed { get; private set; }
        public float LavaHeight => Lava ? Lava.Height : -7f;
        public bool DoorOpen { get; private set; }
        public int OrbParries { get; private set; }
        public int MissileParries { get; private set; }
        public int RunSerial { get; private set; }
        public bool IsPlaying => Phase == DGamePhase.Playing;
        float dyingTime;
        bool waitForStartRelease;
        DPlayerMotor subscribedPlayer;

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        bool quitRequested, quitReady;
        void OnEnable() { Application.wantsToQuit += DeferNativeQuit; }
        void OnDisable() { Application.wantsToQuit -= DeferNativeQuit; }
        bool DeferNativeQuit()
        {
            // Leave Windows' close-button message before Unity destroys its native window.
            // This editor version can otherwise re-enter the window theme callback at shutdown.
            if (quitReady) return true;
            quitRequested = true;
            return false;
        }
#endif

        void Start() { if (!ManualSimulation) ResetRun(false); }
        void OnDestroy() { if (subscribedPlayer) subscribedPlayer.Died -= Die; }

        void Update()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (quitRequested) { quitRequested = false; quitReady = true; Application.Quit(); return; }
#endif
            if (ManualSimulation) return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (Phase == DGamePhase.Instruction && keyboard.spaceKey.wasPressedThisFrame)
            {
                BeginRun();
                waitForStartRelease = true;
                if (Player) Player.InputEnabled = false;
            }
            else if (waitForStartRelease && !keyboard.spaceKey.isPressed)
            {
                waitForStartRelease = false;
                if (Player) Player.InputEnabled = IsPlaying;
            }
            // A key held during Dying has no fresh edge when the Lost screen appears.
            if ((Phase == DGamePhase.Lost || Phase == DGamePhase.Won) && keyboard.rKey.wasPressedThisFrame)
                RestartRun();
        }

        void FixedUpdate() { if (!ManualSimulation) Tick(Time.fixedDeltaTime); }

        public void BeginRun() { ResetRun(true); }
        public void RestartRun() { ResetRun(true); }

        public void ResetRun(bool beginImmediately)
        {
            if (subscribedPlayer != Player)
            {
                if (subscribedPlayer) subscribedPlayer.Died -= Die;
                subscribedPlayer = Player;
                if (subscribedPlayer) subscribedPlayer.Died += Die;
            }
            Phase = beginImmediately ? DGamePhase.Playing : DGamePhase.Instruction;
            Elapsed = 0; dyingTime = 0; DoorOpen = false;
            OrbParries = 0; MissileParries = 0; waitForStartRelease = false;
            if (beginImmediately) RunSerial++;
            if (Lava) Lava.ResetLava();
            if (Attacks) Attacks.ResetAttacks();
            if (Level) Level.ResetPanels();
            if (Player)
            {
                Player.ResetAt(Level ? Level.GetPlayerSpawn(Player.Position) : Player.Position);
                Player.InputEnabled = beginImmediately;
            }
            if (Orb)
            {
                Vector2 spawn = Level ? Level.GetOrbSpawn(Orb.Position) : Orb.Position;
                Vector2 direction = Level ? Level.InitialOrbDirection : new Vector2(-.85f, -.52f);
                Orb.ResetOrb(spawn, direction.normalized);
                if (!beginImmediately) Orb.Freeze();
            }
            if (Lava) Lava.ResetVisual();
            if (CameraRig) CameraRig.ResetCamera();
            if (Effects) Effects.ResetEffects();
            if (Player && Player.View) Player.View.ResetVisual();
            if (Lava) Lava.SetHeight(Lava.Height);
            if (Level) Level.SetDoorOpen(false);
            if (HUD) { HUD.SetDoorOpen(false); HUD.UpdateHUD(this); }
        }

        public void Tick(float dt)
        {
            if (dt <= 0) return;
            if (IsPlaying)
            {
                Elapsed += dt;
                if (Lava) Lava.Step(dt);
                if (Player && Player.Alive && Player.BodyCollider &&
                    Player.BodyCollider.bounds.min.y <= LavaHeight) Die();
            }
            else if (Phase == DGamePhase.Dying)
            {
                dyingTime += dt;
                if (dyingTime >= DeathDuration) Phase = DGamePhase.Lost;
            }
            if (Lava) Lava.TickVisual(dt);
            if (Effects) Effects.TickVisual(dt);
            if (HUD) HUD.UpdateHUD(this);
        }

        public void Die()
        {
            if (!IsPlaying) return;
            Phase = DGamePhase.Dying;
            dyingTime = 0;
            Vector2 origin = Player ? Player.Position : Vector2.zero;
            if (Attacks) Attacks.StopAll();
            if (Orb) Orb.Freeze();
            if (Player) { Player.InputEnabled = false; Player.Kill(); }
            if (Effects) Effects.PlayDeath(origin);
            if (HUD) HUD.UpdateHUD(this);
        }

        public void OnOrbParry(Vector2 contact)
        {
            if (!IsPlaying) return;
            OrbParries++;
            if (Attacks) Attacks.TriggerEnergyWave();
            if (Effects)
            {
                Effects.PlayWave(Player ? Player.Position : contact);
                Effects.PlayImpact(contact, new Color(.4f, 1, .9f));
            }
        }

        public void OnMissileParry()
        {
            if (IsPlaying) MissileParries++;
        }

        public void OpenDoor()
        {
            if (!IsPlaying || DoorOpen) return;
            DoorOpen = true;
            if (Orb) Orb.Absorb();
            if (Level) Level.SetDoorOpen(true);
            if (HUD) HUD.SetDoorOpen(true);
        }

        public void TryExit(DPlayerMotor player)
        {
            if (!IsPlaying || !DoorOpen || player != Player || !player.Alive ||
                !Level || !Level.Exit || !Level.Exit.Contains(player)) return;
            Phase = DGamePhase.Won;
            if (Player) Player.InputEnabled = false;
            if (Orb) Orb.Freeze();
            if (Attacks) Attacks.StopAll();
            if (HUD) HUD.UpdateHUD(this);
        }
    }
}
