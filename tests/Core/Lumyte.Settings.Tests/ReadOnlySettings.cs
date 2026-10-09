namespace Lumyte.Settings.Tests;

/// <summary>A model requiring a custom copy implementation.</summary>
public sealed class ReadOnlySettings
{
    /// <summary>Gets the read-only collection.</summary>
    public List<int> Values { get; } = [];
}
