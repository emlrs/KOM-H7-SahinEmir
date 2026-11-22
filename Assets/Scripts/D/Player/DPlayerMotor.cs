using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CombinedD
{
    /// <summary>Update collects edges. FixedUpdate changes velocity; Physics2D resolves the capsule.</summary>
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D), typeof(CapsuleCollider2D))]
    public sealed class DPlayerMotor : MonoBehaviour
    {
        public DGameController Game;
        public DCharacterView View;
        public Rigidbody2D Body;
        public Collider2D BodyCollider, ShieldCollider;
        public Transform SpawnPoint;
        public LayerMask GroundMask = 1 << 8;
        public bool ManualSimulation, UseExternalInput;
        [Header("Inertia and braking (world units / second)")]
        public float MaxSpeed = 6.8f, GroundAcceleration = 20f, GroundBrake = 12f, ReverseBrake = 24f;
        public float AirAcceleration = 10f, AirBrake = 2f;
        [Header("Jump")]
        // 13.5 / 30 gives a ~3-unit apex: comfortable margin above the course's 2.1-unit rises.
        public float JumpSpeed = 13.5f, Gravity = 30f, FallGravity = 38f, JumpCut = .48f, MaxFallSpeed = 18f;
        public float CoyoteTime = .12f, JumpBuffer = .14f;
        [Header("E: release a short tap to parry; hold while stopped to guard")]
        public float MaximumTapSeconds = .22f, GuardHoldSeconds = .22f, ParrySeconds = .20f, GuardStopSpeed = .12f;
        [Header("Verification ablations — delivery values are all true")]
        public bool UseAcceleration = true, GuardLocksMovement = true, RequireStoppedGuard = true;
        [SerializeField] private bool inputEnabled = true, alive = true;
        [SerializeField] private int facing = 1;
        private float move, bufferRemaining, coyoteRemaining, parryRemaining;
        private bool jumpHeld, defendHeld, trackingPress, guardActive, jumpConsumed;
        private double pressTime, manualTime;
        private readonly Queue<InputEdge> edges = new Queue<InputEdge>();
        private readonly RaycastHit2D[] groundHits = new RaycastHit2D[8];
        private struct InputEdge { public byte Kind; public double Time; }
        private const byte JumpPress = 0, JumpRelease = 1, DefendPress = 2, DefendRelease = 3;
        public const float ContactTolerance = .08f;

        public bool InputEnabled
        {
            get => inputEnabled;
            set { inputEnabled = value; if (!value) ClearInput(); }
        }
        public Vector2 Position => Body ? Body.position : (Vector2)transform.position;
        public Vector2 Velocity => Body ? Body.linearVelocity : Vector2.zero;
        public bool Grounded { get; private set; }
        public bool Alive => alive;
        public bool GuardActive => alive && inputEnabled && guardActive;
        public bool ParryActive => alive && inputEnabled && parryRemaining > 0f && !guardActive;
        public float ParryRemaining => parryRemaining;
        public int Facing => facing;
        public int ParrySerial { get; private set; }
        public event Action Died;

        private void Awake()
        {
            ConfigureBody();
            if (!View) View = GetComponentInChildren<DCharacterView>(true);
            if (View) View.Bind(this);
            RefreshShield();
        }
        private void ConfigureBody()
        {
            if (!Body) Body = GetComponent<Rigidbody2D>();
            if (!BodyCollider) BodyCollider = GetComponent<CapsuleCollider2D>();
            Body.bodyType = RigidbodyType2D.Dynamic; Body.gravityScale = 0f;
            Body.constraints = RigidbodyConstraints2D.FreezeRotation;
            Body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            Body.interpolation = RigidbodyInterpolation2D.Interpolate;
            Body.linearDamping = 0f; Body.angularDamping = 0f;
        }
        private void Update()
        {
            if (UseExternalInput || !inputEnabled || !alive) return;
            var keys = Keyboard.current; if (keys == null) return;
            float horizontal = (keys.dKey.isPressed || keys.rightArrowKey.isPressed ? 1f : 0f)
                - (keys.aKey.isPressed || keys.leftArrowKey.isPressed ? 1f : 0f);
            QueueInput(new DInputFrame(horizontal, keys.spaceKey.wasPressedThisFrame, keys.spaceKey.isPressed,
                keys.spaceKey.wasReleasedThisFrame, keys.eKey.wasPressedThisFrame,
                keys.eKey.isPressed, keys.eKey.wasReleasedThisFrame), Time.timeAsDouble);
        }
        private void FixedUpdate() { if (!ManualSimulation) Step(Time.fixedDeltaTime); }
        public void SetInput(DInputFrame frame) => QueueInput(frame, manualTime);
        private void QueueInput(DInputFrame frame, double at)
        {
            if (frame == null || !inputEnabled || !alive) return;
            move = Mathf.Clamp(frame.Move, -1f, 1f); jumpHeld = frame.JumpHeld; defendHeld = frame.DefendHeld;
            if (frame.JumpPressed) edges.Enqueue(new InputEdge { Kind = JumpPress, Time = at });
            if (frame.JumpReleased) edges.Enqueue(new InputEdge { Kind = JumpRelease, Time = at });
            if (frame.DefendPressed) edges.Enqueue(new InputEdge { Kind = DefendPress, Time = at });
            if (frame.DefendReleased) edges.Enqueue(new InputEdge { Kind = DefendRelease, Time = at });
        }

        public void Step(float dt)
        {
            if (dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt)) return;
            manualTime += dt;
            if (!Body) ConfigureBody();
            if (!alive || !inputEnabled)
            {
                ClearInput();
                if (Body) Body.linearVelocity = Vector2.zero;
                return;
            }
            parryRemaining = Mathf.Max(0f, parryRemaining - dt);
            bufferRemaining = Mathf.Max(0f, bufferRemaining - dt);
            Grounded = FindGround();
            if (Grounded) { coyoteRemaining = CoyoteTime; jumpConsumed = false; }
            else coyoteRemaining = Mathf.Max(0f, coyoteRemaining - dt);
            bool cutJump = false;
            while (edges.Count > 0)
            {
                InputEdge edge = edges.Dequeue();
                if (edge.Kind == JumpPress) bufferRemaining = JumpBuffer;
                else if (edge.Kind == JumpRelease) cutJump = true;
                else if (edge.Kind == DefendPress)
                {
                    if (!trackingPress) { trackingPress = true; pressTime = edge.Time; }
                }
                else
                {
                    // Release drops guard first; only elapsed press time can qualify as a short tap.
                    guardActive = false;
                    double duration = edge.Time - pressTime;
                    if (trackingPress && duration >= 0 && duration <= MaximumTapSeconds + .000001)
                    { parryRemaining = ParrySeconds; ParrySerial++; }
                    trackingPress = false;
                }
            }
            if (Mathf.Abs(move) > .001f) facing = move > 0f ? 1 : -1;
            Vector2 velocity = Body.linearVelocity;
            if (!defendHeld || !Grounded) guardActive = false;
            double now = UseExternalInput ? manualTime : Time.timeAsDouble;
            bool stopped = Mathf.Abs(velocity.x) <= GuardStopSpeed && Mathf.Abs(move) < .001f;
            if (!guardActive && Grounded && defendHeld && trackingPress
                && now - pressTime >= GuardHoldSeconds && (stopped || !RequireStoppedGuard))
            {
                guardActive = true; parryRemaining = 0f;
            }

            if (guardActive && GuardLocksMovement)
            {
                // This zero is allowed only after the ordinary brake has already stopped the body.
                velocity.x = 0f; bufferRemaining = 0f;
            }
            else
            {
                float desired = move * MaxSpeed;
                float rate = Grounded
                    ? (Mathf.Abs(move) < .001f ? GroundBrake
                        : velocity.x * move < 0f ? ReverseBrake : GroundAcceleration)
                    : (Mathf.Abs(move) < .001f ? AirBrake : AirAcceleration);
                velocity.x = UseAcceleration ? Mathf.MoveTowards(velocity.x, desired, rate * dt) : desired;
                if (bufferRemaining > 0f && coyoteRemaining > 0f && !jumpConsumed)
                {
                    velocity.y = JumpSpeed; Grounded = false; jumpConsumed = true;
                    coyoteRemaining = 0f; bufferRemaining = 0f; guardActive = false;
                    // A very short Space tap may have both edges queued before this FixedUpdate.
                    if (!jumpHeld) cutJump = true;
                }
            }
            if (cutJump && velocity.y > 0f) velocity.y *= JumpCut;
            if (Grounded && velocity.y <= 0f) velocity.y = -.5f;
            else velocity.y = Mathf.Max(-MaxFallSpeed, velocity.y - (velocity.y < 0f ? FallGravity : Gravity) * dt);
            Body.linearVelocity = velocity;
            RefreshShield();
        }

        private bool FindGround()
        {
            if (!BodyCollider || !BodyCollider.enabled || Body.linearVelocity.y > .1f) return false;
            var filter = new ContactFilter2D { useLayerMask = true, layerMask = GroundMask, useTriggers = false };
            int count = BodyCollider.Cast(Vector2.down, filter, groundHits, .065f, true);
            for (int i = 0; i < count; i++)
                if (groundHits[i].collider && groundHits[i].normal.y > .65f) return true;
            return false;
        }
        public void ResetAt(Vector2 position, int facing = 1)
        {
            ConfigureBody(); ClearInput(); this.facing = facing < 0 ? -1 : 1;
            alive = true; inputEnabled = true; manualTime = 0; ParrySerial = 0;
            Grounded = false; coyoteRemaining = 0; jumpConsumed = false;
            Body.simulated = true; BodyCollider.enabled = true; Body.position = position;
            transform.position = position; Body.linearVelocity = Vector2.zero; Body.angularVelocity = 0;
            RefreshShield(); if (View) View.ResetVisual();
        }
        public void Kill()
        {
            if (!alive) return;
            alive = false; inputEnabled = false; Grounded = false; ClearInput();
            Body.linearVelocity = Vector2.zero; Body.angularVelocity = 0; Body.simulated = false;
            Died?.Invoke();
        }
        private void OnDisable() => ClearInput();
        private void ClearInput()
        {
            edges.Clear(); move = 0f; jumpHeld = defendHeld = trackingPress = guardActive = false;
            bufferRemaining = parryRemaining = 0f;
            if (ShieldCollider) ShieldCollider.enabled = false;
        }
        private void RefreshShield()
        {
            if (!ShieldCollider) return;
            ShieldCollider.transform.localScale = new Vector3(facing, 1, 1);
            ShieldCollider.enabled = ParryActive || GuardActive;
        }

        public bool CanParryAt(Vector2 ballCenter, float radius, Vector2 incoming)
        {
            if (!ParryActive || !ShieldCollider || !ShieldCollider.enabled || radius < 0f) return false;
            Vector2 delta = ballCenter - Position; float front = delta.x * facing;
            if (front < -ContactTolerance || (front < 0f && delta.y < .64f)) return false;
            // An outgoing or wrong-side contact cannot count again merely because it remains nearby.
            if (incoming.sqrMagnitude > .00001f && Vector2.Dot(incoming, delta) > .001f) return false;
            Vector2 nearest = ShieldCollider.ClosestPoint(ballCenter);
            return (nearest - ballCenter).sqrMagnitude <= (radius + ContactTolerance) * (radius + ContactTolerance);
        }
        public bool CanGuardFrom(Vector2 attackPoint)
        {
            if (!GuardActive || !ShieldCollider || !ShieldCollider.enabled) return false;
            Vector2 delta = attackPoint - Position; float front = delta.x * facing;
            // The visible crown covers overhead; the back and underside are deliberately open.
            return front >= -ContactTolerance && delta.sqrMagnitude > .0001f
                && (front > .08f || delta.y > .64f) && delta.y >= -Mathf.Abs(front) * 1.15f;
        }
        public void NotifyParry(Vector2 point) { if (View) View.FlashParry(point); }

    }
}
