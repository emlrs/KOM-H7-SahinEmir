using UnityEngine;
using UnityEngine.SceneManagement;

namespace CombinedD
{
    public sealed class DLaser : MonoBehaviour
    {
        public LineRenderer Beam;
        public TextMesh WarningLabel;
        public bool Warning { get; private set; }
        public bool Active { get; private set; }
        public bool Finished { get; private set; }
        public Vector2 Source { get; private set; }
        public Vector2 AimPoint { get; private set; }
        public Vector2 Target { get; private set; }
        public float Remaining { get; private set; }
        public float WarningDuration { get; private set; }
        public float ActiveDuration { get; private set; }
        public float Elapsed { get; private set; }
        public float ActiveElapsed { get; private set; }
        public bool GuardBlocked { get; private set; }
        public Vector2 LastDamageTarget { get; private set; }

        private DAttackDirector owner;
        private double phaseRemaining;
        private bool guardFeedbackShown;
        private Vector2 lastDirection = Vector2.left;
        private const float BeamRadius = .13f;
        private readonly RaycastHit2D[] hits = new RaycastHit2D[16];

        public void Initialize(DAttackDirector director, Vector2 source, Vector2 target, float warning, float duration)
        {
            owner = director;
            Source = source;
            AimPoint = target;
            Target = target;
            transform.position = source;
            WarningDuration = Mathf.Max(0f, warning);
            ActiveDuration = Mathf.Max(0f, duration);
            Remaining = WarningDuration;
            phaseRemaining = WarningDuration;
            Elapsed = ActiveElapsed = 0f;
            Warning = true;
            Active = Finished = GuardBlocked = guardFeedbackShown = false;
            if ((target - source).sqrMagnitude > .000001f) lastDirection = (target - source).normalized;
            if (Beam)
            {
                Beam.enabled = true;
                Beam.useWorldSpace = true;
                Beam.positionCount = 2;
            }
            if (WarningLabel)
            {
                WarningLabel.gameObject.SetActive(true);
                WarningLabel.text = "!";
            }
            TrackAim();
            UpdateVisual();
        }

        public void Step(float dt)
        {
            if (Finished || dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt)) return;
            if (!owner || !owner.Player || !owner.Player.Alive || (owner.Game && !owner.Game.IsPlaying))
            { Cancel(); return; }

            // Aim, drawing and collision share this step's single complete segment.
            TrackAim();
            double available = dt;
            if (Warning)
            {
                double spent = System.Math.Min(phaseRemaining, available);
                phaseRemaining -= spent;
                available -= spent;
                // Float-authored 1.7 seconds and 85 float-authored .02 steps differ by <1 microsecond.
                if (phaseRemaining < .000001) phaseRemaining = 0;
                Remaining = (float)phaseRemaining;
                Elapsed = WarningDuration - Remaining;
                if (Remaining > 0f) { UpdateVisual(); return; }
                Warning = false;
                Active = true;
                Remaining = ActiveDuration;
                phaseRemaining = ActiveDuration;
                if (WarningLabel) WarningLabel.gameObject.SetActive(false);
                owner.NotifyLaserFired(this);
                if (Finished) return;
            }
            if (Active)
            {
                if (Remaining <= 0f) { Cancel(); return; }
                double spent = System.Math.Min(phaseRemaining, available);
                phaseRemaining -= spent;
                if (phaseRemaining < .000001) phaseRemaining = 0;
                Remaining = (float)phaseRemaining;
                ActiveElapsed = ActiveDuration - Remaining;
                Elapsed = WarningDuration + ActiveElapsed;
                UpdateVisual();
                DamageAlongBeam();
                if (Finished) return; // Die() can synchronously cancel the director's episode.
                if (Remaining <= 0f) { Cancel(); return; }
            }
        }

