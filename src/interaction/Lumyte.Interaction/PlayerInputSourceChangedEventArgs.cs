namespace Lumyte.Interaction;

public sealed class PlayerInputSourceChangedEventArgs(
    PlayerInputSource? previous,
    PlayerInputSource? current) : EventArgs
{
    public PlayerInputSource? Previous { get; } = previous;

    public PlayerInputSource? Current { get; } = current;
}
