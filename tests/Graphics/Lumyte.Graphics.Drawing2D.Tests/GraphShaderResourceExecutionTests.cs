using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.RenderGraph;
using Xunit;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Exercises logical graph operations with generated typed roots and native reference identities.</summary>
public sealed class GraphShaderResourceExecutionTests
{
    private static BarrierScope ShaderRead => new(PipelineStage.VertexShader | PipelineStage.FragmentShader | PipelineStage.ComputeShader, ResourceAccess.ShaderRead);

    private static BarrierScope ColorWrite => new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite);

    /// <summary>Each operation binds its table and typed roots atomically before its requested command.</summary>
    /// <param name="operation">Zero for draw, one for indexed draw, two for compute.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void TypedOperationsBindRootsAndPreserveNestedOwners(int operation)
    {
        var device = new ProfilingDevice();
        using var graph = new Graph(device);
        using IGraphicsShaderDataBuffer<DrawingInstruction> instructions = device.CreateShaderDataBuffer<DrawingInstruction>(DrawingShaders2D.Vertex, 1);
        using IGraphicsBuffer<Vector4> data = device.CreateBuffer<Vector4>(new() { Count = 1, Usage = BufferUsage.ShaderRead });
        using IGraphicsBuffer<ushort> indices = device.CreateBuffer<ushort>(new() { Count = 8, Usage = BufferUsage.Index });
        using var texture = new DispatchTexture();
        using var targetTexture = new DispatchTexture();
        RenderGraphShaderDataBuffer<DrawingInstruction> logicalInstructions = graph.ImportShaderDataBuffer(instructions, ShaderRead);
        RenderGraphBuffer<Vector4> logicalData = graph.ImportBuffer(data, ShaderRead);
        RenderGraphTexture image = graph.ImportTexture(texture, TextureState.Sampled, ShaderRead);
        RenderGraphTexture target = graph.ImportTexture(targetTexture, TextureState.ColorAttachment, ColorWrite);
        ShaderResources resources = graph.CreatePreparedPayload();
        IGpuRef<DrawingInstruction>? rootInstructions = null;
        graph.ConnectPreparedBuffer(resources, logicalInstructions, 0, 1, reference => rootInstructions = reference);
        IGpuRef<Vector4>? rootData = null;
        graph.ConnectPreparedBuffer(resources, logicalData, 0, 1, reference => rootData = reference);
        IGpuRef<IGraphicsTextureView>? nestedTexture = null;
        graph.ConnectPreparedTexture(resources, image, reference => nestedTexture = reference);
        IGpuRef<IGraphicsSampler>? nestedSampler = null;
        resources.RegisterSampler(graph.GetPreparedSampler(new()), reference => nestedSampler = reference);
        DrawingInstruction instruction = default;
        resources.WhenReady(() =>
        {
            instruction = instruction with { Texture = nestedTexture!, Sampler = nestedSampler! };
            instructions.CopyFrom(new[] { instruction });
        });
        RenderGraphPass pass = graph.AddPass("typed", context =>
        {
            var arguments = new DrawingArguments(rootInstructions!, rootData!, 1, 0, 0, 1, default, default, default, nestedTexture!, nestedTexture!, nestedSampler!);
            if (operation == 2)
            {
                IComputeEncoder encoder = context.Commands.BeginComputePass(new());
                resources.Dispatch(encoder, in arguments, 2, 3, 4);
                encoder.End();
            }
            else
            {
                using IGraphicsTextureView targetView = context.GetTexture(target).CreateView();
                IRenderEncoder encoder = context.Commands.BeginRenderPass(new() { ColorAttachments = [new() { View = targetView, LoadOp = AttachmentLoadOp.Load, StoreOp = AttachmentStoreOp.Store }] });
                if (operation == 0)
                {
                    resources.Draw(encoder, in arguments, 3, 2);
                }
                else
                {
                    encoder.SetIndexBuffer(indices.SliceElements(0, 8));
                    resources.DrawIndexed(encoder, in arguments, 6, 2, 1, -1, 3);
                }

                encoder.End();
            }
        }).UseShaderDataBuffer(logicalInstructions, ShaderRead).UseBuffer(logicalData, ShaderRead).UseTexture(image, TextureState.Sampled, ShaderRead).KeepAlive();
        if (operation != 2)
        {
            pass.UseTexture(target, TextureState.ColorAttachment, ColorWrite);
        }

        pass.UseSampler(graph.GetPreparedSampler(new()));
        pass.AttachPreparedPayload(resources);
        using CompiledRenderGraph compiled = graph.Compile();
        using var commands = new ProfilingCommands();
        compiled.Record(commands);
        IArgumentTable table = Assert.Single(commands.Events.OfType<IArgumentTable>());
        DrawingArguments recorded = Assert.Single(commands.Events.OfType<DrawingArguments>());
        Assert.Same(table, ProfilingDevice.Owner(recorded.Instructions));
        Assert.Same(table, ProfilingDevice.Owner(recorded.Data));
        Assert.Same(table, ProfilingDevice.Owner(recorded.SourceTexture));
        Assert.Same(table, ProfilingDevice.Owner(recorded.GraphSampler));
        DrawingInstruction packed = Assert.Single(device.Instructions);
        Assert.Same(table, ProfilingDevice.Owner(packed.Texture));
        Assert.Same(table, ProfilingDevice.Owner(packed.Sampler));
        int binding = commands.Events.IndexOf(table);
        Assert.IsType<DrawingArguments>(commands.Events[binding + 1]);
        Assert.Equal(operation switch { 0 => (object)(3u, 2u), 1 => ("indexed", 6u, 2u, 1u, -1, 3u), _ => ("dispatch", 2u, 3u, 4u) }, commands.Events[binding + 2]);
        commands.Dispose();
    }

    /// <summary>Render and compute shapes cannot be interchanged even with valid generated argument types.</summary>
    /// <param name="graphics">Whether this operation declares a color output.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExecutionRejectsMismatchedShape(bool graphics)
    {
        var device = new ProfilingDevice();
        using var graph = new Graph(device);
        using var targetTexture = new DispatchTexture();
        RenderGraphTexture target = graph.ImportTexture(targetTexture, TextureState.ColorAttachment, ColorWrite);
        ShaderResources resources = graph.CreatePreparedPayload();
        resources.RegisterSampler(graph.GetPreparedSampler(new()), _ => { });
        RenderGraphPass pass = graph.AddPass("shape", _ => { }).KeepAlive();
        if (graphics)
        {
            pass.UseTexture(target, TextureState.ColorAttachment, ColorWrite);
        }

        pass.UseSampler(graph.GetPreparedSampler(new()));
        pass.AttachPreparedPayload(resources);
        using CompiledRenderGraph compiled = graph.Compile();
        using var commands = new ProfilingCommands();
        compiled.Record(commands);
        var arguments = default(DrawingArguments);
        Assert.Throws<InvalidOperationException>(() =>
        {
            if (graphics)
            {
                resources.Dispatch(commands.BeginComputePass(new()), in arguments, 1);
            }
            else
            {
                resources.Draw(commands.BeginRenderPass(new()), in arguments, 3);
            }
        });
        if (!graphics)
        {
            Assert.Throws<InvalidOperationException>(() => resources.DrawIndexed(commands.BeginRenderPass(new()), in arguments, 3));
        }

        Assert.All(commands.Events, item => Assert.IsType<TextureBarrierDesc>(item));
        commands.Dispose();
    }

    /// <summary>All live references publish before callbacks, but no operation records until every callback succeeds.</summary>
    [Fact]
    public void CallbackFailureInvalidatesEarlierCallbackReferencesGlobally()
    {
        var device = new ProfilingDevice();
        using var graph = new Graph(device);
        ShaderResources first = graph.CreatePreparedPayload();
        ShaderResources later = first.CreateSibling();
        IGpuRef<IGraphicsSampler>? firstHandle = null;
        first.RegisterSampler(graph.GetPreparedSampler(new()), reference =>
        {
            Assert.True(first.IsReady);
            Assert.True(later.IsReady);
            Assert.NotNull(ProfilingDevice.Owner(reference));
            firstHandle = reference;
        });
        IGpuRef<IGraphicsSampler>? laterHandle = null;
        later.RegisterSampler(graph.GetPreparedSampler(new()), reference => laterHandle = reference);
        graph.AddPass("first", _ => throw new InvalidOperationException("Must not record")).KeepAlive().UseSampler(graph.GetPreparedSampler(new())).AttachPreparedPayload(first);
        graph.AddPass("later", _ => throw new InvalidOperationException("Must not record")).KeepAlive().UseSampler(graph.GetPreparedSampler(new())).AttachPreparedPayload(later);
        using var callbackCommands = new ProfilingCommands();
        bool earlierRan = false;
        first.WhenReady(() =>
        {
            Assert.True(first.IsReady);
            Assert.True(later.IsReady);
            Assert.Same(ProfilingDevice.Owner(firstHandle!), ProfilingDevice.Owner(laterHandle!));
            var arguments = default(DrawingArguments);
            Assert.Throws<InvalidOperationException>(() => first.Dispatch(callbackCommands.BeginComputePass(new()), in arguments, 1));
            Assert.Throws<InvalidOperationException>(() => first.Draw(callbackCommands.BeginRenderPass(new()), in arguments, 3));
            earlierRan = true;
        });
        later.WhenReady(() => throw new InvalidOperationException("Injected finalizer failure"));
        using CompiledRenderGraph compiled = graph.Compile();
        using var commands = new ProfilingCommands();
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => compiled.Record(commands));
        Assert.Equal("Injected finalizer failure", failure.Message);
        Assert.True(earlierRan);
        Assert.False(first.IsReady);
        Assert.False(later.IsReady);
        Assert.NotNull(firstHandle);
        Assert.NotNull(laterHandle);
        var arguments = default(DrawingArguments);
        Assert.Throws<InvalidOperationException>(() => first.Dispatch(callbackCommands.BeginComputePass(new()), in arguments, 1));
        Assert.Throws<InvalidOperationException>(() => later.Dispatch(callbackCommands.BeginComputePass(new()), in arguments, 1));
        Assert.Empty(commands.Events);
        Assert.Empty(callbackCommands.Events);
        commands.Dispose();
    }

    /// <summary>A base-typed sibling keeps graph validation and cannot register undeclared native storage.</summary>
    [Fact]
    public void BaseTypedSiblingStillRejectsUndeclaredNativeBuffer()
    {
        var device = new ProfilingDevice();
        using var graph = new Graph(device);
        using IGraphicsBuffer<uint> native = device.CreateBuffer<uint>(new() { Count = 1, Usage = BufferUsage.ShaderRead });
        ShaderResources original = graph.CreatePreparedPayload();
        ShaderResources sibling = original.CreateSibling();
        Assert.IsAssignableFrom<ShaderResources>(sibling);
        original.Cancel();
        IGpuRef<uint>? handle = null;
        sibling.RegisterBuffer(native.SliceElements(0, 1), reference => handle = reference);
        graph.AddPass("sibling", _ => { }).KeepAlive().AttachPreparedPayload(sibling);
        using CompiledRenderGraph compiled = graph.Compile();
        using var commands = new ProfilingCommands();
        Assert.Throws<ArgumentException>(() => compiled.Record(commands));
        Assert.Null(handle);
        Assert.Empty(device.Tables);
        Assert.Empty(commands.Events);
        commands.Dispose();
    }

    /// <summary>Selective cancellation does not discard an intervening module's resource group or lifetime lease.</summary>
    [Fact]
    public void CancelPreservesInterveningGroupAndRetainedLifetime()
    {
        var device = new ProfilingDevice();
        using var graph = new Graph(device);
        ShaderResources canceled = graph.CreatePreparedPayload();
        IGpuRef<IGraphicsSampler>? discarded = null;
        canceled.RegisterSampler(graph.GetPreparedSampler(new()), reference => discarded = reference);
        ShaderResources survivor = graph.CreatePreparedPayload();
        IGpuRef<IGraphicsSampler>? live = null;
        survivor.RegisterSampler(graph.GetPreparedSampler(new()), reference => live = reference);
        IDisposable lease = survivor.Retain();
        canceled.Cancel();
        graph.AddPass("survivor", _ => { }).KeepAlive().UseSampler(graph.GetPreparedSampler(new())).AttachPreparedPayload(survivor);
        using CompiledRenderGraph compiled = graph.Compile();
        using var commands = new ProfilingCommands();
        compiled.Record(commands);
        Assert.True(survivor.IsReady);
        Assert.Null(discarded);
        Assert.Single(device.Tables);
        IArgumentTable owner = ProfilingDevice.Owner(live!);
        commands.Dispose();
        compiled.Dispose();
        graph.Dispose();
        Assert.Equal(1, device.ActiveTables);
        Assert.Same(owner, ProfilingDevice.Owner(live!));
        lease.Dispose();
        lease.Dispose();
        Assert.Equal(0, device.ActiveTables);
        Assert.False(survivor.IsReady);
    }

    /// <summary>Two actual color outputs consume their exact budget; compute reserves none.</summary>
    /// <param name="graphics">Whether two color outputs are used.</param>
    /// <param name="budget">The finite native combined binding limit.</param>
    /// <param name="succeeds">Whether the whole operation fits.</param>
    [Theory]
    [InlineData(false, 1u, true)]
    [InlineData(true, 2u, false)]
    [InlineData(true, 3u, true)]
    public void ActualAttachmentsDetermineFiniteBindingBudget(bool graphics, uint budget, bool succeeds)
    {
        var device = new ProfilingDevice
        {
            Caps = new() { ArgumentTableBindingLimits = new() { CombinedBindings = budget, ColorAttachmentBindingWeight = 1 } },
        };
        using var graph = new Graph(device);
        using var firstTexture = new DispatchTexture();
        using var secondTexture = new DispatchTexture();
        RenderGraphTexture first = graph.ImportTexture(firstTexture, TextureState.ColorAttachment, ColorWrite);
        RenderGraphTexture second = graph.ImportTexture(secondTexture, TextureState.ColorAttachment, ColorWrite);
        ShaderResources resources = graph.CreatePreparedPayload();
        IGpuRef<IGraphicsSampler>? sampler = null;
        resources.RegisterSampler(graph.GetPreparedSampler(new()), reference => sampler = reference);
        RenderGraphPass pass = graph.AddPass("budget", _ => { }).KeepAlive();
        if (graphics)
        {
            pass.UseTexture(first, TextureState.ColorAttachment, ColorWrite).UseTexture(second, TextureState.ColorAttachment, ColorWrite);
        }

        pass.UseSampler(graph.GetPreparedSampler(new()));
        pass.AttachPreparedPayload(resources);
        using CompiledRenderGraph compiled = graph.Compile();
        using var commands = new ProfilingCommands();
        if (succeeds)
        {
            compiled.Record(commands);
            Assert.Single(device.Tables);
            Assert.Equal(1u, device.Tables[0].SamplerCapacity);
            Assert.NotNull(ProfilingDevice.Owner(sampler!));
        }
        else
        {
            Assert.Throws<NotSupportedException>(() => compiled.Record(commands));
            Assert.Empty(device.Tables);
            Assert.Null(sampler);
        }

        Assert.All(commands.Events, item => Assert.IsType<TextureBarrierDesc>(item));
        commands.Dispose();
    }

    /// <summary>Actual outputs must be unique, local, and already declared as color writes.</summary>
    /// <param name="invalidKind">Undeclared, duplicate, or foreign output.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void AttachmentsRequireTruthfulPassDeclarations(int invalidKind)
    {
        var device = new ProfilingDevice();
        using var graph = new Graph(device);
        using var foreign = new Graph(device);
        using var native = new DispatchTexture();
        using var foreignNative = new DispatchTexture();
        RenderGraphTexture target = graph.ImportTexture(native, TextureState.ColorAttachment, ColorWrite);
        RenderGraphTexture other = foreign.ImportTexture(foreignNative, TextureState.ColorAttachment, ColorWrite);
        RenderGraphPass pass = graph.AddPass("invalid-output", context =>
        {
            RenderGraphTexture[] outputs = invalidKind switch { 0 => [target], 1 => [target, target], _ => [other] };
            using RenderGraphRenderEncoder render = context.BeginRenderPass(new()
            {
                ColorAttachments = outputs.Select(texture => new RenderGraphColorAttachmentDesc { Texture = texture, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store }).ToArray(),
            });
        }).KeepAlive();
        if (invalidKind == 1)
        {
            pass.UseTexture(target, TextureState.ColorAttachment, ColorWrite);
        }

        using CompiledRenderGraph compiled = graph.Compile();
        using var commands = new ProfilingCommands();
        Assert.Throws<ArgumentException>(() => compiled.Record(commands));
        Assert.Empty(device.Tables);
        commands.Dispose();
    }

    /// <summary>Base casts cannot attach a sibling to a foreign graph or attach it more than once.</summary>
    [Fact]
    public void BaseTypedSiblingRetainsForeignAndDuplicateAttachmentGuards()
    {
        var device = new ProfilingDevice();
        using var graph = new Graph(device);
        using var foreign = new Graph(device);
        ShaderResources original = graph.CreatePreparedPayload();
        ShaderResources sibling = original.CreateSibling();
        original.Cancel();
        using var sampler = new DispatchSampler();
        sibling.RegisterSampler(sampler, _ => { });
        RenderGraphPass foreignPass = foreign.AddPass("foreign", _ => { }).KeepAlive();
        Assert.Throws<ArgumentException>(() => foreignPass.AttachPreparedPayload(sibling));
        RenderGraphPass pass = graph.AddPass("local", _ => { }).KeepAlive();
        pass.UseSampler(sampler);
        pass.AttachPreparedPayload(sibling);
        Assert.Throws<ArgumentException>(() => pass.AttachPreparedPayload(sibling));
        using CompiledRenderGraph compiled = graph.Compile();
        using var commands = new ProfilingCommands();
        compiled.Record(commands);
        Assert.True(sibling.IsReady);
        Assert.Single(device.Tables);
        commands.Dispose();
    }
}
