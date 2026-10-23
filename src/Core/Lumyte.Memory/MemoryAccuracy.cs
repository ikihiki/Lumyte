namespace Lumyte.Memory;

/// <summary>Describes whether a charge is measured or estimated.</summary>
public enum MemoryAccuracy
{
    /// <summary>The charged byte count is measured.</summary>
    Exact,

    /// <summary>The charged byte count is an estimate.</summary>
    Estimate,
}
