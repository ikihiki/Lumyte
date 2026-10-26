using Lumyte.Graphics.Abstractions;

namespace Lumyte.Resources.Graphics;

/// <summary>Owns a pipeline and shader pair on the original device epoch, with retryable cleanup.</summary>
public sealed class GraphicsProgramResource
{
    private readonly GraphicsResourceRuntime _runtime;
    private IGraphicsShader? _vertex;
    private IGraphicsShader? _fragment;
    private IGraphicsPipeline? _pipeline;
    private GraphicsProgramResources? _resources;

    internal GraphicsProgramResource(GraphicsResourceRuntime runtime, IGraphicDevice device, ulong epoch)
    {
        _runtime = runtime;
        Device = device;
        Epoch = epoch;
    }

    /// <summary>Gets the physical device that owns this program.</summary>
    public IGraphicDevice Device { get; }

    /// <summary>Gets the physical epoch captured during construction.</summary>
    public ulong Epoch { get; }

    /// <summary>Gets the borrowed program retained by the resource lease.</summary>
    public GraphicsProgramResources Resources => _resources ?? throw new InvalidOperationException("The program is not available.");

    /// <summary>Creates a borrowed drawing-program view; the caller retains the pipeline's resource dependency.</summary>
    /// <param name="pipeline">The shared pipeline resource with a fragment shader.</param>
    /// <param name="sampler">The optional shared sampler, retained by the same caller.</param>
    /// <returns>A nonowning adapter for drawing program leases.</returns>
    public static GraphicsProgramResource FromPipeline(GraphicsResource<IGraphicsPipeline> pipeline, GraphicsResource<IGraphicsSampler>? sampler = null)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        if (sampler is not null && !ReferenceEquals(pipeline.Device, sampler.Device))
        {
            throw new ArgumentException("The shared sampler belongs to a different device.", nameof(sampler));
        }

        IGraphicsPipeline native = pipeline.Value;
        ShaderArtifact fragment = native.Desc.FragmentShader?.Artifact
            ?? throw new ArgumentException("A drawing program requires a fragment shader.", nameof(pipeline));
        return new(pipeline.Runtime, pipeline.Device, pipeline.Epoch)
        {
            _resources = new(pipeline.Device, native, fragment, sampler?.Value),
        };
    }

    internal void Create(GraphicsProgramArtifacts artifacts, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _vertex = Device.CreateShader(artifacts.Vertex);
        cancellationToken.ThrowIfCancellationRequested();
        _fragment = Device.CreateShader(artifacts.Fragment);
        cancellationToken.ThrowIfCancellationRequested();
        _pipeline = Device.CreateGraphicsPipeline(new() { VertexShader = _vertex, FragmentShader = _fragment });
        cancellationToken.ThrowIfCancellationRequested();
        _resources = new(Device, _pipeline, artifacts.Fragment);
    }

    internal async ValueTask ReleaseAsync()
    {
        if (_pipeline is null && _fragment is null && _vertex is null)
        {
            return;
        }

        if (Device.Status == GraphicsDeviceStatus.Lost)
        {
            await _runtime.WaitForRetirementAsync(Epoch);
        }

        _pipeline?.Dispose();
        _pipeline = null;
        _fragment?.Dispose();
        _fragment = null;
        _vertex?.Dispose();
        _vertex = null;
        _resources = null;
    }
}
