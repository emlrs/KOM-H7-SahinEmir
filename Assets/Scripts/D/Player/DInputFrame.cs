namespace CombinedD
{
    /// <summary>Update sample; edges survive until the following physics step.</summary>
    public sealed class DInputFrame
    {
        public float Move;
        public bool JumpPressed, JumpHeld, JumpReleased;
        public bool DefendPressed, DefendHeld, DefendReleased;

        public DInputFrame(float move, bool jumpPressed, bool jumpHeld, bool jumpReleased,
            bool defendPressed, bool defendHeld, bool defendReleased)
        {
            Move = move; JumpPressed = jumpPressed; JumpHeld = jumpHeld; JumpReleased = jumpReleased;
            DefendPressed = defendPressed; DefendHeld = defendHeld; DefendReleased = defendReleased;
        }
    }
}
