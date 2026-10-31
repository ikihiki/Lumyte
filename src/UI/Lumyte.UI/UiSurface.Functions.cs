using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

/// <summary>Connects a widget tree with caller-managed synchronization.</summary>
public sealed partial class UiSurface
{
    internal static void BuildFunctions(Widget root, Widget? previous, UiUseEnvironment? inherited = null)
    {
        var pending = new System.Collections.Generic.Stack<(Widget Next, Widget? Previous, UiUseEnvironment? Context)>();
        pending.Push((root, previous, inherited));
        while (pending.TryPop(out (Widget Next, Widget? Previous, UiUseEnvironment? Context) pair))
        {
            (Widget next, Widget? before, UiUseEnvironment? context) = pair;
            next.BuildContexts = context = UiUseEnvironment.Overlay(context, next.ProvidedContexts);
            if (before?.GetType() != next.GetType() || before.Key != next.Key)
            {
                before = null;
            }

            if (next is UiFunctionWidget function)
            {
                function.BuildContent(before as UiFunctionWidget);
            }

            for (int slot = 0; slot < next.ChildCollections.Count; slot++)
            {
                WidgetChildren after = next.ChildCollections[slot];
                if (after.IsPortal)
                {
                    continue;
                }

                IReadOnlyList<Widget> old = before is not null && slot < before.ChildCollections.Count && !before.ChildCollections[slot].IsPortal
                    ? before.ChildCollections[slot].Items : [];
                Dictionary<string, Widget>? keyed = null;
                for (int index = after.Count - 1; index >= 0; index--)
                {
                    Widget child = after[index];
                    if (child.Key is not null)
                    {
                        keyed ??= old.Where(item => item.Key is not null).ToDictionary(item => item.Key!, StringComparer.Ordinal);
                    }

                    Widget? match = child.Key is { } key ? keyed!.GetValueOrDefault(key) : index < old.Count && old[index].Key is null ? old[index] : null;
                    pending.Push((child, match, context));
                }
            }
        }
    }
}
