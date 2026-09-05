namespace Lumyte.Interaction;

public sealed record GestureKind
{
    public static GestureKind Tap { get; } = new("Tap");

    public static GestureKind DoubleTap { get; } = new("DoubleTap");

    public static GestureKind Drag { get; } = new("Drag");

    public static GestureKind Pinch { get; } = new("Pinch");

    public static GestureKind Swipe { get; } = new("Swipe");

    public GestureKind(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    public string Name { get; }

    public override string ToString() => Name;
}
