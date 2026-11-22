using UnityEngine;

namespace CombinedD
{
    public class DEffectsController : MonoBehaviour
    {
        public SpriteRenderer WaveRing, ImpactRing;
        public Transform[] DeathFragments;

        float waveTime = 100, impactTime = 100, deathTime = 100;
        Vector2 waveOrigin, impactOrigin, deathOrigin;
        Color impactColor;

        public void ResetEffects()
        {
            waveTime = 100; impactTime = 100; deathTime = 100;
            if (WaveRing) WaveRing.gameObject.SetActive(false);
            if (ImpactRing) ImpactRing.gameObject.SetActive(false);
            if (DeathFragments != null) foreach (var fragment in DeathFragments) if (fragment) fragment.gameObject.SetActive(false);
        }

        public void PlayWave(Vector2 contact)
        {
            waveOrigin = contact; waveTime = 0;
            if (WaveRing) WaveRing.gameObject.SetActive(true);
        }

        public void PlayImpact(Vector2 contact, Color color)
        {
            impactOrigin = contact; impactColor = color; impactTime = 0;
            if (ImpactRing) ImpactRing.gameObject.SetActive(true);
        }

        public void PlayDeath(Vector2 origin)
        {
            deathOrigin = origin; deathTime = 0;
            if (DeathFragments != null) foreach (var fragment in DeathFragments) if (fragment) fragment.gameObject.SetActive(true);
        }

        public void TickVisual(float dt)
        {
            waveTime += dt; impactTime += dt;
            AnimateRing(WaveRing, waveOrigin, waveTime, 1.05f, 29, new Color(.35f, .80f, 1f, .6f));
            AnimateRing(ImpactRing, impactOrigin, impactTime, .4f, 2.4f, impactColor);
            if (deathTime < 1.5f && DeathFragments != null)
            {
                deathTime += dt;
                for (int i = 0; i < DeathFragments.Length; i++)
                {
                    Transform fragment = DeathFragments[i]; if (!fragment) continue;
                    float angle = (i * 137.5f + 20) * Mathf.Deg2Rad;
                    Vector2 velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (2.5f + i % 5 * .6f);
                    fragment.position = deathOrigin + velocity * deathTime + Vector2.down * deathTime * deathTime * 2.6f;
                    fragment.rotation = Quaternion.Euler(0, 0, i * 22 + deathTime * (i % 2 == 0 ? 260 : -210));
                    var sprite = fragment.GetComponent<SpriteRenderer>();
                    if (sprite) { Color color = sprite.color; color.a = Mathf.Clamp01(1 - deathTime / 1.5f); sprite.color = color; }
                    if (deathTime >= 1.5f) fragment.gameObject.SetActive(false);
                }
            }
        }

        static void AnimateRing(SpriteRenderer ring, Vector2 origin, float time, float duration, float diameter, Color color)
        {
            if (!ring || !ring.gameObject.activeSelf) return;
            if (time >= duration) { ring.gameObject.SetActive(false); return; }
            ring.transform.position = origin;
            ring.transform.localScale = Vector3.one * Mathf.Lerp(.3f, diameter, time / duration);
            color.a *= 1 - time / duration;
            ring.color = color;
        }
    }
}
