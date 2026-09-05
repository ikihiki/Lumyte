using Lumyte.Input;

namespace Lumyte.Interaction;

public abstract record PlayerInputSource
{
    private PlayerInputSource()
    {
    }

    public sealed record Keyboard(IKeyboard Device) : PlayerInputSource;

    public sealed record Mouse(IMouse Device) : PlayerInputSource;

    public sealed record Touch(ITouchscreen Device) : PlayerInputSource;

    public sealed record Gamepad(IGamepad Device) : PlayerInputSource;
}
