namespace Lumyte.StateMachines;

/// <summary>An optional trigger key compared by reference identity.</summary>
public sealed class StateMachineTrigger
{
    private StateMachineTrigger()
    {
    }

    /// <summary>Creates an opaque trigger key distinguished by reference identity.</summary>
    /// <returns>The computed result.</returns>
    public static StateMachineTrigger Create() => new();
}
