using Lumyte.UI.Windowing;

namespace Lumyte.UI.Application;

/// <summary>Supplies borrowed defaults while constructing an application-owned manager.</summary>
public sealed class UiWindowManagerContext
{
    internal UiWindowManagerContext(IUiWindowManager manager, IUiWindowSource source, IUiDispatcher dispatcher, TimeProvider timeProvider)
    {
        DefaultManager = manager;
        RootSource = source;
        Dispatcher = dispatcher;
        TimeProvider = timeProvider;
    }

    /// <summary>Gets the borrowed default manager.</summary>
    public IUiWindowManager DefaultManager { get; }

    /// <summary>Gets the source which the application will register.</summary>
    public IUiWindowSource RootSource { get; }

    /// <summary>Gets the owner dispatcher.</summary>
    public IUiDispatcher Dispatcher { get; }

    /// <summary>Gets the application clock.</summary>
    public TimeProvider TimeProvider { get; }
}
