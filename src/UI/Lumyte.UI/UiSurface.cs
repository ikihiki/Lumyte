using System.Runtime.ExceptionServices;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

/// <summary>Owns persistent UI instances with caller-managed synchronization.</summary>
public sealed partial class UiSurface : IDisposable
{
    private readonly HashSet<UiElement> _treeValidators = new(ReferenceEqualityComparer.Instance);
    private UiElement _rootElement;
    private bool _isDisposed;

    /// <summary>Initializes a new instance of the <see cref="UiSurface"/> class.</summary>
    /// <param name="root">The passive root declaration.</param>
    public UiSurface(Widget root)
    {
        ValidateRoot(root);
        var plan = new RebuildPlan(null, root);
        _rootElement = plan.Root;
        _applyingRebuild = true;
        try
        {
            plan.Prepare();
            if (!_isDisposed)
            {
                Publish(plan);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
        finally
        {
            _applyingRebuild = false;
        }
    }

    /// <summary>Gets the currently adopted root configuration.</summary>
    public Widget Root => RootElement.Widget;

    /// <summary>Gets the persistent root instance while this surface is alive.</summary>
    public UiElement RootElement
    {
        get
        {
            ThrowIfDisposed();
            return _rootElement;
        }
    }

    internal ulong TreeRevision { get; private set; }

    internal ulong StructureRevision { get; private set; }

    /// <summary>Ends every mounted lifetime without changing construction-time declarations.</summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _pendingRebuild = null;
        _treeValidators.Clear();
        Unmount(CollectElements(_rootElement));
    }

    internal static List<UiElement> CollectElements(UiElement root)
    {
        var result = new List<UiElement>();
        var pending = new System.Collections.Generic.Stack<UiElement>();
        pending.Push(root);
        while (pending.TryPop(out UiElement? element))
        {
            result.Add(element);
            for (int index = element.Children.Count - 1; index >= 0; index--)
            {
                pending.Push(element.Children[index]);
            }
        }

        return result;
    }

    private static void ValidateRoot(Widget root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var pending = new System.Collections.Generic.Stack<Widget>();
        pending.Push(root);
        while (pending.TryPop(out Widget? widget))
        {
            foreach (WidgetChildren slot in widget.ChildCollections)
            {
                HashSet<string>? keys = null;
                foreach (Widget child in slot)
                {
                    if (child.Key is { } key && !(keys ??= new(StringComparer.Ordinal)).Add(key))
                    {
                        throw new ArgumentException("Sibling keys must be unique within a slot.", nameof(root));
                    }

                    pending.Push(child);
                }
            }
        }
    }

    private static void Unmount(IEnumerable<UiElement> elements)
    {
        List<Exception>? failures = null;
        foreach (UiElement element in elements)
        {
            try
            {
                element.Unmount();
            }
            catch (Exception error)
            {
                (failures ??= []).Add(error);
            }
        }

        ThrowFailures(failures);
    }

    private static void ThrowFailures(List<Exception>? failures)
    {
        if (failures is { Count: 1 })
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }

        if (failures is not null)
        {
            throw new AggregateException(failures);
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_isDisposed, this);
}
