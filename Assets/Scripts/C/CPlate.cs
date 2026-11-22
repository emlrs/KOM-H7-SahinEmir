using System.Collections.Generic;
using UnityEngine;

namespace CollisionC
{
    /// <summary>Persistent one/two-hit plate. An episode token can damage it at most once per reset.</summary>
    [DisallowMultipleComponent]
    public sealed class CPlate : MonoBehaviour
    {
        [Range(1, 2)] public int MaximumHits = 1;
        public int RemainingHits { get; private set; }
        public bool Broken => RemainingHits <= 0;
        public SpriteRenderer Face, Frame, Crack;
        public Collider2D Solid;
        private readonly HashSet<int> consumedTokens = new HashSet<int>();
        private SpriteRenderer cachedFace, cachedFrame;
        private Color originalFace, originalFrame;

        private void Awake() => ResetPlate();
        private void CachePresentation()
        {
            if (!Solid) Solid = GetComponent<Collider2D>();
            if (Face && cachedFace != Face) { cachedFace = Face; originalFace = Face.color; }
            if (Frame && cachedFrame != Frame) { cachedFrame = Frame; originalFrame = Frame.color; }
        }
        public void ResetPlate()
        {
            CachePresentation();
            RemainingHits = Mathf.Clamp(MaximumHits, 1, 2);
            consumedTokens.Clear();
            if (Solid) { Solid.isTrigger = false; Solid.enabled = true; }
            if (Face) { Face.enabled = true; Face.color = originalFace; }
            if (Frame) { Frame.enabled = true; Frame.color = originalFrame; }
            if (Crack) Crack.enabled = false;
        }
        /// <returns>True when this distinct contact reduced durability, including the first armored hit.</returns>
        public bool ApplyImpact(int contactToken)
        {
            if (Broken || !consumedTokens.Add(contactToken)) return false;
            CachePresentation();
            RemainingHits--;
            if (Broken)
            {
                if (Solid) Solid.enabled = false;
                if (Face) Face.enabled = false;
                if (Frame) Frame.enabled = false;
                if (Crack) Crack.enabled = false;
            }
            else
            {
                if (Crack) Crack.enabled = true;
                if (Face) Face.color = Color.Lerp(originalFace, new Color(.38f, .22f, .13f), .34f);
            }
            return true;
        }
    }
}
