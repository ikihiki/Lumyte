namespace Lumyte.Interaction;

public sealed class ActionBindingSlot
{
    internal ActionBindingSlot(
        string id,
        string mapName,
        string actionId,
        ActionBindingPart part,
        InputValueKind valueKind,
        InputControlDescriptor defaultControl)
    {
        Id = id;
        MapName = mapName;
        ActionId = actionId;
        Part = part;
        ValueKind = valueKind;
        DefaultControl = defaultControl;
        Control = defaultControl;
    }

    public string Id { get; }

    public string MapName { get; }

    public string ActionId { get; }

    public ActionBindingPart Part { get; }

    public InputValueKind ValueKind { get; }

    public InputControlDescriptor DefaultControl { get; }

    public InputControlDescriptor Control { get; internal set; }

    public bool IsOverridden => Control != DefaultControl;
}
