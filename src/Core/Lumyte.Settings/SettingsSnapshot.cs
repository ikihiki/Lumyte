namespace Lumyte.Settings;

/// <summary>A detached copy of committed settings and its module revision.</summary>
/// <param name="Revision">The module revision.</param>
/// <param name="Value">The detached value.</param>
/// <typeparam name="T">The settings model.</typeparam>
public sealed record SettingsSnapshot<T>(long Revision, T Value)
    where T : class, new();
