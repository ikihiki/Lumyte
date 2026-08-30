namespace Lumyte.Resources;

public readonly record struct ResourceExecutionLane(string Name)
{
    public static ResourceExecutionLane Default { get; } = new("default");

    public static ResourceExecutionLane Cpu { get; } = new("cpu");

    public static ResourceExecutionLane Graphics { get; } = new("graphics");
}
