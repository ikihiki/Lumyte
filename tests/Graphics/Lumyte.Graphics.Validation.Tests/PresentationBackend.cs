using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation.Tests;

internal sealed class PresentationBackend : IGraphicDevice
{
    public event EventHandler<GraphicsDeviceLostEventArgs>? DeviceLost
    {
        add { }
        remove { }
    }

    public GraphicsDeviceStatus Status => GraphicsDeviceStatus.Ready;

    public GraphicsDeviceLoss? Loss => null;

    public DeviceCaps Caps { get; } = new() { ShaderTarget = ShaderTarget.Wgsl };

    public IGraphicsQueue Queue { get; } = new PresentationQueue();

    public PresentationSemaphore Semaphore { get; } = new();

    public ValueTask WaitForRetirementAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public BufferLayout<T> GetBufferLayout<T>()
        where T : unmanaged => throw new NotSupportedException();

    public TextureCopyLayout GetTextureCopyLayout(TextureFormat format) => throw new NotSupportedException();

    public IGraphicsBuffer<T> CreateBuffer<T>(BufferDesc<T> desc)
        where T : unmanaged => throw new NotSupportedException();

    public IGraphicsTexture CreateTexture(TextureDesc desc) => throw new NotSupportedException();

    public IGraphicsSampler CreateSampler(SamplerDesc desc) => throw new NotSupportedException();

    public IArgumentTable CreateArgumentTable(ArgumentTableDesc desc) => throw new NotSupportedException();

    public IGraphicsShader CreateShader(ShaderArtifact artifact) => throw new NotSupportedException();

    public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc) => throw new NotSupportedException();

    public IGraphicsComputePipeline CreateComputePipeline(ComputePipelineDesc desc) => throw new NotSupportedException();

    public IGraphicsShaderDataBuffer<T> CreateShaderDataBuffer<T>(ShaderArtifact artifact, ulong count, MemoryPreference memory = MemoryPreference.Automatic)
        where T : struct, IShaderArguments => throw new NotSupportedException();

    public IGraphicsSemaphore CreateSemaphore() => Semaphore;

    public IGraphicsCommandBuffer CreateCommandBuffer(CommandBufferDesc desc) => new PresentationCommand();
}
