namespace Lumyte.Graphics.Planning;

/// <summary>Declares graphics frame scope.</summary>
public sealed class GraphicsFrameScope : IDisposable
{
    private readonly CleanupLedger _leases = new();
    private bool _compiled;
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

    /// <summary>Declares compile async.</summary>
    /// <param name = "cancellationToken">The cancellation token.</param>
    /// <returns>The declared result.</returns>
    public ValueTask<CompiledGraphicsPlan> CompileAsync(CancellationToken cancellationToken = default)
    {
        Session.Validate();
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _declarationScope) != 0)
        {
            throw new InvalidOperationException("Complete every typed graphics declaration before compiling the frame.");
        }

        if (_compiled)
        {
            throw new InvalidOperationException("A frame compiles once.");
        }

        _compiled = true;
        CompiledGraphicsPlan result = Plan.Compile();
        return ValueTask.FromResult(result);
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

        if (_activePlans != 0)
        {
            throw new InvalidOperationException("Compiled/prepared/executing plans still own this frame.");
        }

        Session.Execution.VerifyAccess();
        _leases.Cleanup(Session.Budget.CleanupAttempts);
        IsDisposed = true;
        Session.ReleaseFrame(this);
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
}
