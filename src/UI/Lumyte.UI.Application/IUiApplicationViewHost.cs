using Lumyte.UI.Windowing;

namespace Lumyte.UI.Application;

/// <summary>Defines the IUiApplicationViewHost lifecycle boundary; operations begin on the UI owner.</summary>
internal interface IUiApplicationViewHost
{
    /// <summary>Gets the borrowed Window value.</summary>
    UiWindowId Window { get; }

    /// <summary>Gets the borrowed Dispatcher value.</summary>
    IUiDispatcher Dispatcher { get; }

    /// <summary>Captures only the current trusted input operation identity.</summary>
    /// <returns>The operation result or completion.</returns>
    Guid? CaptureInputOperation();

    /// <summary>Reserves tracked ownership before acquiring candidate resources.</summary>
    /// <returns>The operation result or completion.</returns>
    IUiApplicationViewChange BeginChange();
}
