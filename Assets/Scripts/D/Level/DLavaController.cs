using UnityEngine;

namespace CombinedD
{
    public class DLavaController : MonoBehaviour
    {
        public Transform LavaBody, LavaSurface;
        public Transform[] LavaEmbers;
        public Transform StartMarker;
        public float RiseSpeed = 1.25f;

        public float Height { get; private set; }

        float visualTime;
        float lavaBodyWidth, lavaSurfaceWidth;

        // Reset the number first; the game applies geometry after resetting the camera.
        public void ResetLava()
        {
            Height = StartMarker ? StartMarker.position.y : -7f;
        }

        public void Step(float dt)
        {
            Height += RiseSpeed * dt;
            SetHeight(Height);
        }

        public void SetHeight(float height)
        {
            Height = height;
            if (LavaBody) LavaBody.position = new Vector3(0, height - 120, 0);
            if (LavaSurface) LavaSurface.position = new Vector3(0, height, 0);
        }

        public void ResetVisual()
        {
            visualTime = 0;
        }

        public void TickVisual(float dt)
        {
            visualTime += dt;
            if (LavaEmbers != null && LavaSurface)
            {
                for (int i = 0; i < LavaEmbers.Length; i++)
                {
                    Transform ember = LavaEmbers[i]; if (!ember) continue;
                    float phase = Mathf.Repeat(visualTime * .32f + i * .137f, 1);
                    ember.position = new Vector3(-14.5f + i * (29f / 23f) + Mathf.Sin(visualTime + i) * .15f,
                        LavaSurface.position.y + .08f + phase * .8f, 0);
                    ember.localScale = Vector3.one * (.05f + .1f * (1 - phase));
                    var sprite = ember.GetComponent<SpriteRenderer>();
                    if (sprite) sprite.color = new Color(1, .65f, .16f, (1 - phase) * .7f);
                }
            }
        }

        public void FitWidth(float viewportWidth)
        {
            FitLavaWidth(LavaBody, ref lavaBodyWidth, viewportWidth);
            FitLavaWidth(LavaSurface, ref lavaSurfaceWidth, viewportWidth);
        }

        static void FitLavaWidth(Transform lava, ref float normalWidth, float viewportWidth)
        {
            if (!lava) return;
            var renderer = lava.GetComponent<SpriteRenderer>();
            if (!renderer || !renderer.sprite) return;
            if (normalWidth <= 0) normalWidth = renderer.bounds.size.x;
            float parentScale = lava.parent ? Mathf.Abs(lava.parent.lossyScale.x) : 1;
            Vector3 scale = lava.localScale;
            scale.x = Mathf.Max(normalWidth, viewportWidth) / Mathf.Max(.001f, renderer.sprite.bounds.size.x * parentScale);
            lava.localScale = scale; // Surface glow is its existing child; colours/heights remain untouched.
        }
    }
}
