using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D.Tests;

internal sealed class DispatchDevice : IGraphicDevice
{
    public DeviceCaps Caps { get; } = new() { MaxTextureDimension2D = 4096 };

    public IGraphicsQueue Queue => throw new InvalidOperationException("Drawing preparation must not access the queue.");

    public BufferLayout<T> GetBufferLayout<T>()
        where T : unmanaged => throw new NotSupportedException();

    public IGraphicsBuffer<T> CreateBuffer<T>(BufferDesc<T> desc)
        where T : unmanaged => throw new InvalidOperationException("Unexpected GPU allocation during external renderer dispatch.");

    public IGraphicsTexture CreateTexture(TextureDesc desc) => throw new InvalidOperationException("Unexpected GPU allocation during external renderer dispatch.");

    public IGraphicsSampler CreateSampler(SamplerDesc desc) => throw new NotSupportedException();

    public IArgumentTable CreateArgumentTable(ArgumentTableDesc desc) => throw new NotSupportedException();

    public IGraphicsShader CreateShader(ShaderArtifact artifact) => throw new NotSupportedException();

    public IGraphicsSemaphore CreateSemaphore() => throw new NotSupportedException();

    public IGraphicsCommandBuffer CreateCommandBuffer(CommandBufferDesc desc) => new DispatchCommands();

    public TextureCopyLayout GetTextureCopyLayout(TextureFormat format) => throw new NotSupportedException();

    public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc) => throw new NotSupportedException();

    public IGraphicsComputePipeline CreateComputePipeline(ComputePipelineDesc desc) => throw new NotSupportedException();

    public IGraphicsShaderDataBuffer<T> CreateBuffer<T>(ShaderArtifact artifact, ulong count, MemoryPreference memory = MemoryPreference.Automatic)
        where T : struct, IShaderData => throw new NotSupportedException();
}
