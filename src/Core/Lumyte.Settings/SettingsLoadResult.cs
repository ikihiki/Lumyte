using System.Collections.Immutable;

namespace Lumyte.Settings;

/// <summary>The initial load outcome and diagnostics.</summary>
/// <param name="Status">The outcome.</param>
/// <param name="Errors">The diagnostic messages.</param>
public sealed record SettingsLoadResult(SettingsLoadStatus Status, ImmutableArray<string> Errors);
