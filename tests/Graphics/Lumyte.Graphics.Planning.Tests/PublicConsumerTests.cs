using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Planning.Tests;

/// <summary>Exercises the planning library strictly through its public consumer surface.</summary>
public sealed class PublicConsumerTests
{
    private static readonly PlanRange _whole = new(0, 4);
    private static readonly BarrierScope _copyRead = new(PipelineStage.Copy, ResourceAccess.CopyRead);
    private static readonly BarrierScope _copyWrite = new(PipelineStage.Copy, ResourceAccess.CopyWrite);

    /// <summary>Compilation culls unobserved work and preparation never records commands.</summary>
    /// <returns>The asynchronous test completion.</returns>
    [Fact]
    public async Task CopyCullsDeadWorkAndSeparatesPreparationAsync()
    {
        await using var host = new PlanningHost();
        var device = new ManagedDevice();
        await using var session = new SessionOwner(host.Module, device);
        await using var frame = new FrameOwner(session.CreateFrame());
        PlanBuffer<int> source = Import(frame);
        PlanPass copy = frame.Plan.AddPass("copy");
        PlanBuffer<int> destination = copy.Write(Create(frame), _whole, PlanWriteMode.Discard, _copyWrite);
        copy.Read(source, _whole, _copyRead);
        copy.Copy(source, new(1, 2), destination, new(2, 2));
        frame.Plan.Export(destination);
        frame.Plan.AddPass("dead-empty-pass");
        using CompiledGraphicsPlan compiled = await frame.CompileAsync();
        Assert.Equal(new[] { copy }, compiled.Passes);
        Assert.Empty(device.Events);
        await using var preparedOwner = new PreparedOwner(await compiled.PrepareAsync());
        PreparedGraphicsExecution prepared = preparedOwner.Value;
        Assert.Equal(new[] { "allocate-buffer", "allocate-commands" }, device.Events);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await compiled.PrepareAsync());
        await using var executionOwner = new ExecutionOwner(session.CreateExecution(prepared));
        GraphicsExecution execution = executionOwner.Value;
        Assert.Throws<InvalidOperationException>(() => session.CreateExecution(prepared));
        await execution.RecordAsync();
        Assert.Contains("copy:1:2:2", device.Events);
        Assert.DoesNotContain("submit", device.Events);
        await execution.FinishAsync();
        await execution.SubmitAsync();
        await execution.WaitAsync();
        await execution.RetireAsync();
        Assert.Equal(GraphicsRecoveryState.Retired, execution.Recovery);
        Assert.True(device.Events.IndexOf("dispose-commands") < device.Events.IndexOf("dispose-buffer"));
    }

    /// <summary>Same-version RAW and cross-version WAR/WAW edges retain their declared scopes.</summary>
    /// <returns>The asynchronous test completion.</returns>
    [Fact]
    public async Task VersionHazardsProduceOrderedBarriersAsync()
    {
        await using var host = new PlanningHost();
        var device = new ManagedDevice();
        await using var session = new SessionOwner(host.Module, device);
        await using var frame = new FrameOwner(session.CreateFrame());
        ComputeKernel<TestArguments> kernel = session.ImportComputeKernel<TestArguments>(new ManagedKernel(), new TestLease());
        PlanPass producer = frame.Plan.AddPass("producer");
        PlanBuffer<int> first = producer.Write(Create(frame), _whole, PlanWriteMode.Discard, _copyWrite);
        producer.Dispatch(kernel, default(TestArguments), 1);
        producer.KeepAlive();
        PlanPass reader = frame.Plan.AddPass("reader");
        reader.Read(first, _whole, _copyRead);
        reader.Dispatch(kernel, default(TestArguments), 1);
        reader.KeepAlive();
        PlanPass overwrite = frame.Plan.AddPass("overwrite");
        PlanBuffer<int> second = overwrite.Write(first, _whole, PlanWriteMode.Discard, _copyWrite);
        overwrite.Dispatch(kernel, default(TestArguments), 1);
        frame.Plan.Export(second);
        using CompiledGraphicsPlan compiled = await frame.CompileAsync();
        Assert.Equal(new[] { producer, reader, overwrite }, compiled.Passes);
        Assert.Contains(compiled.Barriers, b => b.Before == producer && b.After == reader && b.Source.Resource == b.Destination.Resource && b.Source.Scope == _copyWrite && b.Destination.Scope == _copyRead);
        Assert.Contains(compiled.Barriers, b => b.Before == reader && b.After == overwrite && b.Source.Access == PlanAccess.Read);
        Assert.Contains(compiled.Barriers, b => b.Before == producer && b.After == overwrite && b.Source.Access == PlanAccess.Write);
        await using var preparedOwner = new PreparedOwner(await compiled.PrepareAsync());
        PreparedGraphicsExecution prepared = preparedOwner.Value;
        await using var executionOwner = new ExecutionOwner(session.CreateExecution(prepared));
        GraphicsExecution execution = executionOwner.Value;
        await execution.RecordAsync();
        Assert.Equal(compiled.Barriers.Count, device.Commands!.Barriers.Count);
    }

    /// <summary>Undefined contents cannot become observable, even through partially initialized exports.</summary>
    /// <param name="partialExport">Whether to observe a partially initialized export instead of an undefined read.</param>
    /// <returns>The asynchronous test completion.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UndefinedReadsAndPartialExportsAreRejectedAsync(bool partialExport)
    {
        await using var host = new PlanningHost();
        await using var session = new SessionOwner(host.Module, new ManagedDevice());
        await using var frame = new FrameOwner(session.CreateFrame());
        PlanBuffer<int> buffer = Create(frame);
        if (partialExport)
        {
            PlanPass writer = frame.Plan.AddPass("partial");
            buffer = writer.Write(buffer, new(0, 2), PlanWriteMode.Discard, _copyWrite);
            frame.Plan.Export(buffer);
        }
        else
        {
            PlanPass reader = frame.Plan.AddPass("undefined");
            reader.Read(buffer, _whole, _copyRead);
            reader.KeepAlive();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await frame.CompileAsync());
    }

    /// <summary>Explicit dependency cycles fail before any backend allocation.</summary>
    /// <returns>The asynchronous test completion.</returns>
    [Fact]
    public async Task CyclesAreRejectedAsync()
    {
        await using var host = new PlanningHost();
        var device = new ManagedDevice();
        await using var session = new SessionOwner(host.Module, device);
        await using var frame = new FrameOwner(session.CreateFrame());
        ComputeKernel<TestArguments> kernel = session.ImportComputeKernel<TestArguments>(new ManagedKernel(), new TestLease());
        PlanPass first = frame.Plan.AddPass("first");
        PlanPass second = frame.Plan.AddPass("second");
        first.Dispatch(kernel, default(TestArguments), 1);
        second.Dispatch(kernel, default(TestArguments), 1);
        first.DependsOn(second);
        second.DependsOn(first);
        first.KeepAlive();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await frame.CompileAsync());
        Assert.Empty(device.Events);
    }

    /// <summary>Repeated imports remain immutable version zero and retain independent leases.</summary>
    /// <returns>The asynchronous test completion.</returns>
    [Fact]
    public async Task ImportsAliasIdentityWithoutMutatingOldVersionsAsync()
    {
        await using var host = new PlanningHost();
        await using var session = new SessionOwner(host.Module, new ManagedDevice());
        await using var frame = new FrameOwner(session.CreateFrame());
        var native = new ManagedBuffer<int>(4, []);
        var firstLease = new TestLease();
        var secondLease = new TestLease();
        PlanBuffer<int> original = frame.Plan.ImportBuffer(native, firstLease);
        PlanPass writer = frame.Plan.AddPass("write");
        PlanBuffer<int> next = writer.Write(original, _whole, PlanWriteMode.Discard, _copyWrite);
        PlanBuffer<int> alias = frame.Plan.ImportBuffer(native, secondLease);
        Assert.Same(original, alias);
        Assert.Equal(0, original.Version);
        Assert.Equal(1, next.Version);
        Assert.Throws<InvalidOperationException>(() => writer.Write(alias, _whole, PlanWriteMode.Discard, _copyWrite));
        Assert.Throws<ArgumentException>(() => frame.Plan.ImportBuffer(native, new TestLease(), initialized: false));
        await frame.Value.RetireAsync();
        Assert.Equal(1, firstLease.Disposals);
        Assert.Equal(1, secondLease.Disposals);
    }

    /// <summary>Foreign resources, passes, kernels and operation-local references are not accepted.</summary>
    /// <returns>The asynchronous test completion.</returns>
    [Fact]
    public async Task ForeignScopesAndRawReferencesAreRejectedAsync()
    {
        await using var host = new PlanningHost();
        await using var firstSession = new SessionOwner(host.Module, new ManagedDevice());
        await using var secondSession = new SessionOwner(host.Module, new ManagedDevice());
        await using var first = new FrameOwner(firstSession.CreateFrame());
        await using var second = new FrameOwner(secondSession.CreateFrame());
        PlanPass pass = first.Plan.AddPass("first");
        PlanPass foreign = second.Plan.AddPass("foreign");
        Assert.Throws<ArgumentException>(() => pass.DependsOn(foreign));
        Assert.Throws<ArgumentException>(() => pass.Read(Create(second), _whole, _copyRead));
        ComputeKernel<TestArguments> kernel = firstSession.ImportComputeKernel<TestArguments>(new ManagedKernel(), new TestLease());
        ComputeKernel<TestArguments> foreignKernel = secondSession.ImportComputeKernel<TestArguments>(new ManagedKernel(), new TestLease());
        Assert.Throws<ArgumentException>(() => pass.Dispatch(foreignKernel, default(TestArguments), 1));
        Assert.Throws<ArgumentException>(() => pass.Dispatch(kernel, new TestArguments(new ManagedReference<int>(4)), 1));
        PlanBuffer<int> source = Import(first);
        pass.Read(source, _whole, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead));
        PlanGpuRef<int> reference = pass.Buffer(source, _whole);
        PlanPass other = first.Plan.AddPass("other");
        Assert.Throws<ArgumentException>(() => other.Dispatch(kernel, new TestArguments(reference), 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => reference.GetElement(4));
        Assert.Throws<InvalidOperationException>(() => firstSession.CreateFrame());
    }

    /// <summary>Symbolic references are patched during prepare, then the imported kernel and values bind during record.</summary>
    /// <returns>The asynchronous test completion.</returns>
    [Fact]
    public async Task DispatchPatchesBindingsBeforeRecordingAsync()
    {
        await using var host = new PlanningHost();
        var device = new ManagedDevice();
        await using var session = new SessionOwner(host.Module, device);
        await using var frame = new FrameOwner(session.CreateFrame());
        var nativeKernel = new ManagedKernel();
        ComputeKernel<TestArguments> kernel = session.ImportComputeKernel<TestArguments>(nativeKernel, new TestLease());
        PlanPass pass = frame.Plan.AddPass("dispatch");
        PlanBuffer<int> buffer = Import(frame);
        pass.Read(buffer, _whole, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead));
        var arguments = new TestArguments(pass.Buffer(buffer, _whole), 73);
        pass.Dispatch(kernel, arguments, 2, 3, 4);
        pass.KeepAlive();
        using CompiledGraphicsPlan compiled = await frame.CompileAsync();
        await using var preparedOwner = new PreparedOwner(await compiled.PrepareAsync());
        PreparedGraphicsExecution prepared = preparedOwner.Value;
        Assert.Contains("register-buffer", device.Events);
        Assert.Equal(ArgumentBufferAccess.Read, Assert.Single(prepared.PhysicalBindings).Access);
        Assert.DoesNotContain("record-begin", device.Events);
        int preparationEvents = device.Events.Count;
        await using var executionOwner = new ExecutionOwner(session.CreateExecution(prepared));
        GraphicsExecution execution = executionOwner.Value;
        await execution.RecordAsync();
        Assert.DoesNotContain(device.Events.Skip(preparationEvents), e => e.StartsWith("allocate-", StringComparison.Ordinal) || e == "register-buffer");
        Assert.Same(nativeKernel, device.Commands!.ComputePipeline);
        Assert.Equal(new byte[] { 73 }, device.Commands.ArgumentBytes);
        Assert.IsType<ManagedReference<int>>(Assert.Single(device.Commands.References));
        Assert.Contains("dispatch:2:3:4", device.Events);
        Assert.Contains("bind-table", device.Events);
    }

    /// <summary>A structured draw prepares its view and binds the imported raster kernel.</summary>
    /// <returns>The asynchronous test completion.</returns>
    [Fact]
    public async Task DrawPreparesAttachmentAndBindsRasterKernelAsync()
    {
        await using var host = new PlanningHost();
        var device = new ManagedDevice();
        await using var session = new SessionOwner(host.Module, device);
        await using var frame = new FrameOwner(session.CreateFrame());
        var nativeKernel = new ManagedKernel();
        RasterKernel<TestArguments> kernel = session.ImportRasterKernel<TestArguments>(nativeKernel, new TestLease());
        PlanTexture texture = frame.Plan.CreateTexture(new() { Width = 8, Height = 8, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.RenderAttachment });
        PlanPass pass = frame.Plan.AddPass("draw");
        PlanTexture target = pass.Write(texture, texture.Range, PlanWriteMode.Discard, new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite), TextureState.ColorAttachment);
        pass.Draw(kernel, default(TestArguments), target, new GraphicsRasterState { RenderState = GraphicsRenderStateDesc.ForColorTargets(1), Viewport = new(0, 0, 8, 8, 0, 1), Scissor = new(0, 0, 8, 8) }, 3, 2);
        frame.Plan.Export(target);
        using CompiledGraphicsPlan compiled = await frame.CompileAsync();
        await using var preparedOwner = new PreparedOwner(await compiled.PrepareAsync());
        PreparedGraphicsExecution prepared = preparedOwner.Value;
        Assert.Contains("allocate-view", device.Events);
        await using var executionOwner = new ExecutionOwner(session.CreateExecution(prepared));
        GraphicsExecution execution = executionOwner.Value;
        Assert.Throws<InvalidOperationException>(execution.Record);
        await execution.RecordAsync();
        Assert.Same(nativeKernel, device.Commands!.RasterPipeline);
        Assert.Equal(AttachmentLoadOp.Clear, Assert.Single(device.Commands.RenderPass!.ColorAttachments).LoadOp);
        Assert.Contains("draw:3:2", device.Events);
        Assert.Contains("render-state", device.Events);
        Assert.Contains("viewport", device.Events);
        Assert.Contains("scissor", device.Events);
    }

    private static PlanBuffer<int> Create(GraphicsFrameScope frame) => frame.Plan.CreateBuffer<int>(new() { Count = 4, Usage = BufferUsage.CopySource | BufferUsage.CopyDestination });

    private static PlanBuffer<int> Import(GraphicsFrameScope frame) => frame.Plan.ImportBuffer(new ManagedBuffer<int>(4, []), new TestLease());
}
