using Lumyte.Memory;
using Lumyte.Memory.DependencyInjection;
using Lumyte.Resources.DependencyInjection;
using Lumyte.Resources.Execution;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lumyte.Resources.Tests;

/// <summary>Verifies typed runtime delivery and construction-time environment validation.</summary>
public sealed class TypedStageRuntimeTests
{
    /// <summary>Declares the test stage environment and observed dispatch counts.</summary>
    private interface IEnvironment : IResourceRuntime
    {
        /// <summary>Gets the number of dispatched builds.</summary>
        int Builds { get; }

        /// <summary>Gets the number of dispatched releases.</summary>
        int Releases { get; }
    }

    /// <summary>Existing pipeline registration supports typed source and transform stages, including async continuations.</summary>
    /// <returns>Completion of construction and manager cleanup.</returns>
    [Fact]
    public async Task SourceAndTransformReceiveTheirDeclaredRuntimeAsync()
    {
        ServiceCollection services = new();
        services.AddMemoryManagement();
        services.AddSingleton<IEnvironment>(static provider => new EnvironmentRuntime(provider.GetRequiredService<IResourceRuntime>()));
        services.AddResources(builder => builder.UseSource<UnusedSource>()
            .AddSourcePipeline<Leaf, SourceStage>()
            .AddTransformPipeline<Leaf, Parent, TransformStage>()
            .UseStageRuntime<Leaf, IEnvironment>()
            .UseStageRuntime<Parent, IEnvironment>());
        await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using ResourceExecutionHost execution = new(provider);
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        ResourceManager manager = scope.ServiceProvider.GetRequiredService<ResourceManager>();
        using ResourceReference<Parent> reference = await manager.AcquireAsync(new ResourceKey<Parent>("asset"));
        using ResourceLease<Parent> lease = reference.Acquire();
        IEnvironment expected = provider.GetRequiredService<IEnvironment>();
        Assert.Same(expected, lease.Value.Runtime);
        Assert.Same(expected, lease.Value.Input.Runtime);
        Assert.Equal(1, expected.Builds);

        lease.Dispose();
        reference.Dispose();
        await manager.DisposeAsync();
        Assert.Equal(2, expected.Releases);
    }

    /// <summary>Wrong explicit bindings and incompatible defaults fail before any stage is dispatched.</summary>
    /// <param name="explicitBinding">Whether the incompatible runtime was explicitly selected.</param>
    /// <returns>Completion of the borrowed host's shutdown.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IncompatibleEnvironmentIsRejectedBeforeBuildAsync(bool explicitBinding)
    {
        await using MemoryManager memory = new();
        await using ResourceExecutionHost execution = new();
        var stage = new SourceStage();
        ArgumentException error = Assert.Throws<ArgumentException>(() => new ResourceManager(
            "invalid",
            new UnusedSource(),
            [stage],
            memory,
            execution.Resources,
            stageRuntimes: explicitBinding ? [new(typeof(Leaf), execution.Resources)] : null));
        Assert.Contains(nameof(IEnvironment), error.Message, StringComparison.Ordinal);
        Assert.Equal("stageRuntimes", error.ParamName);
        Assert.False(stage.Called);
    }

    /// <summary>A stage may declare the shared default runtime type without selecting an override.</summary>
    /// <returns>Completion of the default build and cleanup.</returns>
    [Fact]
    public async Task TypedStageCanUseTheDefaultEnvironmentAsync()
    {
        await using MemoryManager memory = new();
        await using ResourceExecutionHost execution = new();
        await using ResourceManager manager = new("default", new UnusedSource(), [new DefaultStage()], memory, execution.Resources);
        using ResourceReference<DefaultValue> reference = await manager.AcquireAsync(new ResourceKey<DefaultValue>("asset"));
        using ResourceLease<DefaultValue> lease = reference.Acquire();
        Assert.Same(execution.Resources, lease.Value.Runtime);
    }

    private sealed class EnvironmentRuntime(IResourceRuntime inner) : IEnvironment, IResourceExecutor
    {
        public int Builds { get; private set; }

        public int Releases { get; private set; }

        public IResourceExecutor Executor => this;

        public bool ShouldYield => inner.Executor.ShouldYield;

        public ValueTask BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
        {
            Builds++;
            return inner.Executor.BuildAsync(context, cancellationToken);
        }

        public ValueTask ReleaseAsync(IResourceStage stage, IResourceRuntime runtime, object resource)
        {
            Releases++;
            return inner.Executor.ReleaseAsync(stage, runtime, resource);
        }
    }

    private sealed record Leaf(IEnvironment Runtime);

    private sealed record Parent(IEnvironment Runtime, Leaf Input);

    private sealed record DefaultValue(ResourceRuntime Runtime);

    private sealed class SourceStage : IResourceSourceStage<Leaf, IEnvironment>
    {
        internal bool Called { get; private set; }

        public async ValueTask<Leaf> BuildAsync(IEnvironment runtime, ResourceBuildContext context, CancellationToken cancellationToken)
        {
            Called = true;
            Assert.Same(runtime, context.Runtime);
            await Task.Yield();
            Assert.NotNull(SynchronizationContext.Current);
            return new(runtime);
        }

        public ValueTask ReleaseAsync(Leaf resource, IEnvironment runtime)
        {
            Assert.Same(resource.Runtime, runtime);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TransformStage : IResourceTransform<Leaf, Parent, IEnvironment>
    {
        public ResourceDependencyLifetime InputLifetime => ResourceDependencyLifetime.Build;

        public ResourceAddress GetInput(ResourceAddress output) => output;

        public async ValueTask<Parent> BuildAsync(Leaf input, IEnvironment runtime, ResourceBuildContext context, CancellationToken cancellationToken)
        {
            Assert.Same(runtime, context.Runtime);
            await Task.Yield();
            Assert.NotNull(SynchronizationContext.Current);
            return new(runtime, input);
        }

        public ValueTask ReleaseAsync(Parent resource, IEnvironment runtime)
        {
            Assert.Same(resource.Runtime, runtime);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class DefaultStage : IResourceSourceStage<DefaultValue, ResourceRuntime>
    {
        public ValueTask<DefaultValue> BuildAsync(ResourceRuntime runtime, ResourceBuildContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new DefaultValue(runtime));

        public ValueTask ReleaseAsync(DefaultValue resource, ResourceRuntime runtime)
        {
            Assert.Same(resource.Runtime, runtime);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class UnusedSource : IResourceSource
    {
        public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default) => ValueTask.FromResult<string?>(null);
    }
}
