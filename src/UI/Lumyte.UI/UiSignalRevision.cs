namespace Lumyte.UI;

/// <summary>Identifies an attached dependency set and its latest upstream change.</summary>
/// <param name="Connections">The attachment generation.</param>
/// <param name="Values">The latest dependency mutation stamp.</param>
public readonly record struct UiSignalRevision(ulong Connections, ulong Values);
