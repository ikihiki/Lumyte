using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.RenderGraph.Tests;

/// <summary>Exercises graph-native inputs from an assembly with no friend access to graphics internals.</summary>
public sealed partial class PublicGraphEncoderConsumerTests
{
    /// <summary>Logical graph objects expose neither registration coordination nor native encoder escapes.</summary>
    [Fact]
    public void PublicSurfaceIsSealedCompositionWithoutNativeEscape()
    {
        foreach (Type type in new[] { typeof(RenderGraphShaderResources), typeof(RenderGraphRenderEncoder), typeof(RenderGraphComputeEncoder) })
        {
            Assert.True(type.IsSealed);
            Assert.Equal(typeof(object), type.BaseType);
            Assert.DoesNotContain(type.GetMethods(BindingFlags.Public | BindingFlags.Instance), method => method.Name is "SetArgumentTable" or "RegisterTexture" or "RegisterBuffer");
            Assert.DoesNotContain(type.GetProperties(), property => typeof(IArgumentTable).IsAssignableFrom(property.PropertyType) || typeof(IRenderEncoder).IsAssignableFrom(property.PropertyType) || typeof(IComputeEncoder).IsAssignableFrom(property.PropertyType));
        }
    }

    /// <summary>Ordinary application argument codecs are accepted without a graph-specific root description.</summary>
    [Fact]
    public void TypedArgumentsDoNotRequireAResourceOrArtifactDescription()
    {
        using var graph = new RenderGraph(new PublicGraphConsumerDevice());
        using var pipeline = new PublicGraphConsumerComputePipeline(Artifact(ShaderStage.Compute));
        graph.AddPass("typed numeric", context =>
        {
            using RenderGraphComputeEncoder encoder = context.BeginComputePass();
            encoder.SetPipeline(pipeline);
            var arguments = new ConsumerArguments(7f);
            encoder.SetArguments(in arguments);
            encoder.Dispatch(1);
        }).KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();
        using var commands = new PublicGraphConsumerCommands();
        plan.Record(commands);
        Assert.Equal(7f, ReadFloat(Assert.Single(commands.Encoder.Executions)));
    }

