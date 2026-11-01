using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

/// <summary>Owns persistent UI instances with caller-managed synchronization.</summary>
public sealed partial class UiSurface
{
    private Widget? _pendingRebuild;
    private bool _applyingRebuild;

    /// <summary>Queues a fresh root configuration for the next update boundary.</summary>
    /// <param name="root">The passive root declaration.</param>
    public void Rebuild(Widget root)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(root);
        if (ReferenceEquals(root, Root) || ReferenceEquals(root, _pendingRebuild))
        {
            return;
        }

        ValidateRoot(root);
        _pendingRebuild = root;
    }

    /// <summary>Adopts prepared configuration changes without computing layout.</summary>
    public void UpdateTree()
    {
        ThrowIfDisposed();
        if (_applyingRebuild || _pendingRebuild is not { } candidate)
        {
            return;
        }

        _applyingRebuild = true;
        try
        {
            var plan = new RebuildPlan(_rootElement, candidate, validators: _treeValidators);
            plan.Prepare();
            if (_isDisposed)
            {
                return;
            }

            if (ReferenceEquals(_pendingRebuild, candidate))
            {
                _pendingRebuild = null;
            }

            Publish(plan);
        }
        finally
        {
            _applyingRebuild = false;
        }
    }

    internal void RebuildSubtree(UiElement previous, Widget next)
    {
        ThrowIfDisposed();
        if (!ReferenceEquals(previous.Surface, this))
        {
            throw new ArgumentException("The target must be mounted on this surface.", nameof(previous));
        }

        if (_applyingRebuild || _pendingRebuild is not null)
        {
            return;
        }

        ValidateRoot(next);
        _applyingRebuild = true;
        try
        {
            var plan = new RebuildPlan(previous, next, true, _rootElement, _treeValidators);
            plan.Prepare();
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

    private void Publish(RebuildPlan plan)
    {
        bool structureChanged = plan.Removed.Length != 0;
        foreach (ElementUpdate update in plan.Updates)
        {
            structureChanged |= update.ChildrenChanged || update.Previous is null;
            update.Element.Publish(this, update.Next, update.Slots);
            update.Element.UsesTreeValidation = update.UsesTreeValidation;
            if (update.UsesTreeValidation)
            {
                _treeValidators.Add(update.Element);
            }
            else
            {
                _treeValidators.Remove(update.Element);
            }
        }

        foreach (ElementUpdate update in plan.Updates)
        {
            foreach (IReadOnlyList<UiElement> slot in update.Slots)
            {
                foreach (UiElement child in slot)
                {
                    child.AssignParent(update.Element);
                }
            }
        }

        _rootElement = plan.Root;
        TreeRevision = checked(TreeRevision + 1);
        if (structureChanged)
        {
            StructureRevision = checked(StructureRevision + 1);
        }

        foreach (UiElement removed in plan.Removed)
        {
            _treeValidators.Remove(removed);
        }

        List<Exception>? failures = null;
        try
        {
            if (!_isDisposed)
            {
                UpdateHostValidation(plan);
            }

            foreach (ElementUpdate update in plan.Updates)
            {
                if (_isDisposed)
                {
                    break;
                }

                update.Commit?.Invoke();
            }
        }
        catch (Exception error)
        {
            (failures ??= []).Add(error);
        }
        finally
        {
            try
            {
                Unmount(plan.Removed);
            }
            catch (Exception error)
            {
                (failures ??= []).Add(error);
            }
        }

        ThrowFailures(failures);
        foreach (ElementUpdate update in plan.Updates)
        {
            if (_isDisposed)
            {
                break;
            }

            update.Element.NotifyPublished(update.Previous, update.ChildrenChanged);
        }
    }
}
