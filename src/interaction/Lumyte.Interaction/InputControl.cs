namespace Lumyte.Interaction;

public sealed record InputControl<T>(string Device, string Name)
{
    public override string ToString() => $"{Device}/{Name}";
}
