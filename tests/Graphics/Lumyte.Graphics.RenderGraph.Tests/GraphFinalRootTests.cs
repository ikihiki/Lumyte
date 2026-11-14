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
    /// <summary>Supplies the immutable reference-slot count for a specialized test wire type.</summary>
    private interface IReferenceCount
    {
        /// <summary>Gets the exact number of wire slots.</summary>
        static abstract int Count { get; }
    }

    /// <summary>Disabled validation streams roots without asking for optional dependency metadata.</summary>
    [Fact]
    public void ValidationOffNeverQueriesDependenciesAndRetainsNoRootMetadata()
    {
        using var graph = new RenderGraph(new PublicGraphConsumerDevice { ValidationEnabled = false });
        using var data = new ConsumerLinkBuffer(Artifact(element: true)) { ValidationEnabled = false, ThrowOnDependencyQuery = true };
        RenderGraphShaderDataBuffer<ConsumerLink> buffer = graph.ImportShaderDataBuffer(data, default);
        using var pipeline = new PublicGraphConsumerComputePipeline(Artifact());
        var commands = new RootCommands();
        graph.AddPass("untracked root", context =>
        {
            using RenderGraphComputeEncoder encoder = context.BeginComputePass();
            encoder.SetPipeline(pipeline);
            var arguments = new RootInput<OneReference> { Data = context.GetGpuRef(buffer), Value = 7 };
            encoder.SetArguments(arguments);
            encoder.Dispatch(1);
            Assert.Null(commands.Encoder.Selected!.Metadata.References);
            Assert.Null(commands.Encoder.Prepare);
            var numeric = new RootInput<NoReferences> { Value = 9 };
            encoder.SetArguments(numeric);
            encoder.Dispatch(1);
            Assert.Null(commands.Encoder.Selected!.Metadata.References);
            uint unmanaged = 11;
            encoder.SetArguments(in unmanaged, "arguments", "ConsumerLinkRoot");
            encoder.Dispatch(1);
            Assert.Null(commands.Encoder.Selected!.Metadata.References);
        }).UseShaderDataBuffer(buffer, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead)).KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();
        using RootCommands commandLifetime = commands;
        plan.Record(commands);
        Assert.Equal(0, data.DependencyQueries);
        Assert.Empty(commands.Encoder.Bindings);
        Assert.Equal(3, commands.Encoder.FactoryCalls);
    }

    /// <summary>An untracked source reports unavailable rather than an empty dependency set.</summary>
    [Fact]
    public void DisabledSourceReturnsFalseAndNull()
    {
        using var data = new ConsumerLinkBuffer(Artifact(element: true)) { ValidationEnabled = false };
        Assert.False(data.TryGetDependencies(out IShaderReferenceMetadata? dependencies));
        Assert.Null(dependencies);
    }

    /// <summary>Collection visits a buffer once and includes dependencies beyond the referenced element.</summary>
    [Fact]
    public void BufferMetadataAggregatesEveryElement()
    {
        using var table = new PublicGraphConsumerTable(new() { BufferCapacity = 1, TextureCapacity = 1 });
        using var data = new ConsumerLinkBuffer(Artifact(element: true), count: 2);
        using var image = new TestTexture();
        using IGraphicsTextureView view = image.CreateView(new());
        var texture = (IShaderReference)table.WriteTexture(0, view);
        data.SetDependencies(1, new ShaderDependencyMetadata([texture]));
        var element = (IShaderReference)table.WriteBuffer(0, data.SliceElements(0, 1));
        var root = ShaderRootArguments.Create("arguments", "ConsumerLinkRoot", 32, new ShaderDependencyMetadata([element]));
        var binding = ShaderBindingDependencies.Collect(root, table);
        Assert.Contains(texture, binding.References);
        Assert.Same(data, Assert.Single(binding.Buffers).Key);
        Assert.Equal(1, data.DependencyQueries);
    }

    /// <summary>Managed providers stream once, retain no CPU root, and reuse their frozen generation.</summary>
    [Fact]
    public void ManagedSelectionStreamsOnceWithoutSnapshotFallback()
    {
        using var graph = new RenderGraph(new PublicGraphConsumerDevice { ValidationEnabled = true });
        using var pipeline = new PublicGraphConsumerComputePipeline(Artifact());
        var commands = new RootCommands();
        var arguments = new RootInput<NoReferences> { Value = 17 };
        graph.AddPass("streamed root", context =>
        {
            using RenderGraphComputeEncoder encoder = context.BeginComputePass();
            encoder.SetPipeline(pipeline);
            encoder.SetArguments(arguments);
            arguments.Value = 99;
            encoder.Dispatch(1);
            encoder.Dispatch(2);
            TestRootOwner root = commands.Encoder.Selected!;
            Assert.Equal(17u, MemoryMarshal.Read<uint>(((MemoryStorage)root.Storage).Bytes.AsSpan(16)));
            AssertNoInputFields(root);
            var admit = (Action<IReadOnlyCollection<IShaderReference>>?)typeof(ShaderRootArguments)
                .GetField("_admit", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(root.Metadata);

            // A pass without shader resources needs no detached admission callback.
            Assert.Null(admit);
            Assert.Null(commands.Encoder.Prepare);
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
            Assert.Same(commands.Encoder.Selected!.Metadata, binding.Arguments);
            Assert.Null(typeof(ShaderBindingDependencies).GetProperty("Root"));
        });
    }

    /// <summary>Codec and graph-scope rejection preserve both the native generation and graph resource selection.</summary>
    [Fact]
    public void FailedSelectionPreservesPreviousGraphAndNativeRoot()
    {
        var device = new PublicGraphConsumerDevice { ValidationEnabled = true };
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
            data.SetDependencies(0, Metadata(texture));
            var arguments = new RootInput<OneReference> { Data = context.GetGpuRef(buffer), Value = 23 };
            using RenderGraphComputeEncoder encoder = context.BeginComputePass();
            encoder.SetPipeline(pipeline);
            encoder.SetArguments(arguments);
            TestRootOwner previous = commands.Encoder.Selected!;
            FieldInfo selectionField = typeof(RenderGraphComputeEncoder).GetField("_arguments", BindingFlags.Instance | BindingFlags.NonPublic)!;
            object previousResources = selectionField.GetValue(encoder)!;
            Assert.NotNull(previousResources);
            arguments.Failure = new ApplicationException("codec failed");
            Assert.Same(arguments.Failure, Assert.Throws<ApplicationException>(() => encoder.SetArguments(arguments)));
            Assert.Same(previous, commands.Encoder.Selected);
            Assert.Null(commands.Encoder.Prepare);
            Assert.Same(previousResources, selectionField.GetValue(encoder));
            arguments.Failure = null;
            arguments.Data = foreign;
            Assert.Throws<ArgumentException>(() => encoder.SetArguments(arguments));
            Assert.Null(commands.Encoder.Prepare);
            Assert.Same(previous, commands.Encoder.Selected);
            Assert.Same(previousResources, selectionField.GetValue(encoder));
            encoder.Dispatch(1);
            Assert.Same(previous.Metadata, Assert.Single(commands.Encoder.Bindings).Arguments);
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

    /// <summary>Enabled validation refreshes and admits whole-buffer metadata at every dispatch.</summary>
    [Fact]
    public void TransitiveAdmissionRefreshesBetweenExecutions()
    {
        var device = new PublicGraphConsumerDevice { ValidationEnabled = true };
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
            data.SetDependencies(0, Metadata(valid));
            var arguments = new RootInput<OneReference> { Data = context.GetGpuRef(buffer), Value = 31 };
            using RenderGraphComputeEncoder encoder = context.BeginComputePass();
            encoder.SetPipeline(pipeline);
            encoder.SetArguments(arguments);
            encoder.Dispatch(1);
            Assert.Same(((IShaderReference)valid).Table, ((IShaderReference)otherReference!).Table);
            data.SetDependencies(0, Metadata(otherReference!));
            Assert.Throws<ArgumentException>(() => encoder.Dispatch(1));
            data.SetDependencies(0, Metadata(valid));
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
        Assert.NotSame(commands.Encoder.Bindings[0].Buffers.Values.Single(), commands.Encoder.Bindings[1].Buffers.Values.Single());
    }

    /// <summary>Enabled validation admits another range of the same canonical buffer.</summary>
    [Fact]
    public void TransitiveAdmissionAllowsOtherRangeOfSameBufferAndTable()
    {
        var device = new PublicGraphConsumerDevice { ValidationEnabled = true };
        using var graph = new RenderGraph(device);
        using var data = new ConsumerLinkBuffer(Artifact(element: true));
        using var staging = new ConsumerLinkBuffer(Artifact(element: true), memory: MemoryPreference.Upload);
        using var buffer = new TestTypedBuffer<uint>(new() { Count = 2, Usage = BufferUsage.ShaderRead }, []);
        RenderGraphShaderDataBuffer<ConsumerLink> links = graph.ImportShaderDataBuffer(data, default);
        RenderGraphShaderDataBuffer<ConsumerLink> upload = graph.ImportShaderDataBuffer(staging, default);
        RenderGraphBuffer<uint> values = graph.ImportBuffer(buffer, default);
        IGpuRef<uint>? wrong = null;
        RenderGraphPass producer = graph.AddPass("other range", context => wrong = context.GetGpuRef(values, 1, 1))
            .UseBuffer(values, 1, 1, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead))
            .UseShaderDataBuffer(links, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead)).KeepAlive();
        using var pipeline = new PublicGraphConsumerComputePipeline(Artifact());
        var commands = new RootCommands();
        graph.AddPass("transitive range admission", context =>
        {
            IGpuRef<uint> valid = context.GetGpuRef(values, 0, 1);
            IShaderReference validReference = Assert.IsAssignableFrom<IShaderReference>(valid);
            IShaderReference wrongReference = Assert.IsAssignableFrom<IShaderReference>(wrong);
            Assert.Same(validReference.Table, wrongReference.Table);
            Assert.Same(validReference.Resource, wrongReference.Resource);
            Assert.NotEqual(validReference.OffsetInBytes, wrongReference.OffsetInBytes);
            data.SetDependencies(0, new ShaderDependencyMetadata([validReference]));
            var arguments = new RootInput<OneReference> { Data = context.GetGpuRef(links) };
            using RenderGraphComputeEncoder encoder = context.BeginComputePass();
            encoder.SetPipeline(pipeline);
            encoder.SetArguments(arguments);
            encoder.Dispatch(1);
            IShaderReferenceMetadata original = data.ElementDependencies(0);
            staging.SetDependencies(0, new ShaderDependencyMetadata([wrongReference]));
            commands.CopyBuffer(staging.SliceElements(0, 1), data.SliceElements(0, 1));
            Assert.Same(original, data.ElementDependencies(0));
            encoder.Dispatch(1);
            staging.SetDependencies(0, new ShaderDependencyMetadata([validReference]));
            commands.CopyBuffer(staging.SliceElements(0, 1), data.SliceElements(0, 1));
            encoder.Dispatch(1);
            Assert.Equal(1, arguments.Writes);
        }).UseShaderDataBuffer(links, new(PipelineStage.ComputeShader | PipelineStage.Copy, ResourceAccess.ShaderRead | ResourceAccess.CopyWrite))
          .UseShaderDataBuffer(upload, new(PipelineStage.Copy, ResourceAccess.CopyRead))
          .UseBuffer(values, 0, 1, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead))
          .DependsOn(producer).KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();
        using RootCommands commandLifetime = commands;
        plan.Record(commands);
        Assert.Equal(3, commands.Encoder.Bindings.Count);
        Assert.Equal(1, commands.Encoder.FactoryCalls);
    }

    /// <summary>A cached reference is admitted by the consuming pass's resource range, not its registration token.</summary>
    [Fact]
    public void CachedReferenceFromAnotherCallbackUsesConsumingPassDeclaration()
    {
        using var graph = new RenderGraph(new PublicGraphConsumerDevice { ValidationEnabled = true });
        using var data = new ConsumerLinkBuffer(Artifact(element: true), count: 3);
        RenderGraphShaderDataBuffer<ConsumerLink> buffer = graph.ImportShaderDataBuffer(data, default);
        IGpuRef<ConsumerLink>? cached = null;
        bool producerRan = false;
        RenderGraphPass producer = graph.AddPass("cache registered subrange", context =>
        {
            cached = context.GetGpuRef(buffer, 1, 2).GetElement(0);
            producerRan = true;
        }).UseShaderDataBuffer(buffer, 1, 2, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead)).KeepAlive();
        using var pipeline = new PublicGraphConsumerComputePipeline(Artifact());
        var commands = new RootCommands();
        graph.AddPass("consume cached subrange", context =>
        {
            Assert.True(producerRan);
            IGpuRef<ConsumerLink> declared = context.GetGpuRef(buffer, 0, 3);
            IShaderReference expected = Assert.IsAssignableFrom<IShaderReference>(cached);
            IShaderReference own = Assert.IsAssignableFrom<IShaderReference>(declared);
            Assert.Same(own.Table, expected.Table);
            Assert.Same(own.Resource, expected.Resource);
            Assert.NotEqual(own.OffsetInBytes, expected.OffsetInBytes);
            Assert.True(own.SizeInBytes > expected.SizeInBytes);
            Assert.NotEqual(own.RegistrationOffsetInBytes, expected.RegistrationOffsetInBytes);
            Assert.NotEqual(own.RegistrationSizeInBytes, expected.RegistrationSizeInBytes);
            var arguments = new RootInput<OneReference> { Data = cached };
            using RenderGraphComputeEncoder encoder = context.BeginComputePass();
            encoder.SetPipeline(pipeline);
            encoder.SetArguments(arguments);
            encoder.Dispatch(1);
            Assert.Contains(expected, Assert.Single(commands.Encoder.Bindings).References);
            Assert.Equal(1, arguments.Writes);
        }).UseShaderDataBuffer(buffer, 0, 3, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead))
          .DependsOn(producer).KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();
        using RootCommands commandLifetime = commands;
        plan.Record(commands);
        Assert.Equal(1, commands.Encoder.FactoryCalls);
        Assert.Single(commands.Encoder.Bindings);
    }

    /// <summary>Sharing a table does not admit a cached root whose resource the consumer never declared.</summary>
    [Fact]
    public void CachedReferenceFromAnotherCallbackRejectsUndeclaredResource()
    {
        using var graph = new RenderGraph(new PublicGraphConsumerDevice { ValidationEnabled = true });
        using var joined = new ConsumerLinkBuffer(Artifact(element: true));
        using var other = new ConsumerLinkBuffer(Artifact(element: true));
        RenderGraphShaderDataBuffer<ConsumerLink> shared = graph.ImportShaderDataBuffer(joined, default);
        RenderGraphShaderDataBuffer<ConsumerLink> undeclared = graph.ImportShaderDataBuffer(other, default);
        IGpuRef<ConsumerLink>? cached = null;
        bool producerRan = false;
        RenderGraphPass producer = graph.AddPass("cache undeclared resource", context =>
        {
            cached = context.GetGpuRef(undeclared);
            producerRan = true;
        }).UseShaderDataBuffer(shared, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead))
          .UseShaderDataBuffer(undeclared, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead)).KeepAlive();
        using var pipeline = new PublicGraphConsumerComputePipeline(Artifact());
        var commands = new RootCommands();
        graph.AddPass("reject cached undeclared resource", context =>
        {
            Assert.True(producerRan);
            IGpuRef<ConsumerLink> declared = context.GetGpuRef(shared);
            Assert.Same(((IShaderReference)declared).Table, ((IShaderReference)cached!).Table);
            Assert.NotSame(((IShaderReference)declared).Resource, ((IShaderReference)cached!).Resource);
            using RenderGraphComputeEncoder encoder = context.BeginComputePass();
            encoder.SetPipeline(pipeline);
            encoder.SetArguments(new RootInput<OneReference> { Data = declared });
            TestRootOwner previous = commands.Encoder.Selected!;
            Assert.Throws<ArgumentException>(() => encoder.SetArguments(new RootInput<OneReference> { Data = cached }));
            Assert.Same(previous, commands.Encoder.Selected);
            encoder.Dispatch(1);
            Assert.DoesNotContain((IShaderReference)cached!, Assert.Single(commands.Encoder.Bindings).References);
        }).UseShaderDataBuffer(shared, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead))
          .DependsOn(producer).KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();
        using RootCommands commandLifetime = commands;
        plan.Record(commands);
        Assert.Equal(2, commands.Encoder.FactoryCalls);
        Assert.Single(commands.Encoder.Bindings);
    }

    /// <summary>Distinct view registrations of one texture share canonical admission.</summary>
    [Fact]
    public void TransitiveAdmissionAcceptsAnotherViewOfDeclaredTexture()
    {
        using var graph = new RenderGraph(new PublicGraphConsumerDevice { ValidationEnabled = true });
        using var image = new TestTexture();
        using var data = new ConsumerLinkBuffer(Artifact(element: true));
        RenderGraphTexture texture = graph.ImportTexture(image, TextureState.Sampled, default);
        RenderGraphShaderDataBuffer<ConsumerLink> buffer = graph.ImportShaderDataBuffer(data, default);
        var firstView = new TextureViewDesc { MipLevelCount = 1 };
        var secondView = new TextureViewDesc { BaseMipLevel = 1, MipLevelCount = 1 };
        IGpuRef<IGraphicsTextureView>? alias = null;
        RenderGraphPass producer = graph.AddPass("cache another view", context => alias = context.GetGpuRef(texture, secondView))
            .UseTexture(texture, secondView, TextureState.Sampled, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead)).KeepAlive();
        using var pipeline = new PublicGraphConsumerComputePipeline(Artifact());
        var commands = new RootCommands();
        graph.AddPass("canonical texture admission", context =>
        {
            var own = (IShaderReference)context.GetGpuRef(texture, firstView);
            var other = (IShaderReference)alias!;
            Assert.Same(own.Table, other.Table);
            Assert.NotSame(own.Resource, other.Resource);
            Assert.Same(((IGraphicsTextureView)own.Resource).Texture, ((IGraphicsTextureView)other.Resource).Texture);
            data.SetDependencies(0, new ShaderDependencyMetadata([other]));
            using RenderGraphComputeEncoder encoder = context.BeginComputePass();
            encoder.SetPipeline(pipeline);
            encoder.SetArguments(new RootInput<OneReference> { Data = context.GetGpuRef(buffer) });
            encoder.Dispatch(1);
        }).UseTexture(texture, firstView, TextureState.Sampled, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead))
          .UseShaderDataBuffer(buffer, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead)).DependsOn(producer).KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();
        using RootCommands commandLifetime = commands;
        plan.Record(commands);
        Assert.Contains((IShaderReference)alias!, Assert.Single(commands.Encoder.Bindings).References);
    }

    /// <summary>The unmanaged public overload also selects final storage rather than compatibility snapshots.</summary>
    [Fact]
    public void UnmanagedSelectionUsesFinalStorage()
    {
        using var graph = new RenderGraph(new PublicGraphConsumerDevice { ValidationEnabled = true });
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

    private static ShaderDependencyMetadata Metadata(IGpuRef<IGraphicsTextureView> reference)
        => new([Assert.IsAssignableFrom<IShaderReference>(reference)]);

    private static void AssertNoInputFields(TestRootOwner root)
    {
        foreach (FieldInfo field in root.Metadata.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
        {
            Assert.False(field.FieldType == typeof(ShaderValueSnapshot) || field.FieldType == typeof(byte[]) || field.FieldType == typeof(ReadOnlyMemory<byte>));
            Assert.False(typeof(IShaderArguments).IsAssignableFrom(field.FieldType));
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

    private readonly struct NoReferences : IReferenceCount
    {
        public static int Count => 0;
    }

    private readonly struct OneReference : IReferenceCount
    {
        public static int Count => 1;
    }

    private struct RootInput<TCount> : IShaderArguments
        where TCount : struct, IReferenceCount
    {
        private readonly System.Runtime.CompilerServices.StrongBox<int> _counter = new();

        public RootInput()
        {
        }

        public static string ShaderTypeName => "ConsumerLinkRoot";

        public static int TransferSize => 32;

        public static int ReferenceCount => TCount.Count;

        public static IReadOnlyList<ShaderTransferField> TransferFields => throw new InvalidOperationException("Unexpected diagnostic probe.");

        internal uint Value { get; set; }

        internal IGpuRef<ConsumerLink>? Data { get; set; }

        internal int Writes => _counter.Value;

        internal Exception? Failure { get; set; }

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter, allows ref struct
        {
            if (typeof(TWriter).Name.Contains("Snapshot", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Graph used the compatibility snapshot path.");
            }

            _counter.Value++;
            if (Failure is { } failure)
            {
                throw failure;
            }

            Wire wire = new() { Value = Value };
            ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in wire, 1));
            if (Data is { } data)
            {
                writer.WriteReference(data);
                writer.WriteValue(bytes[16..]);
            }
            else
            {
                writer.WriteValue(bytes);
            }
        }
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct Wire
    {
        [FieldOffset(16)]
        public uint Value;
    }

    private sealed class MemoryStorage(int size) : ITestShaderRootStorage
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

    private sealed class RootEncoder : IComputeEncoder, IDisposable
    {
        private IArgumentTable? _table;
        private Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? _prepare;

        internal TestRootOwner? Selected { get; private set; }

        internal Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? Prepare => _prepare;

        internal List<MemoryStorage> Storages { get; } = [];

        internal List<ShaderBindingDependencies> Bindings { get; } = [];

        internal Func<IShaderDataSource, IShaderReferenceMetadata?>? ReadShaderData { get; set; }

        internal int FactoryCalls { get; private set; }

        public void SetArgumentValidation(Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? prepare) => _prepare = prepare;

        public void SetArgumentTable(IArgumentTable table) => _table = table;

        public void SetPipeline(IGraphicsComputePipeline pipeline)
        {
        }

        public void SetArguments<T>(in T arguments)
            where T : struct, IShaderArguments
            => Selected = TestShaderTransfer.Create(in arguments, Allocate, Encode, prepare: _prepare);

        public void SetArguments<T>(in T arguments, string rootParameter, string? shaderTypeName)
            where T : unmanaged
            => Selected = TestShaderTransfer.CreateUnmanaged(in arguments, rootParameter, shaderTypeName, Allocate, prepare: _prepare);

        public void Dispatch(uint groupCountX, uint groupCountY = 1, uint groupCountZ = 1)
        {
            ShaderBindingDependencies? binding = Selected!.Metadata.References == null ? null : ShaderBindingDependencies.Collect(Selected.Metadata, _table, ReadShaderData);
            Selected!.Seal();
            if (binding != null)
            {
                Bindings.Add(binding);
            }
        }

        public void DispatchIndirect(BufferSlice<DispatchIndirectArguments> arguments) => Dispatch(1);

        public void End()
        {
        }

        public void Dispose() => Selected?.Dispose();

        private static byte[] Encode(ShaderValue value) => new byte[16];

        private ITestShaderRootStorage Allocate(int size)
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
        private readonly TestShaderDataOverlay _transfers = new();

        public CommandBufferState State => _inner.State;

        internal RootEncoder Encoder { get; } = new();

        public void Barrier(MemoryBarrierDesc barrier) => _inner.Barrier(barrier);

        public void Barrier<T>(BufferBarrierDesc<T> barrier)
            where T : unmanaged => _inner.Barrier(barrier);

        public void Barrier(TextureBarrierDesc barrier) => _inner.Barrier(barrier);

        public void Barrier<T>(ShaderDataBufferBarrierDesc<T> barrier)
            where T : struct, IShaderArguments => _inner.Barrier(barrier);

        public void CopyBuffer<TSource, TDestination>(BufferSlice<TSource> source, BufferSlice<TDestination> destination)
            where TSource : unmanaged
            where TDestination : unmanaged => _inner.CopyBuffer(source, destination);

        public void CopyBuffer<T>(ShaderDataSlice<T> source, ShaderDataSlice<T> destination)
            where T : struct, IShaderArguments => _transfers.Record((IShaderDataSource)source.Buffer, source.Offset, (IShaderDataSource)destination.Buffer, destination.Offset, source.Count, destination.Buffer.ShaderElementStrideInBytes, () => _inner.CopyBuffer(source, destination));

        public void CopyTexture(TextureCopyRegion source, TextureCopyRegion destination) => _inner.CopyTexture(source, destination);

        public void CopyBufferToTexture(BufferTextureCopyLayout source, TextureCopyRegion destination) => _inner.CopyBufferToTexture(source, destination);

        public void CopyTextureToBuffer(TextureCopyRegion source, BufferTextureCopyLayout destination) => _inner.CopyTextureToBuffer(source, destination);

        public IRenderEncoder BeginRenderPass(RenderPassDesc desc) => _inner.BeginRenderPass(desc);

        public IComputeEncoder BeginComputePass(ComputePassDesc desc)
        {
            Encoder.ReadShaderData = _transfers.ReadDependencies;
            return Encoder;
        }

        public void Finish() => _inner.Finish();

        public void Dispose()
        {
            Encoder.Dispose();
            _inner.Dispose();
        }
    }
}
