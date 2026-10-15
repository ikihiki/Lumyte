using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Tests;

internal sealed class ProgramTestDevice : IGraphicDevice
{
    private int _creations;

    public event EventHandler<GraphicsDeviceLostEventArgs>? DeviceLost
    {
        add
        {
        }

        remove
        {
        }
    }

    public GraphicsDeviceStatus Status => GraphicsDeviceStatus.Ready;

    public GraphicsDeviceLoss? Loss => null;

    public DeviceCaps Caps { get; } = new() { MaxTextureDimension2D = 4096 };

    public IGraphicsQueue Queue => throw new InvalidOperationException("Drawing preparation must not access the queue.");

    internal List<string> Released { get; } = [];

    internal int FailOnCreation { get; init; }

    public ValueTask WaitForRetirementAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Drawing preparation must not wait for device retirement.");

    public BufferLayout<T> GetBufferLayout<T>()
        where T : unmanaged => throw new NotSupportedException();

    public IGraphicsBuffer<T> CreateBuffer<T>(BufferDesc<T> desc)
        where T : unmanaged => throw new InvalidOperationException("Unexpected GPU allocation during external renderer dispatch.");

    public IGraphicsTexture CreateTexture(TextureDesc desc) => throw new InvalidOperationException("Unexpected GPU allocation during external renderer dispatch.");

    public IGraphicsSampler CreateSampler(SamplerDesc desc) => throw new NotSupportedException();

    public IArgumentTable CreateArgumentTable(ArgumentTableDesc desc) => throw new NotSupportedException();

    public IGraphicsShader CreateShader(ShaderArtifact artifact)
    {
        int index = Create();
        return new ProgramTestShader(artifact, () => Released.Add(index == 1 ? "vertex" : "fragment"));
    }

    public IGraphicsSemaphore CreateSemaphore() => throw new NotSupportedException();

    public IGraphicsCommandBuffer CreateCommandBuffer(CommandBufferDesc desc) => throw new NotSupportedException();

    public TextureCopyLayout GetTextureCopyLayout(TextureFormat format) => throw new NotSupportedException();

    public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc)
    {
        Create();
        return new ProgramTestPipeline(desc, () => Released.Add("pipeline"));
    }

    public IGraphicsComputePipeline CreateComputePipeline(ComputePipelineDesc desc) => throw new NotSupportedException();

    public IGraphicsShaderDataBuffer<T> CreateBuffer<T>(ShaderArtifact artifact, ulong count, MemoryPreference memory = MemoryPreference.Automatic)
        where T : struct, IShaderData => throw new NotSupportedException();

    private int Create()
    {
        if (++_creations == FailOnCreation)
        {
            throw new InvalidOperationException("Injected native creation failure.");
        }

        return _creations;
    }
}
