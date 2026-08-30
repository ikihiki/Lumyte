using Lumyte.Interaction;
namespace Lumyte.DevTools.Host.Tests;

public sealed class DemoRuntimeDomainTests
{
    [Fact]
    public async Task KeyboardAndMouseInputsReportRawStateBindingsAndPhases()
    {
        DevToolsHub hub = new();
        using DemoInputDomain domain = new(hub);
        List<InputChanged> events = [];
        using IDisposable subscription = hub.Subscribe(DemoInputDomain.Domain, DemoInputDomain.Changed, (value, _) => { events.Add(value); return ValueTask.CompletedTask; });
        InputSnapshot keyboard = await hub.CommandAsync(DemoInputDomain.Domain, DemoInputDomain.InjectKey, new InjectKeyRequest("Space", true));
        InputSnapshot mouse = await hub.CommandAsync(DemoInputDomain.Domain, DemoInputDomain.InjectMouseButton, new InjectMouseButtonRequest("Left", true));
        InputSnapshot moved = await hub.CommandAsync(DemoInputDomain.Domain, DemoInputDomain.InjectPointer, new InjectPointerRequest(40, 20, 4, -2));
        InputSnapshot wheel = await hub.CommandAsync(DemoInputDomain.Domain, DemoInputDomain.InjectWheel, new InjectWheelRequest(0, 120));

        Assert.Equal(["Space"], keyboard.Raw.PressedKeys);
        Assert.Equal(["Left"], mouse.Raw.PressedMouseButtons);
        Assert.Equal(ActionPhase.Performed, Assert.Single(keyboard.Actions.Actions, action => action.Id == "game.jump").Phase);
        Assert.Equal("game.fire", Assert.Single(events[1].Routes).Action);
        Assert.Equal("game.look", Assert.Single(events[2].Routes).Action);
        Assert.Equal("game.zoom", Assert.Single(events[3].Routes).Action);
        Assert.Equal(4, moved.Raw.PointerDelta.X);
        Assert.Equal(120, wheel.Raw.WheelDelta.Y);
    }

    [Fact]
    public async Task ReleaseAllReleasesKeyboardButtonsAndTransientAxes()
    {
        DevToolsHub hub = new();
        using DemoInputDomain domain = new(hub);
        await hub.CommandAsync(DemoInputDomain.Domain, DemoInputDomain.InjectKey, new InjectKeyRequest("E", true));
        await hub.CommandAsync(DemoInputDomain.Domain, DemoInputDomain.InjectMouseButton, new InjectMouseButtonRequest("Right", true));
        await hub.CommandAsync(DemoInputDomain.Domain, DemoInputDomain.InjectPointer, new InjectPointerRequest(10, 10, 5, 3));

        InputSnapshot released = await hub.CommandAsync(DemoInputDomain.Domain, DemoInputDomain.ReleaseAll, new ReleaseAllInputRequest("focus-loss"));

        Assert.Empty(released.Raw.PressedKeys);
        Assert.Empty(released.Raw.PressedMouseButtons);
        Assert.Equal(default, released.Raw.PointerDelta);
        Assert.Equal(default, released.Raw.WheelDelta);
    }

    [Fact]
    public async Task ResourceTreeShowsSharedDependencyAndReloadGeneration()
    {
        DevToolsHub hub = new();
        await using DemoResourcesDomain domain = new(hub);
        ResourceCommandResult scene = await hub.CommandAsync(DemoResourcesDomain.Domain, DemoResourcesDomain.Load, new ResourceOperationRequest("demo:scene"));
        ResourceCommandResult overlay = await hub.CommandAsync(DemoResourcesDomain.Domain, DemoResourcesDomain.Load, new ResourceOperationRequest("demo:overlay"));
        ResourceCommandResult reloaded = await hub.CommandAsync(DemoResourcesDomain.Domain, DemoResourcesDomain.Reload, new ResourceOperationRequest("demo:scene"));

        Assert.True(scene.Success);
        Assert.Equal(2, scene.Snapshot.AllLoaded.SelectMany(Flatten).Count());
        Assert.Equal(2, overlay.Snapshot.Roots.Count);
        Assert.Contains(overlay.Snapshot.Roots.SelectMany(Flatten), node => node.Key == "demo:palette" && node.IsReference);
        Assert.Equal(1u, Assert.Single(reloaded.Snapshot.Roots, root => root.Key == "demo:scene").Generation);
    }

    [Fact]
    public async Task UnloadIsRootScopedAndInvalidOperationsAreStructured()
    {
        DevToolsHub hub = new();
        await using DemoResourcesDomain domain = new(hub);
        await hub.CommandAsync(DemoResourcesDomain.Domain, DemoResourcesDomain.Load, new ResourceOperationRequest("demo:scene"));
        await hub.CommandAsync(DemoResourcesDomain.Domain, DemoResourcesDomain.Load, new ResourceOperationRequest("demo:overlay"));

        ResourceCommandResult unloaded = await hub.CommandAsync(DemoResourcesDomain.Domain, DemoResourcesDomain.Unload, new ResourceOperationRequest("demo:scene"));
        ResourceCommandResult invalid = await hub.CommandAsync(DemoResourcesDomain.Domain, DemoResourcesDomain.Unload, new ResourceOperationRequest("demo:palette"));

        Assert.True(unloaded.Success);
        Assert.DoesNotContain(unloaded.Snapshot.Roots, root => root.Key == "demo:scene");
        Assert.Contains(unloaded.Snapshot.Roots, root => root.Key == "demo:overlay");
        Assert.False(invalid.Success);
        Assert.Equal("not_loaded_root", invalid.ErrorCode);
    }

    private static IEnumerable<ResourceTreeNode> Flatten(ResourceTreeNode root)
    {
        yield return root;
        foreach (ResourceTreeNode child in root.Dependencies.SelectMany(Flatten))
        {
            yield return child;
        }
    }
}
