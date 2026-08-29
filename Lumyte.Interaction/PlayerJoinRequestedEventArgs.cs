using Lumyte.Input;

namespace Lumyte.Interaction;

public sealed class PlayerJoinRequestedEventArgs(PlayerJoinRequest request) : EventArgs
{
    public PlayerJoinRequest Request { get; } = request;

    public IGamepad Gamepad => Request.Gamepad;
}
