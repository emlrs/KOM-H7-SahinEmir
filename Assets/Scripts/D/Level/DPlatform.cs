using UnityEngine;

namespace CombinedD
{
    // Scene-authoring markers for inspection and route verification, never path following.
    public class DPlatform : MonoBehaviour
    {
        public int Index;
        public DPlatform Next;
        public bool LongCrossing, DefensePad;
        public BoxCollider2D Surface;
    }
}
