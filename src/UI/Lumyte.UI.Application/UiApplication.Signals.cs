using Lumyte.Signals;

namespace Lumyte.UI.Application;

/// <summary>Owns the UI loop and resources from startup through ordered shutdown.</summary>
public sealed partial class UiApplication
{
    private SignalEffect? _windowSignals;
    private bool _windowSignalsStopped;

    private void ObserveWindowSignals(UiWindowDeclaration[] declarations, UiWindowDeclaration[] resolved, SignalReadSet reads)
    {
        if (reads.IsEmpty && declarations.All(declaration => declaration.TitleArgument.Source is null && declaration.OpenArgument.Source is null))
        {
            _windowSignals?.Dispose();
            _windowSignals = null;
            return;
        }

        bool initializing = true;
        SignalEffect next = Signal.Effect(
            () =>
            {
                reads.Read();
                bool changed = !reads.IsCurrent;
                for (int index = 0; index < declarations.Length; index++)
                {
                    changed |= declarations[index].ReadSignals(resolved[index]);
                }

                if (!initializing || changed)
                {
                    RequestRebuildWindows();
                }
            },
            new WindowSignalScheduler(this),
            error =>
            {
                RecordFailure(new UiStateUpdateException(error));
                RequestStop();
            });
        initializing = false;
        SignalEffect? previous = _windowSignals;
        _windowSignals = next;
        previous?.Dispose();
    }

    private void ReleaseWindowSignals()
    {
        _windowSignalsStopped = true;
        _windowSignals?.Dispose();
        _windowSignals = null;
    }

    private async Task DispatchWindowSignalAsync(Action work)
    {
        try
        {
            await Dispatcher.InvokeAsync(() =>
            {
                if (!_windowSignalsStopped)
                {
                    work();
                }
            }).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            if (!_windowSignalsStopped)
            {
                RecordFailure(new UiStateUpdateException(error));
                RequestStop();
            }
        }
    }

    private sealed class WindowSignalScheduler(UiApplication application) : ISignalScheduler
    {
        public void Schedule(Action work) => _ = application.DispatchWindowSignalAsync(work);
    }
}
