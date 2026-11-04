namespace Lumyte.UI.Application;

/// <summary>Owns application content through initialization, quiescence and release.</summary>
public interface IUiApplicationContent
{
    /// <summary>Gets a value indicating whether accepted work has quiesced, independently of surface release.</summary>
    bool IsStopped { get; }

    /// <summary>Publishes the initial view on the owner.</summary>
    /// <param name="context">The borrowed window context.</param>
    /// <param name="cancellationToken">Requests cancellation of startup.</param>
    /// <returns>Initialization completion.</returns>
    ValueTask InitializeAsync(UiApplicationWindowContext context, CancellationToken cancellationToken);

    /// <summary>Advances accepted work without synchronously waiting for tasks.</summary>
    void Advance();

    /// <summary>Stops accepting new work once.</summary>
    void BeginStop();

    /// <summary>Releases UI-dependent resources after GPU drain, retaining the surface for retry.</summary>
    /// <returns>Retryable retirement completion.</returns>
    ValueTask RetireAsync();

    /// <summary>Releases owned scopes once, after the slot has been detached.</summary>
    /// <returns>Final release completion.</returns>
    ValueTask ReleaseAsync();
}
