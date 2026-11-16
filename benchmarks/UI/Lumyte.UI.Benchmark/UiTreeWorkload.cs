using Lumyte.Signals;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Benchmark;

/// <summary>Bounded public UI fixture.</summary>
public sealed class UiTreeWorkload : IDisposable
{
    private readonly int _count;
    private readonly int _depth;
    private bool _alternate;

    /// <summary>Initializes a new instance of the <see cref="UiTreeWorkload"/> class.</summary>
    /// <param name="count">Actual leaf count.</param>
    /// <param name="depth">Wrapper depth.</param>
    public UiTreeWorkload(int count, int depth = 0)
    {
        _count = count;
        _depth = depth;
        Width = Signal.Create<float?>(10);
        Surface = new UiSurface(Declare());
        Surface.SetViewport(new(512, 4096));
        Surface.UpdateLayout();
    }

    /// <summary>Gets the owned surface.</summary>
    public UiSurface Surface { get; }

    /// <summary>Gets the dirty leaf source.</summary>
    public Signal<float?> Width { get; }

    /// <summary>Gets actual generated function builds.</summary>
    public int Builds { get; private set; }

    /// <summary>Counts actual public child identities without internal test helpers.</summary>
    /// <param name="root">Mounted subtree.</param>
    /// <returns>The exact element count.</returns>
    public static int CountElements(UiElement root)
    {
        int count = 0;
        var pending = new Stack<UiElement>();
        pending.Push(root);
        while (pending.TryPop(out UiElement? current))
        {
            count++;
            foreach (UiElement child in current.Children)
            {
                pending.Push(child);
            }
        }

        return count;
    }

    /// <summary>Constructs passive declarations without mounting.</summary>
    /// <param name="reverse">Reverses keyed siblings.</param>
    /// <param name="structural">Changes every key.</param>
    /// <returns>A bounded tree.</returns>
    public Widget Declare(bool reverse = false, bool structural = false)
    {
        var children = new Widget[_count];
        for (int i = 0; i < children.Length; i++)
        {
            int identity = reverse ? children.Length - 1 - i : i;
            children[i] = Rectangle(key: $"{(structural ? "new" : "leaf")}{identity}", width: 10, height: 8);
        }

        children[reverse ? _count - 1 : 0] = BenchmarkUi.Leaf(Width, () => Builds++, key: structural ? "new-function" : "function");
        Widget root = Stack()[children];
        for (int i = 0; i < _depth; i++)
        {
            root = Panel()[root];
        }

        return root;
    }

    /// <summary>Alternates sibling orders or identity sets and applies a rebuild.</summary>
    /// <param name="structural">Whether identities change.</param>
    public void Reconcile(bool structural)
    {
        _alternate = !_alternate;
        Surface.Rebuild(Declare(_alternate, structural && _alternate));
        Surface.UpdateTree();
    }

    /// <summary>Mutates one signal and applies the generated function update.</summary>
    public void UpdateFunction()
    {
        Width.Value = Width.Peek() == 10 ? 12 : 10;
        Surface.UpdateTree();
    }

    /// <summary>Includes mutation, tree processing and incremental layout.</summary>
    public void LayoutLeaf()
    {
        UpdateFunction();
        Surface.UpdateLayout();
    }

    /// <summary>Alternates viewport width and updates layout.</summary>
    public void LayoutViewport()
    {
        _alternate = !_alternate;
        Surface.SetViewport(new(_alternate ? 514 : 512, 4096));
        Surface.UpdateLayout();
    }

    /// <inheritdoc/>
    public void Dispose() => Surface.Dispose();
}
