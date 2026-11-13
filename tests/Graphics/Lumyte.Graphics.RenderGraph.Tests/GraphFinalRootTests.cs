using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;
using Xunit;

namespace Lumyte.Graphics.RenderGraph.Tests;

/// <summary>Exercises final root streaming through ordinary public graph callbacks.</summary>
public sealed class GraphFinalRootTests
{
    /// <summary>Managed providers stream once, retain no CPU root, and reuse their frozen generation.</summary>
    [Fact]
    public void ManagedSelectionStreamsOnceWithoutSnapshotFallback()
    {
        using var graph = new RenderGraph(new PublicGraphConsumerDevice());
        using var pipeline = new PublicGraphConsumerComputePipeline(Artifact());
        var commands = new RootCommands();
        var arguments = new RootInput { Value = 17 };
        graph.AddPass("streamed root", context =>
        {
            using RenderGraphComputeEncoder encoder = context.BeginComputePass();
            encoder.SetPipeline(pipeline);
            encoder.SetArguments(arguments);
            arguments.Value = 99;
            encoder.Dispatch(1);
            encoder.Dispatch(2);
            ShaderRootArguments root = commands.Encoder.Selected!;
            Assert.Equal(17u, MemoryMarshal.Read<uint>(((MemoryStorage)root.Storage).Bytes.AsSpan(16)));
            AssertNoInputFields(root, arguments);
            object scope = typeof(ShaderRootArguments).GetField("_scope", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(root)!;
            Assert.Equal("GraphRootArgumentScope", scope.GetType().Name);
            Assert.All(scope.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic), field =>
            {
                Assert.False(typeof(ShaderValueSnapshot).IsAssignableFrom(field.FieldType));
                Assert.False(typeof(IShaderArguments).IsAssignableFrom(field.FieldType));
                Assert.False(field.FieldType == typeof(byte[]) || field.FieldType == typeof(ReadOnlyMemory<byte>));
                if (field.FieldType == typeof(RenderGraphContext))
                {
                    Assert.Null(field.GetValue(scope));
                }
            });
        }).KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();

        // Discard commands before graph resources, including when recording/assertions throw.
        using RootCommands commandLifetime = commands;
        plan.Record(commands);
        Assert.Equal(1, arguments.Writes);
        Assert.Equal(1, commands.Encoder.FactoryCalls);
        Assert.Equal(1, Assert.Single(commands.Encoder.Storages).Writes);
        Assert.Equal(2, commands.Encoder.Bindings.Count);
        Assert.All(commands.Encoder.Bindings, binding =>
        {
            Assert.Same(commands.Encoder.Selected, binding.Arguments);
            Assert.Throws<InvalidOperationException>(() => binding.Root);
        });
    }

