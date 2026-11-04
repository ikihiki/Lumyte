using Lumyte.UI.Windowing;

namespace Lumyte.UI.Navigation;

/// <summary>Defines the IUiNavigationHost lifecycle boundary; operations begin on the UI owner.</summary>
public interface IUiNavigationHost
{
    /// <summary>Gets the borrowed Window value.</summary>
    UiWindowId Window { get; }

    /// <summary>Gets the borrowed Dispatcher value.</summary>
    IUiDispatcher Dispatcher { get; }

    /// <summary>Captures only the current trusted input operation identity.</summary>
    /// <returns>The operation result or completion.</returns>
    Guid? CaptureInputOperation();

    /// <summary>Reserves tracked ownership before acquiring candidate resources.</summary>
    /// <param name="activationId">The activationId value.</param>
    /// <returns>The operation result or completion.</returns>
    IUiNavigationViewChange BeginChange(Guid activationId);
}
