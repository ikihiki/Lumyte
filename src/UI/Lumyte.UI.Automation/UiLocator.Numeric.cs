using System.Numerics;

namespace Lumyte.UI.Automation;

/// <summary>Closed numeric commands use the normal live input owner, never declaration setters.</summary>
public sealed partial class UiLocator
{
    /// <summary>Sets a finite scalar through the normal numeric input capability.</summary>
    /// <param name="value">The finite desired value, clamped and snapped by the control.</param>
    /// <param name="options">The shared deadline.</param>
    /// <param name="cancellationToken">Cancels waiting.</param>
    /// <returns>The completed operation.</returns>
    public Task SetValueAsync(double value, UiWaitOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(double.IsFinite(value), true, nameof(value));
        return Page.ExecuteAsync(run => SetNumericAsync(run, value, null), options, cancellationToken);
    }

    /// <summary>Sets both finite ordered endpoints atomically through ordinary input.</summary>
    /// <param name="lower">The desired lower endpoint.</param>
    /// <param name="upper">The desired upper endpoint, at least lower.</param>
    /// <param name="options">The shared deadline.</param>
    /// <param name="cancellationToken">Cancels waiting.</param>
    /// <returns>The completed operation.</returns>
    public Task SetRangeAsync(double lower, double upper, UiWaitOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(lower) || !double.IsFinite(upper) || lower > upper)
        {
            throw new ArgumentOutOfRangeException(nameof(lower));
        }

        return Page.ExecuteAsync(run => SetNumericAsync(run, lower, upper), options, cancellationToken);
    }

    private async ValueTask<bool> SetNumericAsync(UiAutomationRun run, double value, double? upper)
    {
        await PrepareItemTargetAsync(run);
        UiAutomationNode? previous = null;
        UiAutomationActions capability = upper.HasValue ? UiAutomationActions.SetRange : UiAutomationActions.SetValue;
        while (true)
        {
            UiAutomationNode? node = Single(run, await run.SnapshotAsync());
            if (node is not null)
            {
                RequireAction(run, node, capability);
            }

            if (node is not null && previous is not null && SameGeometry(previous, node) && Actionable(node, null, out _))
            {
                bool sent = await run.WithInputAsync(async lease => await run.Host.RunOnUiThreadAsync(
                surface =>
                {
                    run.Check();
                    UiAutomationSnapshot snapshot = surface.CaptureAutomationSnapshot();
                    run.Remember(snapshot);
                    UiAutomationNode? current = Single(run, snapshot);
                    if (current is null || !SameGeometry(node, current) || !Actionable(current, null, out Vector2 point)
                        || run.Probe(surface, point).HitElement != current.Id)
                    {
                        return false;
                    }

                    RequireAction(run, current, capability);
                    if (current.Numeric is { } numeric && (upper.HasValue ? numeric.Lower == value && numeric.Upper == upper : numeric.Value == value))
                    {
                        return true;
                    }

                    if (!run.Send(lease, surface, new UiSetNumericInput(current.Id, value, upper, point)))
                    {
                        throw run.Failure(UiAutomationFailureKind.ActionFailed, "Numeric dispatch was rejected; input will not be retried.");
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
            await run.NextFrameAsync("missing, unstable, disabled, pressed, clipped or occluded numeric target");
        }
    }
}
