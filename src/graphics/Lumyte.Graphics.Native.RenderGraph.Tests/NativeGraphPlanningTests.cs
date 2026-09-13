using Lumyte.Graphics.Native.Resources.Tests;
using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Native.RenderGraph.Tests;

public sealed class NativeGraphPlanningTests
{
    private static NativePassUsage Read => new(GpuStage.Copy, GpuAccess.CopyRead);
    private static NativePassUsage Write => new(GpuStage.Copy, GpuAccess.CopyWrite);

    [Fact]
    public async Task DeadPrivateWritersAreNotRecordedOrAllocated()
    {
        var backend = new TestResourceBackend();
        bool recorded = false;
        await using var runtime = await Create(backend, (context, request) =>
        {
            var unused = context.CreateBuffer("unused", new(1024));
            context.AddPass("dead", 0, (_, _) => recorded = true).Write(unused, Write);
            var output = context.ImportBuffer(request.Target);
            context.AddPass("output", 0, (_, _) => { }).Write(output, Write);
        });

        using var execution = await runtime.SubmitAsync(Plan());
        await execution.WaitForCompletionAsync();

        Assert.False(recorded);
        Assert.Equal(16ul, Assert.Single(backend.Regions).Size);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LiveReadsRequireInitializedPrivateContents(bool readWrite)
    {
        await using var runtime = await Create(new(), (context, request) =>
        {
            var undefined = context.CreateBuffer("undefined", new(16));
            var output = context.ImportBuffer(request.Target);
            var pass = context.AddPass("consume", 0, (_, _) => { }).Write(output, Write);
            if (readWrite) { pass.ReadWrite(undefined, Write); } else { pass.Read(undefined, Read); }
        });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await runtime.SubmitAsync(Plan()));

        Assert.Contains("undefined", error.Message);
    }

    [Fact]
    public async Task FullWritesReplaceUnobservedPrecedingContents()
    {
        var recorded = new List<string>();
        await using var runtime = await Create(new(), (context, request) =>
        {
            var temporary = context.CreateBuffer("temporary", new(16));
            var output = context.ImportBuffer(request.Target);
            context.AddPass("old", 0, (_, _) => recorded.Add("old")).Write(temporary, Write);
            context.AddPass("new", 0, (_, _) => recorded.Add("new")).Write(temporary, Write);
            context.AddPass("output", 0, (_, _) => recorded.Add("output")).Read(temporary, Read).Write(output, Write);
        });

        using var execution = await runtime.SubmitAsync(Plan());

        Assert.Equal(["new", "output"], recorded);
    }

    [Fact]
    public async Task NonoverlappingPrivateBuffersReuseOnePhysicalRegion()
    {
        NativeGpuLinearRegion? first = null, second = null;
        var backend = new TestResourceBackend();
        await using var runtime = await Create(backend, (context, request) =>
        {
            var output = context.ImportBuffer(request.Target);
            var a = context.CreateBuffer("a", new(16));
            var b = context.CreateBuffer("b", new(16));
            context.AddPass("a", a, (record, value) => first = record.GetBufferRange(value).Region).Write(a, Write);
            context.AddPass("read-a", 0, (_, _) => { }).Read(a, Read).Write(output, Write);
            context.AddPass("b", b, (record, value) => second = record.GetBufferRange(value).Region).Write(b, Write);
            context.AddPass("read-b", 0, (_, _) => { }).Read(b, Read).ReadWrite(output, Write);
        });

        using var execution = await runtime.SubmitAsync(Plan());

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Equal(2, backend.Regions.Count);
    }

    [Fact]
    public async Task RecordingContextsCloseAtTheEndOfTheirCallback()
    {
        NativePassRecordContext? captured = null;
        await using var runtime = await Create(new(), (context, request) =>
        {
            var output = context.ImportBuffer(request.Target);
            context.AddPass("output", 0, (record, _) => captured = record).Write(output, Write);
        });

        using var execution = await runtime.SubmitAsync(Plan());

        Assert.Throws<ObjectDisposedException>(() => captured!.Commands);
    }

