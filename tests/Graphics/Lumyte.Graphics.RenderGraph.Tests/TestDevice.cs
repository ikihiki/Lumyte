using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph.Tests;

internal sealed class TestDevice : IGraphicDevice
{
    private int _allocations;

    public DeviceCaps Caps => throw new NotSupportedException();

    public IGraphicsQueue Queue => throw new NotSupportedException();

    internal List<string> Lifetime { get; } = [];

    internal int FailureOnAllocation { get; init; }

    internal int Textures { get; private set; }

    internal int Buffers { get; private set; }

    public IGraphicsTexture CreateTexture(TextureDesc desc)
    {
        Allocate();
        Textures++;
        return new TestTexture
        {
            Width = desc.Width,
            Height = desc.Height,
            MipLevels = desc.MipLevels,
            ArrayLayers = desc.ArrayLayers,
            Format = desc.Format,
            Usage = desc.Usage,
            Lifetime = Lifetime,
        };
    }

    public IGraphicsBuffer<T> CreateBuffer<T>(BufferDesc<T> desc)
        where T : unmanaged
    {
        Allocate();
        Buffers++;
        return new TestTypedBuffer<T>(desc, Lifetime);
    }

    public BufferLayout<T> GetBufferLayout<T>()
        where T : unmanaged
    {
        ulong size = (ulong)System.Runtime.CompilerServices.Unsafe.SizeOf<T>();
        return new(size, size, 1, 1);
    }

    public IGraphicsSemaphore CreateSemaphore() => throw new NotSupportedException();

    public IGraphicsCommandBuffer CreateCommandBuffer(CommandBufferDesc desc) => new RecordingCommands();

    public TextureCopyLayout GetTextureCopyLayout(TextureFormat format) => throw new NotSupportedException();

    public IGraphicsSampler CreateSampler(SamplerDesc desc) => throw new NotSupportedException();

    public IArgumentTable CreateArgumentTable(ArgumentTableDesc desc) => throw new NotSupportedException();

    public IGraphicsShader CreateShader(ShaderArtifact artifact) => throw new NotSupportedException();

    public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc) => throw new NotSupportedException();

    public IGraphicsComputePipeline CreateComputePipeline(ComputePipelineDesc desc) => throw new NotSupportedException();

    public IGraphicsShaderDataBuffer<T> CreateBuffer<T>(ShaderArtifact artifact, ulong count, MemoryPreference memory = MemoryPreference.Automatic)
        where T : struct, IShaderData => throw new NotSupportedException();

    private void Allocate()
    {
        if (++_allocations == FailureOnAllocation)
        {
            throw new InvalidOperationException("Simulated allocation failure.");
        }
    }
}
