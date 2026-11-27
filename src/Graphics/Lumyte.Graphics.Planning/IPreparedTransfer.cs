namespace Lumyte.Graphics.Planning;

/// <summary>Owns one declared preparation-only map, typed transfer and unmap.</summary>
internal interface IPreparedTransfer : IDisposable
{
    /// <summary>Starts native mapping on the fixed owner; callers await the actual result without occupying that owner.</summary>
    /// <returns>The native mapping completion, not a cancellation surrogate.</returns>
    ValueTask MapAsync();

    /// <summary>Writes patched immutable values and unmaps before record readiness.</summary>
    void Complete();
}
