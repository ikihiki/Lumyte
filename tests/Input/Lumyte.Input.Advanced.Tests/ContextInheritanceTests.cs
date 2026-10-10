using System.Numerics;
using System.Text.Json;
using Lumyte.Input.Actions;
using Lumyte.Input.Settings;
using Xunit;
using static Lumyte.Input.Actions.Compose;

namespace Lumyte.Input.Advanced.Tests;

/// <summary>Verifies nested context definitions, inheritance and persistence.</summary>
public sealed class ContextInheritanceTests
{
    /// <summary>Verifies child-only activation, local overrides and inherited recognition.</summary>
    [Fact]
    public void ChildUsesInheritedActionsWithLocalBindingsAndCorrections()
    {
        ActionProfile profile = Profile()[Profile.Contexts()[
            Context("base")[
                Context.Actions()[Action("move", ActionValueKind.Axis1D)],
                Context.Bindings()[Binding(id: "base-key", actionId: "move", control: InputControl.ForKey(Key.Space))],
                Context.Recognitions()[Recognition(id: "press", kind: RecognitionKind.Press, actions: ["move"], window: TimeSpan.FromSeconds(1))]],
            Context("middle", parentId: "base")[Context.Actions()[Action("move", ActionValueKind.Axis1D, sensitivity: 0.5f)]],
            Context("game", parentId: "middle")[Context.Bindings()[Binding(id: "game-key", actionId: "move", control: InputControl.ForKey(Key.J))]]]].Build();
        var system = new ActionSystem(profile);
        var events = new List<ActionEvent>();
        system.Changed += events.Add;
        system.ActivateContext("game");
        system.Advance(new InputRecord[] { Record(1, Key.Space, true) }, TimeSpan.Zero);
        Assert.Equal(Vector2.Zero, system.GetState("move").Value);
        system.Advance(new InputRecord[] { Record(2, Key.J, true) }, TimeSpan.Zero);
        Assert.Equal(0.5f, system.GetState("move").Value.X);
        Assert.All(events, action => Assert.Equal("game", action.ContextId));
        Assert.True(system.Buffer.TryConsume("press", TimeSpan.Zero, out RecognizedAction? recognized));
        Assert.Equal("game", recognized!.ContextId);
        Assert.Equal("middle", profile.Contexts[2].ParentId);
        Assert.Empty(profile.Actions);
    }

    /// <summary>Verifies inherited recognizers maintain separate progress for active contexts.</summary>
    [Fact]
    public void ParentAndChildRecognizeIndependentlyAndDeactivateIndependently()
    {
        ActionProfile profile = NestedProfile();
        var system = new ActionSystem(profile);
        var recognized = new List<RecognizedAction>();
        system.Recognized += recognized.Add;
        system.ActivateContext("base");
        system.ActivateContext("game");
        system.Advance(new InputRecord[] { Record(1, Key.Space, true) }, TimeSpan.Zero);
        system.DeactivateContext("game");
        system.Advance(ReadOnlyMemory<InputRecord>.Empty, TimeSpan.FromSeconds(1));
        RecognizedAction result = Assert.Single(recognized);
        Assert.Equal("base", result.ContextId);
    }

    /// <summary>Verifies named recognition overrides replace the parent's definition in a child.</summary>
    [Fact]
    public void ChildOverridesRecognitionByLocalIdentifier()
    {
        ActionProfile profile = NestedProfile();
        profile = profile with
        {
            Recognitions = profile.Recognitions.Add(new RecognitionDefinition("hold", "game", RecognitionKind.Press, ["jump"], TimeSpan.FromSeconds(1))),
        };
        var system = new ActionSystem(profile);
        system.ActivateContext("game");
        system.Advance(new InputRecord[] { Record(1, Key.Space, true) }, TimeSpan.Zero);
        Assert.True(system.Buffer.TryConsume("hold", TimeSpan.Zero, out _));
        system.Advance(ReadOnlyMemory<InputRecord>.Empty, TimeSpan.FromSeconds(2));
        Assert.False(system.Buffer.TryConsume("hold", TimeSpan.FromSeconds(2), out _));
    }

