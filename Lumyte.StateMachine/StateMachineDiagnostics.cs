using System.Diagnostics;

namespace Lumyte.StateMachine;

public static class StateMachineDiagnostics
{
    public const string ActivitySourceName = "Lumyte.StateMachine";

    public static ActivitySource Activities { get; } = new(ActivitySourceName);
}
