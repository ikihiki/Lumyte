using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

/// <summary>Connects a widget tree with caller-managed synchronization.</summary>
public sealed partial class UiSurface
{
    private void RebuildFunctions(SignalBinding binding)
    {
        var updates = new List<FunctionUpdate>();
        UiSignalRevision revision = SignalRevision;
        _rebuildDepth++;
        try
        {
            foreach (UiFunctionWidget previous in binding.ChangedFunctions())
            {
                var next = (UiFunctionWidget)UiDeclarationRecipe.Capture(previous)!.Build();
                if (next.Key != previous.Key)
                {
                    Rebuild(CaptureBuild(binding.Build));
                    return;
                }

                BuildFunctions(next, previous, previous.Parent?.BuildContexts);
                if (next.CollectSubtree().Any(node => !node.CanRebuildLocally))
                {
                    Rebuild(CaptureBuild(binding.Build));
                    return;
                }

                ValidateRebuildKeys(next);
                var plan = new RebuildPlan(previous, next);
                plan.ReconcileComponents();
                SignalBinding? signals = PrepareSignals(next, previous);
                updates.Add(new(previous, next, plan, signals));
            }

            if (_isDisposed || _pendingRebuild is not null)
            {
                return;
            }

            if (SignalRevision != revision)
            {
                throw new InvalidOperationException("Signal values changed during function reconciliation.");
            }

            PublishFunctions(binding, updates);
        }
        finally
        {
            _rebuildDepth--;
            foreach (FunctionUpdate update in updates)
            {
                update.Signals?.Dispose();
            }
        }
    }

    private void PublishFunctions(SignalBinding binding, List<FunctionUpdate> updates)
    {
        var matches = new Dictionary<Widget, Widget>(ReferenceEqualityComparer.Instance);
        var replaced = new HashSet<Widget>(ReferenceEqualityComparer.Instance);
        var identities = new Dictionary<Widget, UiAutomationId>(ReferenceEqualityComparer.Instance);
        var slots = new Dictionary<WidgetChildren, Dictionary<Widget, Widget>>();
        bool layoutUnchanged = true;
        foreach (FunctionUpdate update in updates)
        {
            RebuildPlan plan = update.Plan;
            plan.ReuseGeometry();
            layoutUnchanged &= !update.Next.IsLayoutDirty && plan.Matches.Count == plan.Next.Count;
            foreach ((Widget previous, Widget current) in plan.Matches)
            {
                matches.Add(previous, current);
                identities.Add(current, previous.AutomationId);
            }

            replaced.UnionWith(plan.Previous);
            if (update.Previous.Parent is { } parent)
            {
                WidgetChildren slot = parent.ChildCollections.Single(collection => collection.Contains(update.Previous));
                if (!slots.TryGetValue(slot, out Dictionary<Widget, Widget>? replacements))
                {
                    slots.Add(slot, replacements = new(ReferenceEqualityComparer.Instance));
                }

                replacements.Add(update.Previous, update.Next);
            }
        }

        SignalBinding?[] signals = updates.Select(update => update.Signals).ToArray();
        Action[] publishSlots = slots.Select(pair => pair.Key.PrepareReplacements(pair.Value)).ToArray();
        Widget? previousFocus = _focusedWidget;
        Widget? nextFocus = previousFocus is null ? null : matches.GetValueOrDefault(previousFocus) ?? (replaced.Contains(previousFocus) ? null : previousFocus);
        var input = new RebuildInputTransfer(this);
        foreach ((Widget previous, Widget current) in matches)
        {
            current.TransferPresence(previous);
        }

        foreach (Widget previous in replaced)
        {
            previous.SetSurface(null);
        }

        foreach (Action publish in publishSlots)
        {
            publish();
        }

        foreach (FunctionUpdate update in updates)
        {
            if (ReferenceEquals(_root, update.Previous))
            {
                _root = update.Next;
            }

            foreach (Widget node in update.Plan.Next)
            {
                node.SetSurface(this);
                if (identities.TryGetValue(node, out UiAutomationId identity))
                {
                    node.RestoreAutomationIdentity(identity);
                }
            }

            if (update.Next.IsLayoutDirty)
            {
                update.Next.MarkLayoutDirty();
            }

            if (!_signalUpdatesEnabled)
            {
                update.Signals?.Pause();
            }
        }

        binding.Replace(replaced, signals);
        _signalConnections++;
        InvalidateVisualRevision();
        input.Restore(this, matches, previousFocus, nextFocus, layoutUnchanged, replaced);
    }

    private sealed record FunctionUpdate(UiFunctionWidget Previous, UiFunctionWidget Next, RebuildPlan Plan, SignalBinding? Signals);
}
