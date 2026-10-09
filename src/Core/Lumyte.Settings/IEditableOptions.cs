namespace Lumyte.Settings;

/// <summary>Edits, validates and persists one module in the shared settings document.</summary>
/// <typeparam name="T">The settings model.</typeparam>
public interface IEditableOptions<T>
    where T : class, new()
{
    /// <summary>Gets the initial loading diagnostics.</summary>
    SettingsLoadResult LoadResult { get; }

    /// <summary>Gets a consistent deep copy of the committed value.</summary>
    SettingsSnapshot<T> Current { get; }

    /// <summary>Gets the module revision without copying its value.</summary>
    long Revision { get; }

    /// <summary>Captures a revision and independent editable value.</summary>
    /// <returns>The edit.</returns>
    SettingsEdit<T> BeginEdit();

    /// <summary>Captures the candidate synchronously, validates and atomically commits it.</summary>
    /// <param name="edit">An edit from this service.</param>
    /// <param name="cancellationToken">Cancels before committing.</param>
    /// <returns>The save result.</returns>
    Task<SettingsSaveResult<T>> SaveAsync(SettingsEdit<T> edit, CancellationToken cancellationToken = default);

    /// <summary>Restores this module's defaults. Whole-document corruption requires document reset.</summary>
    /// <param name="expectedRevision">The expected module revision.</param>
    /// <param name="cancellationToken">Cancels before committing.</param>
    /// <returns>The save result.</returns>
    Task<SettingsSaveResult<T>> ResetAsync(long expectedRevision, CancellationToken cancellationToken = default);
}
