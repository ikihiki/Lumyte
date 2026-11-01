namespace Lumyte.Input;

/// <summary>Contains a non-destructive history read and its next cursor.</summary>
public sealed class InputReadResult
{
    internal InputReadResult(InputRecord[] records, ulong nextSequence, bool hasGap, bool hasAcquisitionGap = false)
    {
        Records = records;
        NextSequence = nextSequence;
        HasGap = hasGap;
        HasAcquisitionGap = hasAcquisitionGap;
    }

    /// <summary>Gets a value indicating whether acquisition was discarded after the requested cursor.</summary>
    public bool HasAcquisitionGap { get; }

    /// <summary>Gets the immutable retained records.</summary>
    public ReadOnlyMemory<InputRecord> Records { get; }

    /// <summary>Gets the cursor to use for the next read.</summary>
    public ulong NextSequence { get; }

    /// <summary>Gets a value indicating whether unread history was removed.</summary>
    public bool HasGap { get; }
}
