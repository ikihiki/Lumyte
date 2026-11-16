using System.Numerics;

namespace Lumyte.UI.Automation;

/// <summary>Provides scrolling through the ordinary input lease.</summary>
public sealed partial class UiLocator
{
    /// <summary>Dispatches finite logical wheel pixels at the latest actionable target.</summary>
    /// <param name="delta">The finite logical-pixel delta.</param>
    /// <param name="options">The shared deadline.</param>
    /// <param name="cancellationToken">Cancels waiting.</param>
    /// <returns>The completed ordinary input dispatch.</returns>
    public Task ScrollAsync(Vector2 delta, UiWaitOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (!float.IsFinite(delta.X) || !float.IsFinite(delta.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(delta));
        }

        return Page.ExecuteAsync(
            run => run.WithInputAsync(async lease => await run.Host.RunOnUiThreadAsync(
                surface =>
                    {
                        run.Check();
                        UiAutomationSnapshot snapshot = surface.CaptureAutomationSnapshot();
                        run.Remember(snapshot);
                        UiAutomationNode? node = Single(run, snapshot);
                        if (node is null || !Actionable(node, null, out Vector2 point))
                        {
                            throw run.Failure(UiAutomationFailureKind.TargetChanged, "The scroll target is not actionable.");
                        }

                        UiAutomationId? hit = run.Host is IUiAutomationRoutingHost routing
                            ? routing.ProbeWheel(surface, point) : surface.ProbeWheel(point);
                        UiAutomationNode? destination = snapshot.Nodes.FirstOrDefault(candidate => candidate.Id == hit);
                        while (destination is not null && destination.Id != node.Id)
                        {
                            destination = snapshot.Nodes.FirstOrDefault(candidate => candidate.Id == destination.ParentId);
                        }

                        if (destination is null)
                        {
                            throw run.Failure(UiAutomationFailureKind.TargetChanged, "The scroll target is occluded or outside the input scope.");
                        }

                        return run.Send(lease, surface, new UiWheelInput(point, delta));
                    },
                run.Token)),
            options,
            cancellationToken);
    }
}
