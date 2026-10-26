using Lumyte.Graphics.RenderGraph;
using Lumyte.Memory.DependencyInjection;
using Lumyte.Resources.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Resources.Graphics.Tests;

/// <summary>Verifies atomic ownership transfer and dependency-safe cleanup for imported graph resources.</summary>
public sealed class ImportLifetimeTests
{
    /// <summary>Scope and plan cleanup preserve failed ownership while releasing successful children only once.</summary>
    /// <param name="compile">Whether ownership first moves to a compiled graph.</param>
    /// <returns>Completion of manager disposal.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CleanupRetriesInReverseRegistrationOrderAsync(bool compile)
    {
        await using ServiceProvider provider = CreateProvider();
        var events = new List<string>();
        using ResourceRenderGraphImports imports = new RenderGraphBuilder().ImportResources(provider.GetRequiredService<ResourceManager>());
        imports.Retain(new Lifetime("dependency", events));
        imports.Retain(new Lifetime("failing", events, failOnce: true));
        imports.Retain(new Lifetime("released", events));
        using IDisposable owner = compile ? imports.Compile() : imports;
        if (compile)
        {
            imports.Dispose();
            Assert.Empty(events);
        }

        Assert.Throws<InvalidOperationException>(() => owner.Dispose());
        Assert.Equal(["released", "failing"], events);
        Assert.Throws<ObjectDisposedException>(() => imports.Retain(new Lifetime("rejected", events)));
        Assert.Throws<ObjectDisposedException>(() => imports.Graph);
        Assert.Throws<ObjectDisposedException>(() => imports.Compile());

        owner.Dispose();
        Assert.Equal(["released", "failing", "failing", "dependency"], events);
        owner.Dispose();
        Assert.Equal(4, events.Count);
    }

    /// <summary>Failed compilation validation leaves every owner in the import scope without releasing any prefix.</summary>
    /// <returns>Completion of manager disposal.</returns>
    [Fact]
    public async Task FailedValidationDoesNotPartiallyTransferOwnershipAsync()
    {
        await using ServiceProvider provider = CreateProvider();
        var events = new List<string>();
        using ResourceRenderGraphImports imports = new RenderGraphBuilder().ImportResources(provider.GetRequiredService<ResourceManager>());
        var invalidated = new Lifetime("invalidated", events);
        imports.Retain(invalidated);
        imports.Retain(new Lifetime("dependent", events));
        invalidated.Valid = false;

        Assert.Throws<InvalidOperationException>(() => imports.Compile());
        Assert.Empty(events);

        imports.Dispose();
        Assert.Equal(["dependent", "invalidated"], events);
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<GraphicsTestState>();
        services.AddMemoryManagement();
        services.AddResources(builder => builder.UseSource<TestSource>().AddSourcePipeline<CpuValue, CpuStage>());
        return services.BuildServiceProvider();
    }

    private sealed class Lifetime(string name, List<string> events, bool failOnce = false) : IRenderGraphLifetime
    {
        private bool _fail = failOnce;

        internal bool Valid { get; set; } = true;

        public void Validate()
        {
            if (!Valid)
            {
                throw new InvalidOperationException("Ownership expired before compilation.");
            }
        }

        public void Dispose()
        {
            events.Add(name);
            if (_fail)
            {
                _fail = false;
                throw new InvalidOperationException("Injected cleanup failure.");
            }
        }
    }
}
