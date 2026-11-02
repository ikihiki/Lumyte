using System.Numerics;
using System.Runtime.ExceptionServices;
using Lumyte.Signals;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

/// <summary>Applies reactive work at UI boundaries using element-owned observers.</summary>
public sealed partial class UiSurface
{
    private readonly Queue<SignalJob> _signalJobs = new();
    private readonly HashSet<UiElement.SignalObserver> _pendingSignalObservers = [];
    private readonly HashSet<UiElement> _signalElements = new(ReferenceEqualityComparer.Instance);
    private IUiDispatcher? _signalDispatcher;
    private bool _signalPosted;
    private bool _processingSignals;
    private bool _signalUpdatesEnabled = true;
    private ulong _signalConnections;
    private ulong _signalRevision;
    private int _dispatchDepth;
    private int _recordDepth;
    private volatile Exception? _signalFailure;

    /// <summary>Initializes a new instance of the <see cref="UiSurface"/> class.</summary>
    /// <param name="root">The passive declaration root.</param>
    /// <param name="dispatcher">The borrowed owner dispatcher.</param>
    public UiSurface(Widget root, IUiDispatcher dispatcher)
        : this(RequireDispatcher(root, dispatcher))
    {
        _signalDispatcher = dispatcher;
    }

    /// <summary>Initializes a new instance of the <see cref="UiSurface"/> class.</summary>
    /// <param name="build">The initial construction function, tracked automatically.</param>
    public UiSurface(Func<Widget> build)
        : this(CaptureBuild(build))
    {
    }

    /// <summary>Initializes a new instance of the <see cref="UiSurface"/> class.</summary>
    /// <param name="build">The tracked construction function.</param>
    /// <param name="dispatcher">The borrowed owner dispatcher.</param>
    public UiSurface(Func<Widget> build, IUiDispatcher dispatcher)
        : this(CaptureBuild(build), dispatcher)
    {
    }

    /// <summary>Gets dependency changes, inspecting only pending observers while paused and no observers when idle.</summary>
    public UiSignalRevision SignalRevision
    {
        get
        {
            UiElement.SignalObserver[] pending;
            ulong values;
            ulong connections;
            lock (_signalJobs)
            {
                values = _signalRevision;
                connections = _signalConnections;
                if (_pendingSignalObservers.Count == 0)
                {
                    return new(connections, values);
                }

                pending = _pendingSignalObservers.ToArray();
            }

            foreach (UiElement.SignalObserver observer in pending)
            {
                values = Math.Max(values, observer.Revision);
            }

            return new(connections, values);
        }
    }

    internal UiSignalRevision CommittedSignalRevision { get; private set; }

    /// <summary>Pauses automatic application; explicit UI update boundaries can still prepare current values.</summary>
    /// <param name="enabled">Whether automatic dispatcher work may publish changes.</param>
    public void SetSignalUpdatesEnabled(bool enabled)
    {
        ThrowIfDisposed();
        _signalUpdatesEnabled = enabled;
        if (enabled)
        {
            ProcessSignals(false);
        }
    }

    /// <summary>Queues a newly evaluated construction function.</summary>
    /// <param name="build">The tracked construction function.</param>
    public void Rebuild(Func<Widget> build) => Rebuild(CaptureBuild(build));

    internal static Widget CaptureIndependentBuild(Func<Widget> build, out SignalReadSet reads)
    {
        (Widget root, SignalReadSet captured) = Signal.Untracked(() =>
        {
            Widget result = Signal.CaptureReads(build, out SignalReadSet dependencies);
            return (result, dependencies);
        });
        reads = captured;
        return root;
    }

    internal static Widget CaptureBuild(Func<Widget> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        Widget root = CaptureIndependentBuild(build, out SignalReadSet reads)
            ?? throw new InvalidOperationException("Construction returned no declaration.");
        root.Construction = build;
        root.ConstructionReads = reads.IsEmpty ? null : reads;
        return root;
    }