    [Fact]
    public async Task RecordingCannotResolveUndeclaredResources()
    {
        await using var runtime = await Create(new(), (context, request) =>
        {
            var output = context.ImportBuffer(request.Target);
            var hidden = context.CreateBuffer("hidden", new(16));
            context.AddPass("output", hidden, (record, value) => record.GetBufferRange(value)).Write(output, Write);
        });

        var error = await Assert.ThrowsAsync<ArgumentException>(async () => await runtime.SubmitAsync(Plan()));

        Assert.Equal("resource", error.ParamName);
    }

    [Fact]
    public async Task ExecutionReferencesCannotBeReusedByTheNextBuild()
    {
        NativePassBuffer? saved = null;
        await using var runtime = await Create(new(), (context, request) =>
        {
            var output = context.ImportBuffer(request.Target);
            saved ??= output;
            context.AddPass("output", 0, (_, _) => { }).Write(saved, Write);
        });
        GpuRenderGraphPlan plan = Plan();
        using var first = await runtime.SubmitAsync(plan);

        var error = await Assert.ThrowsAsync<ArgumentException>(async () => await runtime.SubmitAsync(plan));

        Assert.Equal("resource", error.ParamName);
    }

    [Fact]
    public async Task MultiOutputFeaturesMayCullAnUnusedOutputWriter()
    {
        int unused = 0;
        await using var runtime = await Create(new(), (context, request) =>
        {
            var output = context.ImportBuffer(request.Target);
            var other = context.ImportBuffer(request.Other!);
            context.AddPass("output", 0, (_, _) => { }).Write(output, Write);
            context.AddPass("unused", 0, (_, _) => unused++).Write(other, Write);
        });

        using var execution = await runtime.SubmitAsync(Plan(twoOutputs: true));

        Assert.Equal(0, unused);
    }

    [Fact]
    public async Task RepeatedStructuresReuseTheBoundedScheduleCache()
    {
        await using var runtime = await Create(new(), (context, request) =>
        {
            var output = context.ImportBuffer(request.Target);
            context.AddPass("output", 0, (_, _) => { }).Write(output, Write);
        });
        GpuRenderGraphPlan plan = Plan();

        using var first = await runtime.SubmitAsync(plan);
        using var second = await runtime.SubmitAsync(plan);

        Assert.Equal(new NativeRenderPreparationStatistics(1, 1, 1), runtime.PreparationStatistics);
    }

    [Theory]
    [InlineData("resource")]
    [InlineData("pass")]
    public async Task DuplicateNamesWithinOneBuildAreRejected(string kind)
    {
        await using var runtime = await Create(new(), (context, _) =>
        {
            if (kind == "resource") { context.CreateBuffer("same", new(16)); context.CreateBuffer("same", new(16)); }
            else { context.AddPass("same", 0, (_, _) => { }); context.AddPass("same", 0, (_, _) => { }); }
        });

        var error = await Assert.ThrowsAsync<ArgumentException>(async () => await runtime.SubmitAsync(Plan()));

        Assert.Equal("name", error.ParamName);
    }

    internal static async Task<NativeRenderRuntime> Create(TestResourceBackend backend, Action<NativePassBuildContext, Request> build)
    {
        var registry = new NativeRenderPassRegistry(); registry.Register(new Contract(), _ => new Implementation(build));
        return (NativeRenderRuntime)await new NativeRenderProvider("test", (_, _) => new(backend), registry).CreateAsync(new());
    }
    internal static GpuRenderGraphPlan Plan(bool twoOutputs = false)
    {
        var graph = new GpuRenderGraph(); var target = graph.CreateBuffer("target", new(16));
        var other = twoOutputs ? graph.CreateBuffer("other", new(16)) : null;
        graph.AddPass("feature", new Contract(), new(target, other)); graph.ExportBuffer(target); return graph.Compile();
    }
    internal sealed record Request(GpuRenderGraphBuffer Target, GpuRenderGraphBuffer? Other = null);
    internal sealed class Contract : IGpuRenderPassContract<Request, Request>
    {
        public string Id => "planning";
        public int Version => 1;
        public Request Snapshot(Request request) => request;
        public Request Declare(GpuPassDeclarationContext context, Request request)
        { context.Write(request.Target); if (request.Other is not null) { context.Write(request.Other); } return request; }
    }
    private sealed class Implementation(Action<NativePassBuildContext, Request> build) : INativeRenderPass<Request, Request>
    {
        public ValueTask BuildAsync(NativePassBuildContext context, Request request, Request result, CancellationToken cancellationToken = default)
        { build(context, request); return ValueTask.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
