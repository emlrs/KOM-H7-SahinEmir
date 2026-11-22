using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CombinedD
{
    /// <summary>A constant-speed circle. Real shape casts find every contact before motion is committed.</summary>
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(Rigidbody2D), typeof(CircleCollider2D))]
    public sealed class DOrbMotor : MonoBehaviour
    {
        public DPlayerMotor Player;
        public DGameController Game;
        public DEffectsController Effects;
        public Rigidbody2D Body;
        public CircleCollider2D CollisionShape;
        public LayerMask CollisionMask = (1 << 9) | (1 << 10) | (1 << 11) | (1 << 12) | (1 << 13);
        public bool ManualSimulation;
        public float Speed = 8f, MinimumAngle = 22f, MaximumAngle = 62f;
        public bool DirectedParry = true;
        public bool Active { get; private set; }
        public Vector2 Position => Body ? Body.position : (Vector2)transform.position;
        public Vector2 Velocity => Active ? direction * Speed : Vector2.zero;
        public float Radius => CollisionShape ? CollisionShape.radius
            * Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y)) : .28f;
        public int ParryCount { get; private set; }
        public int WallBounceCount { get; private set; }
        public int SolverLimitCount { get; private set; }
        public Vector2 LastParryDirection { get; private set; }
        private Vector2 direction = new Vector2(-.85f, -.52f).normalized;
        private System.Random random = new System.Random();
        private readonly RaycastHit2D[] hits = new RaycastHit2D[64];
        private readonly List<RaycastHit2D> solids = new List<RaycastHit2D>(4);
        private readonly HashSet<DPanel> touchingPanels = new HashSet<DPanel>();
        private readonly List<DPanel> releasedPanels = new List<DPanel>();
        private readonly HashSet<DReceiver> rejectedReceivers = new HashSet<DReceiver>();
        private int contactToken, lastParrySerial = -1;
        private const float TieDistance = .001f, Separation = .002f;

        private void Awake() => EnsureBody();
        private void FixedUpdate() { if (!ManualSimulation) Step(Time.fixedDeltaTime); }
        private void EnsureBody()
        {
            if (!Body) Body = GetComponent<Rigidbody2D>();
            if (!CollisionShape) CollisionShape = GetComponent<CircleCollider2D>();
            Body.bodyType = RigidbodyType2D.Kinematic;
            Body.gravityScale = 0; Body.freezeRotation = true;
            Body.linearVelocity = Vector2.zero; Body.angularVelocity = 0;
            CollisionShape.isTrigger = false;
        }

        public void ResetOrb(Vector2 position, Vector2 initialDirection)
        {
            EnsureBody(); SetPosition(position);
            direction = initialDirection.sqrMagnitude > .000001f ? initialDirection.normalized : new Vector2(-.85f, -.52f).normalized;
            Active = true; ParryCount = WallBounceCount = SolverLimitCount = contactToken = 0;
            lastParrySerial = -1; LastParryDirection = Vector2.zero;
            touchingPanels.Clear(); rejectedReceivers.Clear();
            CollisionShape.enabled = true;
            foreach (var renderer in GetComponentsInChildren<Renderer>(true)) renderer.enabled = true;
            foreach (var trail in GetComponentsInChildren<TrailRenderer>(true)) { trail.Clear(); trail.emitting = true; }
        }
        public void SetRandomSeed(int seed) => random = new System.Random(seed);
        public void Freeze()
        {
            Active = false; if (Body) Body.linearVelocity = Vector2.zero;
            foreach (var trail in GetComponentsInChildren<TrailRenderer>(true)) trail.emitting = false;
        }
        public void Absorb()
        {
            Freeze(); if (CollisionShape) CollisionShape.enabled = false;
            foreach (var renderer in GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            foreach (var trail in GetComponentsInChildren<TrailRenderer>(true)) trail.Clear();
        }

        // Conservative radial closing time: no precise prediction or knowledge of future input.
        // A near, approaching orb cancels a laser even if its present straight line could narrowly miss.
        public float ThreatTimeToPlayer()
        {
            if (!Active || !Player || !Player.Alive) return float.PositiveInfinity;
            Vector2 delta = Player.Position - Position;
            float distance = delta.magnitude;
            if (distance < .001f) return 0;
            float closing = Vector2.Dot(Velocity - Player.Velocity, delta / distance);
            if (closing <= .001f) return float.PositiveInfinity;
            float playerReach = Player.BodyCollider ? Player.BodyCollider.bounds.extents.magnitude : .9f;
            return Mathf.Max(0, distance - Radius - playerReach) / closing;
        }

        public void Step(float dt)
        {
            if (!Active || dt <= 0 || float.IsNaN(dt) || float.IsInfinity(dt)) return;
            EnsureBody(); Physics2D.SyncTransforms(); rejectedReceivers.Clear();
            // Short slices bound contact work without changing the speed or accumulating a clock bonus.
            while (Active && dt > .000001f)
            {
                float slice = Mathf.Min(dt, .02f);
                Sweep(Speed * slice); dt -= slice;
            }
        }

        private ContactFilter2D Filter()
        {
            var filter = new ContactFilter2D { useTriggers = true };
            filter.SetLayerMask(CollisionMask);
            return filter;
        }

        private void Sweep(float remaining)
        {
            int iterations = 0;
            while (Active && remaining > .000001f && iterations++ < 24)
            {
                ReleasePanels();
                Vector2 start = Position;
                // The backend cast and ClosestPoint use slightly different polygon skins.
                // Look beyond this step to discover a late-reported hit, then refine it and
                // keep it only if the REAL contact lies inside the original distance budget.
                float queryLookahead = Mathf.Max(.06f, Physics2D.defaultContactOffset * 6);
                int count = gameObject.scene.GetPhysicsScene2D().CircleCast(start, Radius, direction,
                    remaining + queryLookahead, Filter(), hits);
                float nearest = float.PositiveInfinity;
                RaycastHit2D first = default;
                bool parry = false, bodyHit = false;
                DReceiver receiver = null;
                solids.Clear();
                for (int i = 0; i < count; i++)
                {
                    RaycastHit2D hit = hits[i]; Collider2D shape = hit.collider;
                    if (!Usable(shape)) continue;
                    hit.distance = RefineDistance(shape, start, direction, hit.distance);
                    if (hit.distance > remaining) continue;
                    bool body = Player && shape == Player.BodyCollider;
                    bool shield = Player && shape == Player.ShieldCollider;
                    bool validParry = (body || shield) && Player.Alive && Player.ParrySerial != lastParrySerial
                        && Player.CanParryAt(start + direction * hit.distance, Radius, direction);
                    if (shield && !validParry) continue; // A held guard never protects from this orb.
                    DReceiver candidate = shape.isTrigger ? shape.GetComponentInParent<DReceiver>() : null;
                    if (shape.isTrigger && (!candidate || rejectedReceivers.Contains(candidate))) continue;
                    bool solid = !body && !shield && !shape.isTrigger;
                    if (solid && Vector2.Dot(direction, Normal(shape, start + direction * hit.distance, hit.normal)) >= -.00001f) continue;
                    if (hit.distance < nearest - TieDistance)
                    {
                        nearest = hit.distance; first = hit; parry = validParry; bodyHit = body;
                        receiver = candidate; solids.Clear();
                    }
                    else if (Mathf.Abs(hit.distance - nearest) > TieDistance) continue;
                    else
                    {
                        // Equal-time shield contact takes priority over body death.
                        if (validParry) { parry = true; first = hit; }
                        bodyHit |= body;
                        if (!receiver) receiver = candidate;
                    }
                    if (solid) solids.Add(hit);
                }
                if (float.IsPositiveInfinity(nearest))
                { SetPosition(start + direction * remaining); ReleasePanels(); return; }
                SetPosition(start + direction * Mathf.Max(0, nearest));
                remaining = Mathf.Max(0, remaining - nearest);
                if (parry) { ResolveParry(first); continue; }
                if (bodyHit)
                {
                    if (Game) Game.Die(); else if (Player) Player.Kill();
                    Freeze(); return;
                }
                if (receiver)
                {
                    if (receiver.TryAccept(this)) return;
                    rejectedReceivers.Add(receiver); continue;
                }
                foreach (var hit in solids)
                {
                    if (!Usable(hit.collider)) continue;
                    Vector2 normal = Normal(hit.collider, Position, hit.normal);
                    if (Vector2.Dot(direction, normal) >= -.00001f) continue;
                    direction = Vector2.Reflect(direction, normal).normalized;
                    WallBounceCount++;
                    var panel = hit.collider.GetComponentInParent<DPanel>();
                    if (panel && touchingPanels.Add(panel)) panel.ApplyImpact(++contactToken);
                    if (Effects) Effects.PlayImpact(hit.point, panel ? new Color(1, .65f, .25f) : new Color(.35f, .85f, 1));
                }
                // No wall nudge: reflection spends exactly the remaining world-distance budget.
            }
            if (Active && remaining > .000001f) SolverLimitCount++;
        }

        private bool Usable(Collider2D shape) => shape && shape.enabled && shape.gameObject.activeInHierarchy
            && shape != CollisionShape && shape.attachedRigidbody != Body;
        private float RefineDistance(Collider2D shape, Vector2 start, Vector2 heading, float distance)
        {
            // Unity's circle cast can report contact beyond the real polygon-skin boundary.
            // Find the actual radius contact BEFORE moving, so no wall nudge adds travel distance.
            Vector2 end = start + heading * distance;
            if (distance <= 0 || Vector2.Distance(end, shape.ClosestPoint(end)) >= Radius) return distance;
            if (Vector2.Distance(start, shape.ClosestPoint(start)) <= Radius) return 0;
            float clear = 0, touching = distance;
            for (int pass = 0; pass < 14; pass++)
            {
                float middle = (clear + touching) * .5f;
                Vector2 point = start + heading * middle;
                if (Vector2.Distance(point, shape.ClosestPoint(point)) >= Radius) clear = middle;
                else touching = middle;
            }
            return clear;
        }
        private Vector2 Normal(Collider2D shape, Vector2 center, Vector2 fallback)
        {
            Vector2 outward = center - shape.ClosestPoint(center);
            return outward.sqrMagnitude > .00000001f ? outward.normalized
                : fallback.sqrMagnitude > .000001f ? fallback.normalized : -direction;
        }

        private void ResolveParry(RaycastHit2D hit)
        {
            Vector2 incoming = direction;
            Vector2 normal = Normal(hit.collider, Position, hit.normal);
            lastParrySerial = Player.ParrySerial;
            // A moving player can start a step slightly inside the circle. Correct only that
            // geometric overlap, sweep the correction against solids, and grant no immunity time.
            for (int pass = 0; pass < 4; pass++)
            {
                Separate(Player.ShieldCollider, incoming);
                Separate(Player.BodyCollider, incoming);
                if (ClearOfPlayer(Position, 0)) break;
            }
            direction = PickExit(normal, incoming);
            LastParryDirection = direction; ParryCount++;
            Player.NotifyParry(hit.point);
            if (Game) Game.OnOrbParry(hit.point);
            if (Effects) Effects.PlayImpact(hit.point, new Color(.6f, 1, .85f));
        }

        private Vector2 PickExit(Vector2 normal, Vector2 incoming)
        {
            if (!DirectedParry) return Vector2.Reflect(incoming, normal).normalized;
            if (TryPickExit(normal, out Vector2 exit)) return exit;
            // A rising player can catch a crown parry even when the ball is faster horizontally.
            // Move only far enough toward the visible front to clear that translated shape.
            // This bounded correction is swept against walls; it cannot tunnel or grant immunity.
            int facing = Player.Facing < 0 ? -1 : 1;
            float front = (Position.x - Player.Position.x) * facing;
            float reach = Player.ShieldCollider ? Player.ShieldCollider.bounds.extents.x * 2 : .8f;
            float lead = Mathf.Clamp(reach + Radius + .06f - front, 0, 1.1f);
            MoveEgress(new Vector2(facing, 0), lead, true);
            if (TryPickExit(normal, out exit)) return exit;
            float fallback = Mathf.Clamp(MaximumAngle, MinimumAngle, 80) * Mathf.Deg2Rad;
            return new Vector2(facing * Mathf.Cos(fallback), (normal.y < -.1f ? -1 : 1) * Mathf.Sin(fallback));
        }

        private bool TryPickExit(Vector2 normal, out Vector2 exit)
        {
            float min = Mathf.Clamp(MinimumAngle, 1, 80), max = Mathf.Clamp(MaximumAngle, min, 80);
            int facing = Player.Facing < 0 ? -1 : 1;
            for (int attempt = 0; attempt < 66; attempt++)
            {
                float angle = (attempt < 64 ? Mathf.Lerp(min, max, (float)random.NextDouble()) : min) * Mathf.Deg2Rad;
                float vertical = attempt < 64 ? (random.Next(2) == 0 ? -1 : 1) : (attempt == 64 ? 1 : -1);
                Vector2 candidate = new Vector2(facing * Mathf.Cos(angle), vertical * Mathf.Sin(angle));
                float forwardSpeed = Player.Velocity.x * facing;
                if (forwardSpeed > .1f && candidate.x * facing * Speed < forwardSpeed + .15f) continue;
                if (Vector2.Dot(candidate, normal) > .001f && ClearOfPlayer(Position + candidate * .02f, 0)
                    && ExitClearsMovingPlayer(candidate)) { exit = candidate; return true; }
            }
            exit = Vector2.zero; return false;
        }

        private bool ExitClearsMovingPlayer(Vector2 candidate)
        {
            // Relative motion turns a linearly translated capsule into one ordinary real-shape cast.
            Vector2 relative = candidate * Speed - Player.Velocity;
            float speed = relative.magnitude;
            if (speed < .001f) return false;
            int count = gameObject.scene.GetPhysicsScene2D().CircleCast(Position, Radius, relative / speed,
                speed * .20f, Filter(), hits);
            for (int i = 0; i < count; i++)
            {
                Collider2D shape = hits[i].collider;
                if (shape != Player.BodyCollider && shape != Player.ShieldCollider) continue;
                if (hits[i].distance > .001f || Vector2.Dot(relative, Normal(shape, Position, hits[i].normal)) < -.001f) return false;
            }
            return true;
        }

        private void Separate(Collider2D shape, Vector2 incoming)
        {
            if (!shape || !shape.enabled) return;
            Vector2 offset = Position - shape.ClosestPoint(Position);
            float gap = offset.magnitude;
            if (gap >= Radius) return;
            Vector2 outward = gap > .000001f ? offset / gap : -incoming;
            MoveEgress(outward, Radius + Separation - gap, false);
        }

        private void MoveEgress(Vector2 outward, float distance, bool checkPlayer)
        {
            if (distance <= 0) return;
            int count = gameObject.scene.GetPhysicsScene2D().CircleCast(Position, Radius, outward, distance, Filter(), hits);
            for (int i = 0; i < count; i++)
            {
                Collider2D other = hits[i].collider;
                if (!Usable(other) || other.isTrigger) continue;
                if (!checkPlayer && (other == Player.BodyCollider || other == Player.ShieldCollider)) continue;
                if (Vector2.Dot(outward, Normal(other, Position, hits[i].normal)) < 0)
                    distance = Mathf.Min(distance, Mathf.Max(0, RefineDistance(other, Position, outward, hits[i].distance)));
            }
            SetPosition(Position + outward * distance);
        }

        private bool ClearOfPlayer(Vector2 center, float extra) => ClearOf(Player.ShieldCollider, center, extra)
            && ClearOf(Player.BodyCollider, center, extra);
        private bool ClearOf(Collider2D shape, Vector2 center, float extra) => !shape || !shape.enabled
            || (center - shape.ClosestPoint(center)).sqrMagnitude >= (Radius + extra) * (Radius + extra);
        private void ReleasePanels()
        {
            releasedPanels.Clear();
            foreach (var panel in touchingPanels)
                if (!panel || panel.Broken || !panel.Solid || !panel.Solid.enabled
                    || Vector2.Distance(Position, panel.Solid.ClosestPoint(Position)) > Radius + .025f) releasedPanels.Add(panel);
            foreach (var panel in releasedPanels) touchingPanels.Remove(panel);
        }
        private void SetPosition(Vector2 position)
        {
            if (Body) Body.position = position;
            transform.position = new Vector3(position.x, position.y, transform.position.z);
            Physics2D.SyncTransforms();
        }
    }
}
