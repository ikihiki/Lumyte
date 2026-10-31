using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

internal sealed class UiDeclarationRecipe(Func<object> factory, UiDeclarationRecipe[][] children, bool[] initialized, UiUseEnvironment? contexts)
{
    private readonly Func<object> _factory = factory;
    private readonly UiDeclarationRecipe[][] _children = children;
    private readonly bool[] _initialized = initialized;
    private readonly UiUseEnvironment? _contexts = contexts;

    internal static UiDeclarationRecipe? Capture(Widget root)
    {
        if (root.DeclarationRecipe is { } cached && root.DeclarationRecipeRevision == root.StructureRevision)
        {
            return cached;
        }

        var recipes = new Dictionary<Widget, UiDeclarationRecipe>(ReferenceEqualityComparer.Instance);
        var pending = new System.Collections.Generic.Stack<(Widget Node, bool Complete)>();
        pending.Push((root, false));
        while (pending.TryPop(out (Widget Node, bool Complete) frame))
        {
            Widget node = frame.Node;
            if (node.CompositionFactory is not { } create)
            {
                return null;
            }

            if (node is UiFunctionWidget)
            {
                recipes.Add(node, new(create, [Array.Empty<UiDeclarationRecipe>()], [false], node.ProvidedContexts));
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
            recipes.Add(node, new(create, slots, node.ChildCollections.Select(collection => collection.IsInitialized).ToArray(), node.ProvidedContexts));
        }

        root.DeclarationRecipe = recipes[root];
        root.DeclarationRecipeRevision = root.StructureRevision;
        return root.DeclarationRecipe;
    }

    internal Widget Build()
    {
        var pending = new System.Collections.Generic.Stack<BuildFrame>();
        pending.Push(new(this));
        while (true)
        {
            BuildFrame frame = pending.Peek();
            if (frame.Slot == frame.Recipe._children.Length)
            {
                pending.Pop();
                if (!pending.TryPeek(out BuildFrame? parent))
                {
                    frame.Root.DeclarationRecipe = this;
                    frame.Root.DeclarationRecipeRevision = frame.Root.StructureRevision;
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
            Root.ProvidedContexts = recipe._contexts;
            if (Root.ChildCollections.Count != recipe._children.Length)
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
