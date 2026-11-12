using System.Buffers.Binary;
using System.Reflection;
using System.Text.Json;
using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.RenderGraph.Tests;

/// <summary>Compiles typed application argument and callback resolution examples without friend access.</summary>
public sealed partial class PublicGraphEncoderConsumerTests
{
    /// <summary>Logical groups and their factories have no string-member or artifact-root bag API.</summary>
    [Fact]
    public void TypedResourceSurfaceHasNoNamedMemberBag()
    {
        Assert.DoesNotContain(typeof(RenderGraphShaderResources).GetMethods(), method => method.Name is "Value" or "Snapshot");
        foreach (MethodInfo method in typeof(RenderGraphShaderResources).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            Assert.DoesNotContain(method.GetParameters(), parameter => parameter.ParameterType == typeof(string) || parameter.ParameterType == typeof(ShaderArtifact));
        }

        Assert.Equal(typeof(RenderGraph), Assert.Single(typeof(RenderGraphShaderResourceExtensions).GetMethod("CreateShaderResources")!.GetParameters()).ParameterType);
        Assert.DoesNotContain(typeof(RenderGraphContext).Assembly.GetExportedTypes(), type => type.Name.Contains("ImageArguments", StringComparison.Ordinal) || type.Name.Contains("ArgumentRegistration", StringComparison.Ordinal));
    }

