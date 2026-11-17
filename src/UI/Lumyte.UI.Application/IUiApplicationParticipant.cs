namespace Lumyte.UI.Application;

/// <summary>Coordinates optional integrations on the same owner and shutdown boundary.</summary>
internal interface IUiApplicationParticipant
{
    /// <summary>Gets a value indicating whether accepted integration work has drained.</summary>
    bool IsStopped { get; }

    /// <summary>Gets an optional deadline for owner work.</summary>
    TimeSpan? NextWorkDelay => null;

    /// <summary>Starts explicitly configured integration services.</summary>
    /// <param name="cancellationToken">Startup cancellation.</param>
    /// <returns>Startup completion.</returns>
    ValueTask StartAsync(CancellationToken cancellationToken);

    /// <summary>Provides borrowed function context before any initial or staged root is evaluated.</summary>
    /// <param name="context">The non-reusable owning window.</param>
    /// <param name="root">A fresh context carrier, not application content.</param>
    /// <returns>The carrier with optional integration context.</returns>
    Lumyte.UI.Compose.Definitions.Widget ProvideRootContext(UiApplicationWindowContext context, Lumyte.UI.Compose.Definitions.Widget root) => root;

    /// <summary>Registers a prepared window.</summary>
    /// <param name="context">The borrowed window context.</param>
    void OpenWindow(UiApplicationWindowContext context);

    /// <summary>Stops a window target.</summary>
    /// <param name="context">The borrowed window context.</param>
    void CloseWindow(UiApplicationWindowContext context);

    /// <summary>Checks that a window's accepted integration work has drained.</summary>
    /// <param name="context">The retiring window.</param>
    /// <returns>Whether teardown may continue.</returns>
    bool IsWindowStopped(UiApplicationWindowContext context);

    /// <summary>Retires integration GPU resources after frame drain and surface detach, before native destruction.</summary>
    /// <param name="context">The retiring window.</param>
    /// <returns>Completion of resource retirement.</returns>
    ValueTask RetireWindowResourcesAsync(UiApplicationWindowContext context) => ValueTask.CompletedTask;

    /// <summary>Advances integration work.</summary>
    void Advance();

    /// <summary>Stops admitting new work.</summary>
    void BeginStop();

    /// <summary>Releases integration resources.</summary>
    /// <returns>Integration cleanup completion.</returns>
    ValueTask DisposeAsync();
}
