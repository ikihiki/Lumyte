namespace Lumyte.Graphics.RenderGraph.Tests;

/// <summary>Verifies typed extension composition and frame ownership without a resource manager.</summary>
public sealed class RenderGraphExtensionTests
{
    /// <summary>Different extensions share cached dependencies within a graph and isolate frame-local state.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ExtensionsShareDependenciesAndSeparateFrameStateAsync()
    {
        int created = 0;
        using RenderGraphExtensionsBuilder builder = new RenderGraphExtensionsBuilder()
            .Add(_ => new SharedState(++created))
            .Add(graph => new DrawingState(graph.GetExtension<SharedState>()));
        using RenderGraphExtensions extensions = await builder.BuildAsync();
        using var first = new RenderGraph(extensions: extensions);
        using var second = new RenderGraph(extensions: extensions);
        DrawingState drawing = first.GetExtension<DrawingState>();
        Assert.Same(first.GetExtension<SharedState>(), drawing.Shared);
        Assert.Same(drawing, first.GetExtension<DrawingState>());
        Assert.NotSame(drawing.Shared, second.GetExtension<SharedState>());
        Assert.Equal(2, created);
        Assert.Throws<InvalidOperationException>(() => builder.Add(_ => new object()));
        Assert.Throws<InvalidOperationException>(() => first.GetExtension<UnregisteredState>());
        Assert.True(first.IsBuilding);
    }

    /// <summary>Defaults are cached, while an explicit factory takes priority without invoking the fallback.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ExplicitFactoriesOverrideModuleDefaultsAsync()
    {
        using var defaults = new RenderGraph();
        SharedState first = defaults.GetOrAddExtension(_ => new SharedState(1));
        Assert.Same(first, defaults.GetOrAddExtension<SharedState>(_ => throw new InvalidOperationException()));
        using RenderGraphExtensionsBuilder builder = new RenderGraphExtensionsBuilder().Add(_ => new SharedState(2));
        Assert.Throws<ArgumentException>(() => builder.Add(_ => new SharedState(3)));
        using RenderGraphExtensions extensions = await builder.BuildAsync();
        using var configured = new RenderGraph(extensions: extensions);
        Assert.Equal(2, configured.GetOrAddExtension<SharedState>(_ => throw new InvalidOperationException()).Number);
    }

    /// <summary>A reentrant validation cannot transfer a new owner into an already closed graph.</summary>
    [Fact]
    public void ReentrantValidationDoesNotLoseCallerOwnership()
    {
        using var graph = new RenderGraph();
        var lifetime = new ClosingLifetime(graph);
        Assert.Throws<ObjectDisposedException>(() => graph.Retain(lifetime));
        Assert.False(lifetime.Disposed);
        lifetime.Dispose();
        Assert.True(lifetime.Disposed);
    }

    /// <summary>Failed or cyclic factories close preparation but retain every registered cleanup owner.</summary>
    /// <param name="cycle">Whether the factory fails through a recursive dependency.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedFactoryPreservesCleanupAndRejectsFurtherPreparationAsync(bool cycle)
    {
        var releases = new List<string>();
        var lifetime = new Lifetime("partial", releases);
        using RenderGraphExtensions extensions = await new RenderGraphExtensionsBuilder().Add<SharedState>(graph =>
        {
            graph.Retain(lifetime);
            return cycle ? graph.GetExtension<SharedState>() : throw new InvalidOperationException("Preparation failed.");
        }).BuildAsync();
        using var graph = new RenderGraph(extensions: extensions);
        Assert.Throws<InvalidOperationException>(() => graph.GetExtension<SharedState>());
        Assert.False(graph.IsBuilding);
        Assert.Empty(releases);
        Assert.Throws<InvalidOperationException>(() => graph.Compile());
        Assert.Throws<InvalidOperationException>(() => graph.AddPass("late", _ => { }));
        graph.Dispose();
        Assert.Equal(["partial"], releases);
    }

    /// <summary>A factory cannot hide a dependency cycle by catching its exception.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CaughtCycleStillRejectsPublicationAsync()
    {
        using RenderGraphExtensions extensions = await new RenderGraphExtensionsBuilder().Add<SharedState>(graph =>
        {
            Assert.Throws<InvalidOperationException>(() => graph.GetExtension<SharedState>());
            return new(1);
        }).BuildAsync();
        using var graph = new RenderGraph(extensions: extensions);
        Assert.Throws<InvalidOperationException>(() => graph.GetExtension<SharedState>());
        Assert.False(graph.IsBuilding);
    }

