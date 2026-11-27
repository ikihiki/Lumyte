using System.Runtime.CompilerServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning.Tests;

/// <summary>Provides deterministic managed test instrumentation.</summary>
internal sealed class ManagedDevice : IGraphicDevice, IGraphicsQueue
{
    public event EventHandler<GraphicsDeviceLostEventArgs>? DeviceLost
    {
        add
        {
        }

        remove
        {
        }
    }

    public GraphicsDeviceStatus Status { get; set; } = GraphicsDeviceStatus.Ready;

    public GraphicsDeviceLoss? Loss => null;

    public DeviceCaps Caps { get; } = new();

    public IGraphicsQueue Queue => this;

    internal TaskCompletionSource? ShaderDataMapGate { get; set; }

    internal TaskCompletionSource ShaderDataMapStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal int ShaderDataWriteFailures { get; set; }

    internal List<object> ShaderDataAllocations { get; } = [];

    internal List<string> Events { get; } = [];

    internal bool UnknownSubmission { get; set; }

    internal Action? OnSubmit { get; set; }

    internal Action? OnCreateCommands { get; set; }

    internal int CommandDisposeFailures { get; set; }

    internal ManagedCommands? Commands { get; private set; }

    public ValueTask WaitForRetirementAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Events.Add("retirement-proof");
        return ValueTask.CompletedTask;
    }

    public BufferLayout<T> GetBufferLayout<T>()
        where T : unmanaged
        => new((ulong)Unsafe.SizeOf<T>(), (ulong)Unsafe.SizeOf<T>(), 1, 1);

    public IGraphicsBuffer<T> CreateBuffer<T>(BufferDesc<T> desc)
        where T : unmanaged
    {
        Events.Add("allocate-buffer");
        return new ManagedBuffer<T>(desc.Count, Events);
    }

    public IGraphicsTexture CreateTexture(TextureDesc desc)
    {
        Events.Add("allocate-texture");
        return new ManagedTexture(desc, Events);
    }

    public IArgumentTable CreateArgumentTable(ArgumentTableDesc desc)
    {
        Events.Add("allocate-table");
        return new ManagedTable(desc, Events);
    }

    public IGraphicsCommandBuffer CreateCommandBuffer(CommandBufferDesc desc)
    {
        Events.Add("allocate-commands");
        Commands = new(this);
        OnCreateCommands?.Invoke();
        return Commands;
    }

    public IGraphicsSubmission Submit(IReadOnlyList<IGraphicsCommandBuffer> commandBuffers)
    {
        Events.Add("submit");
        if (UnknownSubmission)
        {
            throw new InvalidOperationException("Acceptance unknown.");
        }

        OnSubmit?.Invoke();
        return new ManagedSubmission((ManagedCommands)commandBuffers.Single(), Events);
    }

    public IGraphicsSubmission Submit(QueueSubmitDesc desc) => throw new NotSupportedException();

    public IGraphicsSampler CreateSampler(SamplerDesc desc) => throw new NotSupportedException();

    public IGraphicsShader CreateShader(ShaderArtifact artifact) => throw new NotSupportedException();

    public IGraphicsSemaphore CreateSemaphore() => throw new NotSupportedException();

    public TextureCopyLayout GetTextureCopyLayout(TextureFormat format) => new() { BytesPerTexel = 4, BufferOffsetAlignmentInBytes = 1, BytesPerRowAlignment = 1 };

    public IMeshPipeline CreateMeshPipeline(MeshPipelineDesc desc) => throw new NotSupportedException();

    public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc) => throw new NotSupportedException();

    public IGraphicsComputePipeline CreateComputePipeline(ComputePipelineDesc desc) => throw new NotSupportedException();

    public IGraphicsShaderDataBuffer<T> CreateShaderDataBuffer<T>(ShaderArtifact artifact, ulong count, MemoryPreference memory = MemoryPreference.Automatic)
        where T : struct, IShaderArguments
    {
        Events.Add("allocate-shaderdata");
        var buffer = new ManagedShaderDataBuffer<T>(count, memory, this);
        ShaderDataAllocations.Add(buffer);
        return buffer;
    }
}
