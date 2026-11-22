using UnityEngine;

namespace CollisionC
{
    // C scene defaults; independent from the preserved A and B settings.
    [CreateAssetMenu(menuName = "Collision C/Orb Settings", fileName = "C_OrbSettings")]
    public sealed class COrbSettings : ScriptableObject
    {
        [Header("Speed (world units / second)")]
        [Min(0.1f)] public float InitialSpeed = 5.5f;
        [Min(0.1f)] public float MaximumSpeed = 8f;
        [Min(0f)] public float AccelerationPerSecond = 0.025f;

        [Header("Parry: random diagonal in the facing half-plane")]
        [Range(5f, 80f)] public float MinimumParryAngle = 22f;
        [Range(5f, 80f)] public float MaximumParryAngle = 62f;
        [Tooltip("Both upward and downward diagonals are allowed. No receiver aim assist.")]
        public bool AllowDownwardParries = true;

        [Header("Swept collision solver")]
        public LayerMask CollisionMask = ~0;
        [Tooltip("A small geometric separation, NOT a period of invulnerability.")]
        [Range(0.001f, 0.02f)] public float ContactSkin = 0.004f;
        [Tooltip("A plate must geometrically separate by this gap before its next hit episode.")]
        [Range(0.01f, 0.1f)] public float EpisodeSeparation = 0.025f;
        [Range(0.005f, 0.05f)] public float MaximumTimeSlice = 0.02f;
        [Range(8, 64)] public int MaximumContactsPerSlice = 24;

        private void OnValidate()
        {
            InitialSpeed = Mathf.Max(0.1f, InitialSpeed);
            MaximumSpeed = Mathf.Max(InitialSpeed, MaximumSpeed);
            AccelerationPerSecond = Mathf.Max(0f, AccelerationPerSecond);
            MinimumParryAngle = Mathf.Clamp(MinimumParryAngle, 5f, 80f);
            MaximumParryAngle = Mathf.Clamp(MaximumParryAngle, MinimumParryAngle, 80f);
        }
    }
}
