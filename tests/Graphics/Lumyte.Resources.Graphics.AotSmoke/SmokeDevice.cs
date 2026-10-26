namespace Lumyte.Resources.Graphics.AotSmoke;

internal sealed class SmokeDevice : IGraphicDevice, IGraphicsDeviceOwner
{
    public event EventHandler<GraphicsDeviceLostEventArgs>? DeviceLost
    {
        add { }
        remove { }
    }

    public GraphicsDeviceStatus Status { get; private set; } = GraphicsDeviceStatus.Ready;

    public GraphicsDeviceLoss? Loss => null;

    public IGraphicDevice Device => this;

    public DeviceCaps Caps => throw new NotSupportedException();

    public IGraphicsQueue Queue => throw new NotSupportedException();

    internal List<SmokeShader> Shaders { get; } = [];

    public BufferLayout<T> GetBufferLayout<T>()
        where T : unmanaged => throw new NotSupportedException();

    public IGraphicsBuffer<T> CreateBuffer<T>(BufferDesc<T> desc)
        where T : unmanaged => throw new NotSupportedException();

    public IGraphicsTexture CreateTexture(TextureDesc desc) => throw new NotSupportedException();

    public IGraphicsSampler CreateSampler(SamplerDesc desc) => throw new NotSupportedException();

    public IArgumentTable CreateArgumentTable(ArgumentTableDesc desc) => throw new NotSupportedException();

    public IGraphicsShader CreateShader(ShaderArtifact artifact)
    {
        var shader = new SmokeShader(artifact);
        Shaders.Add(shader);
        return shader;
    }

    public IGraphicsSemaphore CreateSemaphore() => throw new NotSupportedException();

    public IGraphicsCommandBuffer CreateCommandBuffer(CommandBufferDesc desc) => throw new NotSupportedException();

    public TextureCopyLayout GetTextureCopyLayout(TextureFormat format) => throw new NotSupportedException();

    public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc) => throw new NotSupportedException();

    public IGraphicsComputePipeline CreateComputePipeline(ComputePipelineDesc desc) => throw new NotSupportedException();

    public IGraphicsShaderDataBuffer<T> CreateBuffer<T>(ShaderArtifact artifact, ulong count, MemoryPreference memory = MemoryPreference.Automatic)
        where T : struct, IShaderData => throw new NotSupportedException();

    public ValueTask WaitForRetirementAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public ValueTask DisposeAsync()
    {
        Status = GraphicsDeviceStatus.Disposed;
        return ValueTask.CompletedTask;
    }
}
