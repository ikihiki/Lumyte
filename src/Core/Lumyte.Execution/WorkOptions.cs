namespace Lumyte.Execution;

/// <summary>Immutable registration options; gates are copied when registered.</summary>
public sealed class WorkOptions
{
    /// <summary>Gets the execution target.</summary>
    public WorkTarget Target { get; init; }

    /// <summary>Gets the FIFO group; required for owner work.</summary>
    public OrderGroupKey OrderGroup { get; init; }

    /// <summary>Gets the admission authority.</summary>
    public WorkAdmission Admission { get; init; }

    /// <summary>Gets gates which must be open before execution.</summary>
    public ReadOnlyMemory<GateKey> RequiredGates { get; init; }
}
