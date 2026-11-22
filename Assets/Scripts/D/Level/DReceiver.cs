using UnityEngine;

namespace CombinedD
{
    /// <summary>Always ready: actual circle contact opens the door, without a charge or timer.</summary>
    public sealed class DReceiver : MonoBehaviour
    {
        public DGameController Game;
        public Collider2D Zone;

        private void Awake() { if (!Zone) Zone = GetComponent<Collider2D>(); }

        public bool TryAccept(DOrbMotor orb)
        {
            if (!Game || !Game.IsPlaying || Game.DoorOpen || !orb || !orb.Active
                || (Game.Orb && Game.Orb != orb) || !Zone || !Zone.enabled || !Zone.isTrigger
                || !Zone.gameObject.activeInHierarchy || !orb.CollisionShape || !orb.CollisionShape.enabled
                || orb.gameObject.scene != gameObject.scene) return false;
            Physics2D.SyncTransforms();
            // A swept circle first touches while its centre is still outside the trigger.
            float reach = orb.Radius + .002f;
            if ((orb.Position - Zone.ClosestPoint(orb.Position)).sqrMagnitude > reach * reach) return false;
            Game.OpenDoor();
            orb.Absorb();
            return true;
        }

        private void OnTriggerEnter2D(Collider2D other) => TryCollider(other);
        private void OnTriggerStay2D(Collider2D other) => TryCollider(other);
        private void TryCollider(Collider2D other)
        {
            var orb = other ? other.GetComponentInParent<DOrbMotor>() : null;
            if (orb) TryAccept(orb);
        }
    }
}
