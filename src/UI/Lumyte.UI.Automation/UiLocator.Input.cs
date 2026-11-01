using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Input.Abstractions;

namespace Lumyte.UI.Automation;

/// <summary>A lazy chain of semantic queries, resolved against fresh state for every operation and frame.</summary>
public sealed partial class UiLocator
{
    private static bool Visible(UiAutomationNode node) => node.IsVisible && node.Bounds.Width > 0 && node.Bounds.Height > 0;

    private static bool SameGeometry(UiAutomationNode first, UiAutomationNode second) => first.Id == second.Id && first.Bounds == second.Bounds && first.ClippedBounds == second.ClippedBounds;

    private static bool Contains(Rect2D rectangle, Vector2 position) => rectangle.Width > 0 && rectangle.Height > 0 && position.X >= rectangle.X && position.Y >= rectangle.Y && position.X < (double)rectangle.X + rectangle.Width && position.Y < (double)rectangle.Y + rectangle.Height;

    private static bool Actionable(UiAutomationNode node, UiClickOptions? options, out Vector2 point)
    {
        Rect2D clip = node.ClippedBounds;
        point = options?.Position is { } offset ? new(node.Bounds.X + offset.X, node.Bounds.Y + offset.Y) : new(clip.X + (clip.Width / 2), clip.Y + (clip.Height / 2));
        return Visible(node) && node.IsEnabled && node.HitTestVisible && !node.HasActivePress
            && float.IsFinite(point.X) && float.IsFinite(point.Y) && Contains(node.Bounds, point) && Contains(clip, point);
    }

    private static UiModifiers Modifier(Key key) => key switch
    {
        Key.LeftShift or Key.RightShift => UiModifiers.Shift,
        Key.LeftControl or Key.RightControl => UiModifiers.Control,
        Key.LeftAlt or Key.RightAlt => UiModifiers.Alt,
        Key.LeftMeta or Key.RightMeta => UiModifiers.Meta,
        _ => UiModifiers.None,
    };

    private static void SendKey(UiAutomationRun run, IUiAutomationInputLease lease, UiSurface surface, Key key, bool down, UiModifiers modifiers) => run.Send(lease, surface, new UiKeyInput(key, down, Modifiers: modifiers));

    private static void RequireAction(UiAutomationRun run, UiAutomationNode node, UiAutomationActions action)
    {
        if ((node.Actions & action) == 0)
        {
            throw run.Failure(UiAutomationFailureKind.UnsupportedAction, $"The component does not support {action}.");
        }
    }

    private async ValueTask<bool> ClickAsync(UiAutomationRun run, UiClickOptions? options)
    {
        UiAutomationNode? previous = null;
        while (true)
        {
            UiAutomationNode? node = Single(run, await run.SnapshotAsync());
            if (node is not null)
            {
                RequireAction(run, node, UiAutomationActions.Click);
                if (Actionable(node, options, out Vector2 candidatePoint) && previous is not null && SameGeometry(previous, node)
                    && await run.Host.RunOnUiThreadAsync(surface => run.Probe(surface, candidatePoint).ActivationTarget == node.Id, run.Token))
                {
                    bool sent = await run.WithInputAsync(async lease => await run.Host.RunOnUiThreadAsync(
                        surface =>
                        {
                            run.Check();
                            UiAutomationSnapshot snapshot = surface.CaptureAutomationSnapshot();
                            run.Remember(snapshot);
                            UiAutomationNode? current = Single(run, snapshot);
                            if (current is null || !SameGeometry(node, current) || !Actionable(current, options, out Vector2 point)
                                || run.Probe(surface, point).ActivationTarget != current.Id)
                            {
                                return false;
                            }

                            RequireAction(run, current, UiAutomationActions.Click);
                            run.Send(lease, surface, new UiPointerInput(lease.PointerId, UiPointerPhase.Move, point));
                            UiAutomationSnapshot moved = surface.CaptureAutomationSnapshot();
                            run.Remember(moved);
                            UiAutomationNode? afterMove = Single(run, moved);
                            if (afterMove is null || (afterMove.Actions & UiAutomationActions.Click) == 0 || !SameGeometry(current, afterMove) || !Actionable(afterMove, options, out Vector2 movedPoint)
                                || movedPoint != point || run.Probe(surface, point).ActivationTarget != current.Id)
                            {
                                throw run.Failure(UiAutomationFailureKind.TargetChanged, "The target changed after pointer Move; input will not be retried.");
                            }

                            run.Send(lease, surface, new UiPointerInput(lease.PointerId, UiPointerPhase.Down, point));
                            run.Send(lease, surface, new UiPointerInput(lease.PointerId, UiPointerPhase.Up, point));
                            return true;
                        },
                        run.Token));
                    if (sent)
                    {
                        return true;
                    }
                }
            }

            previous = node;
            await run.NextFrameAsync("missing, unstable, disabled, pressed, clipped or occluded click target");
        }
    }

