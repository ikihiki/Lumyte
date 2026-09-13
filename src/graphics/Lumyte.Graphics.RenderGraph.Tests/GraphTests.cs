using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.RenderGraph.Tests;

public sealed class GraphTests
{
    private static readonly GpuGraphTextureDescription Description = new(8, 8, GpuFormat.Rgba8Unorm);

    [Fact]
    public void CompileDropsOverwrittenContentAndUnusedPasses()
    {
        var graph = new GpuRenderGraph();
        var source = graph.CreateTexture("source", Description);
        var unused = graph.CreateTexture("unused", Description);
        var target = graph.CreateTexture("target", Description);
        graph.AddPass("old", Contract.Instance, new(null, source));
        graph.AddPass("unused", Contract.Instance, new(null, unused));
        graph.AddPass("new", Contract.Instance, new(null, source));
        graph.AddPass("copy", Contract.Instance, new(source, target));
        graph.ExportTexture(target);

        var plan = graph.Compile();

        Assert.Equal(["new", "copy"], plan.Passes.Select(pass => pass.Name));
        Assert.Equal([source, target], plan.Resources);
    }

    [Fact]
    public void ReadModifyWriteHasNoSelfDependency()
    {
        var graph = new GpuRenderGraph();
        var target = graph.CreateTexture("target", Description);
        graph.AddPass("clear", Contract.Instance, new(null, target));
        graph.AddPass("blend", Contract.Instance, new(target, target));
        graph.ExportTexture(target);

        Assert.Equal(["clear", "blend"], graph.Compile().Passes.Select(pass => pass.Name));
    }

    [Fact]
    public void SurvivingReaderPrecedesOverwritingWriter()
    {
        var graph = new GpuRenderGraph();
        var source = graph.CreateTexture("source", Description);
        var copied = graph.CreateTexture("copied", Description);
        graph.AddPass("first", Contract.Instance, new(null, source));
        graph.AddPass("read", Contract.Instance, new(source, copied));
        graph.ExportTexture(copied);
        graph.AddPass("overwrite", Contract.Instance, new(null, source));
        graph.ExportTexture(source);

        Assert.Equal(["first", "read", "overwrite"], graph.Compile().Passes.Select(pass => pass.Name));
    }

    [Fact]
    public void OutputKeepsContentAtTheDeclarationPoint()
    {
        var graph = new GpuRenderGraph();
        var target = graph.CreateTexture("target", Description);
        graph.AddPass("output", Contract.Instance, new(null, target));
        graph.MarkOutput(target);
        graph.AddPass("later", Contract.Instance, new(null, target));

        Assert.Equal("output", Assert.Single(graph.Compile().Passes).Name);
    }

    [Fact]
    public void OldOutputContentRequiresCopyWhenALaterWriteSurvives()
    {
        var graph = new GpuRenderGraph();
        var output = graph.CreateTexture("output", Description);
        var laterOutput = graph.CreateTexture("later", Description);
        graph.AddPass("first", Contract.Instance, new(null, output));
        graph.ExportTexture(output);
        graph.AddPass("overwrite", Contract.Instance, new(null, output));
        graph.AddPass("read later", Contract.Instance, new(output, laterOutput));
        graph.ExportTexture(laterOutput);

        Assert.Contains("Copy the earlier content", Assert.Throws<InvalidOperationException>(() => graph.Compile()).Message);
    }

    [Fact]
    public void ReadsOfUninitializedLiveResourcesFailAtCompile()
    {
        var graph = new GpuRenderGraph();
        var source = graph.CreateTexture("source", Description);
        var target = graph.CreateTexture("target", Description);
        graph.AddPass("copy", Contract.Instance, new(source, target));
        graph.MarkOutput(target);

        Assert.Contains("uninitialized 'source'", Assert.Throws<InvalidOperationException>(() => graph.Compile()).Message);
    }

    [Fact]
    public void FailedDeclarationRollsBackCreatedResourcesAndPassName()
    {
        var graph = new GpuRenderGraph();
        var failed = new ThrowingContract();

        Assert.Throws<InvalidOperationException>(() => graph.AddPass("fail", failed, 0));

        var texture = graph.CreateTexture("fail/created", Description);
        graph.AddPass("fail", Contract.Instance, new(null, texture));
        graph.MarkOutput(texture);
        Assert.Single(graph.Compile().Passes);
    }

    [Fact]
    public void ResourceFromAnotherGraphIsRejected()
    {
        var graph = new GpuRenderGraph();
        var foreign = new GpuRenderGraph().CreateTexture("foreign", Description);

        Assert.Throws<ArgumentException>(() => graph.AddPass("invalid", Contract.Instance, new(null, foreign)));
    }

    [Fact]
    public void PassSnapshotOwnsMutableRequestData()
    {
        var graph = new GpuRenderGraph();
        var values = new[] { 1, 2 };
        graph.AddPass("snapshot", new ArrayContract(), values);
        values[0] = 99;

        var request = Assert.IsType<int[]>(Assert.Single(graph.Compile().Passes).Request);

        Assert.Equal([1, 2], request);
    }

    internal sealed record Request(GpuRenderGraphTexture? Source, GpuRenderGraphTexture Target);
    internal sealed class Contract : IGpuRenderPassContract<Request, GpuRenderGraphTexture>
    {
        public static Contract Instance { get; } = new();
        public string Id => "test.copy";
        public int Version => 1;
        public Request Snapshot(Request request) => request;
        public GpuRenderGraphTexture Declare(GpuPassDeclarationContext context, Request request)
        {
            if (request.Source is not null) { context.Read(request.Source); }
            context.Write(request.Target);
            return request.Target;
        }
    }

    private sealed class ThrowingContract : IGpuRenderPassContract<int, int>
    {
        public string Id => "test.throw";
        public int Version => 1;
        public int Snapshot(int request) => request;
        public int Declare(GpuPassDeclarationContext context, int request)
        { context.CreateTexture("created", Description); throw new InvalidOperationException("Deliberate failure"); }
    }
    private sealed class ArrayContract : IGpuRenderPassContract<int[], int>
    {
        public string Id => "test.array";
        public int Version => 1;
        public int[] Snapshot(int[] request) => (int[])request.Clone();
        public int Declare(GpuPassDeclarationContext context, int[] request) { context.Preserve(); return 0; }
    }
}
