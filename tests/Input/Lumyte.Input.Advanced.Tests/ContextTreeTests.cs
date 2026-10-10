using System.Numerics;
using Lumyte.Input.Actions;
using Lumyte.Input.Settings;
using Xunit;
using static Lumyte.Input.Actions.Compose;

namespace Lumyte.Input.Advanced.Tests;

/// <summary>Verifies context trees and definition references across construction, runtime and persistence.</summary>
public sealed class ContextTreeTests
{
    /// <summary>Verifies nested anonymous contexts inherit definitions and use object-based runtime operations.</summary>
    [Fact]
    public void NestedChildrenInferInheritanceAndUseDefinitionReferences()
    {
        Compose.Definitions.Action jump = Action("jump", ActionValueKind.Button);
        Compose.Definitions.Binding binding = Binding(id: "jump-key", actionId: jump, control: InputControl.ForKey(Key.Space));
        Compose.Definitions.Recognition press = Recognition(id: "press", kind: RecognitionKind.Press, actions: [jump], window: TimeSpan.FromSeconds(1));
        Compose.Definitions.Context leaf = Context();
        Compose.Definitions.Context middle = Context()[leaf];
        Compose.Definitions.Context root = Context(id: "common")[Context.Actions()[jump], Context.Bindings()[binding], Context.Recognitions()[press], middle];
        ActionProfile profile = Profile()[root].Build();
        Assert.Equal("common", profile.Contexts[1].ParentId);
        Assert.Equal(middle.Identifier, profile.Contexts[2].ParentId);
        Assert.Equal(middle.Identifier, leaf.Build().ParentId);
        Assert.Equal("@context/0/0/0", leaf.Identifier);
        var system = new ActionSystem(profile);
        system.SetValueProcessors(leaf, jump, []);
        system.SetRecognizers(leaf, []);
        system.ActivateContext(leaf);
        system.Advance(new InputRecord[] { Record(1, Key.Space, true) }, TimeSpan.Zero);
        Assert.Equal(1, system.GetState(leaf, jump).Value.X);
        Assert.Equal(1, system.GetState(jump).Value.X);
        Assert.True(system.Buffer.TryConsume(leaf, press, TimeSpan.Zero, out RecognizedAction? result));
        Assert.Equal(leaf.Identifier, result!.ContextId);
        RebindSession session = system.BeginRebind(binding, new RebindOptions(TimeSpan.FromSeconds(1)));
        system.Advance(new InputRecord[] { Record(2, Key.J, true) }, TimeSpan.Zero);
        Assert.Equal(InputControl.ForKey(Key.J), session.Candidate);
        session.Cancel();
        system.DeactivateContext(leaf);
        system.Advance(ReadOnlyMemory<InputRecord>.Empty, TimeSpan.Zero);
        Assert.Equal(Vector2.Zero, system.GetState(leaf, jump).Value);
    }

    /// <summary>Verifies anonymous paths are deterministic and persisted definitions accept original handles.</summary>
    [Fact]
    public void AnonymousIdentifiersSurviveSettingsRoundTrip()
    {
        Compose.Definitions.Action jump = Action("jump", ActionValueKind.Button);
        Compose.Definitions.Context child = Context();
        Compose.Definitions.Context root = Context()[Context.Actions()[jump], Context.Bindings()[Binding(id: "key", actionId: jump, control: InputControl.ForKey(Key.Space))], child];
        ActionProfile first = Profile()[root].Build();
        ActionProfile second = Profile()[Context()[Context()]].Build();
        Assert.Equal(first.Contexts.Select(context => context.Id), second.Contexts.Select(context => context.Id));
        ActionProfile restored = InputSettingsConverter.BuildProfile(InputSettingsConverter.ToSettings(first));
        var system = new ActionSystem(restored);
        system.ActivateContext(child);
        system.Advance(new InputRecord[] { Record(1, Key.Space, true) }, TimeSpan.Zero);
        Assert.Equal(1, system.GetState(child, jump).Value.X);
        Assert.Equal(root.Identifier, restored.Contexts[1].ParentId);
    }

