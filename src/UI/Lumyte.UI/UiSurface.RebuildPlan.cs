using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

/// <summary>Connects a widget tree with caller-managed synchronization.</summary>
public sealed partial class UiSurface
{
    private sealed class RebuildPlan
    {
        private readonly Widget _previous;
        private readonly Widget _next;

        internal RebuildPlan(Widget previous, Widget next)
        {
            _previous = previous;
            _next = next;
            Match(previous, next, Matches);
        }

        internal IReadOnlyList<Widget> Previous => _previous.CollectSubtree();

        internal IReadOnlyList<Widget> Next => _next.CollectSubtree();

        internal Dictionary<Widget, Widget> Matches { get; } = new(ReferenceEqualityComparer.Instance);

        internal static void Match(Widget previous, Widget next, Dictionary<Widget, Widget> matches)
        {
            var pending = new System.Collections.Generic.Stack<(Widget Previous, Widget Next)>();
            pending.Push((previous, next));
            while (pending.TryPop(out (Widget Previous, Widget Next) pair))
            {
                (previous, next) = pair;
                if (previous.GetType() != next.GetType() || previous.Key != next.Key)
                {
                    continue;
                }

                matches.Add(previous, next);
                for (int slot = 0; slot < Math.Min(previous.ChildCollections.Count, next.ChildCollections.Count); slot++)
                {
                    if (previous.ChildCollections[slot].IsPortal || next.ChildCollections[slot].IsPortal)
                    {
                        continue;
                    }

                    IReadOnlyList<Widget> before = previous.ChildCollections[slot].Items;
                    IReadOnlyList<Widget> after = next.ChildCollections[slot].Items;
                    Dictionary<string, Widget>? keyed = null;
                    foreach (Widget child in before)
                    {
                        if (child.Key is { } key)
                        {
                            (keyed ??= new(StringComparer.Ordinal)).Add(key, child);
                        }
                    }

                    for (int index = after.Count - 1; index >= 0; index--)
                    {
                        Widget child = after[index];
                        Widget? old = child.Key is { } key ? keyed?.GetValueOrDefault(key) : index < before.Count && before[index].Key is null ? before[index] : null;
                        if (old is not null)
                        {
                            pending.Push((old, child));
                        }
                    }
                }
            }
        }

        internal void ReconcileComponents()
        {
            foreach ((Widget previous, Widget current) in Matches)
            {
                current.ReconcileFrom(previous);
            }
        }

        internal Dictionary<Widget, UiAutomationId> CaptureIdentities()
        {
            var identities = new Dictionary<Widget, UiAutomationId>(ReferenceEqualityComparer.Instance);
            foreach ((Widget previous, Widget current) in Matches)
            {
                identities.Add(current, previous.AutomationId);
            }

            return identities;
        }

        internal void ReuseGeometry()
        {
            foreach ((Widget previous, Widget current) in Matches.Reverse())
            {
                bool childrenMatch = previous.ChildCollections.Count == current.ChildCollections.Count;
                if (childrenMatch)
                {
                    for (int slot = 0; slot < current.ChildCollections.Count && childrenMatch; slot++)
                    {
                        WidgetChildren before = previous.ChildCollections[slot];
                        WidgetChildren after = current.ChildCollections[slot];
                        if (before.IsPortal && after.IsPortal)
                        {
                            continue;
                        }

                        childrenMatch &= before.Count == after.Count;
                        for (int index = 0; index < after.Count && childrenMatch; index++)
                        {
                            childrenMatch &= Matches.TryGetValue(before[index], out Widget? matched) && ReferenceEquals(matched, after[index]) && !after[index].IsLayoutDirty;
                        }
                    }
                }

                if (childrenMatch)
                {
                    current.TryReuseSignalLayout(previous);
                }
            }
        }
    }
}
