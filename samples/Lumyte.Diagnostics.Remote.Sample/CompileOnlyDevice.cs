using Lumyte.Graphics.Abstractions;

namespace Lumyte.Diagnostics.Remote.Sample;

// Graph compilation uses declarations only. Every backend operation fails deliberately,
// so the remote diagnostic sample cannot silently pretend to record or execute GPU work.
internal sealed class CompileOnlyDevice : IGraphicDevice
{
    public event EventHandler<GraphicsDeviceLostEventArgs>? DeviceLost
    {
        add => throw Unsupported();
        remove => throw Unsupported();
    }

    public GraphicsDeviceStatus Status => throw Unsupported();

    public GraphicsDeviceLoss? Loss => throw Unsupported();

    public DeviceCaps Caps => throw Unsupported();

    public IGraphicsQueue Queue => throw Unsupported();

    public ValueTask WaitForRetirementAsync(CancellationToken cancellationToken = default) => throw Unsupported();

    public BufferLayout<T> GetBufferLayout<T>()
        where T : unmanaged => throw Unsupported();

    public IGraphicsBuffer<T> CreateBuffer<T>(BufferDesc<T> desc)
        where T : unmanaged => throw Unsupported();

    public IGraphicsTexture CreateTexture(TextureDesc desc) => throw Unsupported();

    public IGraphicsSampler CreateSampler(SamplerDesc desc) => throw Unsupported();

    public IArgumentTable CreateArgumentTable(ArgumentTableDesc desc) => throw Unsupported();

    public IGraphicsShader CreateShader(ShaderArtifact artifact) => throw Unsupported();

    public IGraphicsSemaphore CreateSemaphore() => throw Unsupported();

    public IGraphicsCommandBuffer CreateCommandBuffer(CommandBufferDesc desc) => throw Unsupported();

    public TextureCopyLayout GetTextureCopyLayout(TextureFormat format) => throw Unsupported();

    public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc) => throw Unsupported();

    public IGraphicsComputePipeline CreateComputePipeline(ComputePipelineDesc desc) => throw Unsupported();

    public IGraphicsShaderDataBuffer<T> CreateShaderDataBuffer<T>(ShaderArtifact artifact, ulong count, MemoryPreference memory = MemoryPreference.Automatic)
        where T : struct, IShaderData => throw Unsupported();

    private static NotSupportedException Unsupported() => new("This diagnostic sample only compiles render graphs; GPU operations are unavailable.");
}
