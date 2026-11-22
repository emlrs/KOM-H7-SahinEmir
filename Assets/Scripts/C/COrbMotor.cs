using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CollisionC
{
    /// <summary>
    /// A kinematic circle with an explicit swept-contact solver. Every contact comes
    /// from Unity's real Collider2D shapes (CircleCast / ClosestPoint), not arena math.
    /// Solids reflect; receiver triggers pass through unless they accept the orb.
    /// Rigidbody velocity stays zero so the native solver cannot move the orb twice.
    /// </summary>
    [DefaultExecutionOrder(40)]
    [RequireComponent(typeof(Rigidbody2D), typeof(CircleCollider2D))]
    public sealed class COrbMotor : MonoBehaviour
    {
        public CPlayerMotor Player;
        public CGameController Game;
        public COrbSettings Settings;
        public Rigidbody2D Body;
        public CircleCollider2D CollisionShape;
        [Tooltip("Only editor verification uses manual stepping.")]
        public bool ManualSimulation;

        public bool Active { get; private set; }
        public bool ReceiverCharged { get; private set; }
        public Vector2 Position => Body != null ? Body.position : (Vector2)transform.position;
        public Vector2 Velocity => Active ? direction * CurrentSpeed : Vector2.zero;
        public int ParryCount { get; private set; }
        public int WallBounceCount { get; private set; }
        public int SolverLimitCount { get; private set; }
        public float ActiveElapsed { get; private set; }
        public Vector2 LastParryDirection { get; private set; }
        public Vector2 LastParryPosition { get; private set; }
        public float Radius => CollisionShape != null
            ? CollisionShape.radius * Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y)) : 0.2f;
        public float CurrentSpeed => Mathf.Min(Config.MaximumSpeed,
            Config.InitialSpeed + Config.AccelerationPerSecond * ActiveElapsed);

        private Vector2 direction = new Vector2(-0.86f, -0.5f).normalized;
        private readonly RaycastHit2D[] castHits = new RaycastHit2D[128];
        private readonly HashSet<CPlate> plateEpisodes = new HashSet<CPlate>();
        private readonly List<CPlate> releasedPlates = new List<CPlate>();
        private readonly List<RaycastHit2D> simultaneousSolids = new List<RaycastHit2D>(8);
        private readonly HashSet<CReceiver> rejectedReceivers = new HashSet<CReceiver>();
        private System.Random random;
        private COrbSettings fallbackSettings;
        private int contactToken;
        private int lastParrySerial = -1;
        private TrailRenderer trail;
        private const float ContactTie = 0.006f;

        private COrbSettings Config
        {
            get
            {
                if (Settings != null) return Settings;
                if (fallbackSettings == null)
                {
                    fallbackSettings = ScriptableObject.CreateInstance<COrbSettings>();
                    fallbackSettings.hideFlags = HideFlags.HideAndDontSave;
                }
                return fallbackSettings;
            }
        }

        private void Awake()
        {
            EnsureComponents();
            random = new System.Random(unchecked(Environment.TickCount ^ Guid.NewGuid().GetHashCode()));
        }

        private void EnsureComponents()
        {
            if (Body == null) Body = GetComponent<Rigidbody2D>();
            if (CollisionShape == null) CollisionShape = GetComponent<CircleCollider2D>();
            if (Body != null)
            {
                Body.bodyType = RigidbodyType2D.Kinematic;
                Body.gravityScale = 0f;
                Body.freezeRotation = true;
                Body.useFullKinematicContacts = true;
                Body.linearVelocity = Vector2.zero;
                Body.angularVelocity = 0f;
            }
            if (CollisionShape != null) CollisionShape.isTrigger = false;
            if (trail == null) trail = GetComponentInChildren<TrailRenderer>(true);
        }

        private void FixedUpdate()
        {
            if (!ManualSimulation) Step(Time.fixedDeltaTime);
        }

        public void ResetOrb(Vector2 position, Vector2 initialDirection)
        {
            EnsureComponents();
            SetPosition(position);
            direction = initialDirection.sqrMagnitude > 0.0001f ? initialDirection.normalized : new Vector2(-0.86f, -0.5f).normalized;
            ActiveElapsed = 0f;
            ParryCount = 0;
            WallBounceCount = 0;
            SolverLimitCount = 0;
            lastParrySerial = -1;
            LastParryDirection = Vector2.zero;
            LastParryPosition = position;
            ReceiverCharged = false;
            plateEpisodes.Clear();
            rejectedReceivers.Clear();
            Active = true;
            if (CollisionShape != null) CollisionShape.enabled = true;
            foreach (var renderer in GetComponentsInChildren<Renderer>(true)) renderer.enabled = true;
            if (trail != null) { trail.Clear(); trail.emitting = true; }
            Physics2D.SyncTransforms();
        }

        public void Freeze()
        {
            Active = false;
            if (Body != null) Body.linearVelocity = Vector2.zero;
            if (trail != null) trail.emitting = false;
        }

        public void Absorb()
        {
            Freeze();
            if (CollisionShape != null) CollisionShape.enabled = false;
            foreach (var renderer in GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            if (trail != null) trail.Clear();
        }

        // Test hook is a method, not a serialized override in a saved scene.
        public void SetRandomSeed(int seed) => random = new System.Random(seed);

        public void Step(float dt)
        {
            if (!Active || dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt)) return;
            EnsureComponents();
            Physics2D.SyncTransforms();
            rejectedReceivers.Clear();
            float remainingTime = dt;
            while (Active && remainingTime > 0.000001f)
            {
                float slice = Mathf.Min(remainingTime, Mathf.Clamp(Config.MaximumTimeSlice, 0.005f, 0.05f));
                float distance = TravelIntegral(ActiveElapsed + slice) - TravelIntegral(ActiveElapsed);
                ActiveElapsed += slice;
                SweepDistance(distance);
                remainingTime -= slice;
            }
        }

        // Integrating the capped linear speed makes travel independent of frame rate.
        // This clock advances ONLY while the orb is active; a freeze stops acceleration.
        private float TravelIntegral(float seconds)
        {
            float start = Mathf.Min(Config.InitialSpeed, Config.MaximumSpeed);
            float acceleration = Mathf.Max(0f, Config.AccelerationPerSecond);
            if (acceleration < 0.000001f) return start * seconds;
            float toCap = Mathf.Max(0f, (Config.MaximumSpeed - start) / acceleration);
            float accelerating = Mathf.Min(seconds, toCap);
            return start * accelerating + 0.5f * acceleration * accelerating * accelerating
                + Mathf.Max(0f, seconds - toCap) * Config.MaximumSpeed;
        }

        private ContactFilter2D QueryFilter()
        {
            var filter = new ContactFilter2D();
            filter.SetLayerMask(Config.CollisionMask);
            filter.useTriggers = true;
            return filter;
        }

        private void SweepDistance(float remaining)
        {
            int iterations = 0;
            while (Active && remaining > 0.000001f && iterations++ < Mathf.Clamp(Config.MaximumContactsPerSlice, 8, 64))
            {
                ReleaseSeparatedPlateEpisodes();
                Vector2 start = Position;
                int count = gameObject.scene.GetPhysicsScene2D().CircleCast(start, Radius, direction, remaining, QueryFilter(), castHits);
                float nearest = float.PositiveInfinity;
                RaycastHit2D nearestHit = default;
                bool parry = false;
                bool bodyHit = false;
                CReceiver receiver = null;
                simultaneousSolids.Clear();

                for (int i = 0; i < count; i++)
                {
                    var hit = castHits[i];
                    var collider = hit.collider;
                    if (!Usable(collider)) continue;
                    bool isBody = Player != null && collider == Player.BodyCollider;
                    bool isShield = Player != null && collider == Player.ShieldCollider;
                    bool canParry = (isBody || isShield) && CanParry(start + direction * hit.distance);
                    if (isShield && !canParry) continue; // Wrong side/window never creates an invisible wall.
                    CReceiver candidateReceiver = collider.isTrigger ? collider.GetComponentInParent<CReceiver>() : null;
                    if (collider.isTrigger && (candidateReceiver == null || !ReceiverCharged || rejectedReceivers.Contains(candidateReceiver))) continue;
                    if (!isBody && !isShield && !collider.isTrigger)
                    {
                        Vector2 normal = ContactNormal(collider, start + direction * hit.distance, hit.normal);
                        if (Vector2.Dot(direction, normal) >= -0.00001f) continue;
                    }
                    if (hit.distance < nearest - ContactTie)
                    {
                        nearest = hit.distance;
                        nearestHit = hit;
                        parry = canParry;
                        bodyHit = isBody;
                        receiver = candidateReceiver;
                        simultaneousSolids.Clear();
                    }
                    else if (Mathf.Abs(hit.distance - nearest) > ContactTie) continue;
                    else
                    {
                        // A valid physical shield contact wins over a body hit in the same sweep.
                        if (canParry) { parry = true; nearestHit = hit; }
                        bodyHit |= isBody;
                        if (receiver == null) receiver = candidateReceiver;
                    }
                    if (!isBody && !isShield && !collider.isTrigger) simultaneousSolids.Add(hit);
                }

                if (float.IsPositiveInfinity(nearest))
                {
                    SetPosition(start + direction * remaining);
                    remaining = 0f;
                    ReleaseSeparatedPlateEpisodes();
                    break;
                }

                // Move to the actual shape-query contact before invoking location-checked gameplay.
                SetPosition(start + direction * Mathf.Max(0f, nearest));
                remaining = Mathf.Max(0f, remaining - nearest);
                ReleaseSeparatedPlateEpisodes();
                if (parry)
                {
                    ResolveParry(nearestHit);
                    continue;
                }
                if (bodyHit)
                {
                    if (Game != null) Game.Die();
                    else if (Player != null) Player.Kill();
                    Freeze();
                    break;
                }
                if (receiver != null)
                {
                    if (receiver.TryAccept(this)) break;
                    rejectedReceivers.Add(receiver);
                    continue;
                }

                foreach (var hit in simultaneousSolids)
                {
                    if (!Usable(hit.collider)) continue;
                    Vector2 normal = ContactNormal(hit.collider, Position, hit.normal);
                    if (Vector2.Dot(direction, normal) < -0.00001f)
                    {
                        direction = Vector2.Reflect(direction, normal).normalized;
                        WallBounceCount++;
                        var plate = hit.collider.GetComponentInParent<CPlate>();
                        if (plate != null && !plateEpisodes.Contains(plate))
                        {
                            plateEpisodes.Add(plate);
                            plate.ApplyImpact(++contactToken);
                        }
                        Impact(hit.point, plate != null ? new Color(1f, 0.68f, 0.26f) : new Color(0.3f, 0.85f, 1f));
                    }
                    SetPosition(Position + normal * Config.ContactSkin);
                }
            }
            if (Active && remaining > 0.000001f)
            {
                // Never tunnel through geometry if an inspector-edited pathological layout exhausts
                // the contact budget. The next time slice resumes at this safe contact position.
                SolverLimitCount++;
            }
        }

        private bool Usable(Collider2D collider)
        {
            return collider != null && collider.enabled && collider.gameObject.activeInHierarchy
                && collider != CollisionShape && collider.attachedRigidbody != Body;
        }

        private bool CanParry(Vector2 center)
        {
            return Player != null && Player.Alive && Player.ParrySerial != lastParrySerial
                && Player.CanParryAt(center, Radius, direction);
        }

        private Vector2 ContactNormal(Collider2D collider, Vector2 center, Vector2 fallback)
        {
            Vector2 fromSurface = center - collider.ClosestPoint(center);
            if (fromSurface.sqrMagnitude > 0.0000001f) return fromSurface.normalized;
            return fallback.sqrMagnitude > 0.001f ? fallback.normalized : -direction;
        }

        private void ResolveParry(RaycastHit2D hit)
        {
            Vector2 incoming = direction;
            Vector2 normal = ContactNormal(hit.collider, Position, hit.normal);
            int facing = Player.Facing >= 0 ? 1 : -1;
            lastParrySerial = Player.ParrySerial;

            // A contact skin and geometric separation replace a grace/invulnerability timer.
            // Never remove body lethality or ignore a later genuine return collision.
            MoveEgressSafely(normal * Config.ContactSkin);
            SeparateFromPlayer(incoming);
            direction = PickParryDirection(facing, normal);
            LastParryDirection = direction;
            LastParryPosition = Position;
            ParryCount++;
            if (Game != null && Game.ReceiverReady) ReceiverCharged = true;
            Player.NotifyParry(hit.point);
            if (Game != null) Game.OnParry();
            Impact(hit.point, new Color(0.7f, 1f, 1f));
        }

        private Vector2 PickParryDirection(int facing, Vector2 contactNormal)
        {
            if (random == null) random = new System.Random();
            Vector2 candidate = new Vector2(facing, 1f).normalized;
            float minimum = Mathf.Clamp(Config.MinimumParryAngle, 5f, 80f);
            float maximum = Mathf.Clamp(Config.MaximumParryAngle, minimum, 80f);
            for (int attempt = 0; attempt < 64; attempt++)
            {
                float radians = Mathf.Lerp(minimum, maximum, (float)random.NextDouble()) * Mathf.Deg2Rad;
                float sign = Config.AllowDownwardParries && random.Next(2) == 0 ? -1f : 1f;
                candidate = new Vector2(facing * Mathf.Cos(radians), sign * Mathf.Sin(radians));
                // Only reject angles entering the shield/body at this exact contact.
                // The receiver, player future motion and arena walls never influence random aiming.
                if (Vector2.Dot(candidate, contactNormal) > 0.001f
                    && ClearOfPlayer(Position + candidate * 0.035f, Config.ContactSkin * 0.5f)) return candidate;
            }
            // A crown contact always has at least one outward diagonal in the facing half-plane.
            float fallbackAngle = maximum * Mathf.Deg2Rad;
            return new Vector2(facing * Mathf.Cos(fallbackAngle),
                (contactNormal.y < -0.1f && Config.AllowDownwardParries ? -1f : 1f) * Mathf.Sin(fallbackAngle));
        }

        private void SeparateFromPlayer(Vector2 incoming)
        {
            for (int pass = 0; pass < 4; pass++)
            {
                SeparateFrom(Player.ShieldCollider, incoming);
                SeparateFrom(Player.BodyCollider, incoming);
                if (ClearOfPlayer(Position, Config.ContactSkin * 0.25f)) break;
            }
        }

        private void SeparateFrom(Collider2D collider, Vector2 incoming)
        {
            if (collider == null || !collider.enabled) return;
            Vector2 offset = Position - collider.ClosestPoint(Position);
            float gap = offset.magnitude;
            if (gap >= Radius + Config.ContactSkin) return;
            Vector2 outward = gap > 0.00001f ? offset / gap : -incoming;
            MoveEgressSafely(outward * (Radius + Config.ContactSkin - gap));
        }

        private void MoveEgressSafely(Vector2 correction)
        {
            float distance = correction.magnitude;
            if (distance < 0.000001f) return;
            Vector2 heading = correction / distance;
            int count = gameObject.scene.GetPhysicsScene2D().CircleCast(Position, Radius, heading, distance, QueryFilter(), castHits);
            for (int i = 0; i < count; i++)
            {
                var collider = castHits[i].collider;
                if (!Usable(collider) || collider.isTrigger || collider == Player.BodyCollider || collider == Player.ShieldCollider) continue;
                if (Vector2.Dot(heading, ContactNormal(collider, Position, castHits[i].normal)) >= 0f) continue;
                distance = Mathf.Min(distance, Mathf.Max(0f, castHits[i].distance - Config.ContactSkin));
            }
            SetPosition(Position + heading * distance);
        }

        private bool ClearOfPlayer(Vector2 center, float extra)
        {
            return ClearOf(Player.BodyCollider, center, extra) && ClearOf(Player.ShieldCollider, center, extra);
        }

        private bool ClearOf(Collider2D collider, Vector2 center, float extra)
        {
            return collider == null || !collider.enabled
                || Vector2.Distance(center, collider.ClosestPoint(center)) >= Radius + extra;
        }

        private void ReleaseSeparatedPlateEpisodes()
        {
            releasedPlates.Clear();
            foreach (var plate in plateEpisodes)
            {
                if (plate == null || plate.Broken || plate.Solid == null || !plate.Solid.enabled
                    || Vector2.Distance(Position, plate.Solid.ClosestPoint(Position)) > Radius + Config.EpisodeSeparation)
                    releasedPlates.Add(plate);
            }
            foreach (var plate in releasedPlates) plateEpisodes.Remove(plate);
        }

        private void Impact(Vector2 position, Color tint)
        {
            if (Game != null && Game.Environment != null) Game.Environment.PlayImpact(position, tint);
        }

        private void SetPosition(Vector2 position)
        {
            if (Body != null) Body.position = position;
            transform.position = new Vector3(position.x, position.y, transform.position.z);
            Physics2D.SyncTransforms();
        }

        private void OnDestroy()
        {
            if (fallbackSettings != null) Destroy(fallbackSettings);
        }
    }
}
