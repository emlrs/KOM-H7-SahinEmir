using UnityEngine;
using UnityEngine.SceneManagement;

namespace CombinedD
{
    public sealed class DMissile : MonoBehaviour
    {
        public SpriteRenderer BodyVisual;
        public TrailRenderer Trail;
        public CircleCollider2D CollisionShape;
        public float TelegraphSeconds = .30f;
        public float LaunchDelayRemaining { get; private set; }
        public bool Active { get; private set; }
        public bool Deflected { get; private set; }
        public Vector2 Position => transform.position;
        public Vector2 Velocity => LaunchDelayRemaining > 0f && !Deflected ? Vector2.zero : velocity;
        public Vector2 FlightVelocity => velocity;

        private DAttackDirector owner;
        private Vector2 velocity;
        private Vector2 travelPosition;
        private float lifetime;
        private float radius = .16f;
        private Vector3 visualScale = Vector3.one;
        private readonly RaycastHit2D[] hits = new RaycastHit2D[8];

        public void Initialize(DAttackDirector director, Vector2 pos, Vector2 target, float speed)
        {
            owner = director;
            transform.position = travelPosition = pos;
            velocity = (target - pos).normalized * Mathf.Max(.1f, speed);
            if (velocity.sqrMagnitude < .001f) velocity = Vector2.right * speed;
            if (CollisionShape)
            {
                radius = CollisionShape.radius * Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y));
                CollisionShape.enabled = false; // The same real circular shape is swept below.
            }
            lifetime = 0f;
            LaunchDelayRemaining = Mathf.Max(0f, TelegraphSeconds);
            if (BodyVisual) visualScale = BodyVisual.transform.localScale;
            Deflected = false;
            Active = true;
            if (BodyVisual) BodyVisual.color = new Color(1f, .56f, .12f);
            if (Trail) Trail.Clear();
            FaceVelocity();
        }

        public void Step(float dt)
        {
            if (!Active || dt <= 0f) return;
            lifetime += dt;
            if (Deflected)
            {
                travelPosition += velocity * dt;
                Vector2 side = new Vector2(-velocity.y, velocity.x).normalized;
                transform.position = travelPosition + side * (Mathf.Sin(lifetime * 32f) * .14f);
                transform.Rotate(0f, 0f, 540f * dt);
                if (lifetime >= .9f) Cancel();
                return; // Deflected missiles never perform another damage query.
            }
            if (LaunchDelayRemaining > 0f)
            {
                float waiting = Mathf.Min(dt, LaunchDelayRemaining);
                LaunchDelayRemaining -= waiting;
                dt -= waiting;
                if (BodyVisual)
                {
                    BodyVisual.transform.localScale = visualScale * (1f + .28f * Mathf.Abs(Mathf.Sin(lifetime * 25f)));
                    BodyVisual.color = Color.Lerp(new Color(1f, .56f, .12f), Color.white, .5f);
                }
                if (LaunchDelayRemaining > 0f) return;
                if (BodyVisual) { BodyVisual.transform.localScale = visualScale; BodyVisual.color = new Color(1f, .56f, .12f); }
                if (Trail) Trail.Clear();
                if (dt <= 0f) return;
            }
            Vector2 step = velocity * dt;
            if (ResolvePlayerHit(step)) return;
            transform.position = travelPosition = Position + step;
            if (lifetime >= 10f || OutsideView()) Cancel();
        }

        private bool ResolvePlayerHit(Vector2 step)
        {
            DPlayerMotor player = owner ? owner.Player : null;
            if (!player || !player.Alive || !player.BodyCollider) return false;
            ContactFilter2D filter = new ContactFilter2D { useTriggers = true };
            int layers = 1 << player.BodyCollider.gameObject.layer;
            if (player.ShieldCollider) layers |= 1 << player.ShieldCollider.gameObject.layer;
            filter.SetLayerMask(layers);
            int count = gameObject.scene.GetPhysicsScene2D().CircleCast(
                Position, radius, step.normalized, step.magnitude, filter, hits);
            RaycastHit2D bodyHit = default;
            RaycastHit2D parryHit = default;
            for (int i = 0; i < count; i++)
            {
                RaycastHit2D hit = hits[i];
                if (hit.collider == player.BodyCollider && (!bodyHit.collider || hit.distance < bodyHit.distance)) bodyHit = hit;
                if (hit.collider != player.ShieldCollider && hit.collider != player.BodyCollider) continue;
                if (!player.CanParryAt(hit.centroid, radius, velocity)) continue;
                if (!parryHit.collider || hit.distance < parryHit.distance) parryHit = hit;
            }
            if (parryHit.collider && (!bodyHit.collider || parryHit.distance <= bodyHit.distance + .025f))
            {
                transform.position = travelPosition = parryHit.centroid;
                player.NotifyParry(parryHit.point);
                if (owner.Game) owner.Game.OnMissileParry();
                Deflect(player.Facing);
                return true;
            }
            if (!bodyHit.collider) return false;
            transform.position = bodyHit.centroid;
            if (owner.Game) owner.Game.Die();
            else player.Kill();
            Cancel();
            return true;
        }

        public void Deflect(int direction)
        {
            if (!Active || Deflected) return;
            Deflected = true;
            lifetime = 0f;
            travelPosition = Position;
            velocity = new Vector2(direction < 0 ? -.85f : .85f, .53f).normalized * Mathf.Max(6f, velocity.magnitude * 1.35f);
            if (BodyVisual) BodyVisual.color = new Color(.3f, 1f, .92f);
        }

        private bool OutsideView()
        {
            if (!owner || !owner.ViewCamera) return false;
            Vector3 viewport = owner.ViewCamera.WorldToViewportPoint(Position);
            return viewport.x < -.12f || viewport.x > 1.12f || viewport.y < -.12f || viewport.y > 1.12f;
        }

        private void FaceVelocity() => transform.rotation = Quaternion.Euler(0f, 0f,
            Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg);

        public void Cancel()
        {
            if (!Active && !gameObject.activeSelf) return;
            Active = false;
            gameObject.SetActive(false); // Damage and presentation end synchronously.
            Destroy(gameObject);
        }
    }
}
