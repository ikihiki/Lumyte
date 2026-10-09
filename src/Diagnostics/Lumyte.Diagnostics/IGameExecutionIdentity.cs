namespace Lumyte.Diagnostics;

/// <summary>Identity shared by telemetry and the diagnostic session.</summary>
public interface IGameExecutionIdentity
{
    /// <summary>Gets the unique game execution ID.</summary>
    Guid InstanceId { get; }
}
