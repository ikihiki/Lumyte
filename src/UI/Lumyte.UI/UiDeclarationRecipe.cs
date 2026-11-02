using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

internal sealed class UiDeclarationRecipe(Func<object> factory, UiDeclarationRecipe[][] children, bool[] initialized, bool complete = false)
{
    private readonly Func<object> _factory = factory;
    private readonly UiDeclarationRecipe[][] _children = children;
    private readonly bool[] _initialized = initialized;
    private readonly bool _complete = complete;

    internal static UiDeclarationRecipe? Capture(Widget root)
    {
        if (root.DeclarationRecipe is { } cached)
        {
            return cached;
        }

        var recipes = new Dictionary<Widget, UiDeclarationRecipe>(ReferenceEqualityComparer.Instance);
        var pending = new System.Collections.Generic.Stack<(Widget Node, bool Complete)>();
        pending.Push((root, false));
        while (pending.TryPop(out (Widget Node, bool Complete) frame))
        {
            Widget node = frame.Node;
            if (recipes.ContainsKey(node))
            {
                continue;
            }

            if (node.Construction is { } construct)
            {
                recipes.Add(node, new(() => UiSurface.CaptureBuild(construct), [], [], complete: true));
                continue;
            }

            if (node.CompositionFactory is not { } create)
            {
                return null;
            }

            if (node.RebuildsChildTree)
            {
                recipes.Add(node, new(create, node.ChildCollections.Select(_ => Array.Empty<UiDeclarationRecipe>()).ToArray(), new bool[node.ChildCollections.Count]));
                continue;
            }

            if (!frame.Complete)
            {
                pending.Push((node, true));
                foreach (WidgetChildren collection in node.ChildCollections)
                {
                    foreach (Widget child in collection)
                    {
                        pending.Push((child, false));
                    }
                }

                continue;
            }

            UiDeclarationRecipe[][] slots = node.ChildCollections.Select(collection => collection.Select(child => recipes[child]).ToArray()).ToArray();
            recipes.Add(node, new(create, slots, node.ChildCollections.Select(collection => collection.IsInitialized).ToArray()));
        }

        root.DeclarationRecipe = recipes[root];
        return root.DeclarationRecipe;
    }

    internal static UiDeclarationRecipe? Capture(UiElement root)
    {
        var recipes = new Dictionary<UiElement, UiDeclarationRecipe>(ReferenceEqualityComparer.Instance);
        var pending = new System.Collections.Generic.Stack<(UiElement Element, bool Complete)>();
        pending.Push((root, false));
        while (pending.TryPop(out (UiElement Element, bool Complete) frame))
        {
            UiElement element = frame.Element;
            if (element.Widget.Construction is { } construct)
            {
                recipes.Add(element, new(() => UiSurface.CaptureBuild(construct), [], [], complete: true));
                continue;
            }

            if (element.Widget.CompositionFactory is not { } factory)
            {
                return null;
            }

            if (!frame.Complete)
            {
                pending.Push((element, true));
                foreach (UiElement child in element.Children)
                {
                    pending.Push((child, false));
                }

                continue;
            }

            var slots = new UiDeclarationRecipe[element.Slots.Count][];
            bool[] initialized = new bool[slots.Length];
            for (int index = 0; index < slots.Length; index++)
            {
                WidgetChildren declaration = element.Widget.ChildCollections[index];
                if (declaration.IsPortal)
                {
                    var content = new List<UiDeclarationRecipe>();
                    foreach (Widget child in declaration)
                    {
                        if (Capture(child) is not { } recipe)
                        {
                            return null;
                        }

                        content.Add(recipe);
                    }

                    slots[index] = content.ToArray();
                    initialized[index] = declaration.IsInitialized;
                }
                else
                {
                    slots[index] = element.Slots[index].Select(child => recipes[child]).ToArray();
                    initialized[index] = true;
                }
            }

            recipes.Add(element, new(factory, slots, initialized));
        }

        return recipes[root];
    }

    internal Widget Build()
    {
        var pending = new System.Collections.Generic.Stack<BuildFrame>();
        pending.Push(new(this));
        while (true)
        {
            BuildFrame frame = pending.Peek();
            if (frame.Recipe._complete || frame.Slot == frame.Recipe._children.Length)
            {
                pending.Pop();
                if (!pending.TryPeek(out BuildFrame? parent))
                {
                    frame.Root.DeclarationRecipe = this;
                    return frame.Root;
                }

                parent.Items![parent.Child++] = frame.Root;
                continue;
            }

            WidgetChildren slot = frame.Root.ChildCollections[frame.Slot];
            if (!slot.IsInitialized && frame.Recipe._initialized[frame.Slot])
            {
                UiDeclarationRecipe[] recipes = frame.Recipe._children[frame.Slot];
                frame.Items ??= new Widget[recipes.Length];
                if (frame.Child < recipes.Length)
                {
                    pending.Push(new(recipes[frame.Child]));
                    continue;
                }

                slot.Initialize(frame.Items);
            }

            frame.Slot++;
            frame.Child = 0;
            frame.Items = null;
        }
    }

    private sealed class BuildFrame
    {
        internal BuildFrame(UiDeclarationRecipe recipe)
        {
            Recipe = recipe;
            Root = recipe._factory() as Widget ?? throw new InvalidOperationException("A widget recipe returned another target type.");
            if (!recipe._complete && Root.ChildCollections.Count != recipe._children.Length)
            {
                throw new InvalidOperationException("A component changed its child collection registration shape.");
            }
        }

        internal UiDeclarationRecipe Recipe { get; }

        internal Widget Root { get; }

        internal int Slot { get; set; }

        internal int Child { get; set; }

        internal Widget[]? Items { get; set; }
    }
}
