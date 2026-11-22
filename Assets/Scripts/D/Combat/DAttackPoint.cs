using UnityEngine;

namespace CombinedD
{
    // Scene-authored source. Its transform is the source of truth at runtime.
    public sealed class DAttackPoint : MonoBehaviour
    {
        [Tooltip("-1 is a left source; +1 is a right source.")]
        public int Side = -1;

        private void OnValidate() => Side = Side < 0 ? -1 : 1;

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, .68f, .18f);
            Gizmos.DrawWireSphere(transform.position, .25f);
            Gizmos.DrawLine(transform.position,
                transform.position + Vector3.right * -Side * .8f);
        }
    }
}
