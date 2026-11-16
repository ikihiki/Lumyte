using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation.Tests;

internal sealed class FactoryBackend : IGraphicDevice
{
    public event EventHandler<GraphicsDeviceLostEventArgs>? DeviceLost
    {
        add { }
        remove { }
    }

    public GraphicsDeviceStatus Status => GraphicsDeviceStatus.Ready;

    public GraphicsDeviceLoss? Loss => null;

    public DeviceCaps Caps { get; } = new() { ShaderTarget = ShaderTarget.Wgsl };

    public IGraphicsQueue Queue { get; } = new FactoryQueue();

    internal FactoryShader? Shader { get; private set; }

    internal FactoryTexture? Texture { get; private set; }

    internal GraphicsPipelineDesc? GraphicsDesc { get; private set; }

    internal ComputePipelineDesc? ComputeDesc { get; private set; }

    internal Action? BeforeShaderReturn { get; set; }

    internal bool ReuseShader { get; set; }

    internal Exception? Failure { get; set; }

    public ValueTask WaitForRetirementAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public BufferLayout<T> GetBufferLayout<T>()
        where T : unmanaged => throw new NotSupportedException();

    public TextureCopyLayout GetTextureCopyLayout(TextureFormat format) => throw new NotSupportedException();

    public IGraphicsBuffer<T> CreateBuffer<T>(BufferDesc<T> desc)
        where T : unmanaged => throw new NotSupportedException();

    public IGraphicsTexture CreateTexture(TextureDesc desc) => Texture = new FactoryTexture(desc);

    public IGraphicsSampler CreateSampler(SamplerDesc desc) => throw new NotSupportedException();

    public IArgumentTable CreateArgumentTable(ArgumentTableDesc desc) => throw new NotSupportedException();

    public IGraphicsShader CreateShader(ShaderArtifact artifact)
    {
        if (Failure is { } failure)
        {
            throw failure;
        }

        BeforeShaderReturn?.Invoke();
        return ReuseShader && Shader is not null ? Shader : Shader = new FactoryShader(artifact);
    }

    public IGraphicsSemaphore CreateSemaphore() => throw new NotSupportedException();

    public IGraphicsCommandBuffer CreateCommandBuffer(CommandBufferDesc desc) => throw new NotSupportedException();

    public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc)
    {
        GraphicsDesc = desc;
        return new FactoryPipeline(desc);
    }

    public IGraphicsComputePipeline CreateComputePipeline(ComputePipelineDesc desc)
    {
        ComputeDesc = desc;
        return new FactoryComputePipeline(desc);
    }

    public IGraphicsShaderDataBuffer<T> CreateShaderDataBuffer<T>(ShaderArtifact artifact, ulong count, MemoryPreference memory = MemoryPreference.Automatic)
        where T : struct, IShaderArguments => throw new NotSupportedException();

    private sealed class FactoryQueue : IGraphicsQueue
    {
        public IGraphicsSubmission Submit(IReadOnlyList<IGraphicsCommandBuffer> commandBuffers) => throw new NotSupportedException();

        public IGraphicsSubmission Submit(QueueSubmitDesc desc) => throw new NotSupportedException();
    }
}
