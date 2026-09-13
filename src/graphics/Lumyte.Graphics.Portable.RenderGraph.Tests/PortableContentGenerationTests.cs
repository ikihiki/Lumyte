using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.Portable.Resources;
using Lumyte.Graphics.Portable.Resources.Tests.Unit.Management;

namespace Lumyte.Graphics.Portable.RenderGraph.Tests;

public sealed class PortableContentGenerationTests
{
    [Fact]
    public async Task UnacceptedWriterIsInvalidatedWhenBuildFails()
    {
        PortablePassContentGeneration<int>? generation = null; var lease = new GraphFixture.Lease();
        bool first = true, available = true; var backend = new ManagerTestBackend();
        await using PortableRenderRuntime runtime = await GraphFixture.Runtime(backend, (context, output) =>
        {
            PortablePassBuilder writer = WriteOutput(context, output);
            if (first)
            { first = false; generation = context.RegisterContent(7, lease, [writer]); throw new InvalidOperationException("after registration"); }
            available = context.TryUseContent(generation, out _);
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.SubmitAsync(GraphFixture.Plan()).AsTask());
        using GpuRenderGraphExecution retry = await runtime.SubmitAsync(GraphFixture.Plan());
        await retry.WaitForCompletionAsync();

        Assert.False(available);
        Assert.Equal(1, lease.Returns);
        generation!.Dispose();
    }

    [Fact]
    public async Task APrunedWriterDoesNotPublishContent()
    {
        PortablePassContentGeneration<int>? generation = null; var lease = new GraphFixture.Lease();
        bool first = true, available = true; var backend = new ManagerTestBackend();
        await using PortableRenderRuntime runtime = await GraphFixture.Runtime(backend, (context, output) =>
        {
            if (first)
            {
                first = false; PortablePassBuffer unused = context.CreateBuffer("unused", new(8, GpuBufferUsage.Storage));
                PortablePassBuilder writer = context.AddPass("unused writer", 0, static (_, _) => { }).Write(unused, PortablePassUsage.StorageWrite);
                generation = context.RegisterContent(7, lease, [writer]);
            }
            else { available = context.TryUseContent(generation, out _); }
            WriteOutput(context, output);
        });
        using GpuRenderGraphExecution firstExecution = await runtime.SubmitAsync(GraphFixture.Plan());
        await firstExecution.WaitForCompletionAsync();

        using GpuRenderGraphExecution retry = await runtime.SubmitAsync(GraphFixture.Plan());
        await retry.WaitForCompletionAsync();

        Assert.False(available);
        Assert.Empty(backend.Created.OfType<ManagerTestBackend.Buffer>());
        runtime.Resources.Collect(); Assert.Equal(1, lease.Returns);
        generation!.Dispose();
    }

    [Fact]
    public async Task PendingContentCanBeReusedAndOwnerDisposalDoesNotEndReaderUse()
    {
        PortablePassContentGeneration<int>? generation = null; var lease = new GraphFixture.Lease();
        bool first = true, available = false; var backend = new ManagerTestBackend(); backend.TestQueue.AutoComplete = false;
        await using PortableRenderRuntime runtime = await GraphFixture.Runtime(backend, (context, output) =>
        {
            if (!first) { available = context.TryUseContent(generation, out int value); Assert.Equal(7, value); }
            PortablePassBuilder writer = WriteOutput(context, output);
            if (first) { first = false; generation = context.RegisterContent(7, lease, [writer]); }
        });
        using GpuRenderGraphExecution writerExecution = await runtime.SubmitAsync(GraphFixture.Plan());

        using GpuRenderGraphExecution readerExecution = await runtime.SubmitAsync(GraphFixture.Plan());
        generation!.Dispose(); writerExecution.Dispose(); readerExecution.Dispose(); runtime.Resources.Collect();
        Assert.True(available); Assert.Equal(0, lease.Returns);
        backend.TestQueue.Complete(0); await writerExecution.WaitForCompletionAsync(); runtime.Resources.Collect();
        Assert.Equal(0, lease.Returns);
        backend.TestQueue.Complete(1); await readerExecution.WaitForCompletionAsync(); runtime.Resources.Collect();

        Assert.Equal(1, lease.Returns);
    }

    [Fact]
    public async Task DelayedWriterDiagnosticsFailAcceptedReadersAndDerivedContent()
    {
        PortablePassContentGeneration<int>? source = null, derived = null;
        var sourceLease = new GraphFixture.Lease(); var derivedLease = new GraphFixture.Lease();
        int frame = 0; bool reused = true;
        var backend = new ManagerTestBackend(); backend.TestQueue.AutoComplete = false;
        await using PortableRenderRuntime runtime = await GraphFixture.Runtime(backend, (context, output) =>
        {
            if (frame == 1) { Assert.True(context.TryUseContent(source, out _)); }
            if (frame == 2) { reused = context.TryUseContent(derived, out _); }
            PortablePassBuilder writer = WriteOutput(context, output);
            if (frame == 0) { source = context.RegisterContent(7, sourceLease, [writer]); }
            if (frame == 1) { derived = context.RegisterContent(8, derivedLease, [writer]); }
            frame++;
        });
        using GpuRenderGraphExecution writerExecution = await runtime.SubmitAsync(GraphFixture.Plan());
        using GpuRenderGraphExecution readerExecution = await runtime.SubmitAsync(GraphFixture.Plan());
        backend.TestQueue.Complete(1);
        Task waiting = readerExecution.WaitForCompletionAsync().AsTask();
        Assert.False(waiting.IsCompleted);

        backend.TestQueue.Complete(0, new GpuDiagnostic(GpuDiagnosticKind.Validation, "late writer failure"));
        await Assert.ThrowsAnyAsync<Exception>(() => writerExecution.WaitForCompletionAsync().AsTask());
        await Assert.ThrowsAnyAsync<Exception>(() => waiting);
        backend.TestQueue.AutoComplete = true;
        using GpuRenderGraphExecution fresh = await runtime.SubmitAsync(GraphFixture.Plan());
        await fresh.WaitForCompletionAsync();

        Assert.False(reused);
        source!.Dispose(); derived!.Dispose(); runtime.Resources.Collect();
        Assert.Equal(1, sourceLease.Returns); Assert.Equal(1, derivedLease.Returns);
    }

    [Fact]
    public async Task AcceptedPrivateInitializationSurvivesTheRegisteringBuildFailure()
    {
        PortablePassContentGeneration<int>? generation = null; var lease = new GraphFixture.Lease();
        bool first = true, available = false; var backend = new ManagerTestBackend(); backend.TestQueue.AutoComplete = false;
        await using PortableRenderRuntime runtime = await GraphFixture.Runtime(backend, (context, output) =>
        {
            if (first)
            {
                first = false;
                using GpuResourceBatch upload = context.Services.Resources.BeginBatch(); upload.StartCommandRecording();
                generation = context.RegisterContent(7, lease, upload.Submit());
                throw new InvalidOperationException("unrelated build failure");
            }
            available = context.TryUseContent(generation, out _); WriteOutput(context, output);
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.SubmitAsync(GraphFixture.Plan()).AsTask());
        using GpuRenderGraphExecution reader = await runtime.SubmitAsync(GraphFixture.Plan());
        Assert.True(available); generation!.Dispose(); runtime.Resources.Collect(); Assert.Equal(0, lease.Returns);
        backend.TestQueue.Complete(0); backend.TestQueue.Complete(1);
        await reader.WaitForCompletionAsync(); runtime.Resources.Collect();

        Assert.Equal(1, lease.Returns);
    }

    [Fact]
    public async Task ContentFromAnotherRuntimeIsRejected()
    {
        PortablePassContentGeneration<int>? generation = null; var lease = new GraphFixture.Lease();
        await using PortableRenderRuntime first = await GraphFixture.Runtime(new(), (context, output) =>
            generation = context.RegisterContent(7, lease, [WriteOutput(context, output)]));
        using GpuRenderGraphExecution firstExecution = await first.SubmitAsync(GraphFixture.Plan());
        await firstExecution.WaitForCompletionAsync();
        await using PortableRenderRuntime second = await GraphFixture.Runtime(new(), (context, output) => context.TryUseContent(generation, out _));

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(() => second.SubmitAsync(GraphFixture.Plan()).AsTask());

        Assert.Equal("generation", error.ParamName); generation!.Dispose();
    }

    [Fact]
    public async Task ForeignSubmissionDoesNotTakeTheContentLease()
    {
        var lease = new GraphFixture.Lease(); var backend = new ManagerTestBackend();
        await using var foreign = new GpuResourceManager(backend);
        using GpuResourceBatch batch = foreign.BeginBatch(); batch.StartCommandRecording(); GpuSubmissionToken token = batch.Submit(); batch.Dispose();
        await token.WaitAsync(); foreign.Collect();
        await using PortableRenderRuntime runtime = await GraphFixture.Runtime(new(), (context, output) => context.RegisterContent(7, lease, token));

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(() => runtime.SubmitAsync(GraphFixture.Plan()).AsTask());

        Assert.Equal("token", error.ParamName); Assert.Equal(0, lease.Returns); lease.Dispose();
    }

    private static PortablePassBuilder WriteOutput(PortablePassBuildContext context, GpuRenderGraphTexture output)
        => context.AddPass("output", 0, static (_, _) => { }).Write(context.ImportTexture(output), PortablePassUsage.ColorAttachment);

    [Fact]
    public async Task AFailedAncestorInvalidatesDerivedContentBeforeItsGpuUseEnds()
    {
        PortablePassContentGeneration<int>? source = null, derived = null;
        var firstLease = new GraphFixture.Lease(); var secondLease = new GraphFixture.Lease();
        int frame = 0; bool reused = true;
        var backend = new ManagerTestBackend(); backend.TestQueue.AutoComplete = false;
        await using PortableRenderRuntime runtime = await GraphFixture.Runtime(backend, (context, output) =>
        {
            if (frame == 1) { Assert.True(context.TryUseContent(source, out _)); }
            if (frame == 2) { reused = context.TryUseContent(derived, out _); }
            PortablePassBuilder writer = WriteOutput(context, output);
            if (frame == 0) { source = context.RegisterContent(1, firstLease, [writer]); }
            if (frame == 1) { derived = context.RegisterContent(2, secondLease, [writer]); }
            frame++;
        });
        using GpuRenderGraphExecution first = await runtime.SubmitAsync(GraphFixture.Plan());
        using GpuRenderGraphExecution second = await runtime.SubmitAsync(GraphFixture.Plan());
        backend.TestQueue.Complete(0, new GpuDiagnostic(GpuDiagnosticKind.Validation, "ancestor failed"));
        await Assert.ThrowsAnyAsync<Exception>(() => first.WaitForCompletionAsync().AsTask());

        using GpuRenderGraphExecution third = await runtime.SubmitAsync(GraphFixture.Plan());

        Assert.False(reused); Assert.False(second.IsComplete); Assert.Equal(0, secondLease.Returns);
        backend.TestQueue.Complete(1); backend.TestQueue.Complete(2);
        await Assert.ThrowsAnyAsync<Exception>(() => second.WaitForCompletionAsync().AsTask()); await third.WaitForCompletionAsync();
        runtime.Resources.Collect(); Assert.Equal(1, secondLease.Returns);
        source!.Dispose(); derived!.Dispose();
    }

    [Fact]
    public async Task DuplicateContentLeaseCannotAcquireTwoOwners()
    {
        var lease = new GraphFixture.Lease(); var backend = new ManagerTestBackend();
        await using PortableRenderRuntime runtime = await GraphFixture.Runtime(backend, (context, output) =>
        {
            PortablePassBuilder writer = WriteOutput(context, output);
            context.RegisterContent(1, lease, [writer]); context.RegisterContent(2, lease, [writer]);
        });

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(() => runtime.SubmitAsync(GraphFixture.Plan()).AsTask());

        Assert.Equal("lease", error.ParamName); Assert.Equal(1, lease.Returns);
    }

    [Fact]
    public async Task RuntimeShutdownReturnsAnUnreleasedCacheOwner()
    {
        var lease = new GraphFixture.Lease();
        PortableRenderRuntime runtime = await GraphFixture.Runtime(new(), (context, output) => context.RegisterContent(1, lease, [WriteOutput(context, output)]));
        using GpuRenderGraphExecution execution = await runtime.SubmitAsync(GraphFixture.Plan());
        await execution.WaitForCompletionAsync(); execution.Dispose();

        await runtime.DisposeAsync();

        Assert.Equal(1, lease.Returns);
    }
}
