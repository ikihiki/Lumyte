using System.Collections.Immutable;

namespace Lumyte.Settings;

/// <summary>The outcome of resetting the whole document.</summary>
/// <param name="Status">The outcome.</param>
/// <param name="Errors">The diagnostics.</param>
public sealed record SettingsDocumentSaveResult(SettingsSaveStatus Status, ImmutableArray<string> Errors);
