using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

/// <summary>Publishes one coherent batch of disjoint reactive subtree proposals.</summary>
public sealed partial class UiSurface
{
    private static int SignalDepth(UiElement element)
    {
        int depth = 0;
        for (UiElement? parent = element.Parent; parent is not null; parent = parent.Parent)
        {
            depth++;
        }

        return depth;
    }

    private void ApplySignalElements()
    {
        var dirty = new HashSet<UiElement>(_signalElements, ReferenceEqualityComparer.Instance);
        _signalElements.Clear();
        var plans = new List<RebuildPlan>();
        var covered = new HashSet<UiElement>(ReferenceEqualityComparer.Instance);
        RebuildPlan? leaves = null;
        try
        {
            foreach (UiElement element in dirty.OrderBy(SignalDepth))
            {
                if (!ReferenceEquals(element.Surface, this) || covered.Contains(element))
                {
                    continue;
                }

                Widget previous = element.Widget;
                Widget? next = element.Signals?.ConstructionChanged == true && previous.Construction is { } construction
                    ? CaptureBuild(construction) : previous.Recreate(new(element));
                if (next is null)
                {
                    Func<Widget>? build = _rootElement.Widget.Construction;
                    if (build is null)
                    {
                        UiDeclarationRecipe recipe = UiDeclarationRecipe.Capture(_rootElement)
                            ?? throw new InvalidOperationException("Reactive handwritten components require a rebuild hook or enclosing construction function.");
                        build = recipe.Build;
                    }

                    Rebuild(CaptureBuild(build));
                    UpdateTree();
                    return;
                }

                next.Construction ??= previous.Construction;
                next.ConstructionReads ??= previous.ConstructionReads;
                ValidateRoot(next);
                if (RebuildPlan.CanBatchLeaf(element, next))
                {
                    if (leaves is null)
                    {
                        leaves = new(_rootElement, _treeValidators);
                        plans.Add(leaves);
                    }

                    leaves.Updates.Add(new(element, element.Widget, next, [], false));
                    covered.Add(element);
                    continue;
                }

                leaves = null;
                var local = new RebuildPlan(element, next, true, _rootElement, _treeValidators);
                plans.Add(local);
                covered.UnionWith(local.Updates.Select(update => update.Element));
                covered.UnionWith(local.Removed);
            }

            if (plans.Count == 0)
            {
                return;
            }

            using RebuildPlan? combined = plans.Count > 1 ? new(_rootElement, plans, _treeValidators) : null;
            RebuildPlan plan = combined ?? plans[0];
            _applyingRebuild = true;
            try
            {
                plan.Prepare();
                PrepareSignalUpdates(plan);
                if (!_isDisposed)
                {
                    Publish(plan);
                }
            }
            finally
            {
                _applyingRebuild = false;
            }
        }
        finally
        {
            foreach (RebuildPlan plan in plans)
            {
                plan.Dispose();
            }
        }
    }
}
