using Lumyte.UI.Windowing;

namespace Lumyte.UI.Navigation;

/// <summary>Provides borrowed owner services and one activation's cancellation lifetime.</summary>
public sealed class UiPageActivationContext
{
    internal UiPageActivationContext(UiWindowId window, UiPageDestination destination, IUiNavigator navigation, IUiDispatcher dispatcher, CancellationToken lifetime)
    {
        Window = window;
        Destination = destination;
        Navigation = navigation;
        Dispatcher = dispatcher;
        LifetimeCancellation = lifetime;
    }

    /// <summary>Gets this activation's nonreused identity.</summary>
    public Guid ActivationId { get; } = Guid.NewGuid();

    /// <summary>Gets the stable window identity.</summary>
    public UiWindowId Window { get; }

    /// <summary>Gets the captured destination.</summary>
    public UiPageDestination Destination { get; }

    /// <summary>Gets the window's navigator.</summary>
    public IUiNavigator Navigation { get; }

    /// <summary>Gets the shared UI dispatcher.</summary>
    public IUiDispatcher Dispatcher { get; }

    /// <summary>Gets a token canceled when preparation is abandoned or retirement begins.</summary>
    public CancellationToken LifetimeCancellation { get; }
}
