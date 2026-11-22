namespace CollisionC
{
    /// <summary>A single Update sample. Edge flags are queued until the next physics step.</summary>
    public readonly struct CInputFrame
    {
        public readonly float Move;
        public readonly bool ParryPressed, ParryHeld, ParryReleased;
        public CInputFrame(float move, bool parryPressed, bool parryHeld, bool parryReleased)
        {
            Move = move; ParryPressed = parryPressed;
            ParryHeld = parryHeld; ParryReleased = parryReleased;
        }
    }
}
