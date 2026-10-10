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

ActionProfile profile = Profile()[Profile.Contexts()[
    Context("common")[
        Context.Actions()[Action("jump", ActionValueKind.Button)],
        Context.Bindings()[Binding(id: "common-jump", actionId: "jump", control: InputControl.ForKey(Key.Space))],
        Context.Recognitions()[Recognition(id: "jump-press", kind: RecognitionKind.Press,
            actions: ["jump"], window: TimeSpan.FromSeconds(1))]],
    Context("game", parentId: "common")[
        Context.Bindings()[Binding(id: "jump-key", actionId: "jump", control: InputControl.ForKey(Key.J))]]]]
    .Build();
```

`Build()` validates and snapshots mutable construction nodes. Later edits require
another `Build()` and `ApplyProfile()`. Save the resulting profile through
`InputSettingsConverter.ToSettings`; register it as defaults so persisted user
bindings take precedence. Consumers do not need to install the generator.

Contexts own local actions, bindings and recognition definitions. Nested bindings
and recognitions infer their `ContextId`. `parentId` specifies one parent; child
activation uses inherited definitions without activating its parent. Child action
IDs override correction settings (the value kind must remain the same). Local
bindings for an action replace its entire inherited binding group; recognition
IDs override within a context. Use `GetState(contextId, actionId)` and
`Buffer.TryConsume(contextId, recognitionId, now, out action)` for scoped polling
and consumption when parent and child are active together. Priority and exclusivity belong to each context.

Inheritance is resolved when building runtime indices, with independent hysteresis,
recognition and action state per context. `ToSettings` retains `ParentId` and local
actions; inherited copies are not persisted. Rebinding a declaring parent's binding
updates its descendants; declare a child binding to customize only that child.
