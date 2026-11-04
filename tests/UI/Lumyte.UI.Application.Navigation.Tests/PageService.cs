namespace Lumyte.UI.Application.Navigation.Tests;

internal sealed class PageService : IAsyncDisposable
{
    public PageService(UiPageContext context, PageProbe probe)
    {
        Context = context;
        Probe = probe;
        probe.Services.Add(this);
    }

    internal UiPageContext Context { get; }

    internal PageProbe Probe { get; }

    internal int Disposes { get; private set; }

    public ValueTask DisposeAsync()
    {
        Disposes++;
        return Probe.FailDispose && Context.Activation.Destination.Route == "/" ? ValueTask.FromException(new ArithmeticException("scope disposal")) : ValueTask.CompletedTask;
    }
}
