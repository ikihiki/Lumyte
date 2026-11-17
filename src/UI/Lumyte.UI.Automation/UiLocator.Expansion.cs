using System.Numerics;

namespace Lumyte.UI.Automation;

/// <summary>Desired expansion through ordinary leased input.</summary>
public sealed partial class UiLocator
{
    /// <summary>Expands once, without input if already expanded.</summary>
    /// <param name="options">Optional shared deadline.</param>
    /// <param name="cancellationToken">Cancels waiting.</param>
    /// <returns>The completed operation.</returns>
    public Task ExpandAsync(UiWaitOptions? options = null, CancellationToken cancellationToken = default) => Page.ExecuteAsync(run => SetExpansionAsync(run, true), options, cancellationToken);

    /// <summary>Collapses once, without input if already collapsed.</summary>
    /// <param name="options">Optional shared deadline.</param>
    /// <param name="cancellationToken">Cancels waiting.</param>
    /// <returns>The completed operation.</returns>
    public Task CollapseAsync(UiWaitOptions? options = null, CancellationToken cancellationToken = default) => Page.ExecuteAsync(run => SetExpansionAsync(run, false), options, cancellationToken);

    private UiAutomationNode? ExpansionTarget(UiAutomationRun run, UiAutomationSnapshot snapshot, bool desired, out bool satisfied)
    {
        satisfied = false;
        UiAutomationNode? owner = Single(run, snapshot);
        if (owner is null)
        {
            return null;
        }

        RequireAction(run, owner, UiAutomationActions.ExpandCollapse);
        satisfied = owner.IsExpanded == desired;
        return owner;
    }

    private async ValueTask<bool> SetExpansionAsync(UiAutomationRun run, bool desired)
    {
        UiAutomationNode? previous = null;
        while (true)
        {
            UiAutomationNode? node = ExpansionTarget(run, await run.SnapshotAsync(), desired, out bool satisfied);
            if (satisfied)
            {
                return true;
            }

            if (node is not null && previous is not null && SameGeometry(previous, node) && Actionable(node, null, out Vector2 candidate)
                && await run.Host.RunOnUiThreadAsync(surface => run.Probe(surface, candidate).ActivationTarget == node.Id, run.Token))
            {
                bool sent = await run.WithInputAsync(async lease => await run.Host.RunOnUiThreadAsync(
                surface =>
                {
                    run.Check();
                    UiAutomationSnapshot snapshot = surface.CaptureAutomationSnapshot();
                    run.Remember(snapshot);
                    UiAutomationNode? current = ExpansionTarget(run, snapshot, desired, out bool done);
                    if (done)
                    {
                        return true;
                    }

                    if (current is null || !SameGeometry(node, current) || !Actionable(current, null, out Vector2 point)
                        || run.Probe(surface, point).ActivationTarget != current.Id)
                    {
                        return false;
                    }

                    run.Send(lease, surface, new UiPointerInput(lease.PointerId, UiPointerPhase.Move, point));
                    UiAutomationSnapshot moved = surface.CaptureAutomationSnapshot();
                    run.Remember(moved);
                    UiAutomationNode? afterMove = ExpansionTarget(run, moved, desired, out bool nowDone);
                    if (nowDone)
                    {
                        return true;
                    }

                    if (afterMove is null || !SameGeometry(current, afterMove) || !Actionable(afterMove, null, out Vector2 movedPoint)
                        || movedPoint != point || run.Probe(surface, point).ActivationTarget != current.Id)
                    {
                        throw run.Failure(UiAutomationFailureKind.TargetChanged, "The target changed after pointer Move; input will not be retried.");
                    }

                    if (!run.Send(lease, surface, new UiExpandInput(current.Id, desired, point)))
                    {
                        throw run.Failure(UiAutomationFailureKind.ActionFailed, "Expansion dispatch was rejected; input will not be retried.");
                    }

                    return true;
                },
                run.Token));
                if (sent)
                {
                    return true;
                }
            }

            previous = node;
            await run.NextFrameAsync("missing, unstable, disabled, pressed, clipped or occluded selection target");
        }
    }
}
