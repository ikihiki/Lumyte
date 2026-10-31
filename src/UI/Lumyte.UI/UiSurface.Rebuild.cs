using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

/// <summary>Connects a widget tree with caller-managed synchronization.</summary>
public sealed partial class UiSurface
{
    private RebuildCandidate? _pendingRebuild;

    /// <summary>Queues a newly constructed declaration tree for the next tree update boundary.</summary>
    /// <param name="root">A disconnected parentless root; each child slot remains initialized once.</param>
    /// <remarks>Matched type/key or type/position identities retain component-owned state and focus. Building never changes the displayed tree synchronously.</remarks>
    public void Rebuild(Widget root)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(root);
        if (ReferenceEquals(root, _root) || ReferenceEquals(root, _pendingRebuild?.Root))
        {
            return;
        }

        if (root.Owner is not null || root.Surface is not null)
        {
            throw new ArgumentException("A rebuilt tree must be disconnected and parentless.", nameof(root));
        }

        ValidateRebuildKeys(root);
        _pendingRebuild = new(root);
    }

    /// <summary>Applies the latest queued declaration before layout, input or recording; layout also calls this boundary.</summary>
    /// <remarks>Hosts without layout can use this boundary directly. It does not compute geometry.</remarks>
    public void UpdateTree()
    {
        ThrowIfDisposed();
        ApplyRebuild();
    }

    private static void ValidateRebuildKeys(Widget root)
    {
        foreach (Widget node in root.CollectSubtree())
        {
            foreach (WidgetChildren slot in node.ChildCollections)
            {
                var keys = new HashSet<string>(StringComparer.Ordinal);
                foreach (Widget child in slot.Items)
                {
                    if (child.Key is { } key && !keys.Add(key))
                    {
                        throw new ArgumentException("Sibling keys must be unique within each child slot.", nameof(root));
                    }
                }
            }
        }
    }

    private void ApplyRebuild()
    {
        if (_pendingRebuild is not { } candidate)
        {
            return;
        }

        Widget next = candidate.Root;
        var plan = new RebuildPlan(_root, next);
        Dictionary<Widget, Widget> matches = plan.Matches;
        plan.ReconcileComponents();

        if (_isDisposed)
        {
            return;
        }

        PublishDeclaration(candidate, plan);

        next.MarkLayoutDirty();
    }

    private void PublishDeclaration(RebuildCandidate candidate, RebuildPlan plan)
    {
        Widget next = candidate.Root;
        foreach (Widget node in plan.Previous)
        {
            node.SetSurface(null);
        }

        _root = next;
        if (ReferenceEquals(_pendingRebuild, candidate))
        {
            _pendingRebuild = null;
        }

        foreach (Widget node in plan.Next)
        {
            node.SetSurface(this);
        }
    }
}