    /// <summary>Compiled ownership outlives graph disposal and refuses early release while commands are submitted.</summary>
    [Fact]
    public void PlanKeepsAllExtensionOwnersUntilGpuCompletion()
    {
        var releases = new List<string>();
        using var graph = new RenderGraph();
        graph.Retain(new Lifetime("shader", releases));
        graph.Retain(new Lifetime("effect", releases));
        using CompiledRenderGraph plan = graph.Compile();
        Assert.False(graph.IsBuilding);
        graph.Dispose();
        Assert.Empty(releases);
        using var commands = new RecordingCommands();
        plan.Record(commands);
        commands.Finish();
        commands.Submit();
        Assert.Throws<InvalidOperationException>(() => plan.Dispose());
        Assert.Empty(releases);
        commands.Complete();
        plan.Dispose();
        Assert.Equal(["effect", "shader"], releases);
    }

    /// <summary>Ownership is transferred as one group; failed validation leaves the whole group on the graph.</summary>
    [Fact]
    public void CompilationValidationFailureDoesNotPartiallyTransferOwnership()
    {
        var releases = new List<string>();
        var invalid = new Lifetime("invalid", releases);
        using var graph = new RenderGraph();
        graph.Retain(invalid);
        graph.Retain(new Lifetime("dependent", releases));
        invalid.Valid = false;
        Assert.Throws<InvalidOperationException>(() => graph.Compile());
        Assert.True(graph.IsBuilding);
        Assert.Empty(releases);
        graph.Dispose();
        Assert.Equal(["dependent", "invalid"], releases);
    }

    /// <summary>Both abandoned graphs and compiled plans retry the remaining owners in dependency order.</summary>
    /// <param name="compile">Whether to move ownership to a plan before cleanup.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CleanupRetainsFailedOwnerAndDependenciesForRetry(bool compile)
    {
        var releases = new List<string>();
        using var graph = new RenderGraph();
        graph.Retain(new Lifetime("dependency", releases));
        graph.Retain(new Lifetime("failing", releases, failOnce: true));
        graph.Retain(new Lifetime("released", releases));
        using CompiledRenderGraph? plan = compile ? graph.Compile() : null;
        IDisposable owner = plan is null ? graph : plan;
        Assert.Throws<InvalidOperationException>(() => owner.Dispose());
        Assert.Equal(["released", "failing"], releases);
        Assert.False(graph.IsBuilding);
        Assert.Throws<ObjectDisposedException>(() => graph.AddPass("late", _ => { }));
        owner.Dispose();
        Assert.Equal(["released", "failing", "failing", "dependency"], releases);
    }

    /// <summary>Graph and pass mutation stops after owned compilation while raw graphs keep their repeated-compile contract.</summary>
    [Fact]
    public void OwnedGraphFreezesEveryMutationAndRawGraphRemainsReusable()
    {
        using var graph = new RenderGraph();
        _ = graph.GetOrAddExtension(_ => new SharedState(1));
        using var texture = new TestTexture();
        RenderGraphTexture imported = graph.ImportTexture(texture, TextureState.Sampled, default);
        RenderGraphPass first = graph.AddPass("first", _ => { });
        RenderGraphPass second = graph.AddPass("second", _ => { });
        using CompiledRenderGraph plan = graph.Compile();
        Assert.Throws<ObjectDisposedException>(() => graph.Compile());
        Assert.Throws<ObjectDisposedException>(() => graph.MarkOutput(imported));
        Assert.Throws<ObjectDisposedException>(() => graph.ImportTexture(new TestTexture(), TextureState.Sampled, default));
        Assert.Throws<ObjectDisposedException>(() => graph.GetExtension<SharedState>());
        Assert.Throws<ObjectDisposedException>(() => first.KeepAlive());
        Assert.Throws<ObjectDisposedException>(() => first.DependsOn(second));
        Assert.Throws<ObjectDisposedException>(() => first.UseTexture(imported, TextureState.Sampled, new(PipelineStage.FragmentShader, ResourceAccess.ShaderRead)));

        using var raw = new RenderGraph();
        using CompiledRenderGraph rawFirst = raw.Compile();
        raw.AddPass("next", _ => { }).KeepAlive();
        using CompiledRenderGraph rawSecond = raw.Compile();
        Assert.Empty(rawFirst.PassNames);
        Assert.Equal(["next"], rawSecond.PassNames);
    }

