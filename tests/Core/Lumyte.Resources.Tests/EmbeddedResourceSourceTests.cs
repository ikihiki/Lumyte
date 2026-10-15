using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text;
using Lumyte.Memory.DependencyInjection;
using Lumyte.Resources.DependencyInjection;
using Lumyte.Resources.Embedded;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lumyte.Resources.Tests;

/// <summary>Verifies DLL-embedded revisions, independent range readers and pipeline integration.</summary>
public sealed class EmbeddedResourceSourceTests
{
    private const string PayloadName = "Lumyte.Resources.Tests.Embedded.Payload";
    private static readonly ResourceId _alias = new("shaders/payload.bin");

    /// <summary>Aliases and verbatim names resolve the actual resource embedded in the test DLL.</summary>
    /// <returns>Completion of immutable revision and payload checks.</returns>
    [Fact]
    public async Task ReadsEmbeddedDllWithAliasesAndStableRevisionsAsync()
    {
        EmbeddedResourceSource source = CreateSource();
        await using ResourceSourceSnapshot snapshot = await source.OpenSnapshotAsync(_alias);
        Assert.Equal(_alias, snapshot.Id);
        Assert.True(snapshot.CanReadRange);
        Assert.True(snapshot.Length >= 10);
        Assert.Equal(snapshot.Revision, await source.GetRevisionAsync(_alias));
        Assert.Equal(snapshot.Revision, await CreateSource().GetRevisionAsync(new(PayloadName)));
        Assert.NotEqual(snapshot.Revision, await source.GetRevisionAsync(new("empty.bin")));
        await using Stream input = await snapshot.OpenReadAsync();
        using StreamReader reader = new(input);
        Assert.Equal("0123456789", (await reader.ReadToEndAsync()).TrimEnd('\r', '\n'));
    }