    /// <summary>Verifies saved JSON retains declared inheritance and local definitions.</summary>
    [Fact]
    public void SettingsRoundTripRetainsInheritanceWithoutFlattening()
    {
        ActionProfile profile = NestedProfile();
        InputActionSettings settings = InputSettingsConverter.ToSettings(profile);
        string json = JsonSerializer.Serialize(settings, InputSettingsJsonContext.Default.InputActionSettings);
        InputActionSettings restored = JsonSerializer.Deserialize(json, InputSettingsJsonContext.Default.InputActionSettings)!;
        ActionProfile rebuilt = InputSettingsConverter.BuildProfile(restored);
        Assert.Equal("base", rebuilt.Contexts[1].ParentId);
        Assert.Single(rebuilt.Contexts[0].Actions);
        Assert.Empty(rebuilt.Contexts[1].Actions);
        Assert.Single(rebuilt.Bindings);
        Assert.Single(rebuilt.Recognitions);
        var system = new ActionSystem(rebuilt);
        system.ActivateContext("game");
        system.Advance(new InputRecord[] { Record(1, Key.Space, true) }, TimeSpan.Zero);
        Assert.Equal(1, system.GetState("jump").Value.X);
    }

    /// <summary>Verifies invalid inheritance and scope references fail before application.</summary>
    /// <param name="failure">The invalid definition case.</param>
    [Theory]
    [InlineData("missing")]
    [InlineData("self")]
    [InlineData("cycle")]
    [InlineData("sibling")]
    [InlineData("kind")]
    public void InvalidDefinitionsAreRejected(string failure)
    {
        ActionProfile profile = NestedProfile();
        InputContext parent = profile.Contexts[0];
        InputContext child = profile.Contexts[1];
        profile = failure switch
        {
            "missing" => profile with { Contexts = [parent, child with { ParentId = "absent" }] },
            "self" => profile with { Contexts = [parent, child with { ParentId = "game" }] },
            "cycle" => profile with { Contexts = [parent with { ParentId = "game" }, child] },
            "sibling" => profile with { Contexts = [parent, child with { ParentId = null }], Bindings = profile.Bindings.Add(new ActionBinding("child", "jump", "game", InputControl.ForKey(Key.J), Vector2.One)) },
            _ => profile with { Contexts = [parent, child with { Actions = [new ActionDefinition("jump", ActionValueKind.Axis1D)] }] },
        };
        Assert.Throws<ArgumentException>(profile.Validate);
    }

    /// <summary>Verifies rebind and live profile replacement affect inherited mappings at a boundary.</summary>
    [Fact]
    public void RebindingDeclaredParentMappingUpdatesDescendants()
    {
        ActionProfile profile = NestedProfile();
        var system = new ActionSystem(profile);
        system.ActivateContext("game");
        RebindSession session = system.BeginRebind("base-key", new RebindOptions(TimeSpan.FromSeconds(2)));
        system.Advance(new InputRecord[] { Record(1, Key.J, true) }, TimeSpan.Zero);
        ActionProfile prepared = session.PrepareProfile(RebindConflictPolicy.Allow);
        Assert.Equal("base", prepared.Bindings[0].ContextId);
        Assert.Equal("base", prepared.Contexts[1].ParentId);
        session.Confirm(RebindConflictPolicy.Allow);
        system.Advance(new InputRecord[] { Record(2, Key.J, false), Record(3, Key.J, true) }, TimeSpan.Zero);
        Assert.Equal(1, system.GetState("jump").Value.X);
        Assert.Equal(prepared.Contexts.ToArray(), system.ExportProfile().Contexts.ToArray());
    }

    /// <summary>Verifies inheritance indices rebuild after a parent profile edit.</summary>
    [Fact]
    public void ApplyingParentChangesRebuildsInheritedBindings()
    {
        ActionProfile profile = NestedProfile();
        var system = new ActionSystem(profile);
        system.ActivateContext("game");
        system.Advance(new InputRecord[] { Record(1, Key.Space, true) }, TimeSpan.Zero);
        system.ApplyProfile(profile with { Bindings = [profile.Bindings[0] with { Control = InputControl.ForKey(Key.J) }] });
        system.Advance(new InputRecord[] { Record(2, Key.Space, false), Record(3, Key.J, true) }, TimeSpan.Zero);
        Assert.Equal(1, system.GetState("jump").Value.X);
        Assert.Single(system.ExportProfile().Bindings);
    }