    /// <summary>Configuration disposes only explicit owners, once, and resumes after a failed release.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SharedConfigurationOwnsOnlyExplicitRegistrationOwnersAsync()
    {
        var releases = new List<string>();
        var first = new Lifetime("first", releases);
        var second = new Lifetime("second", releases, failOnce: true);
        var borrowed = new DisposableState();
        using RenderGraphExtensions extensions = await new RenderGraphExtensionsBuilder()
            .Add(_ => borrowed, first)
            .Add(_ => new SharedState(1), second)
            .Add(_ => new DrawingState(new(2)), first)
            .BuildAsync();
        using (var graph = new RenderGraph(extensions: extensions))
        {
            Assert.Same(borrowed, graph.GetExtension<DisposableState>());
        }

        Assert.False(borrowed.Disposed);
        Assert.Throws<InvalidOperationException>(() => extensions.Dispose());
        Assert.Equal(["second"], releases);
        Assert.Throws<ObjectDisposedException>(() => new RenderGraph(extensions: extensions));
        extensions.Dispose();
        Assert.Equal(["second", "second", "first"], releases);
        Assert.False(borrowed.Disposed);
    }

    /// <summary>Observer failures and reentrancy cannot steal the graph's prepared ownership.</summary>
    /// <param name="failScope">Whether the diagnostic scope throws instead of the compiled callback.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ObserverFailuresLeaveOwnershipWithGraph(bool failScope)
    {
        var releases = new List<string>();
        var observer = new FailingObserver(failScope);
        using var graph = new RenderGraph(observer: observer);
        observer.Graph = graph;
        graph.Retain(new Lifetime("retained", releases));
        Assert.Throws<InvalidOperationException>(() => graph.Compile());
        Assert.Empty(releases);
        Assert.True(graph.IsBuilding);
        graph.Dispose();
        Assert.Equal(["retained"], releases);
    }

    private sealed record SharedState(int Number);

    private sealed record DrawingState(SharedState Shared);

    private sealed class UnregisteredState;

    private sealed class DisposableState : IDisposable
    {
        internal bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    private sealed class ClosingLifetime(RenderGraph graph) : IRenderGraphLifetime
    {
        internal bool Disposed { get; private set; }

        public void Validate() => graph.Dispose();

        public void Dispose() => Disposed = true;
    }

    private sealed class Lifetime(string name, List<string> releases, bool failOnce = false) : IRenderGraphLifetime
    {
        private bool _failed;

        internal bool Valid { get; set; } = true;

        public void Validate()
        {
            if (!Valid)
            {
                throw new InvalidOperationException("Expired owner.");
            }
        }

        public void Dispose()
        {
            releases.Add(name);
            if (failOnce && !_failed)
            {
                _failed = true;
                throw new InvalidOperationException("Try disposal again.");
            }
        }
    }

    private sealed class FailingObserver(bool failScope) : IRenderGraphObserver
    {
        public bool IsEnabled => true;

        internal RenderGraph Graph { get; set; } = null!;

        public RenderGraphCaptureOptions? RequestCapture(RenderGraphRun run)
        {
            Assert.False(Graph.IsBuilding);
            Assert.Throws<InvalidOperationException>(() => Graph.Compile());
            Assert.Throws<InvalidOperationException>(() => Graph.AddPass("reentered", _ => { }));
            Assert.Throws<InvalidOperationException>(() => Graph.Dispose());
            return null;
        }

        public IDisposable? Begin(RenderGraphRun run, string operation) => failScope ? new FailingScope() : null;

        public void Compiled(RenderGraphRun run, RenderGraphStatistics statistics, RenderGraphSnapshot? snapshot, double durationMilliseconds)
        {
            if (!failScope)
            {
                throw new InvalidOperationException("Observer failed.");
            }
        }

        public void Recorded(RenderGraphRun run, double durationMilliseconds)
        {
        }

        public void Failed(RenderGraphRun run, string operation, Exception exception)
        {
        }

        private sealed class FailingScope : IDisposable
        {
            public void Dispose() => throw new InvalidOperationException("Diagnostic scope failed.");
        }
    }
}
