using System.Collections.Immutable;

namespace Lumyte.Settings;

/// <summary>A save outcome with a detached committed snapshot.</summary>
/// <param name="Status">The outcome.</param>
/// <param name="Snapshot">The committed snapshot when the operation completed.</param>
/// <param name="Errors">Validation or storage diagnostics.</param>
/// <typeparam name="T">The settings model.</typeparam>
public sealed record SettingsSaveResult<T>(SettingsSaveStatus Status, SettingsSnapshot<T> Snapshot, ImmutableArray<string> Errors)
    where T : class, new();
