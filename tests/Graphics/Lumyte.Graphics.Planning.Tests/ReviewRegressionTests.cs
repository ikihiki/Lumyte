using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Planning.Tests;

/// <summary>Reproduces reviewed synchronization, one-shot ownership and capture-admission defects.</summary>
public sealed class ReviewRegressionTests
{
    /// <summary>Pass-wide barriers cannot legalize an internal copy producer-consumer chain.</summary>
    /// <returns>The asynchronous completion.</returns>
    [Fact]
    public async Task InternalCopyHazardRequiresSeparatePassesAsync()
    {
        await using var host = new PlanningHost();
        await using var session = new SessionOwner(host.Module, new ManagedDevice());
        await using var frame = new FrameOwner(session.CreateFrame());
        PlanBuffer<int> source = frame.Plan.ImportBuffer(new ManagedBuffer<int>(4, []), new TestLease());
        PlanBuffer<int> middle = frame.Plan.CreateBuffer<int>(new() { Count = 4, Usage = BufferUsage.CopySource | BufferUsage.CopyDestination });
        PlanBuffer<int> target = frame.Plan.CreateBuffer<int>(new() { Count = 4, Usage = BufferUsage.CopySource | BufferUsage.CopyDestination });
        PlanPass pass = frame.Plan.AddPass("invalid-internal-chain");
        pass.Read(source, source.Range, new(PipelineStage.Copy, ResourceAccess.CopyRead));
        middle = pass.Write(middle, middle.Range, PlanWriteMode.Discard, new(PipelineStage.Copy, ResourceAccess.CopyWrite));
        pass.Copy(source, source.Range, middle, middle.Range);
        pass.Read(middle, middle.Range, new(PipelineStage.Copy, ResourceAccess.CopyRead));
        target = pass.Write(target, target.Range, PlanWriteMode.Discard, new(PipelineStage.Copy, ResourceAccess.CopyWrite));
        pass.Copy(middle, middle.Range, target, target.Range);
        frame.Plan.Export(target);
        Exception? error = Record.Exception(() => frame.CompileAsync().GetAwaiter().GetResult().Dispose());
        Assert.IsType<NotSupportedException>(error);
    }

    /// <summary>A retained export must not reopen a preparation that already had an execution.</summary>
    /// <returns>The asynchronous completion.</returns>
    [Fact]
    public async Task RetainedExportCannotReclaimExecutedPreparationAsync()
    {
        await using var host = new PlanningHost();
        await using var session = new SessionOwner(host.Module, new ManagedDevice());
        await using var frame = new FrameOwner(session.CreateFrame());
        PlanBuffer<int> source = frame.Plan.ImportBuffer(new ManagedBuffer<int>(4, []), new TestLease());
        frame.Plan.Export(source);
        using CompiledGraphicsPlan compiled = await frame.CompileAsync();
        await using var prepared = new PreparedOwner(await compiled.PrepareAsync());
        GraphicsExportLease<IGraphicsBuffer<int>> export = prepared.Value.ExportBuffer(source);
        GraphicsExecution first = session.CreateExecution(prepared.Value);
        await first.ExecuteAsync();
        Assert.Equal(4UL, export.Value.Count);
        GraphicsExecution? replay = null;
        Exception? error = Record.Exception(() => replay = session.CreateExecution(prepared.Value));
        if (replay is not null)
        {
            await replay.RetireAsync();
        }

        await export.RetireAsync();
        Assert.IsType<InvalidOperationException>(error);
    }

    /// <summary>Oversized or empty writes fail at the writer call, before copying or appending unbounded pieces.</summary>
    /// <param name="empty">Whether to attempt a zero-byte write.</param>
    /// <returns>The asynchronous completion.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CaptureRejectsInvalidPieceBeforeReturningToAuthorAsync(bool empty)
    {
        await using var host = new PlanningHost();
        await using var session = new SessionOwner(host.Module, new ManagedDevice());
        await using var frame = new FrameOwner(session.CreateFrame());
        ComputeKernel<MaliciousArguments> kernel = session.ImportComputeKernel<MaliciousArguments>(new ManagedKernel(), new TestLease());
        PlanPass pass = frame.Plan.AddPass("malicious-writer");
        var probe = new CaptureProbe();
        var arguments = new MaliciousArguments(probe, empty);
        Exception? error = Record.Exception(() => pass.Dispatch(kernel, arguments, 1));
        Assert.False(probe.ReturnedFromWrite);
        Assert.IsAssignableFrom<ArgumentException>(error);
    }

    private readonly record struct MaliciousArguments(CaptureProbe Probe, bool Empty) : IShaderArguments
    {
        public static string ShaderTypeName => "Malicious";

        public static int TransferSize => 4;

        public static int ReferenceCount => 0;

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter, allows ref struct
        {
            writer.WriteValue(Empty ? [] : new byte[1024]);
            Probe.ReturnedFromWrite = true;
        }
    }

    private sealed class CaptureProbe
    {
        internal bool ReturnedFromWrite { get; set; }
    }
}
