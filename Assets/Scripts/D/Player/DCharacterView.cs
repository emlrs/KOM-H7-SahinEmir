using System;
using UnityEngine;

namespace CombinedD
{
    /// <summary>Only editable artwork children animate. Physics and the real shield never squash.</summary>
    public sealed class DCharacterView : MonoBehaviour
    {
        [Serializable] private sealed class Piece
        {
            public SpriteRenderer Renderer;
            public Vector3 Position, Scale;
            public Color Color;
        }
        [SerializeField] private DPlayerMotor motor;
        [SerializeField] private Transform pose, head, leftFoot, rightFoot;
        [SerializeField] private Piece[] pieces;
        [SerializeField] private SpriteRenderer[] shieldPieces;
        [SerializeField] private SpriteRenderer flash;
        private float gait, landingAge = 1f, deathAge, flashAge = 1f;
        private bool wasGrounded, wasAlive = true;
        public string AnimationState { get; private set; } = "Idle";
        public bool ShieldVisible => shieldPieces != null && shieldPieces.Length > 0 && shieldPieces[0].enabled;

        public void Bind(DPlayerMotor target) { motor = target; }
        private void Awake() { if (!motor) motor = GetComponentInParent<DPlayerMotor>(); }
        private void LateUpdate() => TickVisual(Time.deltaTime);
        public void TickVisual(float dt)
        {
            if (!motor || !pose || pieces == null) return;
            RestorePieces();
            bool defending = motor.GuardActive || motor.ParryActive;
            if (shieldPieces != null)
                foreach (var piece in shieldPieces)
                {
                    piece.enabled = defending;
                    piece.color = motor.ParryActive ? new Color(.44f, 1f, 1f) : new Color(1f, .76f, .25f);
                }
            flashAge += dt;
            if (flash)
            {
                flash.enabled = flashAge < .22f && motor.Alive;
                flash.transform.localScale = Vector3.one * Mathf.Lerp(.25f, 1.15f, flashAge / .22f);
                flash.color = new Color(.7f, 1f, 1f, Mathf.Clamp01(1f - flashAge / .22f));
            }
            if (!motor.Alive)
            {
                if (wasAlive) deathAge = 0f;
                wasAlive = false; deathAge += dt; AnimationState = "Death";
                float age = Mathf.Min(deathAge, 1.3f);
                for (int i = 0; i < pieces.Length; i++)
                {
                    Piece p = pieces[i]; if (!p.Renderer) continue;
                    float angle = i * 2.399963f;
                    p.Renderer.transform.localPosition = p.Position + new Vector3(Mathf.Cos(angle) * 1.5f * age,
                        (2f + Mathf.Sin(angle)) * age - 2.5f * age * age, 0);
                    p.Renderer.transform.localRotation = Quaternion.Euler(0, 0, age * (i % 2 == 0 ? 170f : -190f));
                    p.Renderer.transform.localScale = p.Scale * Mathf.Lerp(1f, .15f, age / 1.3f);
                    p.Renderer.color = Color.Lerp(p.Color, new Color(.15f, .07f, .05f, 0), age / 1.3f);
                }
                return;
            }
            wasAlive = true;
            if (motor.Grounded && !wasGrounded) landingAge = 0f;
            wasGrounded = motor.Grounded; landingAge += dt;
            Vector2 velocity = motor.Velocity;
            float speed = Mathf.Abs(velocity.x); gait += speed * dt * 2.5f;
            pose.localScale = new Vector3(motor.Facing, 1, 1);
            if (motor.GuardActive)
            {
                AnimationState = "Guard"; pose.localScale = new Vector3(motor.Facing * 1.04f, .95f, 1);
                pose.localPosition = new Vector3(0, -.025f, 0);
            }
            else if (motor.ParryActive) { AnimationState = "Parry"; pose.localRotation = Quaternion.Euler(0, 0, -motor.Facing * 4f); }
            else if (!motor.Grounded)
            {
                AnimationState = velocity.y > 0 ? "Jump" : "Fall";
                leftFoot.localPosition += new Vector3(-.05f, .08f, 0);
                rightFoot.localPosition += new Vector3(.06f, .03f, 0);
                pose.localRotation = Quaternion.Euler(0, 0, -velocity.x * 1.1f);
            }
            else if (landingAge < .16f)
            {
                AnimationState = "Land"; float squash = Mathf.Sin(landingAge / .16f * Mathf.PI);
                pose.localScale = new Vector3(motor.Facing * (1 + squash * .09f), 1 - squash * .09f, 1);
                pose.localPosition = Vector3.down * (squash * .06f);
            }
            else if (speed > .15f)
            {
                AnimationState = "Run"; float stride = Mathf.Sin(gait) * .12f;
                leftFoot.localPosition += new Vector3(stride, Mathf.Max(0, stride * .6f), 0);
                rightFoot.localPosition += new Vector3(-stride, Mathf.Max(0, -stride * .6f), 0);
                pose.localPosition = Vector3.up * (Mathf.Abs(Mathf.Sin(gait)) * .025f);
                pose.localRotation = Quaternion.Euler(0, 0, -velocity.x * 1.2f);
            }
            else
            {
                AnimationState = "Idle";
                head.localPosition += Vector3.up * (Mathf.Sin(Time.time * 2.6f) * .009f);
            }
        }
        public void ResetVisual()
        {
            gait = deathAge = 0f; landingAge = flashAge = 1f; wasGrounded = false; wasAlive = true;
            RestorePieces(); AnimationState = "Idle";
            if (flash) flash.enabled = false;
            if (shieldPieces != null) foreach (var piece in shieldPieces) if (piece) piece.enabled = false;
        }
        public void FlashParry(Vector2 point)
        {
            flashAge = 0f;
            if (flash) { flash.transform.position = point; flash.enabled = true; }
        }
        private void RestorePieces()
        {
            if (pose) { pose.localPosition = Vector3.zero; pose.localScale = Vector3.one; pose.localRotation = Quaternion.identity; }
            if (pieces == null) return;
            foreach (Piece p in pieces)
            {
                if (!p.Renderer) continue;
                p.Renderer.transform.localPosition = p.Position; p.Renderer.transform.localScale = p.Scale;
                p.Renderer.transform.localRotation = Quaternion.identity; p.Renderer.color = p.Color;
            }
        }

    }
}
