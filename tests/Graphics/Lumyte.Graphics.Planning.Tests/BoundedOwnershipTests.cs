using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Planning.Tests;

/// <summary>Checks finite admission, export pinning and bounded successful-owner retirement without GC-based oracles.</summary>
public sealed class BoundedOwnershipTests
{
    /// <summary>Successful execution and frame metadata are removed from the session on every retirement.</summary>
    /// <returns>The asynchronous test completion.</returns>
    [Fact]
    public async Task RepeatedFramesRetireAllSessionEntriesAsync()
    {
        await using var host = new PlanningHost();
        await using var session = new SessionOwner(host.Module, new ManagedDevice());
        for (int i = 0; i < 128; i++)
        {
            await RunCopyAsync(session);
            Assert.Equal(0, session.Value.ActiveFrameCount);
            Assert.Equal(0, session.Value.ActiveExecutionCount);
        }
    }

    /// <summary>Byte admission rejects a live allocation before any native allocation or command creation.</summary>
    /// <returns>The asynchronous test completion.</returns>
    [Fact]
    public async Task ByteBudgetFailsBeforeNativeAllocationAsync()
    {
        await using var host = new PlanningHost();
        var native = new ManagedDevice();
        await using var session = new SessionOwner(host.Module, native, new() { MaximumAllocationBytes = 16 });
        await using var frame = new FrameOwner(session.CreateFrame());
        DeclareCopy(frame);
        using CompiledGraphicsPlan compiled = await frame.CompileAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await compiled.PrepareAsync());
        Assert.Empty(native.Events);
    }

    /// <summary>A retained export is unreadable before completion and pins allocation until its independent lease retires.</summary>
    /// <returns>The asynchronous test completion.</returns>
    [Fact]
    public async Task ExportLeasePinsOwnerUntilExplicitRetirementAsync()
    {
        await using var host = new PlanningHost();
        var native = new ManagedDevice();
        await using var session = new SessionOwner(host.Module, native);
        await using var frame = new FrameOwner(session.CreateFrame());
        PlanBuffer<int> destination = DeclareCopy(frame);
        using CompiledGraphicsPlan compiled = await frame.CompileAsync();
        await using var prepared = new PreparedOwner(await compiled.PrepareAsync());
        GraphicsExportLease<IGraphicsBuffer<int>> export = prepared.Value.ExportBuffer(destination);
        Assert.Throws<InvalidOperationException>(() => export.Value);
        await using var execution = new ExecutionOwner(session.CreateExecution(prepared.Value));
        await execution.Value.ExecuteAsync();
        Assert.Equal(4UL, export.Value.Count);
        Assert.DoesNotContain("dispose-buffer", native.Events);
        Assert.Throws<InvalidOperationException>(compiled.Dispose);
        await export.RetireAsync();
        Assert.Contains("dispose-buffer", native.Events);
        compiled.Dispose();
    }

    /// <summary>Unexecuted prepared commands retire before their export-pinned native resources.</summary>
    /// <param name="claim">Whether an execution was claimed before it was canceled without recording.</param>
    /// <returns>The completed owner-order assertions.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnexecutedPreparationWithExportRetiresCommandBeforeOwnersAsync(bool claim)
    {
        await using var host = new PlanningHost();
        var native = new ManagedDevice();
        await using var session = new SessionOwner(host.Module, native);
        await using var frame = new FrameOwner(session.CreateFrame());
        PlanBuffer<int> destination = DeclareCopy(frame);
        using CompiledGraphicsPlan compiled = await frame.CompileAsync();
        await using var prepared = new PreparedOwner(await compiled.PrepareAsync());
        GraphicsExportLease<IGraphicsBuffer<int>> export = prepared.Value.ExportBuffer(destination);
        if (claim)
        {
            await using var execution = new ExecutionOwner(session.CreateExecution(prepared.Value));
            await execution.Value.RetireAsync();
        }
        else
        {
            await prepared.Value.RetireAsync();
        }

        Assert.DoesNotContain("dispose-buffer", native.Events);
        await export.RetireAsync();
        Assert.Single(native.Events, e => e == "dispose-commands");
        Assert.Single(native.Events, e => e == "dispose-buffer");
        Assert.True(native.Events.IndexOf("dispose-commands") < native.Events.IndexOf("dispose-buffer"));
        compiled.Dispose();
    }

    /// <summary>A failed command return keeps export ownership and retries before releasing allocations.</summary>
    /// <returns>The completed retry order assertions.</returns>
    [Fact]
    public async Task UnexecutedExportRetirementRetriesCommandBeforeAllocationsAsync()
    {
        await using var host = new PlanningHost();
        var native = new ManagedDevice { CommandDisposeFailures = 1 };
        await using var session = new SessionOwner(host.Module, native);
        await using var frame = new FrameOwner(session.CreateFrame());
        PlanBuffer<int> destination = DeclareCopy(frame);
        using CompiledGraphicsPlan compiled = await frame.CompileAsync();
        await using var prepared = new PreparedOwner(await compiled.PrepareAsync());
        GraphicsExportLease<IGraphicsBuffer<int>> export = prepared.Value.ExportBuffer(destination);
        await prepared.Value.RetireAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await export.RetireAsync());
        Assert.DoesNotContain("dispose-buffer", native.Events);
        Assert.DoesNotContain("dispose-commands", native.Events);
        await export.RetireAsync();
        Assert.Equal(2, native.Events.Count(e => e == "dispose-commands-attempt"));
        Assert.Single(native.Events, e => e == "dispose-commands");
        Assert.Single(native.Events, e => e == "dispose-buffer");
        Assert.True(native.Events.IndexOf("dispose-commands") < native.Events.IndexOf("dispose-buffer"));
        compiled.Dispose();
    }

    private static async Task RunCopyAsync(SessionOwner session)
    {
        await using var frame = new FrameOwner(session.CreateFrame());
        DeclareCopy(frame);
        using CompiledGraphicsPlan compiled = await frame.CompileAsync();
        await using var prepared = new PreparedOwner(await compiled.PrepareAsync());
        await using var execution = new ExecutionOwner(session.CreateExecution(prepared.Value));
        await execution.Value.ExecuteAsync();
    }

    private static PlanBuffer<int> DeclareCopy(FrameOwner frame)
    {
        PlanBuffer<int> source = frame.Plan.ImportBuffer(new ManagedBuffer<int>(4, []), new TestLease());
        PlanBuffer<int> undefined = frame.Plan.CreateBuffer<int>(new() { Count = 4, Usage = BufferUsage.CopySource | BufferUsage.CopyDestination });
        PlanPass copy = frame.Plan.AddPass("copy");
        copy.Read(source, source.Range, new(PipelineStage.Copy, ResourceAccess.CopyRead));
        PlanBuffer<int> destination = copy.Write(undefined, undefined.Range, PlanWriteMode.Discard, new(PipelineStage.Copy, ResourceAccess.CopyWrite));
        copy.Copy(source, source.Range, destination, destination.Range);
        frame.Plan.Export(destination);
        return destination;
    }
}
