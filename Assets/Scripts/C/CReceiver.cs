using UnityEngine;

namespace CollisionC
{
    /// <summary>A nonreflecting trigger. The orb sweep and native trigger events share one acceptance gate.</summary>
    [DisallowMultipleComponent]
    public sealed class CReceiver : MonoBehaviour
    {
        public CGameController Game;
        public Collider2D Zone;
        private const float ContactEpsilon = .006f;

        private void Awake() => EnsureZone();
        private void Reset() => EnsureZone();
        private void EnsureZone()
        {
            if (!Zone) Zone = GetComponent<Collider2D>();
            if (Zone) Zone.isTrigger = true;
        }
        public bool TouchesOrb(COrbMotor orb)
        {
            EnsureZone();
            if (!orb || !Zone || !Zone.enabled || !Zone.gameObject.activeInHierarchy || !Zone.isTrigger
                || !orb.CollisionShape || !orb.CollisionShape.enabled || orb.gameObject.scene != gameObject.scene) return false;
            // A circle's centre is outside the trigger at first contact. Include its actual
            // world-space radius; requiring centre containment would miss the cast's entry edge.
            Physics2D.SyncTransforms();
            Vector2 closest = Zone.ClosestPoint(orb.Position);
            float reach = orb.Radius + ContactEpsilon;
            return (closest - orb.Position).sqrMagnitude <= reach * reach;
        }
        public bool TryAccept(COrbMotor orb) => Game && TouchesOrb(orb) && Game.AcceptOrb(orb);
        private void OnTriggerEnter2D(Collider2D other) => TryCollider(other);
        private void OnTriggerStay2D(Collider2D other) => TryCollider(other);
        private void TryCollider(Collider2D other)
        {
            COrbMotor orb = other ? other.GetComponentInParent<COrbMotor>() : null;
            if (orb) TryAccept(orb);
        }
    }
}
