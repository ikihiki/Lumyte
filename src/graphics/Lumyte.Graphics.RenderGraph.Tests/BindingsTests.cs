namespace Lumyte.Graphics.RenderGraph.Tests;

public sealed class BindingsTests
{
    [Fact]
    public void IndependentBuildersAndForksReceiveDistinctPlanGenerations()
    {
        var plan = new GpuRenderGraph().Compile();
        var first = plan.CreateBindings().Build();
        var second = plan.CreateBindings().Build();
        var fork = first.ToBuilder().Build();

        Assert.Equal(3, new[] { first.Generation, second.Generation, fork.Generation }.Distinct().Count());
    }

    [Fact]
    public void BindingsPreserveSnapshotsAcrossBuilderChangesAndOldSubmissions()
    {
        var graph = new GpuRenderGraph();
        var slot = graph.CreateInput("values", ArrayInput.Instance);
        GpuGraphValue<int[]> value = slot;
        graph.AddPass("input", new InputPass(), value);
        var plan = graph.Compile();
        var builder = plan.CreateBindings();
        var source = new[] { 7 };
        builder.Set(slot, source);
        source[0] = 8;
        var old = builder.Build();
        builder.Set(slot, [9]);
        var current = builder.Build();

        Assert.Equal(7, Assert.Single(plan.ValidateBindings(old).GetInput(value)));
        Assert.Equal(9, Assert.Single(plan.ValidateBindings(current).GetInput(value)));
    }

    [Fact]
    public void ConstantsUseDeclaredContractSnapshot()
    {
        var source = new[] { 2 };
        GpuGraphValue<int[]> value = source;
        var graph = new GpuRenderGraph();
        graph.AddPass("input", new InputPass(), value);
        source[0] = 3;
        var plan = graph.Compile();

        Assert.Equal(2, Assert.Single(plan.Passes[0].GetInput(value, plan.CreateBindings().Build())));
    }

    [Fact]
    public void MissingLiveInputFailsBeforeProviderPreparation()
    {
        var graph = new GpuRenderGraph();
        var input = graph.CreateInput("values", ArrayInput.Instance);
        graph.AddPass("input", new InputPass(), input);
        var plan = graph.Compile();

        Assert.Contains("values", Assert.Throws<InvalidOperationException>(() => plan.ValidateBindings()).Message);
    }

    [Fact]
    public void MissingCulledInputDoesNotBlockSubmission()
    {
        var graph = new GpuRenderGraph();
        var input = graph.CreateInput("unused", ArrayInput.Instance);
        graph.AddPass("input", new InputPass(false), input);

        Assert.Empty(graph.Compile().ValidateBindings().Plan.Passes);
    }

    [Fact]
    public void RetentionCannotAddHiddenResourceDependencies()
    {
        var graph = new GpuRenderGraph();
        var hidden = graph.CreateTexture("hidden", new(1, 1, GpuFormat.Rgba8Unorm));
        graph.AddPass("hidden", new HiddenResourcePass(), hidden);

        Assert.Contains("undeclared read", Assert.Throws<InvalidOperationException>(() => graph.Compile().CreateBindings().Build()).Message);
    }

    [Fact]
    public void DifferentLogicalInputsCannotAliasOnePhysicalReference()
    {
        var description = new GpuGraphTextureDescription(1, 1, GpuFormat.Rgba8Unorm);
        var graph = new GpuRenderGraph();
        var source = graph.CreateTextureInput("source", description);
        var target = graph.CreateTextureInput("target", description);
        graph.AddPass("copy", GraphTests.Contract.Instance, new(source.Texture, target.Texture));
        graph.MarkOutput(target.Texture);
        var plan = graph.Compile();
        var builder = plan.CreateBindings();
        var reference = new TestTexture(Guid.NewGuid(), description);
        builder.Set(source, reference);
        builder.Set(target, reference);

        Assert.Contains("aliases", Assert.Throws<ArgumentException>(() => plan.ValidateBindings(builder.Build())).Message);
    }

    [Fact]
    public void ResourceBindingMustMatchDeclaredDescription()
    {
        var graph = new GpuRenderGraph();
        var input = graph.CreateTextureInput("target", new(1, 1, GpuFormat.Rgba8Unorm));
        graph.AddPass("clear", GraphTests.Contract.Instance, new(null, input.Texture));
        graph.MarkOutput(input.Texture);
        var plan = graph.Compile();
        var builder = plan.CreateBindings();
        builder.Set(input, new TestTexture(Guid.NewGuid(), new(2, 1, GpuFormat.Rgba8Unorm)));

        Assert.Contains("description", Assert.Throws<ArgumentException>(() => plan.ValidateBindings(builder.Build())).Message);
    }

    [Fact]
    public void PlanRejectsBindingsFromAnotherCompilation()
    {
        var graph = new GpuRenderGraph();
        var first = graph.Compile();
        var second = graph.Compile();

        Assert.Equal("bindings", Assert.Throws<ArgumentException>(() => first.ValidateBindings(second.CreateBindings().Build())).ParamName);
    }

    internal sealed class TestTexture(Guid runtimeId, GpuGraphTextureDescription description)
        : GpuGraphTextureRef(runtimeId, Guid.NewGuid(), description);
    private sealed class ArrayInput : IGpuGraphInputContract<int[]>
    {
        public static ArrayInput Instance { get; } = new();
        public int[] Snapshot(int[] value) => (int[])value.Clone();
        public void Retain(GpuRenderInputRetentionContext context, int[] snapshot) { }
    }
    private sealed class InputPass(bool preserve = true) : IGpuRenderPassContract<GpuGraphValue<int[]>, int>
    {
        public string Id => "test.input";
        public int Version => 1;
        public GpuGraphValue<int[]> Snapshot(GpuGraphValue<int[]> request) => request;
        public int Declare(GpuPassDeclarationContext context, GpuGraphValue<int[]> request)
        { context.ReadInput(request, ArrayInput.Instance); if (preserve) { context.Preserve(); } return 0; }
    }
    private sealed class HiddenResourcePass : IGpuRenderPassContract<GpuRenderGraphTexture, int>, IGpuGraphInputContract<GpuRenderGraphTexture>
    {
        public string Id => "test.hidden";
        public int Version => 1;
        public GpuRenderGraphTexture Snapshot(GpuRenderGraphTexture request) => request;
        public int Declare(GpuPassDeclarationContext context, GpuRenderGraphTexture request)
        { context.ReadInput<GpuRenderGraphTexture>(request, this); context.Preserve(); return 0; }
        public void Retain(GpuRenderInputRetentionContext context, GpuRenderGraphTexture snapshot) => context.UseDeclared(snapshot);
    }
}