    /// <summary>Verifies the inherited hot update path does not allocate after warming.</summary>
    [Fact]
    public void InheritedIdleUpdatesDoNotAllocate()
    {
        var system = new ActionSystem(NestedProfile());
        system.ActivateContext("game");
        system.Advance(new InputRecord[] { Record(1, Key.Space, true) }, TimeSpan.Zero);
        for (int i = 0; i < 100; i++)
        {
            system.Advance(ReadOnlyMemory<InputRecord>.Empty, TimeSpan.Zero);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            system.Advance(ReadOnlyMemory<InputRecord>.Empty, TimeSpan.Zero);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    /// <summary>Verifies nested snapshots and rejects explicit mismatched context identifiers.</summary>
    [Fact]
    public void NestedNodesSnapshotLocalActionsAndRejectContextMismatch()
    {
        Compose.Definitions.Action action = Action("jump", ActionValueKind.Button);
        Compose.Definitions.Profile node = Profile()[Profile.Contexts()[Context("game")[Context.Actions()[action]]]];
        ActionProfile built = node.Build();
        action.Sensitivity = 0.5f;
        Assert.Equal(1, built.Contexts[0].Actions[0].Sensitivity);
        Assert.Equal(0.5f, node.Build().Contexts[0].Actions[0].Sensitivity);
        Assert.Throws<ArgumentException>(() => Profile()[Profile.Contexts()[Context("game")[Context.Bindings()[Binding(id: "wrong", actionId: "jump", control: InputControl.ForKey(Key.J), contextId: "other")]]]].Build());
    }

    /// <summary>Verifies flat profiles retain their original timed notification ordering.</summary>
    [Fact]
    public void LegacyHoldDefinitionsKeepTheirDeclarationOrder()
    {
        var profile = new ActionProfile(
            [new ActionDefinition("jump", ActionValueKind.Button)],
            [new ActionBinding("a", "jump", "a", InputControl.ForKey(Key.Space), Vector2.One), new ActionBinding("b", "jump", "b", InputControl.ForKey(Key.Space), Vector2.One)],
            [new InputContext("a"), new InputContext("b")],
            [new RecognitionDefinition("b-hold", "b", RecognitionKind.Hold, ["jump"], TimeSpan.FromSeconds(1)), new RecognitionDefinition("a-hold", "a", RecognitionKind.Hold, ["jump"], TimeSpan.FromSeconds(1))]);
        var system = new ActionSystem(profile);
        var notifications = new List<string>();
        system.Recognized += recognition => notifications.Add(recognition.RecognitionId);
        system.ActivateContext("a");
        system.ActivateContext("b");
        system.Advance(new InputRecord[] { Record(1, Key.Space, true) }, TimeSpan.Zero);
        system.Advance(ReadOnlyMemory<InputRecord>.Empty, TimeSpan.FromSeconds(1));
        Assert.Equal(new[] { "b-hold", "a-hold" }, notifications);
    }

    /// <summary>Verifies scoped state and buffer queries separate concurrently active parent and child.</summary>
    [Fact]
    public void ScopedQueriesDistinguishParentAndChild()
    {
        ActionProfile profile = NestedProfile();
        var system = new ActionSystem(profile);
        system.ActivateContext("base");
        system.ActivateContext("game");
        system.Advance(new InputRecord[] { Record(1, Key.Space, true) }, TimeSpan.Zero);
        Assert.Equal(1, system.GetState("game", "jump").Value.X);
        Assert.Equal(1, system.GetState("base", "jump").Value.X);
        Assert.Throws<InvalidOperationException>(() => system.GetState("other", "jump"));
        system.Advance(ReadOnlyMemory<InputRecord>.Empty, TimeSpan.FromSeconds(1));
        Assert.True(system.Buffer.TryConsume("game", "hold", TimeSpan.FromSeconds(1), out RecognizedAction? child));
        Assert.Equal("game", child!.ContextId);
        Assert.True(system.Buffer.TryConsume("base", "hold", TimeSpan.FromSeconds(1), out RecognizedAction? parent));
        Assert.Equal("base", parent!.ContextId);
        Assert.False(system.Buffer.TryConsume("game", "hold", TimeSpan.FromSeconds(1), out _));
        system.DeactivateContext("game");
        system.Advance(ReadOnlyMemory<InputRecord>.Empty, TimeSpan.FromSeconds(1));
        Assert.Equal(Vector2.Zero, system.GetState("game", "jump").Value);
        Assert.Equal(1, system.GetState("base", "jump").Value.X);
    }

    private static ActionProfile NestedProfile() => Profile()[Profile.Contexts()[
        Context("base")[
            Context.Actions()[Action("jump", ActionValueKind.Button)],
            Context.Bindings()[Binding(id: "base-key", actionId: "jump", control: InputControl.ForKey(Key.Space))],
            Context.Recognitions()[Recognition(id: "hold", kind: RecognitionKind.Hold, actions: ["jump"], window: TimeSpan.FromSeconds(1))]],
        Context("game", parentId: "base")]].Build();

    private static InputRecord Record(ulong sequence, Key key, bool down) => new(new InputDeviceId(1), sequence, TimeSpan.Zero, new KeyData(key, down, false));
}
