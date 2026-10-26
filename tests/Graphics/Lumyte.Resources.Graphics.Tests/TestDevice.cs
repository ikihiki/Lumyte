using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Hosting;
using Lumyte.Graphics.Shared;

namespace Lumyte.Resources.Graphics.Tests;

internal sealed class TestDevice : IGraphicDevice, IGraphicsDeviceOwner
{
    private readonly GraphicsDeviceLifetime _lifetime;

    internal TestDevice()
    {
        _lifetime = new(this);
    }

    public event EventHandler<GraphicsDeviceLostEventArgs>? DeviceLost
    {
        add => _lifetime.DeviceLost += value;
        remove => _lifetime.DeviceLost -= value;
    }

    public GraphicsDeviceStatus Status => _lifetime.Status;

    public GraphicsDeviceLoss? Loss => _lifetime.Loss;

    public IGraphicDevice Device => this;

    public DeviceCaps Caps => throw new NotSupportedException();

    public IGraphicsQueue Queue => throw new NotSupportedException();

    internal int DisposeFailures { get; set; }

    internal int Disposals { get; private set; }

    internal int Confirmations { get; private set; }

    internal bool RejectRetirement { get; set; }

    internal TaskCompletionSource? RetirementWait { get; set; }

    internal List<TestShader> Shaders { get; } = [];

    internal int CreateShaderFailOnCall { get; set; }

    internal List<TestProgramPipeline> Programs { get; } = [];

    public BufferLayout<T> GetBufferLayout<T>()
        where T : unmanaged => throw new NotSupportedException();

    public IGraphicsBuffer<T> CreateBuffer<T>(BufferDesc<T> desc)
        where T : unmanaged => throw new NotSupportedException();

    public IGraphicsTexture CreateTexture(TextureDesc desc) => throw new NotSupportedException();

    public IGraphicsSampler CreateSampler(SamplerDesc desc) => throw new NotSupportedException();

    public IArgumentTable CreateArgumentTable(ArgumentTableDesc desc) => throw new NotSupportedException();

    public IGraphicsShader CreateShader(ShaderArtifact artifact)
    {
        if (CreateShaderFailOnCall == Shaders.Count + 1)
        {
            throw new InvalidOperationException("Injected shader creation failure.");
        }

        var shader = new TestShader(artifact);
        Shaders.Add(shader);
        return shader;
    }

    public IGraphicsSemaphore CreateSemaphore() => throw new NotSupportedException();

    public IGraphicsCommandBuffer CreateCommandBuffer(CommandBufferDesc desc) => throw new NotSupportedException();

    public TextureCopyLayout GetTextureCopyLayout(TextureFormat format) => throw new NotSupportedException();

    public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc)
    {
        var program = new TestProgramPipeline(desc);
        Programs.Add(program);
        return program;
    }

    public IGraphicsComputePipeline CreateComputePipeline(ComputePipelineDesc desc) => throw new NotSupportedException();

    public IGraphicsShaderDataBuffer<T> CreateBuffer<T>(ShaderArtifact artifact, ulong count, MemoryPreference memory = MemoryPreference.Automatic)
        where T : struct, IShaderData => throw new NotSupportedException();

    public async ValueTask WaitForRetirementAsync(CancellationToken cancellationToken = default)
    {
        if (RejectRetirement)
        {
            throw new InvalidOperationException("Native retirement is not proven.");
        }

        if (RetirementWait is { } wait)
        {
            await wait.Task.WaitAsync(cancellationToken);
        }

        Confirmations++;
    }

    public ValueTask DisposeAsync()
    {
        if (DisposeFailures > 0)
        {
            DisposeFailures--;
            throw new InvalidOperationException("Injected native cleanup failure.");
        }

        Disposals++;
        _lifetime.Dispose();
        return ValueTask.CompletedTask;
    }

    internal void Lose() => _lifetime.ReportLoss("TestLoss", "Test device lost.");
}
