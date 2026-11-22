using UnityEngine;

namespace CollisionC
{
    [DisallowMultipleComponent]
    public sealed class CExitTrigger : MonoBehaviour
    {
        public CGameController Game;
        public Collider2D Zone;
        private void Awake() => EnsureZone();
        private void Reset() => EnsureZone();
        private void EnsureZone()
        {
            if (!Zone) Zone = GetComponent<Collider2D>();
            if (Zone) Zone.isTrigger = true;
        }
        public bool TouchesPlayer(CPlayerMotor player)
        {
            EnsureZone();
            if (!player || !player.Alive || !Zone || !Zone.enabled || !Zone.gameObject.activeInHierarchy
                || !player.BodyCollider || !player.BodyCollider.enabled || player.gameObject.scene != gameObject.scene) return false;
            Physics2D.SyncTransforms();
            ColliderDistance2D contact = Zone.Distance(player.BodyCollider);
            return contact.isValid && (contact.isOverlapped || contact.distance <= .001f);
        }
        private void OnTriggerEnter2D(Collider2D other) => TryCollider(other);
        private void OnTriggerStay2D(Collider2D other) => TryCollider(other);
        private void TryCollider(Collider2D other)
        {
            CPlayerMotor player = other ? other.GetComponentInParent<CPlayerMotor>() : null;
            if (Game && player) Game.TryExit(player);
        }
    }
}
