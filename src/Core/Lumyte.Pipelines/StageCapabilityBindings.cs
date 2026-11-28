using System.Runtime.CompilerServices;

namespace Lumyte.Pipelines;

internal static class StageCapabilityBindings
{
    private static readonly ConditionalWeakTable<IStageCapabilities, object> _bound = new();

    internal static void Admit(IStageCapabilities capabilities)
    {
        if (capabilities is not IStageBackendCapabilities && capabilities is not IStageScopeBinding)
        {
            return;
        }

        try
        {
            _bound.Add(capabilities, new object());
        }
        catch (ArgumentException error)
        {
            throw new InvalidOperationException("A backend scope capability must be fresh for each invocation.", error);
        }
    }
}