    /// <summary>Reader cursors and all read overloads stay within the selected range.</summary>
    /// <returns>Completion of bounded reads and seeks.</returns>
    [Fact]
    public async Task RangesHaveIndependentCursorsAndRejectWritesAsync()
    {
        await using ResourceSourceSnapshot snapshot = await CreateSource().OpenSnapshotAsync(_alias);
        await using Stream first = await snapshot.OpenReadAsync(2, 3);
        await using Stream second = await snapshot.OpenReadAsync(6, 2);
        byte[] bytes = new byte[16];
        Assert.True(first.CanRead);
        Assert.True(first.CanSeek);
        Assert.False(first.CanWrite);
        Assert.Equal(3L, first.Length);
        Assert.Equal(3, await first.ReadAsync(bytes, 0, bytes.Length));
        Assert.Equal("234", Encoding.UTF8.GetString(bytes.AsSpan(0, 3)));
        Assert.Equal(0, await first.ReadAsync(bytes.AsMemory()));
        Assert.Equal(0L, second.Position);
        Assert.Equal(2, second.Read(bytes, 0, bytes.Length));
        Assert.Equal("67", Encoding.UTF8.GetString(bytes.AsSpan(0, 2)));
        Assert.Equal(1L, first.Seek(-2, SeekOrigin.End));
        Assert.Equal('3', first.ReadByte());
        first.Position = 0;
        Assert.Equal('2', first.ReadByte());
        Assert.Throws<IOException>(() => first.Seek(-1, SeekOrigin.Begin));
        Assert.Throws<IOException>(() => first.Seek(4, SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => first.WriteByte(0));
        Assert.Throws<NotSupportedException>(() => first.SetLength(10));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await snapshot.OpenReadAsync(-1));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await snapshot.OpenReadAsync(snapshot.Length!.Value + 1));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await snapshot.OpenReadAsync(0, 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await snapshot.OpenReadAsync(0, -1));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await snapshot.OpenReadAsync(snapshot.Length!.Value, 1));
        await using Stream end = await snapshot.OpenReadAsync(snapshot.Length!.Value);
        Assert.Equal(0, await end.ReadAsync(bytes.AsMemory()));
    }

    /// <summary>Missing and zero-length embedded resources have distinct outcomes.</summary>
    /// <returns>Completion of resource boundary checks.</returns>
    [Fact]
    public async Task MissingEmptyAndCancelledRequestsHaveDefinedOutcomesAsync()
    {
        EmbeddedResourceSource source = CreateSource();
        Assert.Null(await source.GetRevisionAsync(new("missing")));
        Assert.Null(await source.GetRevisionAsync(new(PayloadName.ToLowerInvariant())));
        await Assert.ThrowsAsync<FileNotFoundException>(async () => await source.OpenSnapshotAsync(new("missing")));
        await Assert.ThrowsAsync<ArgumentException>(async () => await source.GetRevisionAsync(default));
        await Assert.ThrowsAsync<ArgumentException>(async () => await source.OpenSnapshotAsync(default));
        await using ResourceSourceSnapshot empty = await source.OpenSnapshotAsync(new("empty.bin"));
        Assert.Equal(0L, empty.Length);
        await using Stream input = await empty.OpenReadAsync();
        Assert.Equal(-1, input.ReadByte());
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await source.GetRevisionAsync(_alias, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await source.OpenSnapshotAsync(_alias, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await empty.OpenReadAsync(cancellationToken: cancellation.Token));
    }

    /// <summary>Snapshot disposal refuses future readers while existing readers retain the DLL data.</summary>
    /// <returns>Completion of idempotent snapshot and stream release.</returns>
    [Fact]
    public async Task DisposedSnapshotLeavesOpenedReaderIndependentAsync()
    {
        ResourceSourceSnapshot snapshot = await CreateSource().OpenSnapshotAsync(_alias);
        await using Stream input = await snapshot.OpenReadAsync(0, 4);
        await snapshot.DisposeAsync();
        await snapshot.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await snapshot.OpenReadAsync());
        byte[] bytes = new byte[4];
        await input.ReadExactlyAsync(bytes);
        Assert.Equal("0123", Encoding.UTF8.GetString(bytes));
        await input.DisposeAsync();
        Assert.False(input.CanRead);
    }

    /// <summary>Changing the caller's alias dictionary cannot change the source's immutable revision.</summary>
    /// <returns>Completion of alias configuration ownership.</returns>
    [Fact]
    public async Task CopiesAliasConfigurationAtConstructionAsync()
    {
        Dictionary<ResourceId, string> names = new() { [_alias] = PayloadName };
        var source = new EmbeddedResourceSource(new() { Assembly = typeof(EmbeddedResourceSourceTests).Assembly, ResourceNames = names });
        string? revision = await source.GetRevisionAsync(_alias);
        names[_alias] = "empty.bin";
        Assert.Equal(revision, await source.GetRevisionAsync(_alias));
        await using ResourceSourceSnapshot snapshot = await source.OpenSnapshotAsync(_alias);
        Assert.True(snapshot.Length >= 10);
        Assert.Throws<ArgumentException>(() => new EmbeddedResourceSource(new()
        {
            Assembly = typeof(EmbeddedResourceSourceTests).Assembly,
            ResourceNames = new Dictionary<ResourceId, string> { [default] = PayloadName },
        }));
        Assert.Throws<ArgumentException>(() => new EmbeddedResourceSource(new()
        {
            Assembly = typeof(EmbeddedResourceSourceTests).Assembly,
            ResourceNames = new Dictionary<ResourceId, string> { [_alias] = string.Empty },
        }));
    }

    /// <summary>A scoped pipeline reads the embedded source using the ordinary resource build context.</summary>
    /// <returns>Completion of DI acquisition, reference resolution and unchanged reload.</returns>
    [Fact]
    public async Task PipelineUsesEmbeddedSourceThroughDependencyInjectionAsync()
    {
        ServiceCollection services = new();
        services.AddMemoryManagement();
        services.AddSingleton(Options());
        services.AddResources("dll", pipelines => pipelines.UseSource<EmbeddedResourceSource>().AddSourcePipeline<TextValue, TextStage>());
        await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        ResourceManager manager = scope.ServiceProvider.GetRequiredKeyedService<ResourceManager>("dll");
        var key = new ResourceKey<TextValue>(new(_alias));
        using ResourceReference<TextValue> owner = await manager.CreateReferenceAsync(key);
        using ResourceLease<TextValue> first = await manager.AcquireAsync(key);
        Assert.Equal("0123456789", first.Value.Text.TrimEnd('\r', '\n'));
        Assert.Equal(ResourceReloadStatus.Unchanged, (await manager.ReloadAllAsync()).Status);
        Assert.True(manager.TryAcquire(owner.Handle, out ResourceLease<TextValue>? second));
        using (second)
        {
            Assert.Same(first.Value, second!.Value);
        }
    }

    /// <summary>Readers pin collectible assembly storage until their own lifetime ends.</summary>
    /// <returns>Completion of a range read after requesting assembly unload and collecting unrelated owners.</returns>
    [Fact]
    public async Task ReaderRetainsCollectibleAssemblyAfterSnapshotReleaseAsync()
    {
        (Stream input, WeakReference context) = OpenCollectibleReader();
        await using (input)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.True(context.IsAlive);
            byte[] bytes = new byte[4];
            await input.ReadExactlyAsync(bytes);
            Assert.Equal("0123", Encoding.UTF8.GetString(bytes));
        }
    }

    private static EmbeddedResourceSourceOptions Options() => new()
    {
        Assembly = typeof(EmbeddedResourceSourceTests).Assembly,
        ResourceNames = new Dictionary<ResourceId, string> { [_alias] = PayloadName },
    };

    private static EmbeddedResourceSource CreateSource() => new(Options());

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (Stream Input, WeakReference Context) OpenCollectibleReader()
    {
        var context = new AssemblyLoadContext("embedded resource lifetime", isCollectible: true);
        Assembly assembly = context.LoadFromAssemblyPath(typeof(EmbeddedResourceSourceTests).Assembly.Location);
        var source = new EmbeddedResourceSource(new() { Assembly = assembly });
        ResourceSourceSnapshot snapshot = source.OpenSnapshotAsync(new(PayloadName)).GetAwaiter().GetResult();
        Stream input = snapshot.OpenReadAsync(0, 4).GetAwaiter().GetResult();
        snapshot.DisposeAsync().GetAwaiter().GetResult();
        context.Unload();
        return (input, new(context));
    }

    private sealed record TextValue(string Text);

    private sealed class TextStage : IResourceSourceStage<TextValue>
    {
        public async ValueTask<TextValue> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
        {
            ResourceSourceSnapshot snapshot = await context.OpenSourceAsync(context.Address.Id, cancellationToken: cancellationToken);
            await using Stream input = await snapshot.OpenReadAsync(cancellationToken: cancellationToken);
            using StreamReader reader = new(input);
            return new(await reader.ReadToEndAsync(cancellationToken));
        }

        public ValueTask ReleaseAsync(TextValue resource) => ValueTask.CompletedTask;
    }
}
