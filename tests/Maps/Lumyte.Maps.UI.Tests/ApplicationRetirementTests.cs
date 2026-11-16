using Lumyte.Memory.DependencyInjection;
using Lumyte.Resources;
using Lumyte.Resources.DependencyInjection;
using Lumyte.UI.Application;
using Lumyte.UI.Application.Headless;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lumyte.Maps.UI.Tests;

/// <summary>Exercises the actual application turn, content quiescence and held resource retirement boundary.</summary>
public sealed class ApplicationRetirementTests
{
    /// <summary>Retirement cannot make a quiescent content producer eligible for Advance again.</summary>
    /// <param name="mode">Fixed, finite, or spatial session construction.</param>
    /// <returns>The real headless application shutdown oracle.</returns>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task QuiescenceStaysStableWhileRetirementOwnsHeldCleanupAsync(int mode)
    {
        var stage = new ReleaseState(mode == 2 ? "retirement/extra" : "retirement/0/0/0");
        var adopted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var checkedRetirement = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        UiApplicationBuilder builder = UiApplication.CreateBuilder().UseHeadless(new() { ClientSize = new(128, 64) });
        builder.Services.AddMemoryManagement();
        builder.Services.AddSingleton(stage);
        builder.Services.AddSingleton(new Source());
        builder.Services.AddResources(resources => resources.UseSource<Source>().AddSourcePipeline<MapTileContent, HeldStage>());
        Content? content = null;
        builder.UseContent(services => content = new(services.GetRequiredService<ResourceManager>(), stage, mode, adopted, checkedRetirement));
        await using UiApplication app = builder.Build();
        Task running = app.RunAsync();
        try
        {
            await adopted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task stopping = app.StopAsync();
            await stage.ReleaseEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await checkedRetirement.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(content!.QuiescentDuringRetire);
            Assert.Equal(0, content.AdvancesDuringRetire);
            Assert.Null(app.Failure);
            Assert.False(stopping.IsCompleted);
            stage.AllowRelease.TrySetResult();
            await stopping.WaitAsync(TimeSpan.FromSeconds(5));
            await running.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(UiApplicationState.Stopped, app.State);
            Assert.Null(app.Failure);
            Assert.Equal(mode == 2 ? 2 : 1, stage.Releases);
            Assert.Equal(content.Owner, content.RetireOwner);
        }
        finally
        {
            stage.AllowRelease.TrySetResult();
        }
    }

    private sealed class Content(ResourceManager resources, ReleaseState stage, int mode, TaskCompletionSource adopted, TaskCompletionSource checkedRetirement) : IUiApplicationContent
    {
        private readonly MapSession _map = CreateMap(resources, mode);
        private UiApplicationWindowContext? _context;
        private bool _retiring;

        public bool IsStopped => _map.IsStopped;

        internal bool QuiescentDuringRetire { get; private set; }

        internal int AdvancesDuringRetire { get; private set; }

        internal int Owner { get; private set; }

        internal int RetireOwner { get; private set; }

        public ValueTask InitializeAsync(UiApplicationWindowContext context, CancellationToken cancellationToken)
        {
            _context = context;
            Owner = Environment.CurrentManagedThreadId;
            return context.View.SetInitialRootAsync(new MapView { Session = _map }, cancellationToken);
        }

        public void Advance()
        {
            if (_retiring)
            {
                AdvancesDuringRetire++;
            }

            _map.Advance();
            if (_map.PublishedTiles == 1)
            {
                adopted.TrySetResult();
            }
        }

        public void BeginStop() => _map.BeginStop();

        public async ValueTask RetireAsync()
        {
            if (mode == 2)
            {
                // Own spatial retirement is already drained; final sweep now encounters another unused generation.
                using ResourceReference<MapTileContent> extra = await resources.RetainAsync(new ResourceKey<MapTileContent>("retirement/extra"));
            }

            Task disposal = Task.CompletedTask;
            await _context!.Dispatcher.InvokeAsync(() =>
            {
                RetireOwner = Environment.CurrentManagedThreadId;
                _retiring = true;
                disposal = _map.DisposeAsync().AsTask();
            });
            await stage.ReleaseEntered.Task;
            await _context.Dispatcher.InvokeAsync(() =>
            {
                QuiescentDuringRetire = _map.IsStopped;
                checkedRetirement.TrySetResult();
            });
            await disposal;
        }

        public ValueTask ReleaseAsync() => ValueTask.CompletedTask;

        private static MapSession CreateMap(ResourceManager resources, int mode)
        {
            var tiles = new Tiles();
            var style = new MapStyle("test", "v1", new(0, 0, 0, 1), []);
            var options = new MapSessionOptions { SelectVisibleTiles = mode == 1, MaxPrefetchTiles = 0, PrefetchRadius = 0, MaxFallbackDepth = 0 };
            return mode == 2 ? new(resources, tiles, style, options) : new(resources, tiles, style, options, [new(0, 0, 0)]);
        }
    }

    private sealed class Tiles() : MapTileSet("retirement", "v1", "test", 0, 0, "handmade", new("retirement", maxReservationBytesPerTile: 256))
    {
        public override ResourceKey<MapTileContent> GetKey(MapTileId id) => new($"retirement/{id.Zoom}/{id.X}/{id.Y}");
    }

    private sealed class Source : IResourceSource
    {
        public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default) => ValueTask.FromResult<string?>("v1");
    }

    private sealed class ReleaseState(string heldId)
    {
        internal string HeldId { get; } = heldId;

        internal TaskCompletionSource ReleaseEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource AllowRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal int Releases { get; set; }
    }

    private sealed class HeldStage(ReleaseState state) : IResourceSourceStage<MapTileContent>
    {
        public ValueTask<MapTileContent> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new MapTileContent([], context.Address.Id.Value!, 128, 256));

        public async ValueTask ReleaseAsync(MapTileContent resource)
        {
            if (resource.SourceRevision == state.HeldId)
            {
                state.ReleaseEntered.TrySetResult();
                await state.AllowRelease.Task;
            }

            state.Releases++;
        }
    }
}