    private async ValueTask<UiAutomationId> FocusAsync(UiAutomationRun run, IUiAutomationInputLease lease)
    {
        while (true)
        {
            UiAutomationId? focused = await run.Host.RunOnUiThreadAsync(
                surface =>
                {
                    run.Check();
                    UiAutomationSnapshot snapshot = surface.CaptureAutomationSnapshot();
                    run.Remember(snapshot);
                    UiAutomationNode? node = Single(run, snapshot);
                    if (node is null)
                    {
                        return (UiAutomationId?)null;
                    }

                    RequireAction(run, node, UiAutomationActions.Focus);
                    if (!node.IsFocusable || !run.CanReceiveKeys(surface))
                    {
                        return null;
                    }

                    if (node.IsFocused && run.CanReceiveKeys(surface))
                    {
                        return node.Id;
                    }

                    SendKey(run, lease, surface, Key.Tab, true, UiModifiers.None);
                    SendKey(run, lease, surface, Key.Tab, false, UiModifiers.None);
                    return null;
                },
                run.Token);
            if (focused is { } id)
            {
                return id;
            }

            await run.NextFrameAsync("Tab focus traversal");
        }
    }

    private async ValueTask<bool> PressAsync(UiAutomationRun run, Key key, UiModifiers modifiers) => await run.WithInputAsync(async lease =>
    {
        while (true)
        {
            UiAutomationId focused = await FocusAsync(run, lease);
            bool sent = await run.Host.RunOnUiThreadAsync(
                surface =>
                {
                    run.Check();
                    UiAutomationSnapshot snapshot = surface.CaptureAutomationSnapshot();
                    run.Remember(snapshot);
                    UiAutomationNode? node = Single(run, snapshot);
                    if (node is null || node.Id != focused || !node.IsFocused || !node.IsFocusable || !run.CanReceiveKeys(surface))
                    {
                        return false;
                    }

                    Key[] held = new[] { Key.LeftControl, Key.LeftAlt, Key.LeftShift, Key.LeftMeta }.Where(candidate => candidate != key && (modifiers & Modifier(candidate)) != 0).ToArray();
                    UiModifiers state = UiModifiers.None;
                    foreach (Key modifier in held)
                    {
                        state |= Modifier(modifier);
                        SendKey(run, lease, surface, modifier, true, state);
                    }

                    if (held.Length != 0)
                    {
                        UiAutomationSnapshot modified = surface.CaptureAutomationSnapshot();
                        run.Remember(modified);
                        UiAutomationNode? afterModifiers = Single(run, modified);
                        if (afterModifiers is null || afterModifiers.Id != focused || !afterModifiers.IsFocused || !afterModifiers.IsFocusable || !run.CanReceiveKeys(surface))
                        {
                            throw run.Failure(UiAutomationFailureKind.TargetChanged, "Focus changed after modifier input; the key will not be sent or retried.");
                        }
                    }

                    state |= Modifier(key);
                    SendKey(run, lease, surface, key, true, state);
                    state &= ~Modifier(key);
                    SendKey(run, lease, surface, key, false, state);
                    for (int i = held.Length - 1; i >= 0; i--)
                    {
                        state &= ~Modifier(held[i]);
                        SendKey(run, lease, surface, held[i], false, state);
                    }

                    return true;
                },
                run.Token);
            if (sent)
            {
                return true;
            }

            await run.NextFrameAsync("focused target changed before key dispatch");
        }
    });
}
