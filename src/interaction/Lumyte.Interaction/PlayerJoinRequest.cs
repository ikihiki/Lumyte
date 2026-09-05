using Lumyte.Input;

namespace Lumyte.Interaction;

public sealed class PlayerJoinRequest
{
    internal PlayerJoinRequest(IGamepad gamepad) =>
        Gamepad = gamepad ?? throw new ArgumentNullException(nameof(gamepad));

    public IGamepad Gamepad { get; }

    public PlayerJoinRequestStatus Status { get; private set; }

    public event EventHandler? StatusChanged;

    internal void Complete(PlayerJoinRequestStatus status)
    {
        if (Status != PlayerJoinRequestStatus.Pending)
        {
            throw new InvalidOperationException("The join request is already complete.");
        }

        Status = status;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }
}
