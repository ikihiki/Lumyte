using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation.Tests;

internal sealed class QueueBackend : IGraphicDevice, IDisposable
{
    public event EventHandler<GraphicsDeviceLostEventArgs>? DeviceLost;

    public GraphicsDeviceStatus Status { get; private set; } = GraphicsDeviceStatus.Ready;

    public GraphicsDeviceLoss? Loss { get; private set; }

    public DeviceCaps Caps { get; } = new();

    public IGraphicsQueue Queue => NativeQueue;

    internal QueueNative NativeQueue { get; } = new();

    internal int DisposeCount { get; private set; }

    internal int Subscribers => DeviceLost?.GetInvocationList().Length ?? 0;

    internal Action? DuringCreate { get; set; }

    internal PresentationSemaphore? ReusedSemaphore { get; set; }

    public ValueTask WaitForRetirementAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    public void Lose()
    {
        Status = GraphicsDeviceStatus.Lost;
        Loss = new("test", "retired");
        DeviceLost?.Invoke(this, new(Loss));
    }

    public void Dispose() => DisposeCount++;

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

    public IGraphicsSemaphore CreateSemaphore()
    {
        DuringCreate?.Invoke();
        return ReusedSemaphore ?? new PresentationSemaphore();
    }

    public IGraphicsCommandBuffer CreateCommandBuffer(CommandBufferDesc desc) => new PresentationCommand();
}
