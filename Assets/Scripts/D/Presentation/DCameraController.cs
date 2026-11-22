using UnityEngine;

namespace CombinedD
{
    // Read the orb's completed step before attacks use the new camera rectangle.
    [DefaultExecutionOrder(-50)]
    public class DCameraController : MonoBehaviour
    {
        public DGameController Game;
        public DOrbMotor Orb;
        public DPlayerMotor Player;
        public Camera ViewCamera;
        public Transform CameraBottom, CameraTop, CameraLeft, CameraRight;
        public DLavaController Lava;
        public float CameraHalfHeight = 8.7f, CameraFollowSpeed = 2.5f;
        public float CameraTopMargin = 1f; // Keep the orb below the persistent HUD.
        public float InitialCameraY = 3f, FollowLead = 2.4f;
        [Header("Bidirectional camera and temporary framing zoom")]
        public float BaseOrthographicSize = 9.5f;
        public float CameraSmoothTime = .20f, CameraDeadZone = .9f;
        public float CameraCatchUpSpeed = 24f, FallLookAhead = 3.5f;
        public float ZoomInSpeed = 2.5f, PlayerViewPadding = .75f;
        public float CameraSideMargin = 1.888889f, BoundaryClearance = .12f;
        public bool ManualSimulation;

        float cameraVelocity, cameraLead, lastGroundedY;
        Rect placedBounds;
        bool boundariesPlaced;

        // Physical inner edges, including the HUD inset. Useful for verification/tools.
        public Rect VisiblePlayBounds => PlayBounds(ViewCamera ? ViewCamera.transform.position.y : InitialCameraY,
            ViewCamera ? ViewCamera.orthographicSize : BaseOrthographicSize);

        void FixedUpdate()
        {
            if (!ManualSimulation && Game && !Game.ManualSimulation && Game.IsPlaying)
                FollowStep(Time.fixedDeltaTime);
        }

        public void ResetCamera()
        {
            cameraVelocity = 0; cameraLead = FollowLead;
            lastGroundedY = Player ? Player.Position.y : InitialCameraY - FollowLead;
            if (ViewCamera)
            {
                ViewCamera.orthographicSize = Mathf.Max(1, BaseOrthographicSize);
                ViewCamera.transform.position = new Vector3(0, InitialCameraY, -10);
            }
            PositionBoundaries(VisiblePlayBounds);
        }

        public void FollowStep(float dt)
        {
            if (!ViewCamera || !Player || dt <= 0 || float.IsNaN(dt) || float.IsInfinity(dt)) return;
            float previous = ViewCamera.transform.position.y;
            float normalSize = Mathf.Max(1, BaseOrthographicSize);
            // Keep the last placed rectangle when an external window/aspect change occurs.
            Rect oldBounds = boundariesPlaced ? placedBounds : VisiblePlayBounds;
            if (Player.Grounded) lastGroundedY = Player.Position.y;
            // A normal jump does not repeatedly flip the lead. Look down after falling below takeoff.
            float falling = Mathf.Clamp01((lastGroundedY - Player.Position.y - 1f) / 2.5f);
            float leadTarget = Mathf.Lerp(FollowLead, -FallLookAhead, falling);
            cameraLead = Mathf.Lerp(cameraLead, leadTarget, 1 - Mathf.Exp(-dt / .20f));

            Bounds body = Player.BodyCollider && Player.BodyCollider.enabled ? Player.BodyCollider.bounds
                : new Bounds(Player.Position, new Vector3(.7f, 1.6f, 0));
            Vector2 nextPlayer = Player.Position + Player.Velocity * dt;
            float bottom = Mathf.Min(body.min.y, nextPlayer.y - body.extents.y)
                - PlayerViewPadding - FallLookAhead * falling;
            float top = Mathf.Max(body.max.y, nextPlayer.y + body.extents.y) + PlayerViewPadding;
            float horizontal = Mathf.Max(Mathf.Abs(body.min.x), Mathf.Abs(body.max.x)) + PlayerViewPadding;
            horizontal = Mathf.Max(horizontal, Mathf.Abs(nextPlayer.x) + body.extents.x + PlayerViewPadding);
            if (Orb && Orb.Active)
            {
                // Constrain only INWARD wall travel. A ball already touching a stationary wall must
                // bounce; expanding that wall merely to manufacture clearance would let it run away.
                float radius = Orb.Radius;
                float orbBottom = Orb.Position.y - radius, orbTop = Orb.Position.y + radius;
                float orbLeft = Orb.Position.x - radius, orbRight = Orb.Position.x + radius;
                float safeBottom = orbBottom < oldBounds.yMin ? orbBottom - BoundaryClearance : Mathf.Max(oldBounds.yMin, orbBottom - BoundaryClearance);
                float safeTop = orbTop > oldBounds.yMax ? orbTop + BoundaryClearance : Mathf.Min(oldBounds.yMax, orbTop + BoundaryClearance);
                float left = orbLeft < oldBounds.xMin ? orbLeft - BoundaryClearance : Mathf.Max(oldBounds.xMin, orbLeft - BoundaryClearance);
                float right = orbRight > oldBounds.xMax ? orbRight + BoundaryClearance : Mathf.Min(oldBounds.xMax, orbRight + BoundaryClearance);
                bottom = Mathf.Min(bottom, safeBottom); top = Mathf.Max(top, safeTop);
                horizontal = Mathf.Max(horizontal, -left, right);
            }

            float below = Mathf.Max(.1f, CameraHalfHeight / normalSize);
            float above = Mathf.Max(.1f, (CameraHalfHeight - CameraTopMargin) / normalSize);
            float across = Mathf.Max(.2f, ViewCamera.aspect - CameraSideMargin / normalSize);
            float fittingSize = Mathf.Max(normalSize, (top - bottom) / (below + above), horizontal / across);
            // Prefer the player lead when both actors fit. Otherwise centre their smallest safe frame.
            float desired = Player.Position.y + cameraLead;
            float difference = desired - previous;
            desired = Mathf.Abs(difference) <= CameraDeadZone ? previous
                : desired - Mathf.Sign(difference) * CameraDeadZone;
            float minCenter = top - fittingSize * above;
            float maxCenter = bottom + fittingSize * below;
            desired = Mathf.Clamp(desired, minCenter, Mathf.Max(minCenter, maxCenter));
            float followSpeed = Mathf.Min(Mathf.Max(CameraFollowSpeed, CameraCatchUpSpeed),
                Mathf.Max(CameraFollowSpeed, Mathf.Abs(Player.Velocity.y) * 1.35f + CameraFollowSpeed));
            float next = Mathf.SmoothDamp(previous, desired, ref cameraVelocity,
                Mathf.Max(.04f, CameraSmoothTime), followSpeed, dt);
            next = Mathf.Clamp(next, previous - followSpeed * dt, previous + followSpeed * dt);

            // Fast falls and an 8-unit/s orb can exceed a fixed view's capacity. Grow only as much
            // as this step needs; return slowly. A hard maximum zoom would break that guarantee.
            float requiredSize = Mathf.Max(normalSize, (next - bottom) / below,
                (top - next) / above, horizontal / across);
            float size = Mathf.Max(requiredSize,
                Mathf.MoveTowards(ViewCamera.orthographicSize, normalSize, Mathf.Max(.1f, ZoomInSpeed) * dt));
            ViewCamera.orthographicSize = size;
            ViewCamera.transform.position = new Vector3(0, next, -10);
            PositionBoundaries(PlayBounds(next, size));
            // This method never writes the orb transform, Rigidbody, position, or velocity.
        }

