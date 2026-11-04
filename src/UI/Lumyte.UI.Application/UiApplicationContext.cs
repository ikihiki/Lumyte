namespace Lumyte.UI.Application;

/// <summary>Provides application-wide operations to a passive window-list factory.</summary>
public sealed class UiApplicationContext
{
    private readonly UiApplication _application;

    internal UiApplicationContext(UiApplication application)
    {
        _application = application;
    }

    /// <summary>Gets the application's shared dispatcher.</summary>
    public IUiDispatcher Dispatcher => _application.Dispatcher;

    /// <summary>Requests application shutdown without waiting in the callback.</summary>
    public void RequestStop() => _application.RequestStop();

    /// <summary>Coalesces window-list rebuilding at the next owner boundary.</summary>
    public void RequestRebuildWindows() => _application.RequestRebuildWindows();
}
