using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

/// <summary>Declares graphics device session.</summary>
public sealed class GraphicsDeviceSession : IDisposable
{
    private readonly CleanupLedger _artifacts = new();
    private readonly List<GraphicsFrameScope> _frames = [];
    private readonly List<GraphicsExecution> _executions = [];
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref = "GraphicsDeviceSession"/> class.Initializes a new instance of the graphics planning owner.</summary>
    /// <param name = "execution">The execution.</param>
    /// <param name = "device">The device.</param>
    /// <param name = "budget">The budget.</param>
    public GraphicsDeviceSession(GraphicsExecutionModule execution, IGraphicDevice device, GraphicsPlanningBudget? budget = null)
    {
        Execution = execution ?? throw new ArgumentNullException(nameof(execution));
        Device = device ?? throw new ArgumentNullException(nameof(device));
        Budget = budget ?? new();
        if (Budget.CleanupAttempts < 1 || Budget.MaximumPasses < 1 || Budget.MaximumResources < 1 || Budget.MaximumAllocationBytes == 0 || Budget.MaximumArgumentBytes == 0 || Budget.MaximumBarriers < 1 || Budget.MaximumResourceUses < 1 || Budget.MaximumPhysicalBindingUses < 1 || Budget.MaximumShaderDataElements < 1 || Budget.MaximumResourceVersions < 1 || Budget.MaximumCommands < 1 || Budget.MaximumTableReferences < 1 || Budget.MaximumInFlight != 1)
        {
            throw new ArgumentOutOfRangeException(nameof(budget));
        }
    }

    /// <summary>Gets the number of frame owners still retained by this session.</summary>
    public int ActiveFrameCount => _frames.Count;

    /// <summary>Gets the number of active, failed or quarantined executions.</summary>
    public int ActiveExecutionCount => _executions.Count;

    internal GraphicsExecutionModule Execution { get; }

    internal IGraphicDevice Device { get; }

    internal GraphicsPlanningBudget Budget { get; }

    /// <summary>Declares create frame.</summary>
    /// <returns>The declared result.</returns>
    public GraphicsFrameScope CreateFrame()
    {
        Validate();
        if (_frames.Any(f => !f.IsDisposed))
        {
            throw new InvalidOperationException("This initial backend requires exclusive frame interop; retire the previous frame first.");
        }

        var frame = new GraphicsFrameScope(this);
        _frames.Add(frame);
        return frame;
    }

    /// <summary>Declares import compute kernel.</summary>
    /// <param name = "pipeline">The pipeline.</param>
    /// <param name = "useLease">The use lease.</param>
    /// <typeparam name = "TArguments">The typed shader or resource contract.</typeparam>
    /// <returns>The declared result.</returns>
    public ComputeKernel<TArguments> ImportComputeKernel<TArguments>(IGraphicsComputePipeline pipeline, IDisposable useLease)
        where TArguments : struct, IShaderArguments
    {
        Validate();
        ArgumentNullException.ThrowIfNull(pipeline);
        OwnArtifact(useLease);
        return new(this, pipeline);
    }

    /// <summary>Declares import raster kernel.</summary>
    /// <param name = "pipeline">The pipeline.</param>
    /// <param name = "useLease">The use lease.</param>
    /// <typeparam name = "TArguments">The typed shader or resource contract.</typeparam>
    /// <returns>The declared result.</returns>
    public RasterKernel<TArguments> ImportRasterKernel<TArguments>(IGraphicsPipeline pipeline, IDisposable useLease)
        where TArguments : struct, IShaderArguments
    {
        Validate();
        ArgumentNullException.ThrowIfNull(pipeline);
        OwnArtifact(useLease);
        return new(this, pipeline);
    }

