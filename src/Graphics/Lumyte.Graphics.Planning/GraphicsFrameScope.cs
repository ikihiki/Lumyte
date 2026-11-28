namespace Lumyte.Graphics.Planning;

/// <summary>Declares graphics frame scope.</summary>
public sealed class GraphicsFrameScope : IDisposable
{
    private readonly CleanupLedger _leases = new();
    private readonly object _lifetimeGate = new();
    private volatile bool _retiring;
    private bool _disposing;
    private int _consumed;
    private CompiledGraphicsPlan? _compiled;
    private GraphicsExecution? _execution;
    private bool _executing;
    private bool _invalidDeclarations;
    private int _declarationScope;
    private int _activePlans;

    internal GraphicsFrameScope(GraphicsDeviceSession session)
    {
        Session = session;
        Plan = new(this);
    }

    /// <summary>Gets plan.</summary>
    public GraphicsPlanBuilder Plan { get; }

    internal GraphicsDeviceSession Session { get; }

    internal bool IsDisposed { get; private set; }

    internal bool IsConsumed => Volatile.Read(ref _consumed) != 0 || _retiring;

    /// <summary>Declares compile async.</summary>
    /// <param name = "cancellationToken">The cancellation token.</param>
    /// <returns>The declared result.</returns>
    public ValueTask<CompiledGraphicsPlan> CompileAsync(CancellationToken cancellationToken = default)
    {
        Consume();
        try
        {
            return ValueTask.FromResult(CompileCore(cancellationToken));
        }
        finally
        {
            EndOperation();
        }
    }

    /// <summary>Compiles, prepares and executes this frame once, then retires its owners after GPU completion.</summary>
    /// <param name="cancellationToken">Cancellation stops admission or waiting, not submitted GPU work.</param>
    /// <returns>Completion of GPU execution and safe frame retirement.</returns>
    public ValueTask ExecuteAsync(CancellationToken cancellationToken = default)
    {
        Consume();
        return ExecuteCoreAsync(cancellationToken);
    }

    /// <summary>Declares retire async.</summary>
    /// <returns>The declared result.</returns>
    public ValueTask RetireAsync() => Session.Execution.RunAsync(Dispose, retiring: true);

    /// <summary>Declares dispose.</summary>
    public void Dispose()
    {
        if (IsDisposed)
        {
            return;
        }

        Session.Execution.VerifyAccess();
        lock (_lifetimeGate)
        {
            if (_executing || _disposing)
            {
                throw new InvalidOperationException("Frame compilation, execution or disposal is still active.");
            }

            _retiring = true;
            _disposing = true;
        }

        try
        {
            DisposeCore();
        }
        finally
        {
            lock (_lifetimeGate)
            {
                _disposing = false;
            }
        }
    }

    internal void Own(IDisposable lease)
    {
        Session.Validate();
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (_leases.Count >= Session.Budget.MaximumResources)
        {
            throw new InvalidOperationException("Frame import lease reservation exceeded.");
        }

        _leases.Add(lease);
    }

    internal void BeginDeclaration()
    {
        if (Interlocked.CompareExchange(ref _declarationScope, 1, 0) != 0)
        {
            throw new NotSupportedException("Concurrent graphics declaration invocations require explicit serial ordering in the typed graph.");
        }
    }

    internal void EndDeclaration() => Volatile.Write(ref _declarationScope, 0);

    internal void InvalidateDeclarations() => _invalidDeclarations = true;

    internal void ValidateDeclarations()
    {
        if (_invalidDeclarations)
        {
            throw new InvalidOperationException("A failed declaration invocation invalidated this frame.");
        }
    }

    internal void Retain() => _activePlans++;

    internal void Release() => _activePlans--;

    private void Consume()
    {
        lock (_lifetimeGate)
        {
            if (_retiring || Interlocked.CompareExchange(ref _consumed, 1, 0) != 0)
            {
                throw new InvalidOperationException("A frame compiles or executes once and cannot start during retirement.");
            }

            _executing = true;
        }
    }

    private void EndOperation()
    {
        lock (_lifetimeGate)
        {
            _executing = false;
        }
    }

    private CompiledGraphicsPlan CompileCore(CancellationToken cancellationToken)
    {
        Session.Validate();
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _declarationScope) != 0)
        {
            throw new InvalidOperationException("Complete every typed graphics declaration before compiling the frame.");
        }

        return Plan.Compile();
    }

    private async ValueTask ExecuteCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            _compiled = CompileCore(cancellationToken);
            PreparedGraphicsExecution prepared = await _compiled.PrepareAsync(cancellationToken).ConfigureAwait(false);
            _execution = Session.CreateExecution(prepared);
            await _execution.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Preserve the primary exception. The session retains this frame and any
            // claimed execution; Retire/Drain retry cleanup, never submission.
            EndOperation();
            try
            {
                await RetireAsync().ConfigureAwait(false);
            }
            catch
            {
                // Retryable owners remain reachable through the session.
            }

            throw;
        }

        EndOperation();
        await RetireAsync().ConfigureAwait(false);
    }

    private void DisposeCore()
    {
        // Claimed work must retire first. Its quarantine guard refuses unsafe disposal.
        _execution?.Dispose();
        if (_compiled is not null)
        {
            _compiled.CleanupPreparation();
            _compiled.Dispose();
            _compiled = null;
        }

        if (_activePlans != 0)
        {
            throw new InvalidOperationException("Compiled/prepared/executing plans still own this frame.");
        }

        _leases.Cleanup(Session.Budget.CleanupAttempts);
        IsDisposed = true;
        Session.ReleaseFrame(this);
    }
}
