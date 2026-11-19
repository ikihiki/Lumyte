using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

/// <summary>Owns persistent UI instances with caller-managed synchronization.</summary>
public sealed partial class UiSurface
{
    private sealed class RebuildPlan : IDisposable
    {
        private readonly List<(ScrollController Controller, ScrollViewElement Owner)> _scrollReservations = [];
        private readonly Dictionary<UiElement, UiElement> _replacements = new(ReferenceEqualityComparer.Instance);
        private readonly UiElement[] _validators;
        private readonly IReadOnlyDictionary<UiSharedElementKey, UiElement>? _shared;

        internal RebuildPlan(UiElement? previous, Widget next, bool retainUninitialized = false, UiElement? surfaceRoot = null, IEnumerable<UiElement>? validators = null, IReadOnlyDictionary<UiSharedElementKey, UiElement>? shared = null, Action<ElementUpdate>? prepareArguments = null, ElementUpdate? preparedRoot = null, UiUseEnvironment? rootEnvironment = null, bool refreshContent = false, UiProposedTree? baseline = null)
        {
            var retained = new HashSet<UiElement>(ReferenceEqualityComparer.Instance);
            var unchanged = new HashSet<UiElement>(ReferenceEqualityComparer.Instance);
            UiElement changedRoot = Match(previous, next, retained);
            if (previous is not null && !ReferenceEquals(previous, changedRoot))
            {
                _replacements.Add(previous, changedRoot);
            }

            Root = changedRoot;
            Root.HasIndependentContentOwnership = previous?.HasIndependentContentOwnership == true;
            _validators = validators?.ToArray() ?? [];
            _shared = shared;
            var pending = new System.Collections.Generic.Stack<(UiElement Element, Widget Next, UiElement? Parent, UiUseEnvironment? Context)>();
            rootEnvironment ??= (previous?.PreparationSurface ?? surfaceRoot?.PreparationSurface)?._rootEnvironment;
            UiElement? proposedParent = previous is null ? null : baseline?.Parent(previous) ?? previous.Parent;
            UiUseEnvironment? inherited = proposedParent is null ? rootEnvironment
                : baseline is null ? proposedParent.Contexts ?? rootEnvironment : UiUseEnvironment.Overlay(rootEnvironment, baseline.Environment(proposedParent));
            pending.Push((Root, next, proposedParent, inherited));
            try
            {
                while (pending.TryPop(out (UiElement Element, Widget Next, UiElement? Parent, UiUseEnvironment? Context) item))
                {
                    UiElement element = item.Element;
                    Widget widget = item.Next;
                    var environment = UiUseEnvironment.Overlay(item.Context, widget.ProvidedContexts);
                    if (!(refreshContent && ReferenceEquals(element, changedRoot)) && (element.IsMounted || baseline?.Contains(element) == true) && ReferenceEquals(baseline?.Configuration(element) ?? element.Widget, widget) && UiUseEnvironment.Equivalent(baseline?.Contains(element) == true ? UiUseEnvironment.Overlay(rootEnvironment, baseline.Environment(element)) : element.Contexts, environment))
                    {
                        unchanged.Add(element);
                        continue;
                    }

                    ElementUpdate update = ReferenceEquals(element, changedRoot) && preparedRoot is not null
                        ? preparedRoot : new(element, element.IsMounted ? element.Widget : null, widget, [], false);
                    Updates.Add(update);
                    if (element is ScrollViewElement scroll && widget is Compose.Definitions.ScrollView { Controller: { } controller })
                    {
                        controller.Reserve(scroll);
                        _scrollReservations.Add((controller, scroll));
                    }

                    if (!ReferenceEquals(update, preparedRoot))
                    {
                        prepareArguments?.Invoke(update);
                        update.Contexts = environment;
                        update.Content = element.PrepareContent(update.Next, new(item.Parent, environment, update.PreparationCleanup));
                    }

                    widget = update.Next;
                    IReadOnlyList<Widget>[] declaredSlots = update.Content is { } content
                        ? [Array.AsReadOnly(new[] { content.Content })]
                        : widget.ChildCollections.Select((slot, index) => ReferenceEquals(element.Widget, widget) && element.IsMounted && !slot.IsPortal
                            ? (IReadOnlyList<Widget>)element.Slots[index].Select(child => child.Widget).ToArray() : slot).ToArray();
                    var slots = new IReadOnlyList<UiElement>[declaredSlots.Length];
                    bool childrenChanged = slots.Length != element.Slots.Count;
                    for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
                    {
                        IReadOnlyList<Widget> declared = declaredSlots[slotIndex];
                        WidgetChildren? definition = update.Content is null ? widget.ChildCollections[slotIndex] : null;
                        if (definition?.IsPortal == true)
                        {
                            slots[slotIndex] = Array.Empty<UiElement>();
                            continue;
                        }

                        IReadOnlyList<UiElement>[] beforeSlots = baseline?.Slots(element) ?? element.Slots.Select(slot => (IReadOnlyList<UiElement>)slot).ToArray();
                        IReadOnlyList<UiElement> before = slotIndex < beforeSlots.Length ? beforeSlots[slotIndex] : Array.Empty<UiElement>();
                        if (retainUninitialized && ReferenceEquals(element, changedRoot) && !widget.RebuildsChildTree && definition is { IsInitialized: false } && slotIndex < element.Slots.Count)
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
                            if (old is not null && !ReferenceEquals(old, current))
                            {
                                _replacements.TryAdd(old, current);
                            }

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

                    update.Slots = slots;
                    update.ChildrenChanged = childrenChanged;
                    for (int slotIndex = slots.Length - 1; slotIndex >= 0; slotIndex--)
                    {
                        IReadOnlyList<Widget> declared = declaredSlots[slotIndex];
                        if (update.Content is null && widget.ChildCollections[slotIndex].IsPortal)
                        {
                            continue;
                        }

                        for (int index = declared.Count - 1; index >= 0; index--)
                        {
                            pending.Push((slots[slotIndex][index], declared[index], element, environment));
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

                        foreach (UiElement child in baseline?.Children(element) ?? element.Children)
                        {
                            old.Push(child);
                        }
                    }
                }

                Removed = removed.ToArray();
                if ((previous is null ? null : baseline?.Parent(previous) ?? previous.Parent) is { } parent)
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
            catch
            {
                Dispose();
                throw;
            }
        }

        internal RebuildPlan(UiElement root, IEnumerable<UiElement> validators, IReadOnlyDictionary<UiSharedElementKey, UiElement>? shared)
        {
            Root = root;
            _validators = validators.ToArray();
            _shared = shared;
            Removed = [];
        }

        internal RebuildPlan(UiElement root, IReadOnlyList<RebuildPlan> plans, IEnumerable<UiElement> validators, IReadOnlyDictionary<UiSharedElementKey, UiElement>? shared = null)
        {
            Root = root;
            _validators = validators.ToArray();
            _shared = shared;
            var combined = new Dictionary<UiElement, ElementUpdate>(ReferenceEqualityComparer.Instance);
            var replacements = new Dictionary<UiElement, UiElement>(ReferenceEqualityComparer.Instance);
            foreach (RebuildPlan plan in plans)
            {
                foreach ((UiElement before, UiElement after) in plan._replacements)
                {
                    _replacements[before] = after;
                }

                if (!ReferenceEquals(plan.Root, root))
                {
                    Root = plan.Root;
                }

                foreach (ElementUpdate update in plan.Updates)
                {
                    if (ReferenceEquals(update.Previous, update.Next) && update.ChildrenChanged)
                    {
                        for (int slot = 0; slot < update.Slots.Length; slot++)
                        {
                            for (int index = 0; slot < update.Element.Slots.Count && index < Math.Min(update.Slots[slot].Count, update.Element.Slots[slot].Count); index++)
                            {
                                UiElement before = update.Element.Slots[slot][index];
                                UiElement after = update.Slots[slot][index];
                                if (!ReferenceEquals(before, after))
                                {
                                    replacements[before] = after;
                                }
                            }
                        }
                    }

                    if (!combined.TryGetValue(update.Element, out ElementUpdate? existing) || !ReferenceEquals(update.Previous, update.Next) || ReferenceEquals(existing.Previous, existing.Next))
                    {
                        combined[update.Element] = update;
                    }
                }
            }

            foreach (ElementUpdate update in combined.Values)
            {
                bool replaced = false;
                IReadOnlyList<UiElement>[] slots = update.Slots.Select(slot =>
                {
                    if (!slot.Any(replacements.ContainsKey))
                    {
                        return slot;
                    }

                    replaced = true;
                    return (IReadOnlyList<UiElement>)Array.AsReadOnly(slot.Select(child => replacements.GetValueOrDefault(child, child)).ToArray());
                }).ToArray();
                if (replaced)
                {
                    update.Slots = slots;
                    update.ChildrenChanged = true;
                }

                Updates.Add(update);
            }

            Removed = plans.SelectMany(plan => plan.Removed).Distinct().ToArray();
        }

        internal UiElement Root { get; }

        internal bool HasCollectionItemOwnership { get; private set; }

        internal List<ElementUpdate> Updates { get; } = [];

        internal Dictionary<UiElement, IReadOnlyList<UiElement>> CollectionItemRootUpdates { get; } = new(ReferenceEqualityComparer.Instance);

        internal UiElement[] Removed { get; private set; }

        public void Dispose()
        {
            foreach ((ScrollController Controller, ScrollViewElement Owner) reservation in _scrollReservations)
            {
                reservation.Controller.ReleaseReservation(reservation.Owner);
            }

            _scrollReservations.Clear();
            foreach (ElementUpdate update in Updates)
            {
                foreach (Action cleanup in update.PreparationCleanup)
                {
                    cleanup();
                }

                update.PreparationCleanup.Clear();
                update.Signals?.Dispose();
                update.Animations?.Dispose();
            }
        }

        internal static bool CanBatchLeaf(UiElement element, Widget next) => element.Slots.Count == 0
            && element.Widget.ChildCollections.Count == 0 && next.ChildCollections.Count == 0
            && !ReferenceEquals(element.Widget, next)
            && element.Widget.GetType() == next.GetType() && element.Widget.Key == next.Key;

        internal static ElementUpdate PrepareLeaf(UiElement element, Widget next, Action<ElementUpdate> prepareArguments, UiUseEnvironment? rootEnvironment = null)
        {
            var update = new ElementUpdate(element, element.Widget, next, [], false);
            try
            {
                prepareArguments(update);
                update.Contexts = UiUseEnvironment.Overlay(element.Parent?.Contexts ?? rootEnvironment, next.ProvidedContexts);
                update.Content = element.PrepareContent(update.Next, new(element.Parent, update.Contexts, update.PreparationCleanup));
                return update;
            }
            catch
            {
                foreach (Action cleanup in update.PreparationCleanup)
                {
                    cleanup();
                }

                update.PreparationCleanup.Clear();
                update.Signals?.Dispose();
                update.Animations?.Dispose();
                throw;
            }
        }

        internal UiProposedTree Projection() => new(Root, Updates.Select(update => (update.Element, update.Next, update.Slots)));

        internal void RetainReachable(HashSet<UiElement> reachable) => Removed = Removed.Where(element => !reachable.Contains(element)).ToArray();

        internal void FinalizeScrollBindings()
        {
            foreach (ElementUpdate update in Updates)
            {
                if (update.Element is ScrollViewElement scroll && update.Next is Compose.Definitions.ScrollView widget)
                {
                    scroll.FinalizeBinding(widget.Controller);
                }
            }
        }

        internal void Prepare() => Prepare(Root.PreparationSurface ?? throw new InvalidOperationException("Viewport preparation requires its owning surface."));

        internal void Prepare(UiSurface surface)
        {
            // Reserve before validation and preparation callbacks can reenter.
            foreach (ElementUpdate update in Updates)
            {
                if (update.Element is ScrollViewElement scroll && update.Next is Compose.Definitions.ScrollView { Controller: { } controller })
                {
                    controller.Reserve(scroll);
                    _scrollReservations.Add((controller, scroll));
                }
            }

            var changedVisuals = new HashSet<UiElement>(Updates.Select(update => update.Element), ReferenceEqualityComparer.Instance);
            changedVisuals.UnionWith(Removed);
            HashSet<UiSharedElementKey> sharedKeys = _shared is null ? [] : _shared.Where(pair => !changedVisuals.Contains(pair.Value)).Select(pair => pair.Key).ToHashSet();
            foreach (ElementUpdate update in Updates)
            {
                if (update.Next.SharedElementKey is { } key && (string.IsNullOrEmpty(key.Value) || !sharedKeys.Add(key)))
                {
                    throw new ArgumentException("Shared visual keys must be valid and unique within a surface.");
                }
            }

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
            PrepareCollectionOwnership(projection);
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
                var context = new UiElementUpdateContext(surface, update.Next, update.Slots);
                update.ContentCommit = update.Content?.Commit;
                update.Commit = update.Element.Prepare(update.Next, context);
                update.CancelInput = context.InputCancellationRequested;
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

        private void PrepareCollectionOwnership(UiProposedTree projection)
        {
            if (!Root.HasIndependentContentOwnership && !Root.HasCollectionItemOwnership && !Updates.Any(update => update.Content?.CollectionItemRoots is not null))
            {
                return;
            }

            // Inspect prepared content and unchanged siblings together, including local signal batches.
            var elements = new Dictionary<Widget, UiElement>(ReferenceEqualityComparer.Instance);
            var occurrences = new Dictionary<Widget, int>(ReferenceEqualityComparer.Instance);
            var nodes = new List<UiElement>();
            var pending = new System.Collections.Generic.Stack<UiElement>();
            pending.Push(Root);
            while (pending.TryPop(out UiElement? element))
            {
                nodes.Add(element);
                Widget widget = projection.Configuration(element);
                elements.TryAdd(widget, element);
                occurrences[widget] = occurrences.GetValueOrDefault(widget) + 1;
                foreach (UiElement child in projection.Children(element))
                {
                    pending.Push(child);
                }
            }

            var changed = new Dictionary<UiElement, ElementUpdate>(ReferenceEqualityComparer.Instance);
            foreach (ElementUpdate update in Updates)
            {
                changed.Add(update.Element, update);
            }

            var entries = new HashSet<UiElement>(ReferenceEqualityComparer.Instance);
            var freshEntries = new HashSet<UiElement>(ReferenceEqualityComparer.Instance);
            foreach (UiElement element in nodes)
            {
                IReadOnlyList<UiElement> roots = element.CollectionItemRoots;
                bool fresh = element.RequiresFreshContentDescendants || element.FreshItemRealizations;
                if (changed.TryGetValue(element, out ElementUpdate? update) && update.Content is { } content)
                {
                    // Resolve immutable declaration boundaries to this proposal's actual identities.
                    roots = content.CollectionItemRoots is { } declarations
                        ? Array.AsReadOnly(declarations.Select(root => elements[root]).ToArray())
                        : Array.Empty<UiElement>();
                    CollectionItemRootUpdates[element] = roots;
                    fresh = content.RequiresFreshDescendants || content.FreshRealizations;
                }
                else if (roots.Any(_replacements.ContainsKey))
                {
                    // A custom recreation can replace the item entry element itself.
                    roots = Array.AsReadOnly(roots.Select(root => _replacements.GetValueOrDefault(root, root)).ToArray());
                    CollectionItemRootUpdates[element] = roots;
                }

                entries.UnionWith(roots);
                if (fresh)
                {
                    freshEntries.UnionWith(roots);
                }
            }

            if (Root.HasIndependentContentOwnership)
            {
                entries.Add(Root);
            }

            HasCollectionItemOwnership = entries.Count != 0;
            if (entries.Count == 0)
            {
                return;
            }

            var scoped = new System.Collections.Generic.Stack<(UiElement Element, bool InItem, bool Fresh)>();
            scoped.Push((Root, false, Root.HasIndependentContentOwnership));
            while (scoped.TryPop(out (UiElement Element, bool InItem, bool Fresh) item))
            {
                UiElement element = item.Element;
                bool inItem = item.InItem || entries.Contains(element);
                bool fresh = item.Fresh || freshEntries.Contains(element);
                if (inItem)
                {
                    Widget widget = projection.Configuration(element);

                    // An unchanged declaration is legal only on the very same mounted element.
                    int ownMount = element.IsMounted && ReferenceEquals(element.Widget, widget) ? 1 : 0;
                    if (occurrences[widget] != 1 || widget.MountedInstanceCount != ownMount
                        || (fresh && ownMount == 0 && widget.HasBeenMounted))
                    {
                        throw new InvalidOperationException("Realized item descendants must be disjoint and cannot be mounted elsewhere.");
                    }
                }

                foreach (UiElement child in projection.Children(element))
                {
                    scoped.Push((child, inItem, fresh));
                }
            }
        }
    }

    private sealed class ElementUpdate(UiElement element, Widget? previous, Widget next, IReadOnlyList<UiElement>[] slots, bool childrenChanged)
    {
        internal UiElement Element { get; } = element;

        internal Widget? Previous { get; } = previous;

        internal Widget Next { get; set; } = next;

        internal IReadOnlyList<UiElement>[] Slots { get; set; } = slots;

        internal bool ChildrenChanged { get; set; } = childrenChanged;

        internal UiElementContent? Content { get; set; }

        internal List<Action> PreparationCleanup { get; } = [];

        internal UiUseEnvironment? Contexts { get; set; } = element.Contexts;

        internal Action? ContentCommit { get; set; }

        internal Action? Commit { get; set; }

        internal SignalUpdate? Signals { get; set; }

        internal AnimationUpdate? Animations { get; set; }

        internal bool UsesTreeValidation { get; set; }

        internal UiInvalidation Invalidation { get; set; }

        internal bool CancelInput { get; set; }
    }
}
