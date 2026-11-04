namespace Lumyte.UI.Windowing;

/// <summary>A drawing registration whose disposal stops recording and waits for GPU retirement.</summary>
public interface IUiWindowPresentation : IAsyncDisposable
{
    /// <summary>Gets the last geometry actually presented; zero means not yet presented.</summary>
    ulong PresentedGeometryRevision { get; }

    /// <summary>Queues the latest geometry on the UI owner.</summary>
    /// <param name="view">The immutable geometry.</param>
    void Update(UiWindowPresentationView view);
}
