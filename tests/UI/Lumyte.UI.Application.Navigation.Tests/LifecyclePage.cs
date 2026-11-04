using Lumyte.UI.Navigation;
using Xunit;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Application.Navigation.Tests;

internal sealed class LifecyclePage : IUiPage, IAsyncDisposable
{
    private readonly PageProbe _probe;
    private readonly PageVisit _visit;

    public LifecyclePage(UiPageContext context, PageProbe probe, PageService service)
    {
        _probe = probe;
        _visit = new(context, service);
        probe.Visits.Add(_visit);
        if (context.Activation.Destination.Route == "/bad" && probe.FailureStage == "constructor")
        {
            throw new ArithmeticException("constructor");
        }
    }

    public IUiPageLeaveGuard? LeaveGuard => _probe.Guard;

    public async ValueTask PrepareAsync(UiPageContext context, CancellationToken cancellationToken)
    {
        Assert.Same(context, _visit.Context);
        _visit.Prepares++;
        if (context.Activation.Destination.Route == "/bad")
        {
            if (_probe.Preparation is { } preparation)
            {
                await preparation.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            if (_probe.FailureStage == "prepare")
            {
                throw new ArithmeticException("prepare");
            }
        }

        await Task.Delay(1, cancellationToken).ConfigureAwait(false);
    }

    public Widget Build(UiPageContext context)
    {
        Assert.True(context.Activation.Dispatcher.CheckAccess());
        _visit.Builds++;
        if (context.Activation.Destination.Route == "/bad" && _probe.FailureStage == "build")
        {
            throw new ArithmeticException("build");
        }

        return context.Activation.Destination.Route == "/bad" && _probe.FailureStage == "null" ? null! : Panel();
    }

    public async ValueTask RetireAsync(CancellationToken cancellationToken)
    {
        _visit.Retires++;
        if (_probe.FailRetire && _visit.Context.Activation.Destination.Route == "/" && _visit.Retires == 1)
        {
            throw new ArithmeticException("retire");
        }

        await Task.Delay(1, cancellationToken).ConfigureAwait(false);
        _visit.RetiredOnOwner = await _visit.Context.Activation.Dispatcher.InvokeAsync(() => _visit.Context.Activation.Dispatcher.CheckAccess(), cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        _visit.Disposes++;
        return ValueTask.CompletedTask;
    }
}
