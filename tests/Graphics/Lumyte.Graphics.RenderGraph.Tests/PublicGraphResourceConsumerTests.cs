using System.Buffers.Binary;
using System.Reflection;
using System.Text.Json;
using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.RenderGraph.Tests;

/// <summary>Exercises standard pass declarations and ordinary argument codecs without friend access.</summary>
public sealed partial class PublicGraphEncoderConsumerTests
{
    /// <summary>No public resource-set factory, wrapper, registration path or alternate group alias remains.</summary>
    [Fact]
    public void PublicPassSurfaceHasNoResourceSetConcept()
    {
        Type[] exports = typeof(RenderGraphContext).Assembly.GetExportedTypes();
        Assert.DoesNotContain(exports, type => type.Name is "RenderGraphShaderResources" or "PassResources" or "PassShaderResources" || type.Name.Contains("ArgumentRegistration", StringComparison.Ordinal));
        foreach (Type type in exports)
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                Assert.DoesNotContain(method.Name, new[] { "CreateShaderResources", "UseShaderResources", "CreateModuleShaderResources", "DescribeModuleShaderResources", "RegisterModuleTexture", "RegisterModuleBuffer", "SetResources", "SetArgumentTable" });
                Assert.DoesNotContain(method.GetParameters(), parameter => parameter.ParameterType.Name.Contains("ShaderResources", StringComparison.Ordinal));
            }
        }
    }

    /// <summary>UseTexture alone admits a default view and numeric bytes freeze at typed SetArguments.</summary>
    [Fact]
    public void PassDeclarationsResolveReferencesAndReuseTypedArguments()
    {
        var device = new PublicGraphConsumerDevice();
        using var graph = new RenderGraph(device);
        using var sourceImage = new TestTexture();
        using var targetImage = new TestTexture();
        RenderGraphTexture source = graph.ImportTexture(sourceImage, TextureState.Sampled, default);
        RenderGraphTexture target = graph.ImportTexture(targetImage, TextureState.Undefined, default);
        using var pipeline = new PublicGraphConsumerPipeline(TextureArtifact(ShaderStage.Vertex));
        RenderGraphContext? escaped = null;
        graph.AddPass("typed pass", context =>
        {
            escaped = context;
            IGpuRef<IGraphicsTextureView> input = context.GetGpuRef(source);
            Assert.Same(input, context.GetGpuRef(source));
            Assert.Throws<ArgumentException>(() => context.GetGpuRef(target));
            Assert.Throws<ArgumentException>(() => context.GetGpuRef(source, new() { BaseMipLevel = 1 }));
            var arguments = new ConsumerTextureArguments(input, input.GetElement(0), 1f);
            using RenderGraphRenderEncoder render = context.BeginRenderPass(Attachments(target));
            render.SetPipeline(pipeline);
            render.SetArguments(in arguments);
            arguments = arguments with { Float = 2f };
            render.Draw(3);
            render.Draw(3);
            render.SetArguments(in arguments);
            using var indirect = new TestTypedBuffer<DrawIndirectArguments>(new() { Count = 1, Usage = BufferUsage.Indirect }, []);
            render.DrawIndirect(indirect.SliceElements(0, 1));
            using var indexed = new TestTypedBuffer<DrawIndexedIndirectArguments>(new() { Count = 1, Usage = BufferUsage.Indirect }, []);
            render.DrawIndexedIndirect(indexed.SliceElements(0, 1));
        }).UseTexture(source, TextureState.Sampled, new(PipelineStage.VertexShader, ResourceAccess.ShaderRead))
          .UseTexture(target, TextureState.ColorAttachment, new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite)).KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();
        using var commands = new PublicGraphConsumerCommands();
        plan.Record(commands);
        Assert.Equal(new[] { 1f, 1f, 2f, 2f }, commands.Encoder.Executions.Select(ReadFloat));
        Assert.Equal(1u, Assert.Single(device.Tables).TextureCapacity);
        Assert.Throws<InvalidOperationException>(() => escaped!.GetGpuRef(source));
    }

    /// <summary>Each pass is an admitted capacity unit, even if its individual draws would fit separately.</summary>
    /// <param name="declared">The number of shader-declared images.</param>
    /// <param name="capacity">The admitted image slot limit.</param>
    [Theory]
    [InlineData(0, 0u)]
    [InlineData(1, 0u)]
    [InlineData(1, 1u)]
    [InlineData(2, 1u)]
    public void PassAggregateLimitIsCheckedBeforeCallbacksOrNativeWrites(int declared, uint capacity)
    {
        var device = new PublicGraphConsumerDevice { Limits = new() { TextureSlots = capacity } };
        using var graph = new RenderGraph(device);
        using var firstImage = new TestTexture();
        using var secondImage = new TestTexture();
        using var targetImage = new TestTexture();
        RenderGraphTexture[] sources = [graph.ImportTexture(firstImage, TextureState.Sampled, default), graph.ImportTexture(secondImage, TextureState.Sampled, default)];
        RenderGraphTexture target = graph.ImportTexture(targetImage, TextureState.Undefined, default);
        bool recorded = false;
        RenderGraphPass pass = graph.AddPass("admission", context =>
        {
            recorded = true;
            if (declared != 0)
            {
                using var pipeline = new PublicGraphConsumerPipeline(TextureArtifact(ShaderStage.Vertex));
                IGpuRef<IGraphicsTextureView> reference = context.GetGpuRef(sources[0]);
                var arguments = new ConsumerTextureArguments(reference, reference, 1f);
                using RenderGraphRenderEncoder render = context.BeginRenderPass(Attachments(target));
                render.SetPipeline(pipeline);
                render.SetArguments(in arguments);
                render.Draw(3);
                render.Draw(3);
            }
        }).UseTexture(target, TextureState.ColorAttachment, new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite)).KeepAlive();
        foreach (RenderGraphTexture texture in sources.Take(declared))
        {
            pass.UseTexture(texture, TextureState.Sampled, new(PipelineStage.FragmentShader, ResourceAccess.ShaderRead));
        }

        using CompiledRenderGraph plan = graph.Compile();
        using var commands = new PublicGraphConsumerCommands();
        if ((uint)declared > capacity)
        {
            Assert.Throws<NotSupportedException>(() => plan.Record(commands));
            Assert.False(recorded);
            Assert.Empty(device.Tables);
            Assert.Empty(commands.Encoder.Snapshots);
        }
        else
        {
            plan.Record(commands);
            Assert.True(recorded);
            Assert.Equal(declared, device.Tables.Count);
        }
    }

    /// <summary>Independent passes are automatically partitioned without a consumer resource-set object.</summary>
    [Fact]
    public void SeparatePassesPartitionAtTheExactLimit()
    {
        var device = new PublicGraphConsumerDevice { Limits = new() { TextureSlots = 1 } };
        using var graph = new RenderGraph(device);
        using var firstImage = new TestTexture();
        using var secondImage = new TestTexture();
        RenderGraphTexture[] sources = [graph.ImportTexture(firstImage, TextureState.Sampled, default), graph.ImportTexture(secondImage, TextureState.Sampled, default)];
        var references = new List<IShaderReference>();
        foreach (RenderGraphTexture source in sources)
        {
            graph.AddPass("one image", context => references.Add((IShaderReference)context.GetGpuRef(source)))
                .UseTexture(source, TextureState.Sampled, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead)).KeepAlive();
        }

        using CompiledRenderGraph plan = graph.Compile();
        using var commands = new PublicGraphConsumerCommands();
        plan.Record(commands);
        Assert.Equal(2, device.Tables.Count);
        Assert.NotSame(references[0].Table, references[1].Table);
        Assert.All(device.Tables, table => Assert.Equal(1u, table.TextureCapacity));
    }

    /// <summary>Exact view/range declarations can share allocation-wide hazard state without admitting undeclared keys.</summary>
    [Fact]
    public void ExactBindingViewsRangesAndShaderRolesStayValidated()
    {
        var device = new PublicGraphConsumerDevice();
        using var graph = new RenderGraph(device);
        using var foreign = new RenderGraph(device);
        using var image = new TestTexture();
        using var buffer = new TestTypedBuffer<int>(new() { Count = 4, Usage = BufferUsage.ShaderRead }, []);
        using var copyOnly = new TestTypedBuffer<int>(new() { Count = 4, Usage = BufferUsage.CopySource }, []);
        RenderGraphTexture texture = graph.ImportTexture(image, TextureState.Sampled, default);
        RenderGraphBuffer<int> handle = graph.ImportBuffer(buffer, default);
        RenderGraphBuffer<int> copied = graph.ImportBuffer(copyOnly, default);
        RenderGraphBuffer<int> other = foreign.ImportBuffer(buffer, default);
        var view = new TextureViewDesc { BaseMipLevel = 1, MipLevelCount = 1 };
        graph.AddPass("exact keys", context =>
        {
            Assert.Equal(3ul, context.GetGpuRef(handle, 1, 3).Count);
            Assert.Equal(1ul, context.GetGpuRef(handle, 1, 3).GetElement(1).Count);
            Assert.NotNull(context.GetGpuRef(texture, view));
            Assert.Throws<ArgumentException>(() => context.GetGpuRef(texture));
            Assert.Throws<ArgumentException>(() => context.GetGpuRef(handle, 0, 3));
            Assert.Throws<ArgumentException>(() => context.GetGpuRef(handle, 1, 4));
            Assert.Throws<ArgumentException>(() => context.GetGpuRef(other, 1, 3));
            Assert.Throws<ArgumentException>(() => context.GetGpuRef(copied));
        }).UseTexture(texture, view, TextureState.Sampled, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead))
          .UseBuffer(handle, 1, 3, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead))
          .UseBuffer(copied, new(PipelineStage.Copy, ResourceAccess.CopyRead)).KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();
        using var commands = new PublicGraphConsumerCommands();
        plan.Record(commands);
        Assert.Equal(1u, Assert.Single(device.Tables).BufferCapacity);
    }

    /// <summary>Distinct exact declarations share allocation hazards without losing native view/range identities.</summary>
    [Fact]
    public void MultipleViewsAndRangesAreAllAdmittedFromOnePass()
    {
        var device = new PublicGraphConsumerDevice();
        using var graph = new RenderGraph(device);
        using var image = new TestTexture();
        using var buffer = new TestTypedBuffer<int>(new() { Count = 4, Usage = BufferUsage.ShaderRead }, []);
        RenderGraphTexture texture = graph.ImportTexture(image, TextureState.Sampled, default);
        RenderGraphBuffer<int> data = graph.ImportBuffer(buffer, default);
        var firstView = new TextureViewDesc { MipLevelCount = 1 };
        var secondView = new TextureViewDesc { BaseMipLevel = 1, MipLevelCount = 1 };
        BarrierScope scope = new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead);
        graph.AddPass("multiple exact declarations", context =>
        {
            Assert.NotSame(context.GetGpuRef(texture, firstView), context.GetGpuRef(texture, secondView));
            Assert.NotSame(context.GetGpuRef(data, 0, 1), context.GetGpuRef(data, 2, 1));
            Assert.Throws<ArgumentException>(() => context.GetGpuRef(data));
        }).UseTexture(texture, firstView, TextureState.Sampled, scope)
          .UseTexture(texture, secondView, TextureState.Sampled, scope)
          .UseBuffer(data, 0, 1, scope).UseBuffer(data, 2, 1, scope).KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();
        using var commands = new PublicGraphConsumerCommands();
        plan.Record(commands);
        IArgumentTable table = Assert.Single(device.Tables);
        Assert.Equal(2u, table.TextureCapacity);
        Assert.Equal(2u, table.BufferCapacity);
    }

    /// <summary>Only pass-declared sampler keys resolve; immutable samplers create no write roots.</summary>
    [Fact]
    public void SamplersUseNaturalPassDeclarations()
    {
        var device = new PublicGraphConsumerDevice();
        using var graph = new RenderGraph(device);
        var desc = new SamplerDesc();
        using IGraphicsSampler borrowed = device.CreateSampler(desc);
        using IGraphicsSampler unknown = device.CreateSampler(desc);
        graph.AddPass("samplers", context =>
        {
            Assert.Same(context.GetGpuRef(desc), context.GetGpuRef(new SamplerDesc()));
            Assert.Same(borrowed, ((IShaderReference)context.GetGpuRef(borrowed)).Resource);
            Assert.Throws<ArgumentException>(() => context.GetGpuRef(unknown));
        }).UseSampler(desc).UseSampler(borrowed).KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();
        using var commands = new PublicGraphConsumerCommands();
        plan.Record(commands);
        Assert.Equal(2u, Assert.Single(device.Tables).SamplerCapacity);
    }

    /// <summary>Undeclared transitive references fail at each native capture, even if another pass shares the physical table.</summary>
    /// <param name="indirect">Whether dispatch counts are GPU-sourced.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TransitiveMetadataScopeLifetimeAndRecoveryAreCheckedPerCommand(bool indirect)
    {
        var device = new PublicGraphConsumerDevice();
        using var graph = new RenderGraph(device);
        using var firstImage = new TestTexture();
        using var secondImage = new TestTexture();
        using var data = new ConsumerLinkBuffer(LinkArtifact(element: true));
        RenderGraphTexture first = graph.ImportTexture(firstImage, TextureState.Sampled, default);
        RenderGraphTexture second = graph.ImportTexture(secondImage, TextureState.Sampled, default);
        RenderGraphShaderDataBuffer<ConsumerLink> buffer = graph.ImportShaderDataBuffer(data, default);
        IGpuRef<IGraphicsTextureView>? otherReference = null;
        RenderGraphPass producer = graph.AddPass("other pass", context => otherReference = context.GetGpuRef(second))
            .UseTexture(second, TextureState.Sampled, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead)).KeepAlive();
        using var pipeline = new PublicGraphConsumerComputePipeline(LinkArtifact(element: false));
        graph.AddPass("transitive capture", context =>
        {
            var valid = new ConsumerLink(context.GetGpuRef(first));
            var foreignLink = new ConsumerLink(otherReference!);
            data.SetTransferredValue(0, IShaderArguments.Capture(in valid));
            var arguments = new ConsumerLinkArguments(context.GetGpuRef(buffer), 1f);
            using var counts = new TestTypedBuffer<DispatchIndirectArguments>(new() { Count = 1, Usage = BufferUsage.Indirect }, []);
            using RenderGraphComputeEncoder compute = context.BeginComputePass();
            compute.SetPipeline(pipeline);
            compute.SetArguments(in arguments);
            Execute();
            data.SetTransferredValue(0, IShaderArguments.Capture(in foreignLink));
            Assert.Throws<ArgumentException>(Execute);
            data.SetTransferredValue(0, IShaderArguments.Capture(in valid));
            Execute();
            data.Dispose();
            Assert.Throws<ObjectDisposedException>(Execute);

            void Execute()
            {
                if (indirect)
                {
                    compute.DispatchIndirect(counts.SliceElements(0, 1));
                }
                else
                {
                    compute.Dispatch(1);
                }
            }
        }).UseTexture(first, TextureState.Sampled, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead))
          .UseShaderDataBuffer(buffer, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead)).DependsOn(producer).KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();
        using var commands = new PublicGraphConsumerCommands();
        plan.Record(commands);
        Assert.Single(device.Tables);
        Assert.Equal(2, commands.Encoder.Executions.Count);
        Assert.Single(commands.Encoder.Snapshots);
    }

    /// <summary>Shader-data producer/consumer identities remain coherent despite an intervening independent partition.</summary>
    [Fact]
    public void ShaderDataTransferDependencyKeepsStableNativeReferenceIdentity()
    {
        var device = new PublicGraphConsumerDevice { Limits = new() { TextureSlots = 1 } };
        using var graph = new RenderGraph(device);
        using var firstImage = new TestTexture();
        using var secondImage = new TestTexture();
        using var data = new ConsumerLinkBuffer(LinkArtifact(element: true));
        RenderGraphTexture first = graph.ImportTexture(firstImage, TextureState.Sampled, default);
        RenderGraphTexture second = graph.ImportTexture(secondImage, TextureState.Sampled, default);
        RenderGraphShaderDataBuffer<ConsumerLink> buffer = graph.ImportShaderDataBuffer(data, default);
        graph.AddPass("publish metadata", context =>
        {
            var value = new ConsumerLink(context.GetGpuRef(first));
            data.SetTransferredValue(0, IShaderArguments.Capture(in value));
        }).UseTexture(first, TextureState.Sampled, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead))
          .UseShaderDataBuffer(buffer, new(PipelineStage.Copy, ResourceAccess.CopyWrite)).KeepAlive();
        graph.AddPass("unrelated", context => Assert.NotNull(context.GetGpuRef(second)))
            .UseTexture(second, TextureState.Sampled, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead)).KeepAlive();
        using var pipeline = new PublicGraphConsumerComputePipeline(LinkArtifact(element: false));
        graph.AddPass("consume metadata", context =>
        {
            var arguments = new ConsumerLinkArguments(context.GetGpuRef(buffer), 1f);
            using RenderGraphComputeEncoder compute = context.BeginComputePass();
            compute.SetPipeline(pipeline);
            compute.SetArguments(in arguments);
            compute.Dispatch(1);
        }).UseTexture(first, TextureState.Sampled, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead))
          .UseShaderDataBuffer(buffer, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead)).KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();
        using var commands = new PublicGraphConsumerCommands();
        plan.Record(commands);
        Assert.Equal(2, device.Tables.Count);
        Assert.Single(commands.Encoder.Executions);
    }

    /// <summary>Culled invalid views are never resolved, while live invalid views fail before callbacks and table creation.</summary>
    /// <param name="live">Whether the invalid declaration is observable.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CullingAndPreflightRespectFrozenViewDeclarations(bool live)
    {
        var device = new PublicGraphConsumerDevice();
        using var graph = new RenderGraph(device);
        using var image = new TestTexture();
        RenderGraphTexture texture = graph.ImportTexture(image, TextureState.Sampled, default);
        bool recorded = false;
        RenderGraphPass pass = graph.AddPass("invalid view", _ => recorded = true)
            .UseTexture(texture, new() { BaseMipLevel = 100 }, TextureState.Sampled, new(PipelineStage.FragmentShader, ResourceAccess.ShaderRead));
        if (live)
        {
            pass.KeepAlive();
        }

        using CompiledRenderGraph plan = graph.Compile();
        using var commands = new PublicGraphConsumerCommands();
        if (live)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => plan.Record(commands));
        }
        else
        {
            plan.Record(commands);
        }

        Assert.False(recorded);
        Assert.Empty(device.Tables);
    }

    /// <summary>Automatic pass ownership belongs to each compiled generation, not to a mutable borrowed builder.</summary>
    [Fact]
    public void RepeatedCompilationKeepsIndependentIntrinsicOwners()
    {
        var device = new PublicGraphConsumerDevice();
        using var graph = new RenderGraph(device);
        using var image = new TestTexture();
        RenderGraphTexture texture = graph.ImportTexture(image, TextureState.Sampled, default);
        var references = new List<IShaderReference>();
        graph.AddPass("borrowed", context => references.Add((IShaderReference)context.GetGpuRef(texture)))
            .UseTexture(texture, TextureState.Sampled, new(PipelineStage.FragmentShader, ResourceAccess.ShaderRead)).KeepAlive();
        using CompiledRenderGraph first = graph.Compile();
        using CompiledRenderGraph second = graph.Compile();
        graph.Dispose();
        using var a = new PublicGraphConsumerCommands();
        using var b = new PublicGraphConsumerCommands();
        first.Record(a);
        second.Record(b);
        Assert.NotSame(references[0].Table, references[1].Table);
        PublicGraphConsumerTable firstTable = Assert.IsType<PublicGraphConsumerTable>(references[0].Table);
        PublicGraphConsumerTable secondTable = Assert.IsType<PublicGraphConsumerTable>(references[1].Table);
        a.Dispose();
        first.Dispose();
        Assert.True(firstTable.Disposed);
        Assert.False(secondTable.Disposed);
        b.Dispose();
        second.Dispose();
        Assert.True(secondTable.Disposed);
    }

    /// <summary>Copy-only ranges bridge encoded identities while capture reads the command-local overlay, not committed metadata.</summary>
    [Fact]
    public void CopyOnlyShaderDataBridgePreservesRangeAndCommandLocalMetadata()
    {
        var device = new PublicGraphConsumerDevice { Limits = new() { TextureSlots = 1 } };
        using var graph = new RenderGraph(device);
        using var image = new TestTexture();
        using var unrelatedImage = new TestTexture();
        using var upload = new ConsumerLinkBuffer(LinkArtifact(element: true), 2, MemoryPreference.Upload);
        using var storage = new ConsumerLinkBuffer(LinkArtifact(element: true), 2);
        RenderGraphTexture texture = graph.ImportTexture(image, TextureState.Sampled, default);
        RenderGraphTexture unrelated = graph.ImportTexture(unrelatedImage, TextureState.Sampled, default);
        RenderGraphShaderDataBuffer<ConsumerLink> source = graph.ImportShaderDataBuffer(upload, default);
        RenderGraphShaderDataBuffer<ConsumerLink> destination = graph.ImportShaderDataBuffer(storage, default);
        RenderGraphPass producer = graph.AddPass("metadata producer", context =>
        {
            var value = new ConsumerLink(context.GetGpuRef(texture));
            upload.SetTransferredValue(0, IShaderArguments.Capture(in value));
        }).UseTexture(texture, TextureState.Sampled, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead))
          .UseShaderDataBuffer(source, 0, 1, new(PipelineStage.Host, ResourceAccess.HostWrite)).KeepAlive();
        graph.AddPass("intervening partition", context => Assert.NotNull(context.GetGpuRef(unrelated)))
            .UseTexture(unrelated, TextureState.Sampled, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead)).DependsOn(producer).KeepAlive();
        RenderGraphPass copy = graph.AddPass("metadata copy", context => context.Commands.CopyBuffer(context.GetShaderDataBuffer(source).SliceElements(0, 1), context.GetShaderDataBuffer(destination).SliceElements(1, 1)))
            .UseShaderDataBuffer(source, 0, 1, new(PipelineStage.Copy, ResourceAccess.CopyRead))
            .UseShaderDataBuffer(destination, 1, 1, new(PipelineStage.Copy, ResourceAccess.CopyWrite)).DependsOn(producer);
        using var pipeline = new PublicGraphConsumerComputePipeline(LinkArtifact(element: false));
        graph.AddPass("metadata consumer", context =>
        {
            var arguments = new ConsumerLinkArguments(context.GetGpuRef(destination, 1, 1), 1f);
            using RenderGraphComputeEncoder compute = context.BeginComputePass();
            compute.SetPipeline(pipeline);
            compute.SetArguments(in arguments);
            compute.Dispatch(1);
        }).UseTexture(texture, TextureState.Sampled, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead))
          .UseShaderDataBuffer(destination, 1, 1, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead)).DependsOn(copy).KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();
        using var commands = new PublicGraphConsumerCommands();
        plan.Record(commands);
        Assert.Equal(2, device.Tables.Count);
        Assert.Contains("shader-copy", commands.Events);
        Assert.Empty(storage.Read(1).Values);
        Lumyte.Graphics.Shared.ShaderBindingSnapshot capture = Assert.Single(commands.Encoder.Bindings);
        Assert.Same(image, Assert.IsAssignableFrom<IGraphicsTextureView>(Assert.Single(capture.References, reference => reference.Resource is IGraphicsTextureView).Resource).Texture);
        Assert.Equal(1ul, Assert.Single(capture.Elements).Key.Element);
    }

    private static ShaderArtifact LinkArtifact(bool element)
    {
        object Reference(string name, string kind, int offset) => new { name, type = new { kind = "struct", name = kind }, binding = new { kind = "uniform", offset, size = 16 } };
        object[] fields = element ? [Reference("Texture", "GpuTextureRef", 0)] :
        [
            Reference("Data", "GpuBufferRef", 0),
            new { name = "Float", type = new { kind = "scalar", scalarType = "float32" }, binding = new { kind = "uniform", offset = 16, size = 4 } },
        ];
        string reflection = JsonSerializer.Serialize(new
        {
            parameters = new[] { new { name = "arguments", type = new { kind = "constantBuffer", elementType = new { kind = "struct", name = element ? "ConsumerLink" : "ConsumerLinkRoot", fields, sizes = new[] { new { kind = "uniform", value = element ? 16 : 32, alignment = 16 } } } } } },
            lumyteReferenceTargets = new Dictionary<string, string> { ["ConsumerLinkRoot.Data"] = "ConsumerLink" },
        });
        byte[] code = new byte[20];
        BinaryPrimitives.WriteUInt32LittleEndian(code, 0x07230203);
        return ShaderArtifact.PackTarget(ShaderTarget.SpirV, ShaderStage.Compute, "main", "metadata-only-test-fixture", code, reflection);
    }

    private static ShaderArtifact TextureArtifact(ShaderStage stage)
    {
        object Field(string name, string kind, int offset, int size) => new { name, type = new { kind = "struct", name = kind }, binding = new { kind = "uniform", offset, size } };
        object[] fields =
        [
            Field("Input", "GpuTextureRef", 0, 16),
            Field("Backdrop", "GpuTextureRef", 16, 16),
            new { name = "Float", type = new { kind = "scalar", scalarType = "float32" }, binding = new { kind = "uniform", offset = 32, size = 4 } },
        ];
        string reflection = JsonSerializer.Serialize(new
        {
            parameters = new[] { new { name = "arguments", type = new { kind = "constantBuffer", elementType = new { kind = "struct", name = "ConsumerTextureInputs", fields, sizes = new[] { new { kind = "uniform", value = 48, alignment = 16 } } } } } },
        });
        byte[] code = new byte[20];
        BinaryPrimitives.WriteUInt32LittleEndian(code, 0x07230203);
        return ShaderArtifact.PackTarget(ShaderTarget.SpirV, stage, "main", "metadata-only-test-fixture", code, reflection);
    }
}
