using System.Numerics;
using Lumyte.Input.Actions;
using Lumyte.Input.Settings;
using Xunit;
using static Lumyte.Input.Actions.Compose;

namespace Lumyte.Input.Advanced.Tests;

/// <summary>Verifies declarative definitions integrate with runtime input and settings.</summary>
public sealed class ActionCompositionTests
{
    /// <summary>Verifies generated factories feed mapping, recognition and settings.</summary>
    [Fact]
    public void DeclarativeProfileMapsAndRecognizesInputAndRoundTripsSettings()
    {
        ActionProfile profile = Profile()[
            Profile.Actions()[Action("jump", ActionValueKind.Button)],
            Profile.Contexts()[Context("game")],
            Profile.Bindings()[Binding(id: "jump-key", actionId: "jump", contextId: "game", control: InputControl.ForKey(Key.Space))],
            Profile.Recognitions()[Recognition(id: "jump-press", contextId: "game", kind: RecognitionKind.Press, actions: ["jump"], window: TimeSpan.FromSeconds(1))]].Build();
        var system = new ActionSystem(profile);
        var device = new InputDeviceId(1);
        system.SetDevices([device]);
        system.ActivateContext("game");
        system.Advance(new InputRecord[] { new(device, 1, TimeSpan.Zero, new KeyData(Key.Space, true, false)) }, TimeSpan.Zero);
        Assert.Equal(Vector2.One, profile.Bindings[0].Scale);
        Assert.Equal(1, system.GetState("jump").Value.X);
        Assert.True(system.Buffer.TryConsume("jump-press", TimeSpan.Zero, out _));
        ActionProfile restored = InputSettingsConverter.BuildProfile(InputSettingsConverter.ToSettings(profile));
        Assert.Equal(profile.Actions.ToArray(), restored.Actions.ToArray());
        Assert.Equal(profile.Bindings.ToArray(), restored.Bindings.ToArray());
    }

    /// <summary>Verifies validation and independence of immutable snapshots.</summary>
    [Fact]
    public void BuildValidatesAndSnapshotsMutableNodesAndSlotCollections()
    {
        Compose.Definitions.Action action = Action("move", ActionValueKind.Axis2D);
        Compose.Definitions.Action[] children = [action];
        Compose.Definitions.Profile node = Profile()[Profile.Actions()[children], Profile.Contexts()[Context("game")]];
        ActionProfile first = node.Build();
        children[0] = Action("replacement", ActionValueKind.Button);
        action.Sensitivity = 2;
        Assert.Equal("move", node.Build().Actions[0].Id);
        Assert.Equal(1, first.Actions[0].Sensitivity);
        Assert.Equal(2, node.Build().Actions[0].Sensitivity);
        action.Sensitivity = -1;
        Assert.Throws<ArgumentException>(() => node.Build());
        Assert.Throws<ArgumentException>(() => Profile()[Profile.Actions()[Action("duplicate", ActionValueKind.Button), Action("duplicate", ActionValueKind.Button)]].Build());
    }
}
