namespace Lumyte.Interaction;

public sealed record InputControlDescriptor(string Device, string Name)
{
    public static InputControlDescriptor From<T>(InputControl<T> control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return new(control.Device, control.Name);
    }

    public override string ToString() => $"{Device}/{Name}";
}
