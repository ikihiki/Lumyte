using System.Diagnostics;

namespace Lumyte.StateMachines;

/// <summary>Provides optional Activity tracing for transition evaluation and callback failures.</summary>
public static class StateMachineDiagnostics
{
    /// <summary>The source name used to subscribe to state machine diagnostics.</summary>
    public const string ActivitySourceName = "Lumyte.StateMachines";

    /// <summary>Gets the activities.</summary>
    public static ActivitySource Activities { get; } = new(ActivitySourceName);
}
