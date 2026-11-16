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
    /// <summary>Public UI delivery moves a captured map, resets on loss, and preserves the wheel anchor.</summary>
    /// <returns>The record-only interaction oracle.</returns>
    [Fact]
    public async Task PointerPanCaptureLossFocusAndWheelAnchorAsync()
    {
        await using ServiceProvider services = Services();
        await using MapSession map = Create(services);
        map.SetCamera(new(0, 0, 1));
        using var surface = new UiSurface(new MapView { Session = map });
        surface.SetViewport(new(200, 100));
        surface.UpdateLayout();
        using UiDrawingScene2D scene = surface.CreateDrawingScene();
        scene.Update();
        scene.Update();
        Assert.Equal(0, scene.LastRecordingCount);
        Assert.True(surface.Dispatch(new UiPointerInput(7, UiPointerPhase.Down, new(20, 30))));
        Assert.True(surface.Dispatch(new UiPointerInput(7, UiPointerPhase.Move, new(220, 130))));
        Assert.Equal(-70.3125, map.Camera.Longitude, 10);
        scene.Update();
        Assert.Equal(1, scene.LastRecordingCount);
        MapCamera moved = map.Camera;
        Assert.True(surface.Dispatch(new UiPointerInput(7, UiPointerPhase.Cancel, default)));
        surface.Dispatch(new UiPointerInput(7, UiPointerPhase.Move, new(40, 30)));
        Assert.Equal(moved, map.Camera);
        surface.Dispatch(new UiPointerInput(7, UiPointerPhase.Down, new(20, 30)));
        surface.Dispatch(new UiFocusLostInput());
        surface.Dispatch(new UiPointerInput(7, UiPointerPhase.Move, new(60, 30)));
        Assert.Equal(moved, map.Camera);
        map.SetCamera(new(10, 20, 1));
        var anchor = new System.Numerics.Vector2(160, 25);
        (double x, double y) = map.Camera.Project();
        double anchorX = x + (60.0 / 1024);
        double anchorY = y - (25.0 / 1024);
        Assert.True(surface.Dispatch(new UiWheelInput(anchor, new(0, -80))));
        Assert.Equal(1.5, map.Camera.Zoom);
        (x, y) = map.Camera.Project();
        double world = 512 * Math.Pow(2, map.Camera.Zoom);
        Assert.Equal(anchorX, x + (60 / world), 12);
        Assert.Equal(anchorY, y - (25 / world), 12);
        surface.SetViewport(new(400, 200));
        surface.UpdateLayout();
        ulong revision = map.DisplayRevision;
        surface.UpdateLayout();
        Assert.Equal(revision, map.DisplayRevision);
        map.ZoomAt(new(200, 100), 100);
        Assert.Equal(2, map.Camera.Zoom);
        map.ZoomAt(new(200, 100), -100);
        Assert.Equal(0, map.Camera.Zoom);
    }

    /// <summary>Wheel positions are element-local under layout offsets, including world wrapping and latitude limits.</summary>
    /// <returns>The projection boundary checks.</returns>
    [Fact]
    public async Task OffsetWheelAnchorWrapsLongitudeAndClampsLatitudeAsync()
    {
        await using ServiceProvider services = Services();
        await using MapSession map = Create(services);
        map.SetCamera(new(179, 20, 1));
        using var surface = new UiSurface(Compose.Stack()[Compose.Panel(height: 20), new MapView { Session = map, Width = 200, Height = 80 }]);
        surface.SetViewport(new(200, 100));
        surface.UpdateLayout();
        (double x, double y) = map.Camera.Project();
        double anchorX = x + (60.0 / 1024);
        double anchorY = y - (15.0 / 1024);
        Assert.True(surface.Dispatch(new UiWheelInput(new(160, 45), new(0, -160))));
        (x, y) = map.Camera.Project();
        Assert.Equal(anchorX % 1, (x + (60.0 / 2048)) % 1, 12);
        Assert.Equal(anchorY, y - (15.0 / 2048), 12);
        map.SetCamera(new(0, MapCamera.LatitudeLimit, 1));
        surface.Dispatch(new UiWheelInput(new(100, 20), new(0, -160)));
        Assert.Equal(MapCamera.LatitudeLimit, map.Camera.Latitude, 10);
        Assert.Throws<ArgumentOutOfRangeException>(() => map.Pan(new(float.NaN, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.ZoomAt(default, double.NaN));
    }

    /// <summary>A real overlay control captures its press before the map and can block wheel bubbling.</summary>
    /// <returns>The input precedence checks.</returns>
    [Fact]
    public async Task OverlayControlOwnsInputBeforeMapAsync()
    {
        await using ServiceProvider services = Services();
        await using MapSession map = Create(services);
        int clicks = 0;
        using var surface = new UiSurface(Compose.Panel()[new MapView { Session = map }, Compose.Button(onClick: () => clicks++)[Compose.Rectangle(width: 40, height: 40)]]);
        surface.SetViewport(new(200, 100));
        surface.UpdateLayout();
        MapCamera initial = map.Camera;
        Assert.True(surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(5, 5))));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Move, new(20, 20)));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(20, 20)));
        Assert.Equal(1, clicks);
        Assert.Equal(initial, map.Camera);
        Assert.False(surface.Dispatch(new UiWheelInput(new(5, 5), new(0, -40))));
        Assert.Equal(initial, map.Camera);
    }

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

    /// <summary>Map wheel and explicit touch gestures precede nested ancestor scrolling.</summary>
    /// <returns>The nested viewport input oracle.</returns>
    [Fact]
    public async Task NestedScrollViewsPreserveMapWheelAndTouchAsync()
    {
        await using ServiceProvider services = Services();
        await using MapSession map = Create(services);
        using var outer = new ScrollController();
        using var inner = new ScrollController();
        using var surface = new UiSurface(Compose.ScrollView(controller: outer)[Compose.Stack()[
            Compose.ScrollView(controller: inner, height: 100)[new MapView { Session = map, Height = 300 }],
            Compose.SizedBox(height: 300)]]);
        surface.SetViewport(new(200, 100));
        surface.UpdateLayout();
        map.SetCamera(new(0, 0, 1));
        Assert.True(surface.Dispatch(new UiWheelInput(new(30, 40), new(0, -40))));
        Assert.Equal(1.25, map.Camera.Zoom);
        Assert.Equal(0, inner.Offset);
        Assert.Equal(0, outer.Offset);
        MapCamera before = map.Camera;
        Assert.True(surface.Dispatch(new UiPointerInput(42, UiPointerPhase.Down, new(30, 40)) { Device = UiPointerDevice.Touch }));
        Assert.True(surface.Dispatch(new UiPointerInput(42, UiPointerPhase.Move, new(30, 20)) { Device = UiPointerDevice.Touch }));
        Assert.NotEqual(before, map.Camera);
        Assert.Equal(0, inner.Offset);
        Assert.Equal(0, outer.Offset);
        surface.Dispatch(new UiPointerInput(42, UiPointerPhase.Up, new(30, 20)) { Device = UiPointerDevice.Touch });

        // Even at the inner scroll boundary, the map retains wheel precedence.
        inner.ScrollTo(200);
        surface.UpdateLayout();
        Assert.True(surface.Dispatch(new UiWheelInput(new(30, 40), new(0, 40))));
        Assert.Equal(1, map.Camera.Zoom);
        Assert.Equal(0, outer.Offset);
        outer.ScrollTo(100);
        surface.UpdateLayout();
        MapCamera unchanged = map.Camera;
        Assert.True(surface.Dispatch(new UiWheelInput(new(30, 40), new(0, 40))));
        Assert.Equal(140, outer.Offset);
        Assert.Equal(unchanged, map.Camera);
    }

    /// <summary>C4 allocation and modifiers publish the exact map body viewport, including unbounded axes.</summary>
    /// <param name="layout">Grid, weighted Flex, ordinary Flex or ScrollView.</param>
    /// <returns>The public camera-anchor viewport oracle.</returns>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task AllocatedBodyViewportMatchesCameraAnchorAsync(int layout)
    {
        await using ServiceProvider services = Services();
        await using MapSession map = Create(services);
        var view = new MapView { Session = map };
        Compose.Definitions.Widget root;
        if (layout == 0)
        {
            Compose.Grid.Cell()(view);
            Compose.Padding.All(10)(view);
            root = Compose.Grid()[view];
        }
        else if (layout == 1)
        {
            Compose.Flex.Expanded()(view);
            root = Compose.Flex()[Compose.SizedBox(height: 20), view];
        }
        else
        {
            root = layout == 2 ? Compose.Flex()[view] : Compose.ScrollView()[view];
        }

        using var surface = new UiSurface(root);
        foreach (System.Numerics.Vector2 viewport in new System.Numerics.Vector2[] { new(200, 100), new(400, 200) })
        {
            surface.SetViewport(viewport);
            surface.UpdateLayout();
            UiElement element = surface.RootElement.Children[layout == 1 ? 1 : 0];
            System.Numerics.Vector2 expected = layout switch
            {
                0 => viewport - new System.Numerics.Vector2(20),
                1 => viewport - new System.Numerics.Vector2(0, 20),
                _ => new System.Numerics.Vector2(viewport.X, 0),
            };
            Assert.Equal(expected, element.ContentSize);
            map.SetCamera(new(0, 0, 1));

            // Zooming at the final body's center must not translate the camera.
            map.ZoomAt(element.ContentSize / 2, 0.25);
            Assert.Equal(0, map.Camera.Longitude, 12);
            Assert.Equal(0, map.Camera.Latitude, 12);
            ulong revision = map.DisplayRevision;
            surface.UpdateLayout();
            Assert.Equal(revision, map.DisplayRevision);
        }
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

    private sealed class Tiles(LocalMvtRegistration registration) : MapTileSet("ui-tests", "v1", MvtDecoder.SchemaId, 0, 2, "hand-authored", registration.Profile)
    {
        public override ResourceKey<MapTileContent> GetKey(MapTileId id) => registration.GetKey(id);
    }
}
