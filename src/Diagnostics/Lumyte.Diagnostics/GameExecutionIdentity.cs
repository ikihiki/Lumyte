namespace Lumyte.Diagnostics;

internal sealed class GameExecutionIdentity : IGameExecutionIdentity
{
    public Guid InstanceId { get; } = Guid.NewGuid();
}
