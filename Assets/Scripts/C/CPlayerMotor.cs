using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CollisionC
{
    /// <summary>Ground-only kinematic capsule. Update collects input; FixedUpdate changes physics.</summary>
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D), typeof(CapsuleCollider2D))]
    public sealed class CPlayerMotor : MonoBehaviour
    {
        public CGameController Game;
        public CCharacterView View;
        public Rigidbody2D Body;
        public Collider2D BodyCollider;
        public Collider2D ShieldCollider;
        public bool ManualSimulation;
        [Header("Yalnız yatay hareket")]
        public float MoveSpeed = 6f;
        public float MinimumX = -9.4f, MaximumX = 9.4f;
        [Header("E: kısa basıp bırak / basılı tutmak savunma değildir")]
        public float MaximumTapSeconds = .22f;
        public float ParrySeconds = .20f;
        [SerializeField] private bool inputEnabled;
        [SerializeField] private bool useExternalInput;
        [SerializeField] private int facing = 1;
        [SerializeField] private bool alive = true;
        [SerializeField] private Transform shieldPivot;
        private float move, groundY = -3.24f, parryRemaining;
        private bool pressTracked;
        private double pressTime, manualTime;
        private readonly Queue<KeyEdge> pending = new Queue<KeyEdge>();
        private struct KeyEdge { public bool Press; public double Time; }

        public const float ContactTolerance = .08f;
        public bool InputEnabled
        {
            get => inputEnabled;
            set { inputEnabled = value; if (!value) ClearInput(); }
        }
        public bool UseExternalInput
        {
            get => useExternalInput;
            set { if (useExternalInput != value) ClearInput(); useExternalInput = value; }
        }
        public Vector2 Position => Body ? Body.position : (Vector2)transform.position;
        public Vector2 Velocity { get; private set; }
        public int Facing => facing;
        public bool Alive => alive;
        public bool ParryActive => alive && inputEnabled && parryRemaining > 0f;
        public float ParryRemaining => parryRemaining;
        public int ParrySerial { get; private set; }
        public event Action ParryStarted, Died;

        // Right-facing physical shield. The small crown at x=-.08 is visibly drawn by the view.
        // Mirror this entire collider for left facing; never move it independently of the player.
        public static Vector2[] ShieldPoints() => new[]
        {
            new Vector2(-.08f,.91f), new Vector2(.24f,.91f), new Vector2(.49f,.76f),
            new Vector2(.65f,.48f), new Vector2(.70f,.12f), new Vector2(.64f,-.30f),
            new Vector2(.46f,-.60f), new Vector2(.35f,-.54f), new Vector2(.52f,-.26f),
            new Vector2(.57f,.12f), new Vector2(.53f,.43f), new Vector2(.40f,.66f),
            new Vector2(.20f,.78f), new Vector2(-.08f,.78f)
        };

        public static CPlayerMotor BuildPlayer(Vector2 position)
        {
            var root = new GameObject("C Player • ground capsule");
            root.transform.position = position;
            var motor = root.AddComponent<CPlayerMotor>();
            motor.ConfigureBody();
            var shield = new GameObject("Physical shield • .20 s on short E release");
            shield.transform.SetParent(root.transform, false);
            motor.shieldPivot = shield.transform;
            var polygon = shield.AddComponent<PolygonCollider2D>();
            polygon.points = ShieldPoints(); polygon.isTrigger = false; polygon.enabled = false;
            motor.ShieldCollider = polygon;
            motor.View = CCharacterView.Build(root.transform, shield.transform);
            motor.View.Bind(motor);
            motor.ResetAt(position);
            motor.InputEnabled = false;
            return motor;
        }

        private void Awake()
        {
            ConfigureBody(); groundY = Position.y;
            if (!View) View = GetComponentInChildren<CCharacterView>(true);
            if (!shieldPivot && ShieldCollider) shieldPivot = ShieldCollider.transform;
            if (View) View.Bind(this);
            RefreshShield();
        }
        private void ConfigureBody()
        {
            if (!Body) Body = GetComponent<Rigidbody2D>();
            if (!BodyCollider) BodyCollider = GetComponent<CapsuleCollider2D>();
            Body.bodyType = RigidbodyType2D.Kinematic; Body.gravityScale = 0f;
            Body.constraints = RigidbodyConstraints2D.FreezeRotation;
            Body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            Body.interpolation = RigidbodyInterpolation2D.Interpolate;
            Body.useFullKinematicContacts = true;
            Body.linearVelocity = Vector2.zero; Body.angularVelocity = 0f;
            if (BodyCollider is CapsuleCollider2D capsule)
            { capsule.size = new Vector2(.68f, 1.52f); capsule.direction = CapsuleDirection2D.Vertical; }
            BodyCollider.isTrigger = false;
        }

        private void Update()
        {
            if (useExternalInput || !inputEnabled || !alive) return;
            Keyboard keys = Keyboard.current; if (keys == null) return;
            float horizontal = (keys.dKey.isPressed || keys.rightArrowKey.isPressed ? 1f : 0f)
                - (keys.aKey.isPressed || keys.leftArrowKey.isPressed ? 1f : 0f);
            QueueInput(new CInputFrame(horizontal, keys.eKey.wasPressedThisFrame,
                keys.eKey.isPressed, keys.eKey.wasReleasedThisFrame), Time.timeAsDouble);
        }
        private void FixedUpdate() { if (!ManualSimulation) Step(Time.fixedDeltaTime); }
        public void SetInput(CInputFrame frame) => QueueInput(frame, manualTime);
        private void QueueInput(CInputFrame frame, double timestamp)
        {
            if (!inputEnabled || !alive) return;
            move = Mathf.Clamp(frame.Move, -1f, 1f);
            if (frame.ParryPressed) pending.Enqueue(new KeyEdge { Press = true, Time = timestamp });
            if (frame.ParryReleased) pending.Enqueue(new KeyEdge { Press = false, Time = timestamp });
        }

        public void Step(float dt)
        {
            if (dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt)) return;
            manualTime += dt;
            parryRemaining = Mathf.Max(0f, parryRemaining - dt);
            if (!alive || !inputEnabled)
            { ClearInput(); Velocity = Vector2.zero; return; }
            while (pending.Count != 0)
            {
                KeyEdge edge = pending.Dequeue();
                if (edge.Press)
                {
                    if (!pressTracked) { pressTracked = true; pressTime = edge.Time; }
                }
                else
                {
                    double heldFor = edge.Time - pressTime;
                    if (pressTracked && heldFor >= 0 && heldFor <= MaximumTapSeconds + .000001)
                    { parryRemaining = ParrySeconds; ParrySerial++; ParryStarted?.Invoke(); }
                    pressTracked = false;
                }
            }
            if (Mathf.Abs(move) > .001f) facing = move > 0 ? 1 : -1;
            Vector2 oldPosition = Position;
            Vector2 target = new Vector2(Mathf.Clamp(oldPosition.x + move * MoveSpeed * dt, MinimumX, MaximumX), groundY);
            // Write the kinematic pose before the orb's sweep; there is no vertical motion or jump path.
            Body.position = target; Body.linearVelocity = Vector2.zero;
            Velocity = (target - oldPosition) / dt;
            RefreshShield();
        }

        public void ResetAt(Vector2 position, int facing = 1)
        {
            ConfigureBody(); ClearInput(); alive = true; this.facing = facing < 0 ? -1 : 1;
            groundY = position.y; manualTime = 0; ParrySerial = 0;
            Body.simulated = true; BodyCollider.enabled = true;
            Body.position = position; transform.position = position;
            Velocity = Vector2.zero; inputEnabled = true; RefreshShield();
            if (View) View.ResetVisual();
        }
        public void Kill()
        {
            if (!alive) return;
            alive = false; inputEnabled = false; ClearInput();
            Velocity = Vector2.zero;
            if (Body) { Body.linearVelocity = Vector2.zero; Body.angularVelocity = 0; Body.simulated = false; }
            Died?.Invoke();
        }
        private void OnDisable() => ClearInput();
        private void ClearInput()
        {
            pending.Clear(); move = 0; pressTracked = false; parryRemaining = 0;
            Velocity = Vector2.zero;
            if (ShieldCollider) ShieldCollider.enabled = false;
        }
        private void RefreshShield()
        {
            if (shieldPivot) shieldPivot.localScale = new Vector3(facing, 1f, 1f);
            if (ShieldCollider) ShieldCollider.enabled = ParryActive;
        }

        public bool CanParryAt(Vector2 ballCenter, float ballRadius, Vector2 incomingDirection)
        {
            if (!ParryActive || !ShieldCollider || !ShieldCollider.enabled || ballRadius < 0f) return false;
            Vector2 delta = ballCenter - Position;
            float front = delta.x * facing;
            // Contact position decides the side, including upper diagonals. Only the visibly
            // covered crown (within .08 horizontally of the centreline) permits an overhead tie.
            if (front < -ContactTolerance) return false;
            if (front < 0f && delta.y < .64f) return false;
            if (incomingDirection.sqrMagnitude > .00001f && Vector2.Dot(incomingDirection, delta) > .001f) return false;
            Vector2 nearest = ShieldCollider.ClosestPoint(ballCenter);
            return (nearest - ballCenter).sqrMagnitude <= Mathf.Pow(ballRadius + ContactTolerance, 2f);
        }
        public void NotifyParry(Vector2 contact) { if (View) View.FlashParry(contact); }
    }
}
