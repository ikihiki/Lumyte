using System.Reflection;
using System.Runtime.Loader;
using Lumyte.Memory;
using Xunit;

namespace Lumyte.Resources.Tests;

/// <summary>Verifies exact CLR type identity throughout resource selection and dependency validation.</summary>
public sealed class ResourceTypeIdentityTests
{
    private static readonly ResourceAddress _address = new(new ResourceId("asset"));

    /// <summary>Closed generic types with the same name and address have separate caches and ownership.</summary>
    /// <returns>Completion of typed acquisition and cache reuse.</returns>
    [Fact]
    public async Task ClosedGenericTypesAtTheSameAddressRemainDistinctAsync()
    {
        await using MemoryManager memory = new();
        await using ResourceExecutionHost execution = new();
        await using ResourceManager manager = new(
            "types",
            new UnusedSource(),
            [new Pipeline(typeof(List<int>)), new Pipeline(typeof(List<string>))],
            memory,
            execution.Resources);
        var integers = new ResourceKey<List<int>>(_address);
        var strings = new ResourceKey<List<string>>(_address);
        Assert.Equal(typeof(List<int>).Name, typeof(List<string>).Name);
        Assert.NotEqual(integers.TypeHandle, strings.TypeHandle);
        using ResourceLease<List<int>> first = await manager.AcquireLeaseForTestAsync(integers);
        using ResourceLease<List<string>> second = await manager.AcquireLeaseForTestAsync(strings);
        first.Value.Add(42);
        second.Value.Add("value");
        using ResourceLease<List<int>> again = await manager.AcquireLeaseForTestAsync(integers);
        Assert.Same(first.Value, again.Value);
        Assert.Equal([42], again.Value);
        Assert.Equal(["value"], second.Value);
        await Assert.ThrowsAsync<NotSupportedException>(async () => await manager.AcquireLeaseForTestAsync(new ResourceKey<string>(_address)));
    }

    /// <summary>The same assembly-qualified type name loaded in two contexts identifies two distinct outputs.</summary>
    /// <returns>Completion of both acquisitions and assembly unload requests.</returns>
    [Fact]
    public async Task IdenticalTypeNamesInDifferentLoadContextsRemainDistinctAsync()
    {
        var firstContext = new AssemblyLoadContext("resource type first", isCollectible: true);
        var secondContext = new AssemblyLoadContext("resource type second", isCollectible: true);
        try
        {
            string path = typeof(MemorySnapshot).Assembly.Location;
            string name = typeof(MemorySnapshot).FullName!;
            Type firstType = firstContext.LoadFromAssemblyPath(path).GetType(name, throwOnError: true)!;
            Type secondType = secondContext.LoadFromAssemblyPath(path).GetType(name, throwOnError: true)!;
            Assert.Equal(firstType.AssemblyQualifiedName, secondType.AssemblyQualifiedName);
            Assert.NotEqual(firstType.TypeHandle, secondType.TypeHandle);
            await using MemoryManager memory = new();
            await using ResourceExecutionHost execution = new();
            await using ResourceManager manager = new(
                "contexts",
                new UnusedSource(),
                [new Pipeline(firstType), new Pipeline(secondType)],
                memory,
                execution.Resources);
            object first = await AcquireFromTypeAsync(manager, firstType);
            object second = await AcquireFromTypeAsync(manager, secondType);
            Assert.Equal(firstType, first.GetType());
            Assert.Equal(secondType, second.GetType());
            Assert.NotSame(first, second);
        }
        finally
        {
            firstContext.Unload();
            secondContext.Unload();
        }
    }

    /// <summary>Two producers for one output type are ambiguous even when their pipeline objects differ.</summary>
    /// <returns>Completion of memory manager disposal.</returns>
    [Fact]
    public async Task DuplicateOutputTypeRegistrationsAreRejectedAsync()
    {
        await using MemoryManager memory = new();
        await using ResourceExecutionHost execution = new();
        Assert.Throws<ArgumentException>(() => new ResourceManager(
            "duplicate",
            new UnusedSource(),
            [new Pipeline(typeof(List<int>)), new Pipeline(typeof(List<int>))],
            memory,
            execution.Resources));
    }

    /// <summary>Static transform cycles are rejected using primary input types.</summary>
    /// <returns>Completion of memory manager disposal.</returns>
    [Fact]
    public async Task StaticInputTypeCycleIsRejectedAsync()
    {
        await using MemoryManager memory = new();
        await using ResourceExecutionHost execution = new();
        ArgumentException error = Assert.Throws<ArgumentException>(() => new ResourceManager(
            "cycle",
            new UnusedSource(),
            [new Pipeline(typeof(List<int>), typeof(List<string>)), new Pipeline(typeof(List<string>), typeof(List<int>))],
            memory,
            execution.Resources));
        Assert.Contains("cycle", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static Task<object> AcquireFromTypeAsync(ResourceManager manager, Type type)
    {
        MethodInfo method = typeof(ResourceTypeIdentityTests).GetMethod(nameof(AcquireTypedAsync), BindingFlags.NonPublic | BindingFlags.Static)!;
        return (Task<object>)method.MakeGenericMethod(type).Invoke(null, [manager])!;
    }

    private static async Task<object> AcquireTypedAsync<T>(ResourceManager manager)
        where T : class
    {
        using ResourceLease<T> lease = await manager.AcquireLeaseForTestAsync(new ResourceKey<T>(_address));
        return lease.Value;
    }

    private sealed class Pipeline(Type outputType, Type? inputType = null) : IResourceStage
    {
        public Type OutputType => outputType;

        public Type? InputType => inputType;

        public ValueTask<object> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
        {
            Assert.Equal(OutputType.TypeHandle, context.OutputTypeHandle);
            return ValueTask.FromResult(Activator.CreateInstance(OutputType)!);
        }

        public ValueTask ReleaseAsync(object resource) => ValueTask.CompletedTask;
    }

    private sealed class UnusedSource : IResourceSource
    {
        public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<string?>(null);
    }
}
