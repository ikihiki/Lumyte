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

        BuildFunctions(root, _root);
        ValidateRebuildKeys(root);
        SignalBinding? candidate = PrepareSignals(root);
        if (_pendingRebuild is not { IsApplying: true })
        {
            _pendingRebuild?.DisposeSignals();
        }

        _pendingRebuild = new(root, candidate);
    }

    /// <summary>Applies the latest queued declaration before layout, input or recording; layout also calls this boundary.</summary>
    /// <remarks>Hosts without layout can use this boundary directly. It does not compute geometry.</remarks>
    public void UpdateTree()
    {
        ThrowIfDisposed();
        ProcessSignals(true);
        ApplyRebuild();
        ReconcileVisuals();
    }

    private static void ValidateRebuildKeys(Widget root)
    {
        var sharedKeys = new HashSet<UiSharedElementKey>();
        foreach (Widget node in root.CollectSubtree())
        {
            node.Transitions?.Validate();
            if (node.SharedElementKey is { } sharedKey && (string.IsNullOrEmpty(sharedKey.Value) || !sharedKeys.Add(sharedKey)))
            {
                throw new ArgumentException("Shared visual keys must be valid and unique within a surface.", nameof(root));
            }

            node.ValidateForConnection(root);
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
        if (_rebuildDepth != 0 || _pendingRebuild is not { } candidate)
        {
            return;
        }

        _rebuildDepth++;
        candidate.IsApplying = true;
        try
        {
            ApplyRebuildCore(candidate);
        }
        finally
        {
            _rebuildDepth--;
            candidate.IsApplying = false;
            if (!ReferenceEquals(candidate, _pendingRebuild))
            {
                candidate.DisposeSignals();
            }
        }
    }

    private void ApplyRebuildCore(RebuildCandidate candidate)
    {
        Widget next = candidate.Root;
        SignalBinding? candidateSignals = candidate.Signals;
        ulong activeRevision = _signals?.Revision ?? 0;
        ulong candidateRevision = candidateSignals?.Revision ?? 0;
        ulong connections = _signalConnections;
        var plan = new RebuildPlan(_root, next);
        Dictionary<Widget, Widget> matches = plan.Matches;
        plan.ReconcileComponents();

        if (_isDisposed)
        {
            return;
        }

        if (_signalConnections != connections || (_signals?.Revision ?? 0) != activeRevision || (candidateSignals?.Revision ?? 0) != candidateRevision)
        {
            throw new UiStateUpdateException(new InvalidOperationException("Signal values changed during declaration reconciliation."));
        }

        foreach ((Widget previous, Widget current) in matches)
        {
            current.TransferPresence(previous);
        }

        InvalidateVisualRevision();
        Dictionary<Widget, UiAutomationId> identities = plan.CaptureIdentities();

        SignalBinding? previousSignals = _signals;
        _signals = candidate.TakeSignals();

        _signalConnections++;
        previousSignals?.Dispose();
        if (!_signalUpdatesEnabled)
        {
            _signals?.Pause();
        }

        plan.ReuseGeometry();

        bool layoutUnchanged = !next.IsLayoutDirty && matches.Count == plan.Next.Count;
        Widget? previousFocus = _focusedWidget;
        Widget? nextFocus = previousFocus is not null ? matches.GetValueOrDefault(previousFocus) : null;
        var input = new RebuildInputTransfer(this);
        PublishDeclaration(candidate, plan, identities);
        input.Restore(this, plan.Matches, previousFocus, nextFocus, layoutUnchanged);
    }

    private void PublishDeclaration(RebuildCandidate candidate, RebuildPlan plan, Dictionary<Widget, UiAutomationId> identities)
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
            if (identities.TryGetValue(node, out UiAutomationId identity))
            {
                node.RestoreAutomationIdentity(identity);
            }
        }
    }
}