    /// <summary>Callbacks resolve existing native references while ordinary argument values snapshot only at SetArguments.</summary>
    [Fact]
    public void ContextResolvesNativeReferencesAndReusesTypedArguments()
    {
        var device = new PublicGraphConsumerDevice();
        using var graph = new RenderGraph(device);
        using var sourceImage = new TestTexture();
        using var targetImage = new TestTexture();
        RenderGraphTexture source = graph.ImportTexture(sourceImage, TextureState.Sampled, default);
        RenderGraphTexture target = graph.ImportTexture(targetImage, TextureState.Undefined, default);
        RenderGraphShaderResources inputs = graph.CreateShaderResources().ReadTexture(source).ReadTexture(source);
        using var pipeline = new PublicGraphConsumerPipeline(TextureArtifact(ShaderStage.Vertex));
        RenderGraphContext? escaped = null;
        graph.AddPass("typed consumer", context =>
        {
            escaped = context;
            IGpuRef<IGraphicsTextureView> input = context.GetGpuRef(inputs, source);
            Assert.Same(input, context.GetGpuRef(inputs, source));
            Assert.Throws<ArgumentException>(() => context.GetGpuRef(inputs, target));
            Assert.Throws<ArgumentException>(() => context.GetGpuRef(inputs, source, new() { BaseMipLevel = 1 }));
            Assert.Throws<InvalidOperationException>(() => inputs.ReadTexture(source));
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
          .UseTexture(target, TextureState.ColorAttachment, new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite))
          .UseShaderResources(inputs, target).KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();
        using var commands = new PublicGraphConsumerCommands();
        plan.Record(commands);
        Assert.Equal(new[] { 1f, 1f, 2f, 2f }, commands.Encoder.Executions.Select(ReadFloat));
        Assert.Equal(1u, Assert.Single(device.Tables).TextureCapacity);
        Assert.Throws<InvalidOperationException>(() => escaped!.GetGpuRef(inputs, source));
    }

    /// <summary>One command cannot combine separately declared cohorts even when they share a physical table.</summary>
    /// <param name="separateTables">Whether the slot budget also forces separate physical partitions.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixedResourceGroupsAreRejectedWithoutReplacingPreviousArguments(bool separateTables)
    {
        var device = new PublicGraphConsumerDevice { Limits = new() { TextureSlots = separateTables ? 1u : uint.MaxValue } };
        using var graph = new RenderGraph(device);
        using var firstImage = new TestTexture();
        using var secondImage = new TestTexture();
        RenderGraphTexture first = graph.ImportTexture(firstImage, TextureState.Sampled, default);
        RenderGraphTexture second = graph.ImportTexture(secondImage, TextureState.Sampled, default);
        RenderGraphShaderResources firstGroup = graph.CreateShaderResources().ReadTexture(first);
        RenderGraphShaderResources secondGroup = graph.CreateShaderResources().ReadTexture(second);
        using var pipeline = new PublicGraphConsumerComputePipeline(TextureArtifact(ShaderStage.Compute));
        graph.AddPass("cohort", context =>
        {
            IGpuRef<IGraphicsTextureView> a = context.GetGpuRef(firstGroup, first);
            IGpuRef<IGraphicsTextureView> b = context.GetGpuRef(secondGroup, second);
            var valid = new ConsumerTextureArguments(a, a, 1);
            var mixed = new ConsumerTextureArguments(a, b, 2);
            using RenderGraphComputeEncoder compute = context.BeginComputePass();
            compute.SetPipeline(pipeline);
            compute.SetArguments(in valid);
            Assert.Throws<ArgumentException>(() => compute.SetArguments(in mixed));
            compute.Dispatch(1);
            using var indirect = new TestTypedBuffer<DispatchIndirectArguments>(new() { Count = 1, Usage = BufferUsage.Indirect }, []);
            compute.DispatchIndirect(indirect.SliceElements(0, 1));
        }).UseTexture(first, TextureState.Sampled, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead))
          .UseTexture(second, TextureState.Sampled, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead))
          .UseShaderResources(firstGroup).UseShaderResources(secondGroup).KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();
        using var commands = new PublicGraphConsumerCommands();
        plan.Record(commands);
        Assert.Equal(separateTables ? 2 : 1, device.Tables.Count);
        Assert.Equal(new[] { 1f, 1f }, commands.Encoder.Executions.Select(ReadFloat));
    }

    /// <summary>Buffer lookups are exact declarations, not callback-time registration or silent subrange creation.</summary>
    [Fact]
    public void TypedRangeLookupRejectsUnregisteredKeysAndForeignResources()
    {
        var device = new PublicGraphConsumerDevice();
        using var graph = new RenderGraph(device);
        using var foreign = new RenderGraph(device);
        using var buffer = new TestTypedBuffer<int>(new() { Count = 4, Usage = BufferUsage.ShaderRead }, []);
        RenderGraphBuffer<int> handle = graph.ImportBuffer(buffer, default);
        RenderGraphBuffer<int> other = foreign.ImportBuffer(buffer, default);
        RenderGraphShaderResources inputs = graph.CreateShaderResources().ReadBuffer(handle, 1, 3);
        Assert.Throws<ArgumentException>(() => inputs.ReadBuffer(other, 0, 1));
        graph.AddPass("range", context =>
        {
            IGpuRef<int> reference = context.GetGpuRef(inputs, handle, 1, 3);
            Assert.Equal(3ul, reference.Count);
            Assert.Equal(1ul, reference.GetElement(1).Count);
            Assert.Throws<ArgumentException>(() => context.GetGpuRef(inputs, handle, 0, 3));
            Assert.Throws<ArgumentException>(() => context.GetGpuRef(inputs, handle, 1, 4));
            Assert.Throws<ArgumentException>(() => context.GetGpuRef(inputs, other, 1, 3));
            Assert.Throws<ArgumentException>(() => context.GetGpuRef(inputs, new SamplerDesc()));
        }).UseBuffer(handle, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead)).UseShaderResources(inputs).KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();
        using var commands = new PublicGraphConsumerCommands();
        plan.Record(commands);
    }

    /// <summary>Undeclared logical resources fail before callback execution or native table publication.</summary>
    [Fact]
    public void MissingPassDeclarationIsRejectedBeforeTypedLookup()
    {
        using var graph = new RenderGraph(new PublicGraphConsumerDevice());
        using var source = new TestTexture();
        RenderGraphTexture handle = graph.ImportTexture(source, TextureState.Sampled, default);
        RenderGraphShaderResources inputs = graph.CreateShaderResources().ReadTexture(handle);
        bool recorded = false;
        graph.AddPass("undeclared", _ => recorded = true).UseShaderResources(inputs).KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();
        using var commands = new PublicGraphConsumerCommands();
        Assert.Throws<ArgumentException>(() => plan.Record(commands));
        Assert.False(recorded);
    }

    /// <summary>Numeric selection does not freeze indirect metadata or bypass per-execution provenance and lifetime checks.</summary>
    /// <param name="indirect">Whether execution uses GPU-sourced dispatch counts.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TransitiveReferencesAreValidatedAtEachCommandCapture(bool indirect)
    {
        var device = new PublicGraphConsumerDevice();
        using var graph = new RenderGraph(device);
        using var firstImage = new TestTexture();
        using var secondImage = new TestTexture();
        using var data = new ConsumerLinkBuffer(LinkArtifact(element: true));
        RenderGraphTexture first = graph.ImportTexture(firstImage, TextureState.Sampled, default);
        RenderGraphTexture second = graph.ImportTexture(secondImage, TextureState.Sampled, default);
        RenderGraphShaderDataBuffer<ConsumerLink> buffer = graph.ImportShaderDataBuffer(data, default);
        RenderGraphShaderResources selected = graph.CreateShaderResources().ReadTexture(first).ReadBuffer(buffer, 0, 1);
        RenderGraphShaderResources other = graph.CreateShaderResources().ReadTexture(second);
        using var pipeline = new PublicGraphConsumerComputePipeline(LinkArtifact(element: false));
        graph.AddPass("transitive capture", context =>
        {
            var firstLink = new ConsumerLink(context.GetGpuRef(selected, first));
            var otherLink = new ConsumerLink(context.GetGpuRef(other, second));
            data.SetTransferredValue(0, IShaderArguments.Capture(in firstLink));
            var arguments = new ConsumerLinkArguments(context.GetGpuRef(selected, buffer, 0, 1), 1f);
            using var counts = new TestTypedBuffer<DispatchIndirectArguments>(new() { Count = 1, Usage = BufferUsage.Indirect }, []);
            using RenderGraphComputeEncoder compute = context.BeginComputePass();
            compute.SetPipeline(pipeline);
            compute.SetArguments(in arguments);
            Execute();
            data.SetTransferredValue(0, IShaderArguments.Capture(in otherLink));
            Assert.Throws<ArgumentException>(Execute);
            data.SetTransferredValue(0, IShaderArguments.Capture(in firstLink));
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
          .UseTexture(second, TextureState.Sampled, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead))
          .UseShaderDataBuffer(buffer, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead))
          .UseShaderResources(selected).UseShaderResources(other).KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();
        using var commands = new PublicGraphConsumerCommands();
        plan.Record(commands);
        Assert.Single(device.Tables);
        Assert.Equal(2, commands.Encoder.Executions.Count);
        Assert.Single(commands.Encoder.Snapshots);
    }

    /// <summary>Sampler lookup distinguishes graph-owned value keys from borrowed object identities.</summary>
    [Fact]
    public void SamplerLookupRequiresItsExactPredeclaredKey()
    {
        var device = new PublicGraphConsumerDevice();
        using var graph = new RenderGraph(device);
        var desc = new SamplerDesc();
        using IGraphicsSampler borrowed = device.CreateSampler(desc);
        using IGraphicsSampler unknown = device.CreateSampler(desc);
        RenderGraphShaderResources inputs = graph.CreateShaderResources().Sampler(desc).Sampler(borrowed);
        graph.AddPass("samplers", context =>
        {
            Assert.Same(context.GetGpuRef(inputs, desc), context.GetGpuRef(inputs, new SamplerDesc()));
            Assert.Same(borrowed, ((IShaderReference)context.GetGpuRef(inputs, borrowed)).Resource);
            Assert.Throws<ArgumentException>(() => context.GetGpuRef(inputs, unknown));
        }).UseShaderResources(inputs).KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();
        using var commands = new PublicGraphConsumerCommands();
        plan.Record(commands);
        Assert.Equal(2u, Assert.Single(device.Tables).SamplerCapacity);
    }

    /// <summary>Common roots permit independently sufficient candidates, but never the union of their transitive resources.</summary>
    [Fact]
    public void DeduplicatedCommonRootRequiresOneWholeCandidate()
    {
        var device = new PublicGraphConsumerDevice();
        using var graph = new RenderGraph(device);
        using var firstImage = new TestTexture();
        using var secondImage = new TestTexture();
        using var data = new ConsumerLinkBuffer(LinkArtifact(element: true), 2);
        RenderGraphTexture first = graph.ImportTexture(firstImage, TextureState.Sampled, default);
        RenderGraphTexture second = graph.ImportTexture(secondImage, TextureState.Sampled, default);
        RenderGraphShaderDataBuffer<ConsumerLink> buffer = graph.ImportShaderDataBuffer(data, default);
        RenderGraphShaderResources a = graph.CreateShaderResources().ReadTexture(first).ReadBuffer(buffer, 0, 2);
        RenderGraphShaderResources b = graph.CreateShaderResources().ReadTexture(second).ReadBuffer(buffer, 0, 2);
        using var pipeline = new PublicGraphConsumerComputePipeline(LinkArtifact(element: false));
        graph.AddPass("common root", context =>
        {
            var firstLink = new ConsumerLink(context.GetGpuRef(a, first));
            var secondLink = new ConsumerLink(context.GetGpuRef(b, second));
            IGpuRef<ConsumerLink> root = context.GetGpuRef(a, buffer, 0, 2);
            Assert.Same(root, context.GetGpuRef(b, buffer, 0, 2));
            var arguments = new ConsumerLinkArguments(root, 1f);
            using RenderGraphComputeEncoder compute = context.BeginComputePass();
            compute.SetPipeline(pipeline);
            compute.SetArguments(in arguments);
            data.SetTransferredValue(0, IShaderArguments.Capture(in firstLink));
            data.SetTransferredValue(1, IShaderArguments.Capture(in firstLink));
            compute.Dispatch(1);
            data.SetTransferredValue(0, IShaderArguments.Capture(in secondLink));
            data.SetTransferredValue(1, IShaderArguments.Capture(in secondLink));
            compute.Dispatch(1);
            data.SetTransferredValue(0, IShaderArguments.Capture(in firstLink));
            Assert.Throws<ArgumentException>(() => compute.Dispatch(1));
            data.SetTransferredValue(1, IShaderArguments.Capture(in firstLink));
            compute.Dispatch(1);
        }).UseTexture(first, TextureState.Sampled, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead))
          .UseTexture(second, TextureState.Sampled, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead))
          .UseShaderDataBuffer(buffer, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead))
          .UseShaderResources(a).UseShaderResources(b).KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();
        using var commands = new PublicGraphConsumerCommands();
        plan.Record(commands);
        Assert.Single(device.Tables);
        Assert.Equal(3, commands.Encoder.Executions.Count);
        Assert.Single(commands.Encoder.Snapshots);
    }

    /// <summary>A native rejection between nonempty partitions leaves the old snapshot and binding cohort selected.</summary>
    [Fact]
    public void FailedNativeReplacementPreservesNonemptyPartitionSelection()
    {
        var device = new PublicGraphConsumerDevice { Limits = new() { TextureSlots = 1 } };
        using var graph = new RenderGraph(device);
        using var firstImage = new TestTexture();
        using var secondImage = new TestTexture();
        RenderGraphTexture first = graph.ImportTexture(firstImage, TextureState.Sampled, default);
        RenderGraphTexture second = graph.ImportTexture(secondImage, TextureState.Sampled, default);
        RenderGraphShaderResources a = graph.CreateShaderResources().ReadTexture(first);
        RenderGraphShaderResources b = graph.CreateShaderResources().ReadTexture(second);
        using var pipeline = new PublicGraphConsumerComputePipeline(TextureArtifact(ShaderStage.Compute));
        var native = new PublicGraphConsumerEncoder();
        graph.AddPass("failed partition replacement", context =>
        {
            IGpuRef<IGraphicsTextureView> oldReference = context.GetGpuRef(a, first);
            IGpuRef<IGraphicsTextureView> newReference = context.GetGpuRef(b, second);
            var oldArguments = new ConsumerTextureArguments(oldReference, oldReference, 1f);
            var newArguments = new ConsumerTextureArguments(newReference, newReference, 2f);
            using RenderGraphComputeEncoder compute = context.BeginComputePass();
            compute.SetPipeline(pipeline);
            compute.SetArguments(in oldArguments);
            compute.Dispatch(1);
            native.SetArgumentsException = new ArgumentException("native schema rejection");
            Assert.Throws<ArgumentException>(() => compute.SetArguments(in newArguments));
            native.SetArgumentsException = null;
            compute.Dispatch(1);
        }).UseTexture(first, TextureState.Sampled, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead))
          .UseTexture(second, TextureState.Sampled, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead))
          .UseShaderResources(a).UseShaderResources(b).KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();
        using var commands = new PublicGraphConsumerCommands { Encoder = native };
        plan.Record(commands);
        Assert.Equal(2, device.Tables.Count);
        Assert.Equal(new[] { 1f, 1f }, native.Executions.Select(ReadFloat));
        Assert.Single(native.Snapshots);
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