        Rect PlayBounds(float center, float size)
        {
            float normalSize = Mathf.Max(1, BaseOrthographicSize);
            float aspect = ViewCamera ? ViewCamera.aspect : 16f / 9f;
            float halfWidth = Mathf.Max(.2f, aspect - CameraSideMargin / normalSize) * size;
            float bottom = center - Mathf.Max(.1f, CameraHalfHeight / normalSize) * size;
            float top = center + Mathf.Max(.1f, (CameraHalfHeight - CameraTopMargin) / normalSize) * size;
            return Rect.MinMaxRect(-halfWidth, bottom, halfWidth, top);
        }

        void PositionBoundaries(Rect bounds)
        {
            const float thickness = .5f;
            SetWall(CameraBottom, new Vector2(0, bounds.yMin - thickness * .5f), new Vector2(bounds.width + 2 * thickness, thickness));
            SetWall(CameraTop, new Vector2(0, bounds.yMax + thickness * .5f), new Vector2(bounds.width + 2 * thickness, thickness));
            SetWall(CameraLeft, new Vector2(bounds.xMin - thickness * .5f, bounds.center.y), new Vector2(thickness, bounds.height + 2 * thickness));
            SetWall(CameraRight, new Vector2(bounds.xMax + thickness * .5f, bounds.center.y), new Vector2(thickness, bounds.height + 2 * thickness));
            placedBounds = bounds; boundariesPlaced = true;
            // The existing lava artwork covers the expanded view; its original width returns at 9.5.
            float lavaWidth = ViewCamera && ViewCamera.orthographicSize > BaseOrthographicSize + .001f
                ? ViewCamera.orthographicSize * ViewCamera.aspect * 2 + 1 : 0;
            if (Lava) Lava.FitWidth(lavaWidth);
            Physics2D.SyncTransforms();
        }

        static void SetWall(Transform wall, Vector2 center, Vector2 worldSize)
        {
            if (!wall) return;
            var box = wall.GetComponent<BoxCollider2D>();
            if (!box) { wall.position = center; return; }
            Vector3 parentScale = wall.parent ? wall.parent.lossyScale : Vector3.one;
            Vector2 worldScale = new Vector2(worldSize.x / Mathf.Max(.001f, box.size.x),
                worldSize.y / Mathf.Max(.001f, box.size.y));
            wall.localScale = new Vector3(worldScale.x / Mathf.Max(.001f, Mathf.Abs(parentScale.x)),
                worldScale.y / Mathf.Max(.001f, Mathf.Abs(parentScale.y)), 1);
            wall.SetPositionAndRotation(center - Vector2.Scale(box.offset, worldScale), Quaternion.identity);
        }
    }
}
