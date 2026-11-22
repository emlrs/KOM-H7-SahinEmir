using UnityEngine;

namespace CombinedD
{
    /// <summary>The orb assigns one token to each separated contact episode.</summary>
    public sealed class DPanel : MonoBehaviour
    {
        [Range(1, 2)] public int MaximumHits = 1;
        public SpriteRenderer Face, Frame, Crack;
        public Collider2D Solid;
        public int RemainingHits { get; private set; }
        public bool Broken => RemainingHits <= 0;
        private int lastToken = int.MinValue;

        private void Awake() => ResetPanel();

        public void ResetPanel()
        {
            if (!Solid) Solid = GetComponent<Collider2D>();
            RemainingHits = Mathf.Clamp(MaximumHits, 1, 2);
            lastToken = int.MinValue;
            if (Solid) Solid.enabled = true;
            if (Face) Face.enabled = true;
            if (Frame) Frame.enabled = true;
            if (Crack) Crack.enabled = false;
        }

        public void ApplyImpact(int token)
        {
            if (Broken || token == lastToken) return;
            lastToken = token;
            RemainingHits--;
            if (Solid) Solid.enabled = !Broken;
            if (Face) Face.enabled = !Broken;
            if (Frame) Frame.enabled = !Broken;
            if (Crack) Crack.enabled = !Broken && RemainingHits < MaximumHits;
        }
    }
}
