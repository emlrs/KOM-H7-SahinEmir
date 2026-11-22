using UnityEngine;

namespace CombinedD
{
    public class DExitTrigger : MonoBehaviour
    {
        public DGameController Game;
        public Collider2D Zone;

        void OnTriggerEnter2D(Collider2D other) { Check(other); }
        void OnTriggerStay2D(Collider2D other) { Check(other); }

        void Check(Collider2D other)
        {
            var player = other.GetComponentInParent<DPlayerMotor>();
            if (player && Game) Game.TryExit(player);
        }

        public bool Contains(DPlayerMotor player)
        {
            if (!Zone || !Zone.enabled || !player || !player.BodyCollider ||
                !player.BodyCollider.enabled || !Zone.gameObject.activeInHierarchy) return false;
            return Zone.Distance(player.BodyCollider).isOverlapped;
        }
    }
}
