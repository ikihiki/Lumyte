namespace Lumyte.Memory;

/// <summary>Describes why tracked memory is retained.</summary>
public enum MemoryPurpose
{
    /// <summary>Temporary memory used while constructing a value.</summary>
    Build,

    /// <summary>Memory retained by a published value.</summary>
    Resident,

    /// <summary>Memory retained by an independent streaming session.</summary>
    Streaming,

    /// <summary>Memory retained by a source snapshot.</summary>
    SourceSnapshot,
}