    /// <summary>Imports an independently leased mesh program without exposing native ownership to stages.</summary>
    /// <typeparam name="TArguments">The exact shader argument ABI.</typeparam>
    /// <param name="pipeline">The immutable native mesh program.</param>
    /// <param name="useLease">The independent session use lease.</param>
    /// <returns>The session-scoped typed mesh authority.</returns>
    public MeshKernel<TArguments> ImportMeshKernel<TArguments>(IMeshPipeline pipeline, IDisposable useLease)
        where TArguments : struct, IShaderArguments
    {
        Validate();
        ArgumentNullException.ThrowIfNull(pipeline);
        OwnArtifact(useLease);
        return new(this, pipeline);
    }

    /// <summary>Imports an independently leased sampler without transferring native allocation ownership.</summary>
    /// <param name = "sampler">The immutable native sampler.</param>
    /// <param name = "useLease">The independently retained use authority.</param>
    /// <returns>A session-scoped symbolic sampler handle.</returns>
    public PlanSampler ImportSampler(IGraphicsSampler sampler, IDisposable useLease)
    {
        Validate();
        ArgumentNullException.ThrowIfNull(sampler);
        OwnArtifact(useLease);
        return new(this, sampler);
    }

    /// <summary>Declares create execution.</summary>
    /// <param name = "prepared">The prepared.</param>
    /// <returns>The declared result.</returns>
    public GraphicsExecution CreateExecution(PreparedGraphicsExecution prepared)
    {
        Validate();
        if (prepared.Session != this)
        {
            throw new ArgumentException("Foreign prepared execution.");
        }

        if (_executions.Count(e => !e.IsDisposed) >= Budget.MaximumInFlight)
        {
            throw new InvalidOperationException("In-flight execution budget exceeded.");
        }

        prepared.Claim();
        var execution = new GraphicsExecution(this, prepared);
        _executions.Add(execution);
        return execution;
    }

    /// <summary>Declares drain async.</summary>
    /// <param name = "cancellationToken">The cancellation token.</param>
    /// <returns>The declared result.</returns>
    public async ValueTask DrainAsync(CancellationToken cancellationToken = default)
    {
        Validate();
        foreach (GraphicsExecution? execution in _executions.Where(e => !e.IsDisposed).ToArray())
        {
            await execution.DrainAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Declares recover retired device async.</summary>
    /// <param name = "cancellationToken">The cancellation token.</param>
    /// <returns>The declared result.</returns>
    public async ValueTask RecoverRetiredDeviceAsync(CancellationToken cancellationToken = default)
    {
        Validate();
        if (Device.Status == GraphicsDeviceStatus.Ready)
        {
            throw new InvalidOperationException("An active generation is not a retirement proof.");
        }

        ValueTask retirement = default;
        await Execution.RunAsync(() => retirement = Device.WaitForRetirementAsync(cancellationToken), retiring: true).ConfigureAwait(false);
        await retirement.ConfigureAwait(false);
        foreach (GraphicsExecution? execution in _executions.Where(e => !e.IsDisposed).ToArray())
        {
            await Execution.RunAsync(execution.RecoverRetiredDevice, retiring: true).ConfigureAwait(false);
        }
    }

    /// <summary>Declares retire async.</summary>
    /// <returns>The declared result.</returns>
    public ValueTask RetireAsync() => Execution.RunAsync(Dispose, retiring: true);

    /// <summary>Declares dispose.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_frames.Any(f => !f.IsDisposed) || _executions.Any(e => !e.IsDisposed))
        {
            throw new InvalidOperationException("Active or quarantined owners prevent session disposal; drain explicitly.");
        }

        Execution.VerifyAccess();
        _artifacts.Cleanup(Budget.CleanupAttempts);
        _disposed = true;
    }

    internal void ReleaseFrame(GraphicsFrameScope frame) => _frames.Remove(frame);

    internal void ReleaseExecution(GraphicsExecution execution) => _executions.Remove(execution);

    internal void Validate() => ObjectDisposedException.ThrowIf(_disposed, this);

    private void OwnArtifact(IDisposable useLease)
    {
        if (_artifacts.Count >= Budget.MaximumResources)
        {
            throw new InvalidOperationException("Session artifact lease reservation exceeded.");
        }

        _artifacts.Add(useLease);
    }
}