        private void TrackAim()
        {
            if (owner && owner.Player) AimPoint = owner.Player.Position;
            Vector2 direction = AimPoint - Source;
            if (direction.sqrMagnitude > .000001f) lastDirection = direction.normalized;
            else direction = lastDirection;
            Target = AimPoint + lastDirection; // Safe finite fallback if the camera is not assigned.
            Camera camera = owner ? owner.ViewCamera : null;
            if (camera)
            {
                // In an orthographic camera a world-space ray is also a viewport ray.
                // Clip both axes and keep the far intersection, including offscreen sources.
                Vector3 viewportSource = camera.WorldToViewportPoint(Source);
                Vector3 viewportNext = camera.WorldToViewportPoint(Source + direction);
                Vector2 viewportDirection = (Vector2)(viewportNext - viewportSource);
                float enter = 0f, exit = float.PositiveInfinity;
                if (ClipAxis(viewportSource.x, viewportDirection.x, ref enter, ref exit) &&
                    ClipAxis(viewportSource.y, viewportDirection.y, ref enter, ref exit) &&
                    !float.IsInfinity(exit))
                    Target = Source + direction * exit;
            }
            if (WarningLabel)
                WarningLabel.transform.position = Source + lastDirection * .9f + Vector2.up * .55f;
        }

        private static bool ClipAxis(float origin, float direction, ref float enter, ref float exit)
        {
            if (Mathf.Abs(direction) < .000001f) return origin >= 0f && origin <= 1f;
            float first = -origin / direction;
            float last = (1f - origin) / direction;
            if (first > last) { float swap = first; first = last; last = swap; }
            enter = Mathf.Max(enter, first);
            exit = Mathf.Min(exit, last);
            return exit >= enter;
        }

        private void DamageAlongBeam()
        {
            GuardBlocked = false;
            LastDamageTarget = Target;
            DPlayerMotor player = owner.Player;
            if (!player.BodyCollider || !player.BodyCollider.enabled) return;
            Vector2 segment = Target - Source;
            if (segment.sqrMagnitude < .000001f) return;
            ContactFilter2D filter = new ContactFilter2D { useTriggers = true };
            int layers = 1 << player.BodyCollider.gameObject.layer;
            if (player.ShieldCollider) layers |= 1 << player.ShieldCollider.gameObject.layer;
            filter.SetLayerMask(layers);
            int count = gameObject.scene.GetPhysicsScene2D().CircleCast(
                Source, BeamRadius, segment.normalized, segment.magnitude, filter, hits);
            RaycastHit2D shieldHit = default;
            RaycastHit2D bodyHit = default;
            for (int i = 0; i < count; i++)
            {
                if (hits[i].collider == player.ShieldCollider &&
                    (!shieldHit.collider || hits[i].distance < shieldHit.distance)) shieldHit = hits[i];
                if (hits[i].collider == player.BodyCollider &&
                    (!bodyHit.collider || hits[i].distance < bodyHit.distance)) bodyHit = hits[i];
            }
            if (!bodyHit.collider) return;
            if (shieldHit.collider && shieldHit.distance <= bodyHit.distance && player.CanGuardFrom(Source))
            {
                GuardBlocked = true;
                if (!guardFeedbackShown && owner.Game && owner.Game.Effects)
                    owner.Game.Effects.PlayImpact(shieldHit.point, new Color(.65f, 1f, .95f));
                guardFeedbackShown = true;
                return;
            }
            if (owner.Game) owner.Game.Die();
            else player.Kill();
        }

        private void UpdateVisual()
        {
            Color color = Warning
                ? new Color(1f, .65f, .12f, .35f + .35f * Mathf.Abs(Mathf.Sin(Elapsed * 8f)))
                : new Color(1f, .24f, .14f, .95f);
            if (Beam)
            {
                Beam.startWidth = Beam.endWidth = Warning ? .075f : BeamRadius * 2f;
                Beam.startColor = Beam.endColor = color;
                Beam.SetPosition(0, Source);
                Beam.SetPosition(1, Target); // Guard prevents damage; it never shortens the beam.
            }
            if (WarningLabel) WarningLabel.color = new Color(1f, .72f, .15f);
        }

        public void Cancel()
        {
            if (Finished) return;
            Warning = Active = false;
            Finished = true;
            Remaining = 0f;
            if (Beam) Beam.enabled = false;
            if (WarningLabel) WarningLabel.gameObject.SetActive(false);
            gameObject.SetActive(false);
            Destroy(gameObject);
        }
    }
}
