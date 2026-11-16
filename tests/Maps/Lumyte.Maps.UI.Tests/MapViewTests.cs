using Lumyte.Maps.Mvt;
using Lumyte.Memory.DependencyInjection;
using Lumyte.Resources;
using Lumyte.Resources.DependencyInjection;
using Lumyte.UI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lumyte.Maps.UI.Tests;

/// <summary>Record-only public UI adapter and borrowed lifetime checks; no GPU claims.</summary>
public sealed class MapViewTests
{
    /// <summary>Checks local retained drawing reuse, camera invalidation, size and remount.</summary>
    /// <returns>The ownership and recording checks.</returns>
    [Fact]
    public async Task RetainedDrawingReusesStaticMapAndUnmountDoesNotDisposeSessionAsync()
    {
        await using ServiceProvider services = Services();
        await using MapSession map = Create(services);
        using (var surface = new UiSurface(new MapView { Session = map }))
        {
            surface.SetViewport(new(200, 100));
            using UiDrawingScene2D drawing = surface.CreateDrawingScene();
            drawing.Update();
            Assert.Equal(1, drawing.LastRecordingCount);
            drawing.Update();
            Assert.Equal(0, drawing.LastRecordingCount);
            map.SetCamera(new(20, 0, 0));
            drawing.Update();
            Assert.Equal(1, drawing.LastRecordingCount);
            drawing.Update();
            Assert.Equal(0, drawing.LastRecordingCount);
            surface.SetViewport(new(0, 0));
            drawing.Update();
        }

        map.SetCamera(new(30, 0, 0));
        using var remounted = new UiSurface(new MapView { Session = map });
        remounted.SetViewport(new(100, 80));
        using UiDrawingScene2D next = remounted.CreateDrawingScene();
        next.Update();
    }

    /// <summary>Checks a rejected replacement preserves the existing mount and both borrow guards.</summary>
    /// <returns>The replacement and ownership checks.</returns>
    [Fact]
    public async Task MountedReplacementRejectsBeforePublicationAndDoubleMountIsRejectedAsync()
    {
        await using ServiceProvider services = Services();
        await using MapSession first = Create(services);
        await using MapSession second = Create(services);
        using var surface = new UiSurface(new MapView { Session = first });
        surface.Rebuild(new MapView { Session = second });
        Assert.Throws<InvalidOperationException>(() => surface.UpdateTree());
        surface.Rebuild(new MapView { Session = first });
        surface.UpdateTree();
        Assert.Same(first, Assert.IsType<MapView>(surface.Root).Session);
        Assert.Throws<InvalidOperationException>(() => new UiSurface(new MapView { Session = first }));
        using var other = new UiSurface(new MapView { Session = second });
        first.SetCamera(new(25, 0, 0));
        surface.SetViewport(new(100, 80));
        using UiDrawingScene2D drawing = surface.CreateDrawingScene();
        drawing.Update();
    }

    private static ServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddMemoryManagement();
        services.AddSingleton(new LocalMvtRegistration("ui-tests", "v1", new("tests"), new Dictionary<MapTileId, string>()));
        services.AddResources(resources => resources.UseSource<LocalMvtSource>().AddSourcePipeline<MapTileContent, MvtResourceStage>());
        return services.BuildServiceProvider();
    }

    private static MapSession Create(ServiceProvider services)
    {
        LocalMvtRegistration registration = services.GetRequiredService<LocalMvtRegistration>();
        return new(services.GetRequiredService<ResourceManager>(), new Tiles(registration), new(MvtDecoder.SchemaId, "v1", new(0, 0, 0, 1), []), new(), []);
    }

    private sealed class Tiles(LocalMvtRegistration registration) : MapTileSet("ui-tests", "v1", MvtDecoder.SchemaId, 0, 0, "hand-authored", registration.Profile)
    {
        public override ResourceKey<MapTileContent> GetKey(MapTileId id) => registration.GetKey(id);
    }
}
