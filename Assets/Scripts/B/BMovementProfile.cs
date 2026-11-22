using System;
using UnityEngine;

namespace LavaRiseB
{
    [Serializable]
    public sealed class BMovementProfile
    {
        public string Name = "Akış";
        public Color Color = new Color(.28f, .92f, 1f);
        [Min(.1f)] public float MaxSpeed = 6.6f;
        [Min(.1f)] public float Acceleration = 12f;
        [Min(.1f)] public float Braking = 7f;
        [Min(.1f)] public float CounterSteer = 18f;
        [Range(.05f, 1f)] public float AirControl = .55f;

        public static BMovementProfile[] Defaults() => new[]
        {
            new BMovementProfile(),
            new BMovementProfile { Name = "Denge", Color = new Color(1f, .77f, .31f), MaxSpeed = 5.4f,
                Acceleration = 16f, Braking = 12f, CounterSteer = 25f, AirControl = .65f }
        };
    }

    /// <summary>One rendered-frame input sample. Press/release edges are queued until FixedUpdate.</summary>
    public readonly struct BInputFrame
    {
        public readonly float Move;
        public readonly bool JumpPressed, JumpHeld, JumpReleased, SwitchPressed;
        public BInputFrame(float move, bool jumpPressed, bool jumpHeld, bool jumpReleased, bool switchPressed)
        {
            Move = Mathf.Clamp(move, -1f, 1f);
            JumpPressed = jumpPressed; JumpHeld = jumpHeld;
            JumpReleased = jumpReleased; SwitchPressed = switchPressed;
        }
        public static BInputFrame Neutral => new BInputFrame(0, false, false, false, false);
    }
}