    /// <summary>Codec and graph-scope rejection preserve both the native generation and graph resource selection.</summary>
    [Fact]
    public void FailedSelectionPreservesPreviousGraphAndNativeRoot()
    {
        var device = new PublicGraphConsumerDevice();
        using var graph = new RenderGraph(device);
        using var image = new TestTexture();
        RenderGraphTexture source = graph.ImportTexture(image, TextureState.Sampled, default);
        using var foreignTable = new PublicGraphConsumerTable(new() { TextureCapacity = 1 });
        var foreign = new PublicGraphConsumerRef<ConsumerLink>(foreignTable, new object(), 0, 1);
        using var data = new ConsumerLinkBuffer(Artifact(element: true));
        RenderGraphShaderDataBuffer<ConsumerLink> buffer = graph.ImportShaderDataBuffer(data, default);
        using var pipeline = new PublicGraphConsumerComputePipeline(Artifact());
        var commands = new RootCommands();
        graph.AddPass("failed selection", context =>
        {
            IGpuRef<IGraphicsTextureView> texture = context.GetGpuRef(source);
            data.SetTransferredReferences(0, Metadata(texture));
            var arguments = new RootInput { Data = context.GetGpuRef(buffer), Value = 23 };
            using RenderGraphComputeEncoder encoder = context.BeginComputePass();
            encoder.SetPipeline(pipeline);
            encoder.SetArguments(arguments);
            ShaderRootArguments previous = commands.Encoder.Selected!;
            FieldInfo selectionField = typeof(RenderGraphComputeEncoder).GetField("_arguments", BindingFlags.Instance | BindingFlags.NonPublic)!;
            object previousResources = selectionField.GetValue(encoder)!;
            Assert.NotNull(previousResources);
            arguments.Failure = new ApplicationException("codec failed");
            Assert.Same(arguments.Failure, Assert.Throws<ApplicationException>(() => encoder.SetArguments(arguments)));
            Assert.Same(previous, commands.Encoder.Selected);
            Assert.Same(previousResources, selectionField.GetValue(encoder));
            arguments.Failure = null;
            arguments.Data = foreign;
            Assert.Throws<ArgumentException>(() => encoder.SetArguments(arguments));
            Assert.Same(previous, commands.Encoder.Selected);
            Assert.Same(previousResources, selectionField.GetValue(encoder));
            encoder.Dispatch(1);
            Assert.Same(previous, Assert.Single(commands.Encoder.Bindings).Arguments);
            Assert.Contains((IShaderReference)texture, commands.Encoder.Bindings[0].References);
        }).UseTexture(source, TextureState.Sampled, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead))
          .UseShaderDataBuffer(buffer, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead)).KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();

        // Discard commands before graph resources, including when recording/assertions throw.
        using RootCommands commandLifetime = commands;
        plan.Record(commands);
        Assert.Equal(3, commands.Encoder.FactoryCalls);
        Assert.False(commands.Encoder.Storages[0].Disposed);
        Assert.All(commands.Encoder.Storages.Skip(1), storage => Assert.True(storage.Disposed));
    }

    /// <summary>Even without optional diagnostics, every dispatch refreshes and admits transitive metadata.</summary>
    [Fact]
    public void TransitiveAdmissionRefreshesBetweenExecutions()
    {
        var device = new PublicGraphConsumerDevice();
        using var graph = new RenderGraph(device);
        using var firstImage = new TestTexture();
        using var otherImage = new TestTexture();
        using var data = new ConsumerLinkBuffer(Artifact(element: true));
        RenderGraphTexture first = graph.ImportTexture(firstImage, TextureState.Sampled, default);
        RenderGraphTexture other = graph.ImportTexture(otherImage, TextureState.Sampled, default);
        RenderGraphShaderDataBuffer<ConsumerLink> buffer = graph.ImportShaderDataBuffer(data, default);
        IGpuRef<IGraphicsTextureView>? otherReference = null;
        RenderGraphPass producer = graph.AddPass("other declaration", context => otherReference = context.GetGpuRef(other))
            .UseTexture(other, TextureState.Sampled, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead)).KeepAlive();
        using var pipeline = new PublicGraphConsumerComputePipeline(Artifact());
        var commands = new RootCommands();
        graph.AddPass("refresh metadata", context =>
        {
            IGpuRef<IGraphicsTextureView> valid = context.GetGpuRef(first);
            data.SetTransferredReferences(0, Metadata(valid));
            var arguments = new RootInput { Data = context.GetGpuRef(buffer), Value = 31 };
            using RenderGraphComputeEncoder encoder = context.BeginComputePass();
            encoder.SetPipeline(pipeline);
            encoder.SetArguments(arguments);
            encoder.Dispatch(1);
            Assert.Same(((IShaderReference)valid).Table, ((IShaderReference)otherReference!).Table);
            data.SetTransferredReferences(0, Metadata(otherReference!));
            Assert.Throws<ArgumentException>(() => encoder.Dispatch(1));
            data.SetTransferredReferences(0, Metadata(valid));
            encoder.Dispatch(1);
            Assert.Equal(1, arguments.Writes);
        }).UseTexture(first, TextureState.Sampled, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead))
          .UseShaderDataBuffer(buffer, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead))
          .DependsOn(producer).KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();

        // Discard commands before graph resources, including when recording/assertions throw.
        using RootCommands commandLifetime = commands;
        plan.Record(commands);
        Assert.Equal(1, commands.Encoder.FactoryCalls);
        Assert.Equal(2, commands.Encoder.Bindings.Count);
        Assert.NotSame(commands.Encoder.Bindings[0].Elements.Values.Single(), commands.Encoder.Bindings[1].Elements.Values.Single());
    }

    /// <summary>The unmanaged public overload also selects final storage rather than compatibility snapshots.</summary>
    [Fact]
    public void UnmanagedSelectionUsesFinalStorage()
    {
        using var graph = new RenderGraph(new PublicGraphConsumerDevice());
        using var pipeline = new PublicGraphConsumerComputePipeline(Artifact());
        var commands = new RootCommands();
        graph.AddPass("unmanaged root", context =>
        {
            using RenderGraphComputeEncoder encoder = context.BeginComputePass();
            encoder.SetPipeline(pipeline);
            uint value = 41;
            encoder.SetArguments(in value, "arguments", "ConsumerLinkRoot");
            value = 99;
            encoder.Dispatch(1);
        }).KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();

        // Discard commands before graph resources, including when recording/assertions throw.
        using RootCommands commandLifetime = commands;
        plan.Record(commands);
        Assert.Equal(1, commands.Encoder.FactoryCalls);
        MemoryStorage storage = Assert.Single(commands.Encoder.Storages);
        Assert.Equal(1, storage.Writes);
        Assert.Equal(41u, MemoryMarshal.Read<uint>(storage.Bytes));
    }

    private static ShaderReferenceMetadata Metadata(IGpuRef<IGraphicsTextureView> reference)
        => new([new(typeof(IGraphicsTextureView), reference, 0)]);

    private static void AssertNoInputFields(ShaderRootArguments root, RootInput input)
    {
        foreach (FieldInfo field in root.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
        {
            Assert.False(field.FieldType == typeof(ShaderValueSnapshot) || field.FieldType == typeof(byte[]) || field.FieldType == typeof(ReadOnlyMemory<byte>));
            Assert.NotSame(input, field.GetValue(root));
        }
    }

    private static ShaderArtifact Artifact(bool element = false)
    {
        object[] fields = element
            ? [new { name = "Texture", type = new { kind = "struct", name = "GpuTextureRef" }, binding = new { kind = "uniform", offset = 0, size = 16 } }]
            : [new { name = "Data", type = new { kind = "struct", name = "GpuBufferRef" }, binding = new { kind = "uniform", offset = 0, size = 16 } },
               new { name = "Value", type = new { kind = "scalar", scalarType = "uint32" }, binding = new { kind = "uniform", offset = 16, size = 4 } }];
        string reflection = JsonSerializer.Serialize(new
        {
            parameters = new[] { new { name = "arguments", type = new { kind = "constantBuffer", elementType = new { kind = "struct", name = element ? "ConsumerLink" : "ConsumerLinkRoot", fields, sizes = new[] { new { kind = "uniform", value = element ? 16 : 32, alignment = 16 } } } } } },
            lumyteReferenceTargets = new Dictionary<string, string> { ["ConsumerLinkRoot.Data"] = "ConsumerLink" },
        });
        byte[] code = new byte[20];
        BinaryPrimitives.WriteUInt32LittleEndian(code, 0x07230203);
        return ShaderArtifact.PackTarget(ShaderTarget.SpirV, ShaderStage.Compute, "main", "metadata-only-test-fixture", code, reflection);
    }

    private sealed class RootInput : IShaderArguments
    {
        public static string ShaderTypeName => "ConsumerLinkRoot";

        public static int TransferSize => 32;

        public static IReadOnlyList<ShaderTransferField> TransferFields => throw new InvalidOperationException("Unexpected diagnostic probe.");

        internal uint Value { get; set; }

        internal IGpuRef<ConsumerLink>? Data { get; set; }

        internal int Writes { get; private set; }

        internal Exception? Failure { get; set; }

        public IReadOnlyList<ShaderTransferField> GetTransferFields() => throw new InvalidOperationException("Unexpected diagnostic probe.");

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter
        {
            if (typeof(TWriter).Name.Contains("Snapshot", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Graph used the compatibility snapshot path.");
            }

            Writes++;
            if (Failure is { } failure)
            {
                throw failure;
            }

            Wire wire = new() { Value = Value };
            writer.WriteBytes(in wire);
            if (Data is { } data)
            {
                writer.WriteReference(0, data);
            }
        }

        [StructLayout(LayoutKind.Explicit, Size = 32)]
        private struct Wire
        {
            [FieldOffset(16)]
            public uint Value;
        }
    }

    private sealed class MemoryStorage(int size) : IShaderRootStorage
    {
        public int Size => Bytes.Length;

        internal byte[] Bytes { get; } = new byte[size];

        internal int Writes { get; private set; }

        internal bool Disposed { get; private set; }

        public void Write(ReadOnlySpan<byte> bytes, int offset)
        {
            bytes.CopyTo(Bytes.AsSpan(offset));
            Writes++;
        }

        public void Seal()
        {
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class RootEncoder : IComputeEncoder, IShaderRootEncoder, IDisposable
    {
        private IArgumentTable? _table;

        internal ShaderRootArguments? Selected { get; private set; }

        internal List<MemoryStorage> Storages { get; } = [];

        internal List<ShaderBindingSnapshot> Bindings { get; } = [];

        internal int FactoryCalls { get; private set; }

        public void SetArgumentTable(IArgumentTable table) => _table = table;

        public void SetPipeline(IGraphicsComputePipeline pipeline)
        {
        }

        public void SetArguments<T>(in T arguments)
            where T : IShaderArguments => throw new InvalidOperationException("Unexpected compatibility setter.");

        public void SetArguments(ShaderValueSnapshot arguments) => throw new InvalidOperationException("Unexpected snapshot setter.");

        public void SetRootArguments<T>(in T arguments, IShaderArgumentScope? scope = null)
            where T : IShaderArguments
            => Selected = ShaderRootArguments.Create(in arguments, Allocate, scope: scope);

        public void SetRootArguments<T>(in T arguments, string rootParameter, string? shaderTypeName, IShaderArgumentScope? scope = null)
            where T : unmanaged
            => Selected = ShaderRootArguments.CreateUnmanaged(in arguments, rootParameter, shaderTypeName, Allocate, scope: scope);

        public void Dispatch(uint groupCountX, uint groupCountY = 1, uint groupCountZ = 1)
            => Bindings.Add(ShaderBindingSnapshot.Capture(Selected!, _table));

        public void DispatchIndirect(BufferSlice<DispatchIndirectArguments> arguments) => Dispatch(1);

        public void End()
        {
        }

        public void Dispose() => Selected?.Dispose();

        private IShaderRootStorage Allocate(int size)
        {
            FactoryCalls++;
            var storage = new MemoryStorage(size);
            Storages.Add(storage);
            return storage;
        }
    }

    // Compose the existing command fixture; only compute-pass creation is overridden.
    private sealed class RootCommands : IGraphicsCommandBuffer
    {
        private readonly PublicGraphConsumerCommands _inner = new();

        public CommandBufferState State => _inner.State;

        internal RootEncoder Encoder { get; } = new();

        public void Barrier(MemoryBarrierDesc barrier) => _inner.Barrier(barrier);

        public void Barrier<T>(BufferBarrierDesc<T> barrier)
            where T : unmanaged => _inner.Barrier(barrier);

        public void Barrier(TextureBarrierDesc barrier) => _inner.Barrier(barrier);

        public void Barrier<T>(ShaderDataBufferBarrierDesc<T> barrier)
            where T : IShaderData => _inner.Barrier(barrier);

        public void CopyBuffer<TSource, TDestination>(BufferSlice<TSource> source, BufferSlice<TDestination> destination)
            where TSource : unmanaged
            where TDestination : unmanaged => _inner.CopyBuffer(source, destination);

        public void CopyBuffer<T>(ShaderDataSlice<T> source, ShaderDataSlice<T> destination)
            where T : IShaderData => _inner.CopyBuffer(source, destination);

        public void CopyTexture(TextureCopyRegion source, TextureCopyRegion destination) => _inner.CopyTexture(source, destination);

        public void CopyBufferToTexture(BufferTextureCopyLayout source, TextureCopyRegion destination) => _inner.CopyBufferToTexture(source, destination);

        public void CopyTextureToBuffer(TextureCopyRegion source, BufferTextureCopyLayout destination) => _inner.CopyTextureToBuffer(source, destination);

        public IRenderEncoder BeginRenderPass(RenderPassDesc desc) => _inner.BeginRenderPass(desc);

        public IComputeEncoder BeginComputePass(ComputePassDesc desc) => Encoder;

        public void Finish() => _inner.Finish();

        public void Dispose()
        {
            Encoder.Dispose();
            _inner.Dispose();
        }
    }
}