    internal void QueueSignalElement(UiElement element) => _signalElements.Add(element);

    internal void ForgetSignalElement(UiElement element) => _signalElements.Remove(element);

    internal void FailSignalUpdate(Exception error) => _signalFailure = error is UiStateUpdateException ? error : new UiStateUpdateException(error);

    internal void PostSignal(UiElement.SignalObserver observer, Action work)
    {
        bool post;
        lock (_signalJobs)
        {
            if (_isDisposed)
            {
                return;
            }

            _signalJobs.Enqueue(new(observer, work));
            _pendingSignalObservers.Add(observer);
            post = !_signalPosted;
            _signalPosted = true;
        }

        if (post && _signalDispatcher is { } dispatcher)
        {
            _ = ObserveDispatchAsync(dispatcher.InvokeAsync(() => ProcessSignals(false)));
        }
    }

    internal void RecordSignalRevision(ulong revision)
    {
        lock (_signalJobs)
        {
            _signalRevision = Math.Max(_signalRevision, revision);
        }
    }

    internal void ForgetSignalObserver(UiElement.SignalObserver observer)
    {
        RecordSignalRevision(observer.Revision);
        lock (_signalJobs)
        {
            _pendingSignalObservers.Remove(observer);
        }
    }

    internal Vector2 MeasureDesiredSize()
    {
        UpdateTree();
        var pass = new UiLayoutPass();
        _layoutPass = pass;
        try
        {
            pass.Compute(_rootElement, UiBoxConstraints.Loose(new(float.PositiveInfinity, float.PositiveInfinity)));
            return _rootElement.Size;
        }
        finally
        {
            _layoutPass = null;
            _rootElement.MarkLayoutDirty();
        }
    }

    private static Widget RequireDispatcher(Widget root, IUiDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        return root;
    }

    private void CommitSignalRevision()
    {
        lock (_signalJobs)
        {
            if (!_isDisposed && _pendingRebuild is null && _signalElements.Count == 0 && _signalJobs.Count == 0 && _pendingSignalObservers.Count == 0)
            {
                CommittedSignalRevision = new(_signalConnections, _signalRevision);
            }
        }
    }

    private void ProcessSignals(bool explicitUpdate)
    {
        if (_isDisposed || _processingSignals || _publishingAnimations || _applyingRebuild || _dispatchDepth != 0 || _recordDepth != 0 || _layoutPass is not null || (!explicitUpdate && !_signalUpdatesEnabled))
        {
            return;
        }

        ThrowSignalFailure();
        _processingSignals = true;
        try
        {
            for (int iteration = 0; iteration < 100; iteration++)
            {
                while (true)
                {
                    SignalJob work;
                    lock (_signalJobs)
                    {
                        _signalPosted = false;
                        if (!_signalJobs.TryDequeue(out work))
                        {
                            break;
                        }
                    }

                    RecordSignalRevision(work.Observer.Revision);
                    lock (_signalJobs)
                    {
                        _pendingSignalObservers.Remove(work.Observer);
                    }

                    work.Work();
                    ThrowSignalFailure();
                }

                if (_isDisposed || _pendingRebuild is not null || _signalElements.Count == 0)
                {
                    CommitSignalRevision();
                    return;
                }

                ApplySignalElements();
            }

            throw new UiStateUpdateException(new InvalidOperationException("Reactive construction did not converge."));
        }
        catch (Exception error)
        {
            FailSignalUpdate(error);
            ThrowSignalFailure();
        }
        finally
        {
            _processingSignals = false;
        }
    }

    private async Task ObserveDispatchAsync(ValueTask work)
    {
        try
        {
            await work.ConfigureAwait(false);
        }
        catch (Exception error)
        {
            if (!_isDisposed)
            {
                FailSignalUpdate(error);
            }
        }
    }

    private void ThrowSignalFailure()
    {
        if (_signalFailure is { } failure)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private readonly record struct SignalJob(UiElement.SignalObserver Observer, Action Work);
}
