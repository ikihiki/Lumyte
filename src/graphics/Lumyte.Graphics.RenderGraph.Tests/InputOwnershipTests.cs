namespace Lumyte.Graphics.RenderGraph.Tests;

public sealed class InputOwnershipTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PlanDoesNotKeepCulledOrLaterSnapshotsAlive(bool registerAfterCompile)
    {
        var (plan, snapshot) = CompileWithUnreferencedSnapshot(registerAfterCompile);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(snapshot.IsAlive, "A live plan retained an unused CPU snapshot through its original graph builder.");
        GC.KeepAlive(plan);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (GpuRenderGraphPlan Plan, WeakReference Snapshot) CompileWithUnreferencedSnapshot(bool registerAfterCompile)
    {
        var graph = new GpuRenderGraph();
        var inputContract = new TreeInput();
        var liveInput = graph.CreateInput("live input", inputContract, new Tree([]));
        graph.AddPass("live input", new TreePass(inputContract), liveInput);
        var target = graph.CreateTexture("target", new(1, 1, GpuFormat.Rgba8Unorm));
        graph.AddPass("live resource", GraphTests.Contract.Instance, new(null, target));
        graph.MarkOutput(target);
        var plan = registerAfterCompile ? graph.Compile() : null;
        var unused = new Tree([]);
        var deadInput = graph.CreateInput("unused input", inputContract, unused);
        graph.AddPass("unused pass", new CulledTreePass(inputContract), deadInput);
        plan ??= graph.Compile();
        return (plan, new WeakReference(unused));
    }

    [Fact]
    public void UnchangedSnapshotsShareOwnershipAcrossBindingsAndPasses()
    {
        var inputContract = new TreeInput();
        var graph = new GpuRenderGraph();
        var input = graph.CreateInput("tree", inputContract);
        GpuGraphValue<Tree> value = input;
        var contract = new TreePass(inputContract);
        graph.AddPass("first", contract, value);
        graph.AddPass("second", contract, value);
        var builder = graph.Compile().CreateBindings();
        var snapshot = new Tree([]);

        builder.Set(input, snapshot);
        var first = builder.Build();
        builder.Build();
        first.ToBuilder().Build();
        builder.Set(input, snapshot);
        builder.Build();

        Assert.Equal(1, inputContract.RetainCount);
    }

    [Fact]
    public void UpdatingOneBranchRetainsOnlyNewSnapshots()
    {
        var inputContract = new TreeInput();
        var graph = new GpuRenderGraph();
        var slot = graph.CreateInput("tree", inputContract);
        graph.AddPass("tree", new TreePass(inputContract), slot);
        var builder = graph.Compile().CreateBindings();
        var unchanged = new Tree([]);
        var first = new Tree([unchanged, new Tree([])]);
        builder.Set(slot, first);
        builder.Build();
        var retainedBefore = inputContract.RetainCount;

        builder.Set(slot, new Tree([unchanged, new Tree([])]));
        builder.Build();
        builder.Set(slot, first);
        builder.Build();

        Assert.Equal(2, inputContract.RetainCount - retainedBefore);
    }

    [Fact]
    public void SharedOwnershipIsStillCheckedAgainstEachPassDeclaration()
    {
        var inputContract = new ResourceInput();
        var graph = new GpuRenderGraph();
        var texture = graph.CreateTextureInput("source", new(1, 1, GpuFormat.Rgba8Unorm)).Texture;
        GpuGraphValue<GpuRenderGraphTexture> value = texture;
        graph.AddPass("declared", new ResourcePass(inputContract, texture), value);
        graph.AddPass("hidden", new ResourcePass(inputContract, null), value);

        var error = Assert.Throws<InvalidOperationException>(() => graph.Compile().CreateBindings().Build());

        Assert.Contains("pass 'hidden'", error.Message);
        Assert.Equal(1, inputContract.RetainCount);
    }

    [Fact]
    public void ResourceOnlyBindingsForkDoesNotRetraverseCpuInputs()
    {
        var inputContract = new TreeInput();
        var graph = new GpuRenderGraph();
        var input = graph.CreateInput("tree", inputContract, new Tree([]));
        var target = graph.CreateTextureInput("target", new(1, 1, GpuFormat.Rgba8Unorm));
        graph.AddPass("tree", new TreePass(inputContract), input);
        graph.AddPass("clear", GraphTests.Contract.Instance, new(null, target.Texture));
        graph.MarkOutput(target.Texture);
        var builder = graph.Compile().CreateBindings();
        var bindings = builder.Build();
        var fork = bindings.ToBuilder();

        fork.Set(target, new BindingsTests.TestTexture(Guid.NewGuid(), target.Texture.Description));
        fork.Build();

        Assert.Equal(1, inputContract.RetainCount);
    }

    private sealed record Tree(IReadOnlyList<Tree> Children);
    private sealed class TreeInput : IGpuGraphInputContract<Tree>
    {
        internal int RetainCount;
        public Tree Snapshot(Tree value) => value;
        public void Retain(GpuRenderInputRetentionContext context, Tree snapshot)
        {
            RetainCount++;
            foreach (var child in snapshot.Children) { context.ReadSnapshot(child, this); }
        }
    }
    private sealed class TreePass(TreeInput input) : IGpuRenderPassContract<GpuGraphValue<Tree>, int>
    {
        public string Id => "tree";
        public int Version => 1;
        public GpuGraphValue<Tree> Snapshot(GpuGraphValue<Tree> request) => request;
        public int Declare(GpuPassDeclarationContext context, GpuGraphValue<Tree> request)
        { context.ReadInput(request, input); context.Preserve(); return 0; }
    }
    private sealed class CulledTreePass(TreeInput input) : IGpuRenderPassContract<GpuGraphValue<Tree>, int>
    {
        public string Id => "culled tree";
        public int Version => 1;
        public GpuGraphValue<Tree> Snapshot(GpuGraphValue<Tree> request) => request;
        public int Declare(GpuPassDeclarationContext context, GpuGraphValue<Tree> request)
        { context.ReadInput(request, input); return 0; }
    }
    private sealed class ResourceInput : IGpuGraphInputContract<GpuRenderGraphTexture>
    {
        internal int RetainCount;
        public GpuRenderGraphTexture Snapshot(GpuRenderGraphTexture value) => value;
        public void Retain(GpuRenderInputRetentionContext context, GpuRenderGraphTexture snapshot)
        { RetainCount++; context.UseDeclared(snapshot); }
    }
    private sealed class ResourcePass(ResourceInput input, GpuRenderGraphTexture? declared)
        : IGpuRenderPassContract<GpuGraphValue<GpuRenderGraphTexture>, int>
    {
        public string Id => "resource";
        public int Version => 1;
        public GpuGraphValue<GpuRenderGraphTexture> Snapshot(GpuGraphValue<GpuRenderGraphTexture> request) => request;
        public int Declare(GpuPassDeclarationContext context, GpuGraphValue<GpuRenderGraphTexture> request)
        {
            if (declared is not null) { context.Read(declared); }
            context.ReadInput(request, input);
            context.Preserve();
            return 0;
        }
    }
}
