using System.Text.Json;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation.Tests;

internal sealed class AlternateCodecBackend : IGraphicDevice
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

    public GraphicsDeviceStatus Status => GraphicsDeviceStatus.Ready;

    public GraphicsDeviceLoss? Loss => null;

    public DeviceCaps Caps { get; } = new() { ShaderTarget = ShaderTarget.Wgsl };

    public IGraphicsQueue Queue { get; } = new TypedQueue();

    internal Type? CreatedType { get; private set; }

    internal TypedTable? Table { get; private set; }

    internal List<object?> NativeReferences { get; } = [];

    internal bool FailWrite { get; set; }

    internal int Writes { get; set; }

    public ValueTask WaitForRetirementAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public BufferLayout<T> GetBufferLayout<T>()
        where T : unmanaged => throw new NotSupportedException();

    public TextureCopyLayout GetTextureCopyLayout(TextureFormat format) => throw new NotSupportedException();

    public IGraphicsBuffer<T> CreateBuffer<T>(BufferDesc<T> desc)
        where T : unmanaged => throw new NotSupportedException();

    public IGraphicsTexture CreateTexture(TextureDesc desc) => throw new NotSupportedException();

    public IGraphicsSampler CreateSampler(SamplerDesc desc) => new TypedSampler(desc);

    public IArgumentTable CreateArgumentTable(ArgumentTableDesc desc) => Table = new TypedTable(desc);

    public IGraphicsShader CreateShader(ShaderArtifact artifact) => throw new NotSupportedException();

    public IGraphicsSemaphore CreateSemaphore() => throw new NotSupportedException();

    public IGraphicsCommandBuffer CreateCommandBuffer(CommandBufferDesc desc) => throw new NotSupportedException();

    public IMeshPipeline CreateMeshPipeline(MeshPipelineDesc desc) => throw new NotSupportedException();

    public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc) => throw new NotSupportedException();

    public IGraphicsComputePipeline CreateComputePipeline(ComputePipelineDesc desc) => throw new NotSupportedException();

    public IGraphicsShaderDataBuffer<T> CreateShaderDataBuffer<T>(ShaderArtifact artifact, ulong count, MemoryPreference memory = MemoryPreference.Automatic)
        where T : struct, IShaderArguments
    {
        CreatedType = typeof(T);
        return new AlternateCodecStorage<T>(this, artifact, count, memory);
    }

    internal static ShaderArtifact Artifact(bool reference = false)
    {
        object fieldType = reference ? new { kind = "struct", name = "GpuSamplerRef" } : new { kind = "scalar", scalarType = "uint32" };
        var type = new
        {
            kind = "struct",
            name = reference ? "TypedSamplerValue" : "TypedNumber",
            sizes = new[] { new { kind = "uniform", value = reference ? 16 : 4, alignment = 4 } },
            fields = new[] { new { name = "Value", type = fieldType, binding = new { offset = 0, size = reference ? 16 : 4 } } },
        };
        string json = JsonSerializer.Serialize(new
        {
            lumyteAbi = 1,
            parameters = new[] { new { name = "__lumyte_schema_" + type.name, type = new { kind = "resource", resultType = new { kind = "pointer", valueType = type } } } },
        });
        return ShaderArtifact.PackTarget(ShaderTarget.Wgsl, ShaderStage.Compute, "main", "test", [1], json);
    }

    private sealed class TypedQueue : IGraphicsQueue
    {
        public IGraphicsSubmission Submit(IReadOnlyList<IGraphicsCommandBuffer> commandBuffers) => throw new NotSupportedException();

        public IGraphicsSubmission Submit(QueueSubmitDesc desc) => throw new NotSupportedException();
    }
}
