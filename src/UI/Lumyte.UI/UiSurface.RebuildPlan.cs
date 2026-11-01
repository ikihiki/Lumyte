using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

/// <summary>Owns persistent UI instances with caller-managed synchronization.</summary>
public sealed partial class UiSurface
{
    private sealed class RebuildPlan
    {
        private readonly UiElement[] _validators;

        internal RebuildPlan(UiElement? previous, Widget next, bool retainUninitialized = false, UiElement? surfaceRoot = null, IEnumerable<UiElement>? validators = null)
        {
            var retained = new HashSet<UiElement>(ReferenceEqualityComparer.Instance);
            var unchanged = new HashSet<UiElement>(ReferenceEqualityComparer.Instance);
            UiElement changedRoot = Match(previous, next, retained);
            Root = changedRoot;
            _validators = validators?.ToArray() ?? [];
            var pending = new System.Collections.Generic.Stack<(UiElement Element, Widget Next)>();
            pending.Push((Root, next));
            while (pending.TryPop(out (UiElement Element, Widget Next) item))
            {
                UiElement element = item.Element;
                Widget widget = item.Next;
                if (element.IsMounted && ReferenceEquals(element.Widget, widget))
                {
                    unchanged.Add(element);
                    continue;
                }

                var slots = new IReadOnlyList<UiElement>[widget.ChildCollections.Count];
                bool childrenChanged = slots.Length != element.Slots.Count;
                for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
                {
                    WidgetChildren declared = widget.ChildCollections[slotIndex];
                    IReadOnlyList<UiElement> before = slotIndex < element.Slots.Count ? element.Slots[slotIndex] : Array.Empty<UiElement>();
                    if (retainUninitialized && ReferenceEquals(element, changedRoot) && !declared.IsInitialized && slotIndex < element.Slots.Count)
                    {
                        slots[slotIndex] = before;
                        unchanged.UnionWith(before);
                        continue;
                    }

                    UiElement[]? changed = before.Count == declared.Count ? null : new UiElement[declared.Count];
                    Dictionary<string, UiElement>? keyed = null;
                    foreach (UiElement old in before)
                    {
                        if (old.Widget.Key is { } key)
                        {
                            (keyed ??= new(StringComparer.Ordinal)).Add(key, old);
                        }
                    }

                    for (int index = 0; index < declared.Count; index++)
                    {
                        Widget child = declared[index];
                        UiElement? old = child.Key is { } key ? keyed?.GetValueOrDefault(key)
                            : index < before.Count && before[index].Widget.Key is null ? before[index] : null;
                        UiElement current = Match(old, child, retained);
                        if (changed is null && !ReferenceEquals(before[index], current))
                        {
                            changed = new UiElement[declared.Count];
                            for (int copied = 0; copied < index; copied++)
                            {
                                changed[copied] = before[copied];
                            }
                        }

                        if (changed is not null)
                        {
                            changed[index] = current;
                        }
                    }

                    childrenChanged |= changed is not null;
                    slots[slotIndex] = changed is null ? before : Array.AsReadOnly(changed);
                }

                Updates.Add(new(element, element.IsMounted ? element.Widget : null, widget, slots, childrenChanged));
                for (int slotIndex = slots.Length - 1; slotIndex >= 0; slotIndex--)
                {
                    WidgetChildren declared = widget.ChildCollections[slotIndex];
                    for (int index = declared.Count - 1; index >= 0; index--)
                    {
                        pending.Push((slots[slotIndex][index], declared[index]));
                    }
                }
            }

            var removed = new List<UiElement>();
            if (previous is not null)
            {
                var old = new System.Collections.Generic.Stack<UiElement>();
                old.Push(previous);
                while (old.TryPop(out UiElement? element))
                {
                    if (unchanged.Contains(element))
                    {
                        continue;
                    }

                    if (!retained.Contains(element))
                    {
                        removed.Add(element);
                    }

                    foreach (UiElement child in element.Children)
                    {
                        old.Push(child);
                    }
                }
            }

            Removed = removed.ToArray();
            if (previous?.Parent is { } parent)
            {
                Root = surfaceRoot ?? throw new ArgumentNullException(nameof(surfaceRoot));
                if (!ReferenceEquals(previous, changedRoot))
                {
                    IReadOnlyList<UiElement>[] slots = parent.Slots.Select(slot => slot.Contains(previous)
                        ? (IReadOnlyList<UiElement>)Array.AsReadOnly(slot.Select(child => ReferenceEquals(child, previous) ? changedRoot : child).ToArray()) : slot).ToArray();
                    Updates.Insert(0, new(parent, parent.Widget, parent.Widget, slots, true));
                }
            }
        }

        internal UiElement Root { get; }

        internal List<ElementUpdate> Updates { get; } = [];

        internal UiElement[] Removed { get; }

        internal void Prepare()
        {
            foreach (ElementUpdate update in Updates)
            {
                foreach (IReadOnlyList<UiElement> slot in update.Slots)
                {
                    HashSet<string>? keys = null;
                    foreach (UiElement child in slot)
                    {
                        if (child.Widget.Key is { } key && !(keys ??= new(StringComparer.Ordinal)).Add(key))
                        {
                            throw new ArgumentException("Sibling keys must be unique within a proposed slot.");
                        }
                    }
                }
            }

            var projection = new UiProposedTree(Root, Updates.Select(update => (update.Element, update.Next, update.Slots)));
            foreach (ElementUpdate update in Updates)
            {
                var context = new UiWidgetValidationContext(projection, update.Element);
                update.Next.Validate(context);
                update.UsesTreeValidation = context.UsesTree;
            }

            if (_validators.Length != 0)
            {
                var changed = new HashSet<UiElement>(Updates.Select(update => update.Element), ReferenceEqualityComparer.Instance);
                foreach (UiElement validator in _validators)
                {
                    if (!changed.Contains(validator) && !Removed.Contains(validator))
                    {
                        validator.Widget.Validate(new(projection, validator));
                    }
                }
            }

            foreach (ElementUpdate update in Updates)
            {
                update.Invalidation = update.Element.ConfigurationChanges(update.Next) | (update.ChildrenChanged ? UiInvalidation.Layout : UiInvalidation.None);
                update.Commit = update.Element.Prepare(update.Next, new(update.Next, update.Slots));
            }
        }

        private static UiElement Match(UiElement? previous, Widget next, HashSet<UiElement> retained)
        {
            if (previous is not null && previous.Widget.GetType() == next.GetType() && previous.Widget.Key == next.Key)
            {
                retained.Add(previous);
                return previous;
            }

            return next.CreateInstance();
        }
    }

    private sealed class ElementUpdate(UiElement element, Widget? previous, Widget next, IReadOnlyList<UiElement>[] slots, bool childrenChanged)
    {
        internal UiElement Element { get; } = element;

        internal Widget? Previous { get; } = previous;

        internal Widget Next { get; } = next;

        internal IReadOnlyList<UiElement>[] Slots { get; } = slots;

        internal bool ChildrenChanged { get; } = childrenChanged;

        internal Action? Commit { get; set; }

        internal bool UsesTreeValidation { get; set; }

        internal UiInvalidation Invalidation { get; set; }
    }
}
