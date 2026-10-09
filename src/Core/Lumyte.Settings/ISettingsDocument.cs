namespace Lumyte.Settings;

/// <summary>Coordinates initialization and explicit recovery of the shared document.</summary>
public interface ISettingsDocument
{
    /// <summary>Generates and validates every registered module, for Engine startup without a Host.</summary>
    void ValidateRegisteredSettings();

    /// <summary>Restores all registered defaults and removes unregistered sections after explicit consent.</summary>
    /// <param name="cancellationToken">Cancels before committing.</param>
    /// <returns>The document reset result.</returns>
    Task<SettingsDocumentSaveResult> ResetAsync(CancellationToken cancellationToken = default);
}
