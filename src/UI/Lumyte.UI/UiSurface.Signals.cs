using System.Numerics;
using System.Runtime.ExceptionServices;
using Lumyte.Signals;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

/// <summary>Connects a widget tree with caller-managed synchronization.</summary>
public sealed partial class UiSurface
{
    private readonly Queue<Action> _signalJobs = new();
    private IUiDispatcher? _signalDispatcher;
    private SignalBinding? _signals;
    private bool _signalPosted;
    private bool _updatingSignals;
    private bool _signalUpdatesEnabled = true;
    private ulong _signalConnections;
    private int _dispatchDepth;
    private int _recordDepth;
    private int _rebuildDepth;
    private volatile Exception? _signalFailure;

    /// <summary>Initializes a new instance of the <see cref="UiSurface"/> class.</summary>
    /// <param name="root">The disconnected declaration root.</param>
    /// <param name="dispatcher">The borrowed owner dispatcher.</param>
    public UiSurface(Widget root, IUiDispatcher dispatcher)
        : this(root)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _signalDispatcher = dispatcher;
    }

    /// <summary>Initializes a new instance of the <see cref="UiSurface"/> class.</summary>
    /// <param name="build">Constructs a fresh disconnected tree.</param>
    public UiSurface(Func<Widget> build)
        : this(CaptureBuild(build))
    {
    }

    /// <summary>Initializes a new instance of the <see cref="UiSurface"/> class.</summary>
    /// <param name="build">Constructs a fresh disconnected tree.</param>
    /// <param name="dispatcher">The borrowed owner dispatcher.</param>
    public UiSurface(Func<Widget> build, IUiDispatcher dispatcher)
        : this(CaptureBuild(build), dispatcher)
    {
    }

    /// <summary>Gets the connection generation and latest dependency change, including while paused.</summary>
    public UiSignalRevision SignalRevision => new(_signalConnections, Math.Max(_signals?.Revision ?? 0, _pendingRebuild?.Signals?.Revision ?? 0));

    /// <summary>Pauses or resumes automatic application; explicit layout still prepares current values.</summary>
    /// <param name="enabled">Whether to apply automatically at owner boundaries.</param>
    public void SetSignalUpdatesEnabled(bool enabled)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (enabled)
        {
            ThrowSignalFailure();
        }

        if (_signalUpdatesEnabled == enabled)
        {
            return;
        }

        _signalUpdatesEnabled = enabled;
        if (enabled)
        {
            _signals?.Resume();
        }
        else
        {
            _signals?.Pause();
        }
    }

    /// <summary>Queues a newly evaluated construction function at the normal rebuild boundary.</summary>
    /// <param name="build">Constructs a fresh disconnected declaration.</param>
    public void Rebuild(Func<Widget> build) => Rebuild(CaptureBuild(build));

    internal static Widget CaptureBuild(Func<Widget> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        Widget root = Signal.CaptureReads(build, out SignalReadSet reads) ?? throw new InvalidOperationException("Construction returned no declaration.");
        root.Construction = build;
        root.ConstructionReads = reads;
        return root;
    }

    internal Vector2 MeasureDesiredSize()
    {
        ThrowIfDisposed();
        UpdateTree();
        var pass = new UiLayoutPass();
        _layoutPass = pass;
        try
        {
            pass.Compute(_root, UiBoxConstraints.Loose(new(float.PositiveInfinity, float.PositiveInfinity)));
            return _root.Size;
        }
        finally
        {
            _layoutPass = null;
            _root.MarkLayoutDirty();
        }
    }

    internal void RefreshSignalArguments()
    {
        SignalBinding? next = PrepareSignals(_root);
        SignalBinding? previous = _signals;
        _signals = next;
        _signalConnections = checked(_signalConnections + 1);
        previous?.Dispose();
        if (!_signalUpdatesEnabled)
        {
            next?.Pause();
        }
    }

    private SignalBinding? PrepareSignals(Widget root)
    {
        Func<bool>[] readers = root.CollectSubtree().SelectMany(widget => widget.SignalArguments).ToArray();
        SignalReadSet? reads = root.ConstructionReads;
        if (readers.Length == 0 && (reads is null || reads.IsEmpty))
        {
            return null;
        }

        var recipe = UiDeclarationRecipe.Capture(root);
        Func<Widget>? build = reads is { IsEmpty: false } ? root.Construction : recipe is not null ? recipe.Build : root.Construction;
        if (build is null)
        {
            throw new InvalidOperationException("Signal declarations need generated composition factories or a surface construction function.");
        }

        var binding = new SignalBinding(this, build, reads, readers);
        binding.Initialize();
        return binding;
    }

    private void ProcessSignals(bool explicitUpdate)
    {
        if (_isDisposed || _updatingSignals || _dispatchDepth != 0 || _rebuildDepth != 0 || _layoutPass is not null || _recordDepth != 0 || (!explicitUpdate && !_signalUpdatesEnabled))
        {
            return;
        }

        _updatingSignals = true;
        try
        {
            while (true)
            {
                Action? work;
                lock (_signalJobs)
                {
                    _signalPosted = false;
                    if (!_signalJobs.TryDequeue(out work))
                    {
                        break;
                    }
                }

                work();
            }

            SignalBinding? binding = _pendingRebuild is { } pending ? pending.Signals : _signals;
            if (binding is not null && (binding.Dirty || binding.Effect!.IsPending))
            {
                Widget next = CaptureBuild(binding.Build);
                if (_isDisposed)
                {
                    return;
                }

                Rebuild(next);
            }
        }
        catch (Exception error)
        {
            _signalFailure = error is UiStateUpdateException ? error : new UiStateUpdateException(error);
            ExceptionDispatchInfo.Capture(_signalFailure).Throw();
        }
        finally
        {
            _updatingSignals = false;
        }

        ThrowSignalFailure();
    }

    private void PostSignal(Action work)
    {
        bool post;
        lock (_signalJobs)
        {
            if (_isDisposed)
            {
                return;
            }

            _signalJobs.Enqueue(work);
            post = !_signalPosted;
            _signalPosted = true;
        }

        if (!post)
        {
            return;
        }

        if (_signalDispatcher is { } dispatcher)
        {
            _ = ObserveDispatchAsync(dispatcher.InvokeAsync(() =>
            {
                ProcessSignals(false);
                if (!_isDisposed && _signalUpdatesEnabled && _dispatchDepth == 0 && _layoutPass is null && _recordDepth == 0)
                {
                    ApplyRebuild();
                }
            }));
        }
        else
        {
            ProcessSignals(false);
            if (!_isDisposed && _signalUpdatesEnabled && _dispatchDepth == 0 && _layoutPass is null && _recordDepth == 0)
            {
                ApplyRebuild();
            }
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
                _signalFailure = error is UiStateUpdateException ? error : new UiStateUpdateException(error);
            }
        }
    }

    private void ThrowSignalFailure()
    {
        if (_signalFailure is not null)
        {
            ExceptionDispatchInfo.Capture(_signalFailure).Throw();
        }
    }

    private void ReleaseSignals()
    {
        _signals?.Dispose();
        _pendingRebuild?.DisposeSignals();
        _signals = null;
        _pendingRebuild = null;
        lock (_signalJobs)
        {
            _signalJobs.Clear();
            _signalPosted = false;
        }
    }

    private sealed class SignalBinding(UiSurface surface, Func<Widget> build, SignalReadSet? reads, Func<bool>[] readers) : ISignalScheduler, IDisposable
    {
        private readonly SignalScope _scope = new();

        internal Func<Widget> Build { get; } = build;

        internal SignalEffect? Effect { get; private set; }

        internal bool Dirty { get; private set; }

        internal ulong Revision => Effect?.Revision ?? 0;

        public void Schedule(Action work) => surface.PostSignal(work);

        public void Dispose() => _scope.Dispose();

        internal void Initialize()
        {
            _scope.Run(() => Effect = Signal.Effect(
                () =>
                {
                    foreach (Func<bool> reader in readers)
                    {
                        Dirty |= reader();
                    }

                    if (reads is not null)
                    {
                        reads.Read();
                        Dirty |= !reads.IsCurrent;
                    }
                },
                this,
                error => surface._signalFailure = new UiStateUpdateException(error)));
        }

        internal void Pause() => Effect?.Pause();

        internal void Resume() => Effect?.Resume();
    }
}
