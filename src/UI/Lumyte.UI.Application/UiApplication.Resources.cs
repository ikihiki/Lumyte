using Lumyte.Execution;
using Lumyte.Platform.TextInput.Abstractions;
using Lumyte.Resources;
using Lumyte.Resources.Execution;
using Lumyte.Resources.Graphics;
using Lumyte.UI.Windowing;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.UI.Application;

/// <summary>Owns the UI loop and resources from startup through ordered shutdown.</summary>
public sealed partial class UiApplication
{
    private readonly Dictionary<UiSurface, UiFocusBookmark> _recoveryFocus = [];
    private ExecutionRuntime? _execution;
    private ResourceManager? _resources;

    internal void DispatchTextKey(TextInputKey key)
    {
        foreach (TextSite site in _textSites)
        {
            site.Connection.DispatchKey(key);
        }

        SynchronizeFocus();
    }

    internal async Task<bool> DrawFrameAsync(UiDriverWindow target, Lumyte.Graphics.Drawing2D.DrawList2D commands)
    {
        bool presented = false;
        await _dispatcher.TrackAsync(async () => presented = await Driver.DrawAsync(target, commands));
        return presented;
    }

    internal async ValueTask BeginGraphicsRecoveryAsync()
    {
        await _dispatcher.InvokeAsync(() =>
        {
            if (State == UiApplicationState.Running)
            {
                SetState(UiApplicationState.Recovering);
            }

            foreach (Entry entry in _entries.Values)
            {
                if (entry.Slot.CurrentSurface is { } surface)
                {
                    _recoveryFocus[surface] = surface.CaptureFocus();
                    surface.Dispatch(new UiFocusLostInput());
                }

                if (Windows.TryGetSnapshot(entry.Context.Window, out UiWindowSnapshot? snapshot) && snapshot?.InputOrigin is { } origin)
                {
                    _router?.Reset(origin);
                }
            }

            SynchronizeFocus();
        });
    }

    internal async ValueTask EndGraphicsRecoveryAsync()
    {
        if (Volatile.Read(ref _stopRequested) != 0)
        {
            return;
        }

        if (_resources is { } resources && _services.GetService<GraphicsResourceRuntime>() is { } graphics)
        {
            string[] dependencies = resources.RuntimeDependencyIds.Where(id => id == graphics.Id).ToArray();
            if (dependencies.Length != 0)
            {
                await resources.RebuildRuntimeAsync(dependencies);
            }
        }

        foreach (Entry entry in _entries.Values)
        {
            if (entry.Content is IUiApplicationGraphicsContent graphicsContent)
            {
                await graphicsContent.RebuildGraphicsAsync(entry.Context);
            }
            else if (entry.Content is not null)
            {
                throw new UiApplicationException("graphics-recovery-failed", new InvalidOperationException("Custom content must acknowledge graphics recovery."));
            }
        }

        foreach ((UiSurface surface, UiFocusBookmark bookmark) in _recoveryFocus)
        {
            surface.TryRestoreFocus(bookmark);
        }

        _recoveryFocus.Clear();
        if (State == UiApplicationState.Recovering)
        {
            SetState(UiApplicationState.Running);
        }
    }

    private async Task PrepareExecutionAsync()
    {
        IExecutionModule[] modules = _services.GetServices<IExecutionModule>().ToArray();
        if (modules.Any(module => module is not ResourceRuntime))
        {
            throw new UiApplicationException("unsupported-execution-module");
        }

        ExecutionRuntime? execution = _services.GetService<ExecutionRuntime>();
        if (execution is not null)
        {
            if (execution.Status != ExecutionStatus.Unprepared)
            {
                throw new UiApplicationException("unsupported-execution-module");
            }

            execution.PrepareOnCurrentHost(OwnerPlacement.Host, Driver.CurrentThread ? CpuExecutionMode.Cooperative : CpuExecutionMode.ThreadPool, Driver.CurrentThread ? 1 : Math.Max(1, Environment.ProcessorCount), new());
            _execution = execution;
            await execution.StartAsync(_startupCancellation.Token);
        }
    }

    private void PumpExecution()
    {
        if (_execution is not null)
        {
            _execution.PumpHostLifecycle(_options.MaximumWorkItemsPerTurn, _options.MaximumWorkTimePerTurn);
            _execution.PumpJobs(_options.MaximumWorkItemsPerTurn, _options.MaximumWorkTimePerTurn);
        }
    }

    private async Task StopExecutionAsync()
    {
        if (_execution is not null)
        {
            await _execution.StopAsync();
            _execution = null;
        }
    }
}