    /// <summary>Verifies object references must point to actual definitions in the context's ancestry.</summary>
    [Fact]
    public void ForeignActionWithMatchingNameIsRejectedWithoutCommittingIdentifiers()
    {
        Compose.Definitions.Action declared = Action("jump", ActionValueKind.Button);
        Compose.Definitions.Action foreign = Action("jump", ActionValueKind.Button);
        Compose.Definitions.Binding binding = Binding(id: "key", actionId: foreign, control: InputControl.ForKey(Key.Space));
        Compose.Definitions.Context context = Context()[Context.Actions()[declared], Context.Bindings()[binding]];
        Compose.Definitions.Profile node = Profile()[context];
        Assert.Throws<ArgumentException>(() => node.Build());
        Assert.Throws<InvalidOperationException>(() => context.Identifier);
        binding.ActionId = declared;
        Assert.Single(node.Build().Contexts);
        Assert.Equal("@context/0", context.Identifier);
    }

    /// <summary>Verifies flat definitions can refer to context objects, including an anonymous parent.</summary>
    [Fact]
    public void FlatReferencesResolveContextAndParentObjects()
    {
        Compose.Definitions.Action jump = Action("jump", ActionValueKind.Button);
        Compose.Definitions.Context parent = Context()[Context.Actions()[jump]];
        Compose.Definitions.Context child = Context(parent: parent);
        Compose.Definitions.Binding binding = Binding(id: "key", actionId: jump, context: child, control: InputControl.ForKey(Key.Space));
        Compose.Definitions.Recognition recognition = Recognition(id: "press", kind: RecognitionKind.Press, actions: [jump], context: child, window: TimeSpan.FromSeconds(1));
        ActionProfile profile = Profile()[Profile.Bindings()[binding], Profile.Recognitions()[recognition], parent, child].Build();
        Assert.Equal(parent.Identifier, profile.Contexts[1].ParentId);
        Assert.Equal(child.Identifier, profile.Bindings[0].ContextId);
        var system = new ActionSystem(profile);
        system.ActivateContext(child);
        system.Advance(new InputRecord[] { Record(1, Key.Space, true) }, TimeSpan.Zero);
        Assert.True(system.Buffer.TryConsume(recognition, TimeSpan.Zero, out _));
    }

    /// <summary>Verifies tree cycles, shared children and contradictory parent references are rejected.</summary>
    /// <param name="failure">The invalid tree case.</param>
    [Theory]
    [InlineData("cycle")]
    [InlineData("shared")]
    [InlineData("parent")]
    [InlineData("foreign-parent")]
    public void InvalidContextTreesAreRejected(string failure)
    {
        Compose.Definitions.Context child = Context();
        Compose.Definitions.Context parent = Context()[child];
        if (failure == "cycle")
        {
            child.Children = [parent];
        }
        else if (failure == "parent")
        {
            child.ParentId = "wrong";
        }
        else if (failure == "foreign-parent")
        {
            child.Parent = Context();
        }

        Compose.Definitions.Profile node = failure == "shared" ? Profile()[parent, Context()[child]] : Profile()[parent];
        Assert.Throws<ArgumentException>(() => node.Build());
    }

    /// <summary>Verifies committed identifiers keep typed handles valid until a new successful build.</summary>
    [Fact]
    public void DefinitionEditsDoNotChangeCommittedRuntimeHandles()
    {
        Compose.Definitions.Action jump = Action("jump", ActionValueKind.Button);
        Compose.Definitions.Context child = Context();
        Compose.Definitions.Context root = Context()[Context.Actions()[jump], Context.Bindings()[Binding(id: "key", actionId: jump, control: InputControl.ForKey(Key.Space))], child];
        var system = new ActionSystem(Profile()[root].Build());
        jump.Id = "renamed";
        child.Id = "other";
        system.ActivateContext(child);
        system.Advance(new InputRecord[] { Record(1, Key.Space, true) }, TimeSpan.Zero);
        Assert.Equal(1, system.GetState(child, jump).Value.X);
        Assert.Equal("jump", jump.Identifier);
        Assert.Equal("@context/0/0", child.Identifier);
    }

    private static InputRecord Record(ulong sequence, Key key, bool down) => new(new InputDeviceId(1), sequence, TimeSpan.Zero, new KeyData(key, down, false));
}
