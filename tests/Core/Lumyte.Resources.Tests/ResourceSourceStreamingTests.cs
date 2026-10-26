using System.Text;
using Lumyte.Memory.DependencyInjection;
using Lumyte.Resources.DependencyInjection;
using Lumyte.Resources.Embedded;
using Lumyte.Resources.FileSystem;

namespace Lumyte.Resources.Tests;

/// <summary>Exercises real source descriptors through manager-owned streaming jobs.</summary>
public sealed class ResourceSourceStreamingTests
{
    /// <summary>Resident descriptors survive trimming and supply independent, seekable session cursors.</summary>
    /// <param name="embedded">Whether the input is embedded rather than a file.</param>
    /// <returns>Completion of source, session and memory lifetime checks.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourcesSupportStreamingAndResidentLifetimeAsync(bool embedded)
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "input"), "0123456789");
            IResourceSource source = embedded
                ? new EmbeddedResourceSource(new()
                {
                    Assembly = typeof(ResourceSourceStreamingTests).Assembly,
                    ResourceNames = new Dictionary<ResourceId, string> { [new("input")] = "Lumyte.Resources.Tests.Embedded.Payload" },
                })
                : new FileResourceSource(new() { RootPath = root });
            var services = new ServiceCollection();
            services.AddSingleton(source);
            services.AddMemoryManagement();
            services.AddResources(builder => builder.UseSource<IResourceSource>().AddSourcePipeline<Factory, Stage>());
            await using ServiceProvider provider = services.BuildServiceProvider();
            await using var execution = new ResourceExecutionHost(provider);
            ResourceManager manager = provider.GetRequiredService<ResourceManager>();
            var key = new ResourceKey<Factory>("input");
            var options = new ResourceStreamOptions { BlockSize = 2, MaxBufferedBlocks = 1 };
            await using ResourceStreamSession<byte> first = await manager.OpenStreamAsync<Factory, byte>(key, options);
            await using ResourceStreamSession<byte> second = await manager.OpenStreamAsync<Factory, byte>(key, options);
            Assert.Equal(0, await manager.TrimAsync());
            Assert.Equal(first.Revision, second.Revision);
            byte[] bytes = new byte[2];
            Assert.Equal(2, await first.ReadAsync(bytes));
            Assert.Equal("01", Encoding.UTF8.GetString(bytes));
            Assert.Equal(0UL, second.Position);
            await second.SeekAsync(6);
            Assert.Equal(2, await second.ReadAsync(bytes));
            Assert.Equal("67", Encoding.UTF8.GetString(bytes));
            await first.DisposeAsync();
            await second.SeekAsync(0);
            Assert.Equal(2, await second.ReadAsync(bytes));
            Assert.Equal("01", Encoding.UTF8.GetString(bytes));
            await second.DisposeAsync();
            Assert.Equal(1, await manager.TrimAsync());
            Assert.Equal(0UL, provider.GetRequiredService<IMemoryManager>().GetSnapshot(new("cpu")).CommittedBytes);
            await manager.DisposeAsync();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class Stage : IResourceSourceStage<Factory>
    {
        public async ValueTask<Factory> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
        {
            ResourceSourceSnapshot snapshot = await context.OpenSourceAsync(context.Address.Id, ResourceDependencyLifetime.Resident, cancellationToken);
            return new(snapshot);
        }

        public ValueTask ReleaseAsync(Factory resource) => ValueTask.CompletedTask;
    }

    private sealed class Factory(ResourceSourceSnapshot snapshot) : IResourceStream<byte>
    {
        public async ValueTask<IResourceStreamReader<byte>> OpenReaderAsync(ResourceStreamOptions options, IMemoryScope memory, CancellationToken cancellationToken = default)
            => new Reader(await snapshot.OpenReadAsync(cancellationToken: cancellationToken));
    }

    private sealed class Reader(Stream input) : IResourceStreamReader<byte>
    {
        public bool CanSeek => input.CanSeek;

        public ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken = default) => input.ReadAsync(destination, cancellationToken);

        public ValueTask SeekAsync(ulong elementOffset, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            input.Seek(checked((long)elementOffset), SeekOrigin.Begin);
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => input.DisposeAsync();
    }
}
