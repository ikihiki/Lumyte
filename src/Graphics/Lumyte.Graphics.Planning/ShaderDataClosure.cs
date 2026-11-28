namespace Lumyte.Graphics.Planning;

internal static class ShaderDataClosure
{
    internal static IReadOnlyList<PlanResourceUse> Collect(PlanResource resource, GraphicsPlanningBudget budget)
    {
        var pending = new Stack<PlanResource>();
        var visited = new HashSet<PlanResource>();
        var uses = new HashSet<PlanResourceUse>();
        pending.Push(resource);
        while (pending.TryPop(out PlanResource? current))
        {
            if (!visited.Add(current))
            {
                continue;
            }

            if (visited.Count > budget.MaximumResourceVersions)
            {
                throw new InvalidOperationException("Shader-data closure exceeds its finite version traversal reservation.");
            }

            foreach (PlanResourceUse use in current.ReferenceUses)
            {
                if (uses.Add(use) && uses.Count > budget.MaximumTableReferences)
                {
                    throw new InvalidOperationException("Shader-data closure exceeds its finite reference reservation.");
                }

                pending.Push(use.Resource);
            }
        }

        return uses.ToArray();
    }
}