    /// <summary>SetArguments captures once; indexed drawing and ended/context scopes stay guarded.</summary>
    [Fact]
    public void DrawAndIndexedDrawReuseSelectedSnapshotAndCloseScope()
    {
        using var graph = new RenderGraph(new PublicGraphConsumerDevice());
        ShaderArtifact artifact = Artifact(ShaderStage.Vertex);
        using var pipeline = new PublicGraphConsumerPipeline(artifact);
        using var image = new TestTexture();
        RenderGraphTexture target = graph.ImportTexture(image, TextureState.Undefined, default);
        int reads = 0;
        var logical = new ConsumerArguments(0, () => ++reads);
        RenderGraphRenderEncoder? escaped = null;
        graph.AddPass("draw", context =>
        {
            escaped = context.BeginRenderPass(Attachments(target));
            Assert.Throws<InvalidOperationException>(() => escaped.SetArguments(logical));
            using var wrong = new PublicGraphConsumerPipeline(Artifact(ShaderStage.Vertex));
            escaped.SetPipeline(wrong);
            escaped.SetArguments(in logical);
            escaped.SetPipeline(pipeline);
            escaped.SetArguments(logical);
            escaped.Draw(3);
            using var indices = new TestTypedBuffer<ushort>(new() { Count = 3, Usage = BufferUsage.Index }, []);
            escaped.SetIndexBuffer(indices.SliceElements(0, 3));
            escaped.DrawIndexed(3);
            escaped.End();
            Assert.Throws<InvalidOperationException>(() => escaped.SetArguments(logical));
        }).UseTexture(target, TextureState.ColorAttachment, new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite))
          .KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();
        using var commands = new PublicGraphConsumerCommands();
        plan.Record(commands);
        Assert.Equal(1, commands.Encoder.Draws);
        Assert.Equal(1, commands.Encoder.IndexedDraws);
        Assert.Equal(2, reads);
        Assert.Equal(new[] { 1f, 2f }, commands.Encoder.Snapshots.Select(snapshot => MemoryMarshal.Read<float>(snapshot.Values.Single(value => value.Path == "Float").Data.Span)));
        Assert.Equal(1, commands.Encoder.Ends);
        Assert.Throws<InvalidOperationException>(() => escaped!.SetPipeline(pipeline));
    }

    /// <summary>Incomplete roots fail before a native argument snapshot or draw is emitted.</summary>
    [Fact]
    public void MissingMemberIsRejectedBeforeNativeDraw()
    {
        using var graph = new RenderGraph(new PublicGraphConsumerDevice());
        ShaderArtifact artifact = Artifact(ShaderStage.Vertex);
        using var pipeline = new PublicGraphConsumerPipeline(artifact);
        using var image = new TestTexture();
        RenderGraphTexture target = graph.ImportTexture(image, TextureState.Undefined, default);
        MissingConsumerArguments logical = default;
        graph.AddPass("missing", context =>
        {
            using RenderGraphRenderEncoder encoder = context.BeginRenderPass(Attachments(target));
            encoder.SetPipeline(pipeline);
            Assert.Throws<ArgumentException>(() => encoder.SetArguments(logical));
        }).UseTexture(target, TextureState.ColorAttachment, new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite))
          .KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();
        using var commands = new PublicGraphConsumerCommands();
        plan.Record(commands);
        Assert.Equal(0, commands.Encoder.Draws);
        Assert.Empty(commands.Encoder.Snapshots);
    }

    /// <summary>Compute uses the ordinary codec schema, not artifact object identity, and closes its callback scope.</summary>
    [Fact]
    public void ComputeDispatchAcceptsCompatibleArtifactsAndClosesCallback()
    {
        using var graph = new RenderGraph(new PublicGraphConsumerDevice());
        ShaderArtifact artifact = Artifact(ShaderStage.Compute);
        using var pipeline = new PublicGraphConsumerComputePipeline(artifact);
        using var wrong = new PublicGraphConsumerComputePipeline(Artifact(ShaderStage.Compute));
        var logical = new ConsumerArguments(1f);
        RenderGraphComputeEncoder? escaped = null;
        graph.AddPass("dispatch", context =>
        {
            escaped = context.BeginComputePass();
            escaped.SetPipeline(wrong);
            escaped.SetArguments(in logical);
            escaped.SetPipeline(pipeline);
            escaped.SetArguments(logical);
            escaped.Dispatch(2, 3, 4);
        }).KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();
        using var commands = new PublicGraphConsumerCommands();
        plan.Record(commands);
        Assert.Equal(1, commands.Encoder.Dispatches);
        Assert.Equal(2, commands.Encoder.Snapshots.Count);
        Assert.Equal(1, commands.Encoder.Ends);
        Assert.Throws<InvalidOperationException>(() => escaped!.Dispatch(1));
    }

    /// <summary>A logical resource set cannot be connected to another graph or two consumers.</summary>
    [Fact]
    public void ForeignGraphAndDuplicateConsumerAreRejected()
    {
        var device = new PublicGraphConsumerDevice();
        using var first = new RenderGraph(device);
        using var second = new RenderGraph(device);
        RenderGraphShaderResources logical = first.CreateShaderResources();
        RenderGraphPass foreign = second.AddPass("foreign", _ => { });
        Assert.Throws<ArgumentException>(() => foreign.UseShaderResources(logical));
        first.AddPass("owner", _ => { }).UseShaderResources(logical);
        Assert.Throws<ArgumentException>(() => first.AddPass("duplicate", _ => { }).UseShaderResources(logical));
    }

    /// <summary>A group connected to another pass cannot resolve references during a callback.</summary>
    [Fact]
    public void WrongPassCannotResolveLogicalInputs()
    {
        using var graph = new RenderGraph(new PublicGraphConsumerDevice());
        using var image = new TestTexture();
        RenderGraphTexture source = graph.ImportTexture(image, TextureState.Sampled, default);
        RenderGraphShaderResources resources = graph.CreateShaderResources().ReadTexture(source);
        graph.AddPass("owner", _ => { }).UseTexture(source, TextureState.Sampled, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead)).UseShaderResources(resources).KeepAlive();
        graph.AddPass("wrong", context => Assert.Throws<ArgumentException>(() => context.GetGpuRef(resources, source))).KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();
        using var commands = new PublicGraphConsumerCommands();
        plan.Record(commands);
    }

    /// <summary>A callback exception remains the primary failure while automatic encoder cleanup runs.</summary>
    /// <param name="cleanupFails">Whether ending the native encoder also throws.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CallbackFailurePreservesOriginalAndEndsEncoder(bool cleanupFails)
    {
        using var graph = new RenderGraph(new PublicGraphConsumerDevice());
        var original = new ApplicationException("callback failure");
        graph.AddPass("throw", context =>
        {
            _ = context.BeginComputePass();
            throw original;
        }).KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();
        var cleanup = new InvalidOperationException("cleanup failure");
        using var failingCommands = new PublicGraphConsumerCommands
        {
            Encoder = new PublicGraphConsumerEncoder { EndException = cleanupFails ? cleanup : null },
        };
        Assert.Same(original, Assert.Throws<ApplicationException>(() => plan.Record(failingCommands)));
        Assert.Equal(1, failingCommands.Encoder.Ends);
        if (cleanupFails)
        {
            Assert.Same(cleanup, original.Data["RenderGraph.EncoderCleanupException"]);
        }
    }

    /// <summary>A using-scoped compute encoder must not replace the callback failure when ending fails.</summary>
    [Fact]
    public void UsingComputeEncoderCleanupFailurePreservesOriginalAndEndsOnce()
    {
        using var graph = new RenderGraph(new PublicGraphConsumerDevice());
        var original = new ApplicationException("callback failure");
        graph.AddPass("throw from using compute", context =>
        {
            using RenderGraphComputeEncoder encoder = context.BeginComputePass();
            throw original;
        }).KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();
        using var commands = new PublicGraphConsumerCommands
        {
            Encoder = new PublicGraphConsumerEncoder { EndException = new InvalidOperationException("cleanup failure") },
        };
        Assert.Same(original, Assert.Throws<ApplicationException>(() => plan.Record(commands)));
        Assert.Equal(1, commands.EndAttempts);
    }

    /// <summary>A using-scoped render encoder must not replace the callback failure when ending fails.</summary>
    [Fact]
    public void UsingRenderEncoderCleanupFailurePreservesOriginalAndEndsOnce()
    {
        using var graph = new RenderGraph(new PublicGraphConsumerDevice());
        using var image = new TestTexture();
        RenderGraphTexture target = graph.ImportTexture(image, TextureState.Undefined, default);
        var original = new ApplicationException("callback failure");
        graph.AddPass("throw from using render", context =>
        {
            using RenderGraphRenderEncoder encoder = context.BeginRenderPass(Attachments(target));
            throw original;
        }).UseTexture(target, TextureState.ColorAttachment, new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite))
          .KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();
        using var commands = new PublicGraphConsumerCommands
        {
            Encoder = new PublicGraphConsumerEncoder { EndException = new InvalidOperationException("cleanup failure") },
        };
        Assert.Same(original, Assert.Throws<ApplicationException>(() => plan.Record(commands)));
        Assert.Equal(1, commands.EndAttempts);
    }

    /// <summary>Cleanup failure after a successful callback remains observable and blocks another scope.</summary>
    [Fact]
    public void SuccessfulCallbackReportsDeferredCleanupFailure()
    {
        using var graph = new RenderGraph(new PublicGraphConsumerDevice());
        var cleanup = new InvalidOperationException("cleanup failure");
        graph.AddPass("deferred cleanup", context =>
        {
            RenderGraphComputeEncoder encoder = context.BeginComputePass();
            encoder.Dispose();
            encoder.Dispose();
            Assert.Throws<InvalidOperationException>(() => context.BeginComputePass());
        }).KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();
        using var commands = new PublicGraphConsumerCommands
        {
            Encoder = new PublicGraphConsumerEncoder { EndException = cleanup },
        };
        Assert.Same(cleanup, Assert.Throws<InvalidOperationException>(() => plan.Record(commands)));
        Assert.Equal(1, commands.EndAttempts);
    }

    /// <summary>A depth-only pass stays graphics even though it declares no color outputs.</summary>
    [Fact]
    public void DepthOnlyPassRecordsGraphDrawWithoutColorOutputs()
    {
        using var graph = new RenderGraph(new PublicGraphConsumerDevice());
        ShaderArtifact artifact = Artifact(ShaderStage.Vertex);
        using var pipeline = new PublicGraphConsumerPipeline(artifact);
        using var depth = new TestTexture { Format = TextureFormat.Depth32Float };
        RenderGraphTexture target = graph.ImportTexture(depth, TextureState.Undefined, default);
        var logical = new ConsumerArguments(1f);
        graph.AddPass("depth only", context =>
        {
            using RenderGraphRenderEncoder encoder = context.BeginRenderPass(new()
            {
                DepthStencilAttachment = new() { Texture = target, DepthLoadOp = AttachmentLoadOp.Clear, DepthStoreOp = AttachmentStoreOp.Store },
            });
            encoder.SetPipeline(pipeline);
            encoder.SetArguments(logical);
            encoder.Draw(3);
        }).UseTexture(target, TextureState.DepthStencilAttachment, new(PipelineStage.DepthStencil, ResourceAccess.DepthStencilWrite))
          .KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();
        using var commands = new PublicGraphConsumerCommands();
        plan.Record(commands);
        Assert.Equal(1, commands.Encoder.Draws);
        Assert.Equal(1, commands.EndAttempts);
    }

    private static RenderGraphRenderPassDesc Attachments(RenderGraphTexture target) => new()
    {
        ColorAttachments = [new() { Texture = target, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store }],
    };

    private static ShaderArtifact Artifact(ShaderStage stage, bool hasRoot = true)
    {
        // Metadata-only fixture: public artifact packing validates the header; no shader compiler or GPU is used.
        object Scalar(string name) => new { kind = "scalar", scalarType = name };
        object Vector(int count) => new { kind = "vector", elementType = Scalar("float32"), elementCount = count };
        object Field(string name, object type, int offset, int size) => new { name, type, binding = new { kind = "uniform", offset, size } };
        object[] fields = [Field("Float", Scalar("float32"), 0, 4), Field("Int", Scalar("int32"), 4, 4), Field("UInt", Scalar("uint32"), 8, 4), Field("Vector2", Vector(2), 16, 8), Field("Vector3", Vector(3), 32, 12), Field("Vector4", Vector(4), 48, 16), Field("Matrix", new { kind = "matrix", rowCount = 4, columnCount = 4, elementType = Scalar("float32") }, 64, 64)];
        string reflection = JsonSerializer.Serialize(new
        {
            parameters = new[] { new { name = "arguments", type = new { kind = "constantBuffer", elementType = new { kind = "struct", name = "ConsumerInputs", fields, sizes = new[] { new { kind = "uniform", value = 128, alignment = 16 } } } } } },
        });
        byte[] code = new byte[20];
        BinaryPrimitives.WriteUInt32LittleEndian(code, 0x07230203);
        return ShaderArtifact.PackTarget(ShaderTarget.SpirV, stage, "main", "metadata-only-test-fixture", code, hasRoot ? reflection : "{\"parameters\":[]}");
    }
}
