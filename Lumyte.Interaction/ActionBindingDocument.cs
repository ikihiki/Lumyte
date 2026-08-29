using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lumyte.Interaction;

public sealed class ActionBindingDocument
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly Dictionary<string, ActionBindingSlot> slotsById;

    private ActionBindingDocument(ActionBindingSlot[] slots)
    {
        Slots = slots;
        slotsById = slots.ToDictionary(slot => slot.Id, StringComparer.Ordinal);
    }

    public IReadOnlyList<ActionBindingSlot> Slots { get; }

    public static ActionBindingDocument Create(IEnumerable<ActionMap> maps)
    {
        ArgumentNullException.ThrowIfNull(maps);
        var slots = new List<ActionBindingSlot>();
        foreach (ActionMap map in maps)
        {
            foreach (ActionBinding binding in map.Bindings)
            {
                string prefix = ActionBindingIdentity.GetPrefix(map.Name, binding);
                AddBindingSlots(slots, prefix, map.Name, binding);
            }
        }

        return new([.. slots]);
    }

    public RebindingSession BeginRebinding(string slotId) =>
        new(this, GetSlot(slotId));

    public ActionBindingSlot GetSlot(string slotId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slotId);
        return slotsById.TryGetValue(slotId, out ActionBindingSlot? slot)
            ? slot
            : throw new KeyNotFoundException($"Binding slot '{slotId}' was not found.");
    }

    public IReadOnlyList<ActionBindingConflict> FindConflicts(
        ActionBindingSlot slot,
        InputControlDescriptor control)
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(control);
        return Slots
            .Where(candidate =>
                !ReferenceEquals(candidate, slot)
                && candidate.ValueKind == slot.ValueKind
                && candidate.Control == control)
            .Select(candidate => new ActionBindingConflict(slot, candidate, control))
            .ToArray();
    }

    public void Reset(string slotId) =>
        GetSlot(slotId).Control = GetSlot(slotId).DefaultControl;

    public string SaveOverrides()
    {
        ActionBindingDocumentOverride[] overrides =
        [.. Slots
            .Where(slot => slot.IsOverridden)
            .Select(slot => new ActionBindingDocumentOverride(slot.Id, slot.Control))];
        return JsonSerializer.Serialize(overrides, s_jsonOptions);
    }

    public void LoadOverrides(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using Activity? activity = InteractionDiagnostics.Activities.StartActivity(
            "ActionBindingDocument.LoadOverrides");
        ActionBindingDocumentOverride[] overrides =
            JsonSerializer.Deserialize<ActionBindingDocumentOverride[]>(json, s_jsonOptions)
            ?? throw new JsonException("The binding override document must be an array.");
        var resolved = new List<(ActionBindingSlot Slot, InputControlDescriptor Control)>();
        int ignoredCount = 0;
        foreach (ActionBindingDocumentOverride item in overrides)
        {
            if (slotsById.TryGetValue(item.SlotId, out ActionBindingSlot? slot))
            {
                resolved.Add((slot, item.Control));
                continue;
            }

            ignoredCount++;
            activity?.AddEvent(new(
                "BindingOverride.Ignored",
                tags: new ActivityTagsCollection
                {
                    ["interaction.binding.slot_id"] = item.SlotId,
                    ["interaction.binding.ignore_reason"] = "slot_not_found",
                }));
        }

        activity?.SetTag("interaction.binding.applied_count", resolved.Count);
        activity?.SetTag("interaction.binding.ignored_count", ignoredCount);
        if (resolved.Count != 0)
        {
            InteractionDiagnostics.BindingOverrides.Add(
                resolved.Count,
                [new("outcome", "applied")]);
        }

        if (ignoredCount != 0)
        {
            InteractionDiagnostics.BindingOverrides.Add(
                ignoredCount,
                [new("outcome", "ignored")]);
        }
        foreach (ActionBindingSlot slot in Slots)
        {
            slot.Control = slot.DefaultControl;
        }

        foreach ((ActionBindingSlot slot, InputControlDescriptor control) in resolved)
        {
            slot.Control = control;
        }
    }

    internal void SetControl(ActionBindingSlot slot, InputControlDescriptor control)
    {
        if (!slotsById.TryGetValue(slot.Id, out ActionBindingSlot? owned)
            || !ReferenceEquals(slot, owned))
        {
            throw new ArgumentException("The binding slot belongs to another document.", nameof(slot));
        }

        slot.Control = control;
    }

    private static void AddBindingSlots(
        List<ActionBindingSlot> slots,
        string prefix,
        string mapName,
        ActionBinding binding)
    {
        switch (binding)
        {
            case ActionBinding<bool> button:
                AddSlot(slots, prefix, mapName, binding.Action.Id, ActionBindingPart.Primary,
                    InputValueKind.Button, button.TypedControl);
                break;
            case ActionBinding<float> scalar:
                AddSlot(slots, prefix, mapName, binding.Action.Id, ActionBindingPart.Primary,
                    InputValueKind.Scalar, scalar.TypedControl);
                break;
            case ActionBinding<Vector2> vector:
                AddSlot(slots, prefix, mapName, binding.Action.Id, ActionBindingPart.Primary,
                    InputValueKind.Vector2, vector.TypedControl);
                break;
            case Vector2CompositeBinding composite:
                AddSlot(slots, prefix, mapName, binding.Action.Id, ActionBindingPart.Up,
                    InputValueKind.Button, composite.Up);
                AddSlot(slots, prefix, mapName, binding.Action.Id, ActionBindingPart.Down,
                    InputValueKind.Button, composite.Down);
                AddSlot(slots, prefix, mapName, binding.Action.Id, ActionBindingPart.Left,
                    InputValueKind.Button, composite.Left);
                AddSlot(slots, prefix, mapName, binding.Action.Id, ActionBindingPart.Right,
                    InputValueKind.Button, composite.Right);
                break;
        }
    }

    private static void AddSlot<T>(
        List<ActionBindingSlot> slots,
        string prefix,
        string mapName,
        string actionId,
        ActionBindingPart part,
        InputValueKind valueKind,
        InputControl<T> control) =>
        slots.Add(new(
            $"{prefix}:{part}",
            mapName,
            actionId,
            part,
            valueKind,
            InputControlDescriptor.From(control)));

}
