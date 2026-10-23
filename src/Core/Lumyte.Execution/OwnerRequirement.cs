namespace Lumyte.Execution;

/// <summary>Physical ownership requirement.</summary>
public enum OwnerRequirement
{
    /// <summary>HostOnly.</summary>
    HostOnly,

    /// <summary>FixedThread.</summary>
    FixedThread,
}
