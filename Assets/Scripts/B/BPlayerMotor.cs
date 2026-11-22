using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LavaRiseB
{
    /// <summary>Dynamic, frictionless capsule. Input is sampled in Update; all movement is fixed-step.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D), typeof(CapsuleCollider2D))]
    public sealed class BPlayerMotor : MonoBehaviour
    {
        [Header("E ile değişen hareket profilleri")]
        public BMovementProfile[] Profiles = BMovementProfile.Defaults();
        [Header("Ortak zıplama / saniye ve Unity birimi")]
        [Min(1f)] public float JumpSpeed = 12.5f;
        [Min(1f)] public float Gravity = 28f;
        [Min(.01f)] public float MaximumHoldTime = .26f;
        [Min(1f)] public float ReleasedRiseGravity = 1.5f;
        [Min(1f)] public float FallGravity = 1.3f;
        [Min(0f)] public float ReleaseSpeedCap = 5.3f;
        [Min(.01f)] public float MaxFallSpeed = 22f;
        [Min(0f)] public float CoyoteTime = .12f;
        [Min(0f)] public float JumpBufferTime = .14f;
        [Header("Zemin; trigger ve duvarlar zemin sayılmaz")]
        public LayerMask GroundLayers = ~0;
        [Range(.02f, .2f)] public float GroundProbeDistance = .075f;
        [Range(.4f, .95f)] public float MinimumGroundNormal = .68f;
        [SerializeField] private PhysicsMaterial2D slipperyMaterial;
        [SerializeField] private int profileIndex;
        [SerializeField] private bool inputEnabled = true;

        private Rigidbody2D body;
        private CapsuleCollider2D capsule;
        private readonly RaycastHit2D[] groundHits = new RaycastHit2D[12];
        private bool externalInput, alive = true, grounded, jumpArmed = true;
        private bool pressQueued, releaseQueued, switchQueued, jumpHeld;
        private float move, coyoteRemaining, bufferRemaining, holdAge, ungroundRemaining;
        private int jumpCount;
        private static PhysicsMaterial2D runtimeMaterial;

        public bool InputEnabled
        {
            get => inputEnabled;
            set { inputEnabled = value; if (!value) ClearInput(); }
        }
        public bool UseExternalInput
        {
            get => externalInput;
            set { externalInput = value; ClearInput(); }
        }
        public Vector2 Velocity => body ? body.linearVelocity : Vector2.zero;
        public bool Grounded => grounded;
        public int ProfileIndex => profileIndex;
        public int JumpCount => jumpCount;
        public bool IsAlive => alive;
        public BMovementProfile CurrentProfile => Profiles[Mathf.Clamp(profileIndex, 0, Profiles.Length - 1)];
        public Rigidbody2D Body => body;
        /// <summary>Explicit verification hook. Normal gameplay leaves this false.</summary>
        public bool ManualSimulation { get; set; }
        public event Action Jumped, Landed, Died;
        public event Action<int> ProfileChanged;

        public static BPlayerMotor BuildPlayer(Vector2 position)
        {
            var root = new GameObject("B Player • frictionless capsule");
            root.transform.position = position;
            var motor = root.AddComponent<BPlayerMotor>();
            motor.ConfigureBody();
            BCharacterView.Build(root.transform).Bind(motor);
            return motor;
        }

        private void Awake() => ConfigureBody();
        private void Reset() => ConfigureBody();
        private void OnValidate()
        {
            if (Profiles == null || Profiles.Length < 2) Profiles = BMovementProfile.Defaults();
            for (int i = 0; i < Profiles.Length; i++) if (Profiles[i] == null) Profiles[i] = new BMovementProfile();
        }
        private void ConfigureBody()
        {
            if (Profiles == null || Profiles.Length < 2) Profiles = BMovementProfile.Defaults();
            body = GetComponent<Rigidbody2D>();
            capsule = GetComponent<CapsuleCollider2D>();
            body.bodyType = RigidbodyType2D.Dynamic;
            body.gravityScale = 0f;
            body.linearDamping = 0f; body.angularDamping = 0f;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            capsule.direction = CapsuleDirection2D.Vertical;
            capsule.size = new Vector2(.64f, 1.42f); capsule.offset = Vector2.zero;
            capsule.isTrigger = false;
            if (!slipperyMaterial)
            {
#if UNITY_EDITOR
                if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/Art/B"))
                {
                    if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/Art")) UnityEditor.AssetDatabase.CreateFolder("Assets", "Art");
                    UnityEditor.AssetDatabase.CreateFolder("Assets/Art", "B");
                }
                slipperyMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>("Assets/Art/B/Slippery.physicsMaterial2D");
                if (!slipperyMaterial && !Application.isPlaying)
                {
                    slipperyMaterial = new PhysicsMaterial2D("B Slippery • friction zero") { friction = 0f, bounciness = 0f };
                    UnityEditor.AssetDatabase.CreateAsset(slipperyMaterial, "Assets/Art/B/Slippery.physicsMaterial2D");
                }
#endif
                if (!slipperyMaterial)
                {
                    if (!runtimeMaterial) runtimeMaterial = new PhysicsMaterial2D("B runtime friction zero")
                        { friction = 0f, bounciness = 0f, hideFlags = HideFlags.DontSave };
                    slipperyMaterial = runtimeMaterial;
                }
            }
            capsule.sharedMaterial = slipperyMaterial;
        }

        private void Update()
        {
            if (externalInput || !alive) return;
            var keyboard = Keyboard.current;
            if (keyboard == null) { ClearInput(); return; }
            float axis = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1f : 0f)
                - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1f : 0f);
            SetInput(new BInputFrame(axis, keyboard.spaceKey.wasPressedThisFrame, keyboard.spaceKey.isPressed,
                keyboard.spaceKey.wasReleasedThisFrame, keyboard.eKey.wasPressedThisFrame));
        }

        public void SetInput(BInputFrame frame)
        {
            if (!inputEnabled || !alive) { ClearInput(); return; }
            move = frame.Move; jumpHeld = frame.JumpHeld;
            if (frame.JumpPressed && jumpArmed) { pressQueued = true; jumpArmed = false; }
            if (frame.JumpReleased) releaseQueued = true;
            if (frame.JumpReleased || (!frame.JumpHeld && !frame.JumpPressed)) jumpArmed = true;
            switchQueued |= frame.SwitchPressed;
        }

        private void FixedUpdate()
        {
            if (!ManualSimulation) ManualFixedStep(Time.fixedDeltaTime);
        }

        /// <summary>Applies the real motor step; callers must separately simulate Physics2D.</summary>
        public void ManualFixedStep(float dt)
        {
            if (!alive || !body.simulated) return;
            dt = Mathf.Clamp(dt, .0001f, .05f);
            Vector2 velocity = body.linearVelocity;
            ungroundRemaining = Mathf.Max(0f, ungroundRemaining - dt);
            bool wasGrounded = grounded;
            grounded = ungroundRemaining <= 0f && velocity.y <= .5f && ProbeGround();
            if (grounded)
            {
                coyoteRemaining = CoyoteTime;
                if (!wasGrounded) Landed?.Invoke();
            }
            else coyoteRemaining = Mathf.Max(0f, coyoteRemaining - dt);
            bufferRemaining = Mathf.Max(0f, bufferRemaining - dt);
            if (pressQueued) bufferRemaining = JumpBufferTime;
            if (switchQueued)
            {
                profileIndex = (profileIndex + 1) % Profiles.Length;
                ProfileChanged?.Invoke(profileIndex);
            }

            BMovementProfile profile = CurrentProfile;
            float target = inputEnabled ? move * profile.MaxSpeed : 0f;
            bool counterSteering = Mathf.Abs(target) > .01f && Mathf.Abs(velocity.x) > .01f && Mathf.Sign(target) != Mathf.Sign(velocity.x);
            float rate = counterSteering ? profile.CounterSteer
                : Mathf.Abs(target) < .01f || Mathf.Abs(velocity.x) > Mathf.Abs(target) ? profile.Braking : profile.Acceleration;
            if (!grounded) rate *= profile.AirControl;
            velocity.x = Mathf.MoveTowards(velocity.x, target, rate * dt);

            if (inputEnabled && bufferRemaining > 0f && coyoteRemaining > 0f)
            {
                velocity.y = JumpSpeed;
                grounded = false; coyoteRemaining = bufferRemaining = 0f;
                ungroundRemaining = .1f; holdAge = 0f;
                jumpCount++; Jumped?.Invoke();
            }
            holdAge += dt;
            if ((releaseQueued || !jumpHeld) && velocity.y > ReleaseSpeedCap)
                velocity.y = ReleaseSpeedCap;
            float gravityMultiplier = velocity.y > 0f
                ? (jumpHeld && holdAge <= MaximumHoldTime ? 1f : ReleasedRiseGravity)
                : FallGravity;
            velocity.y = Mathf.Max(velocity.y - Gravity * gravityMultiplier * dt, -MaxFallSpeed);
            body.linearVelocity = velocity;
            pressQueued = releaseQueued = switchQueued = false;
        }

        private bool ProbeGround()
        {
            var filter = new ContactFilter2D { useTriggers = false, useLayerMask = true, layerMask = GroundLayers };
            int count = capsule.Cast(Vector2.down, filter, groundHits, GroundProbeDistance, true);
            for (int i = 0; i < count; i++)
            {
                RaycastHit2D hit = groundHits[i];
                // A side/ceiling overlap must never refill coyote time, even if a cast reports a start-overlap normal.
                if (hit.collider && hit.collider != capsule && hit.normal.y >= MinimumGroundNormal
                    && hit.point.y <= capsule.bounds.min.y + .16f) return true;
            }
            return false;
        }

        public void ResetAt(Vector2 position, int profile = 0)
        {
            if (!body) ConfigureBody();
            alive = true; inputEnabled = true;
            body.simulated = true; capsule.enabled = true;
            body.position = position; transform.position = new Vector3(position.x, position.y, 0f);
            body.linearVelocity = Vector2.zero; body.angularVelocity = 0f;
            grounded = false; jumpCount = 0;
            coyoteRemaining = bufferRemaining = holdAge = ungroundRemaining = 0f;
            profileIndex = Mathf.Clamp(profile, 0, Profiles.Length - 1);
            ClearInput();
            ProfileChanged?.Invoke(profileIndex);
        }

        public void Kill()
        {
            if (!alive) return;
            alive = false; inputEnabled = false; grounded = false;
            ClearInput(); body.linearVelocity = Vector2.zero; body.simulated = false;
            Died?.Invoke();
        }

        private void ClearInput()
        {
            move = 0f; jumpHeld = false;
            pressQueued = releaseQueued = switchQueued = false;
            bufferRemaining = 0f; jumpArmed = true;
        }
    }
}
