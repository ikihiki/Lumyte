# Lumyte.Input.Actions

Per-player action mapping, contexts, operation recognition and input buffering.

Create an immutable `ActionProfile`, activate contexts and pass new ordered
`InputRecord` batches to `ActionSystem.Advance(records, input.ElapsedTime)`.
Empty batches advance timers. Sequence replay and time reversal are rejected.
Records from several devices must be merged by their global sequence.

Mappings support keys, mouse/controller buttons, sticks and triggers. Contexts
are ordered by priority and activation, with optional exclusive controls.
Action correction includes sensitivity, normalization and smoothing, plus
context-scoped `IActionValueProcessor` pipelines. Recognition supports press,
hold, multi-tap, chords and sequences, plus `IActionRecognizer` extensions.

Consume a completed operation once with `Buffer.TryConsume`. Ordinary release
preserves completed operations; interruptions clear affected entries.

Rebinding captures a candidate without changing the current profile.
`PrepareProfile` builds a candidate, `Confirm` queues an unsaved replacement,
and `Lumyte.Input.Settings` provides save-before-apply integration.

See [the ADR](../../../docs/adr/input/INPUT-0003-actions-and-contexts.md)
and [the runnable sample](../../../samples/Lumyte.Input.Advanced.Sample/README.md).

Declarative definitions use the generated Composition factories:

```csharp
using static Lumyte.Input.Actions.Compose;

ActionProfile profile = Profile()[
    Profile.Actions()[Action("jump", ActionValueKind.Button)],
    Profile.Contexts()[Context("game")],
    Profile.Bindings()[Binding(id: "jump-key", actionId: "jump", contextId: "game", control: InputControl.ForKey(Key.Space))]
].Build();
```

`Build()` validates and snapshots mutable construction nodes. Later edits require
another `Build()` and `ApplyProfile()`. Save the resulting profile through
`InputSettingsConverter.ToSettings`; register it as defaults so persisted user
bindings take precedence. Consumers do not need to install the generator.
