using System.Numerics;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Collections;

/// <summary>Owns an accepted lazy hierarchy independently for each mounted tree.</summary>
/// <typeparam name="T">The node type.</typeparam>
public class TreeViewElement<T> : UiElement<Compose.Definitions.TreeView<T>>, IUiItemViewport
    where T : notnull
{
    private TreeModel<T>? _model;
    private TreeModel<T>.Node[] _rows = [];
    private HashSet<string> _expanded = new(StringComparer.Ordinal);
    private HashSet<string>? _requestedExpansion;
    private bool _refreshing;
    private bool _preparing;
    private long _request;
    private bool _notifyExpansion;
    private int _ownerThread;
    private long _generation;
    private string[]? _requestedPath;
    private long _pathPositionVersion;
    private ScrollController? _controller;
    private (string Key, long Request)? _navigationFocus;

    /// <summary>Initializes a new instance of the <see cref="TreeViewElement{T}"/> class.</summary>
    /// <param name="widget">The declaration.</param>
    protected internal TreeViewElement(Compose.Definitions.TreeView<T> widget)
        : base(widget)
    {
    }

    /// <summary>Gets a detached snapshot of accepted expansion keys.</summary>
    public IReadOnlyList<string> ExpandedKeys => Array.AsReadOnly(_expanded.Order(StringComparer.Ordinal).ToArray());

    /// <summary>Gets accepted selection, including collapsed known nodes.</summary>
    public string? SelectedKey { get; private set; }

    /// <summary>Gets accepted visible preorder keys without discovering children.</summary>
    public IReadOnlyList<string> VisibleKeys => Array.AsReadOnly(_rows.Select(node => node.Key).ToArray());

    /// <inheritdoc/>
    protected override string? AutomationSelectedKey => SelectedKey;

    /// <inheritdoc/>
    protected override UiAutomationActions AutomationActions => UiAutomationActions.SelectOption;

    /// <summary>Requests expansion of a known node at the next atomic layout boundary.</summary>
    /// <remarks>Tree mutations reject reentry throughout unpublished surface preparation, including templates and sibling callbacks.</remarks>
    /// <param name="key">The discovered key.</param>
    /// <param name="expanded">The desired expansion.</param>
    /// <returns>Whether the request is available.</returns>
    public bool SetExpanded(string key, bool expanded)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        RequireIdleOwner();

        if (Surface is null || _model is null || !_rows.Any(row => row.Key == key) || !_model.Known.TryGetValue(key, out TreeModel<T>.Node? node) || !node.CanExpand || !node.Enabled || !node.Visible)
        {
            return false;
        }

        var requested = new HashSet<string>(_requestedExpansion ?? _expanded, StringComparer.Ordinal);
        bool changed = expanded ? requested.Add(key) : requested.Remove(key);
        if (changed)
        {
            _requestedExpansion = requested;
            _request++;
            InvalidateLayout();
        }

        return true;
    }

    /// <summary>Reveals a known key by its parent index, or an unknown key through an explicit path resolver.</summary>
    /// <param name="key">The ordinal key; unknown keys require KeyPathSelector.</param>
    /// <returns>Whether the underlying viewport accepted the request.</returns>
    public bool BringIntoView(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        RequireIdleOwner();

        if (_model is null)
        {
            return false;
        }

        if (!_model.Known.ContainsKey(key))
        {
            if (Widget.KeyPathSelector is not { } selector)
            {
                return false;
            }

            if (_preparing)
            {
                throw new InvalidOperationException("Path resolution cannot reenter.");
            }

            _preparing = true;
            string[] resolved;
            try
            {
                resolved = CopyPath(selector(key));
            }
            finally
            {
                _preparing = false;
            }

            if (resolved[^1] != key)
            {
                throw new ArgumentException("The path resolver must end in the requested key.", nameof(key));
            }

            return QueuePath(resolved, null);
        }

        var path = new List<string>();
        for (string? current = key; current is not null; current = _model.Known[current].Parent)
        {
            path.Add(current);
        }

        path.Reverse();
        return QueuePath(path, null);
    }

    /// <summary>Queues explicit root-to-target discovery and reveal at one atomic layout boundary.</summary>
    /// <param name="path">Nonempty ordinal unique keys, at most 257 nodes.</param>
    /// <returns>Whether a mounted model accepts the request.</returns>
    public bool BringPathIntoView(IReadOnlyList<string> path) => QueuePath(path, null);

    /// <summary>Queues a path using enqueue-time model and position request generations.</summary>
    /// <param name="dispatcher">The borrowed owner dispatcher.</param>
    /// <param name="path">The explicit root-to-target path.</param>
    /// <param name="cancellationToken">Cancels before owner execution.</param>
    /// <returns>The dispatcher completion; adoption occurs at layout.</returns>
    public ValueTask BringPathIntoViewAsync(IUiDispatcher dispatcher, IReadOnlyList<string> path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        RequireIdleOwner(allowOtherThread: true);
        string[] copy = CopyRequestPath(path);
        cancellationToken.ThrowIfCancellationRequested();
        long generation = Volatile.Read(ref _generation);
        ScrollController? controller = Volatile.Read(ref _controller);
        long position = controller?.ReservePositionRequest() ?? 0;
        return dispatcher.InvokeAsync(
            () =>
            {
                if (generation != _generation || controller is null || !ReferenceEquals(controller, _controller)
                    || position != controller.PositionRequestVersion || !QueuePath(copy, position))
                {
                    throw new InvalidOperationException("The hierarchy or position request changed.");
                }
            },
            cancellationToken);
    }

    /// <summary>Queues desired expansion against the model generation present at enqueue.</summary>
    /// <param name="dispatcher">The borrowed owner dispatcher.</param>
    /// <param name="key">The discovered parent key.</param>
    /// <param name="expanded">The desired state.</param>
    /// <param name="cancellationToken">Cancels before owner execution.</param>
    /// <returns>The dispatcher completion.</returns>
    public ValueTask SetExpandedAsync(IUiDispatcher dispatcher, string key, bool expanded, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentException.ThrowIfNullOrEmpty(key);
        RequireIdleOwner(allowOtherThread: true);
        long generation = Volatile.Read(ref _generation);
        return dispatcher.InvokeAsync(
            () =>
            {
                if (generation != _generation || !SetExpanded(key, expanded))
                {
                    throw new InvalidOperationException("The hierarchy changed or the expansion key is unavailable.");
                }
            },
            cancellationToken);
    }

    /// <summary>Queues expansion of a discovered parent.</summary>
    /// <param name="dispatcher">The borrowed owner dispatcher.</param>
    /// <param name="key">The discovered key.</param>
    /// <param name="cancellationToken">Cancels before execution.</param>
    /// <returns>The dispatcher completion.</returns>
    public ValueTask ExpandAsync(IUiDispatcher dispatcher, string key, CancellationToken cancellationToken = default) => SetExpandedAsync(dispatcher, key, true, cancellationToken);

    /// <summary>Queues collapse of a discovered parent.</summary>
    /// <param name="dispatcher">The borrowed owner dispatcher.</param>
    /// <param name="key">The discovered key.</param>
    /// <param name="cancellationToken">Cancels before execution.</param>
    /// <returns>The dispatcher completion.</returns>
    public ValueTask CollapseAsync(IUiDispatcher dispatcher, string key, CancellationToken cancellationToken = default) => SetExpandedAsync(dispatcher, key, false, cancellationToken);

    internal override bool PrepareViewportContent()
    {
        if ((_requestedExpansion is null && _requestedPath is null) || (PreparationSurface?.PreparedViewportContent(this)?.PreparationState is long staged && staged == _request))
        {
            return false;
        }

        _refreshing = true;
        try
        {
            PreparationSurface!.RefreshElementContent(this);
        }
        catch
        {
            _requestedPath = null;
            _requestedExpansion = null;
            throw;
        }
        finally
        {
            _refreshing = false;
        }

        return true;
    }

    /// <inheritdoc/>
    protected override UiElementContent? BuildContent(Widget next, UiElementBuildContext context)
    {
        if (_preparing)
        {
            throw new InvalidOperationException("Hierarchy preparation cannot reenter.");
        }

        _preparing = true;
        try
        {
            var declaration = (Compose.Definitions.TreeView<T>)next;
            TreeModel<T> model = _refreshing ? _model!.Copy() : TreeModel<T>.Create(declaration);
            HashSet<string> expanded = _refreshing ? new(_requestedExpansion ?? _expanded, StringComparer.Ordinal) : ValidateExpansion(declaration.ExpandedKeys);
            string[]? path = _refreshing && _pathPositionVersion == _controller?.PositionRequestVersion ? _requestedPath : null;
            if (path is not null)
            {
                model.RevealPath(declaration, path, expanded);
            }

            TreeModel<T>.Node[] rows = model.Flatten(declaration, expanded);
            long request = _request;
            bool modelReplacement = !_refreshing;
            bool notify = _refreshing && !_expanded.SetEquals(expanded);
            string? selected = _refreshing ? SelectedKey : declaration.SelectedKey;
            if (modelReplacement && selected is not null && !model.Known.ContainsKey(selected))
            {
                selected = null;
            }

            long focusRequest = Surface?.FocusRequest ?? 0;
            UiElement? focused = Surface?.FocusedElement ?? Surface?.DeferredViewportFocus;
            while (focused is not null && focused is not CollectionItemElement)
            {
                focused = focused.Parent;
            }

            string? restoreKey = focused is CollectionItemElement focusedRow && _model?.Known.ContainsKey(focusedRow.Widget.Key!) == true ? focusedRow.Widget.Key : null;
            while (restoreKey is not null && !rows.Any(node => node.Key == restoreKey && node.Enabled))
            {
                restoreKey = _model!.Known[restoreKey].Parent;
            }

            var list = new Compose.Definitions.ListBox<TreeModel<T>.Node>
            {
                Items = rows,
                KeySelector = node => node.Key,
                ItemTemplate = node => CreateRow(declaration, node),
                ItemEnabled = node => node.Enabled,
                ConfigureItem = (node, row) => ConfigureRow(declaration, node, row),
                SelectedKey = selected,
                SelectionValue = () => SelectedKey,
                ItemInvoked = Select,
                Virtualization = declaration.Virtualization,
                Height = declaration.Height,
                Role = UiRole.Group,
                DeferredHierarchy = true,
                RestoreHierarchyFocusKey = restoreKey,
                RequestedRevealKey = path?[^1],
                RevealPositionVersion = _pathPositionVersion,
            };
            return new UiElementContent(list, () =>
            {
                _ownerThread = Environment.CurrentManagedThreadId;
                if (modelReplacement)
                {
                    Interlocked.Increment(ref _generation);
                }

                Volatile.Write(ref _controller, (Children.FirstOrDefault()?.Children.FirstOrDefault() as ScrollViewElement)?.Controller);
                _model = model;
                _rows = rows;
                _expanded = expanded;
                SelectedKey = selected;
                if (_request == request)
                {
                    _requestedExpansion = null;
                    _requestedPath = null;
                }

                _notifyExpansion |= notify;
                bool navigating = _navigationFocus is not null;
                if (_navigationFocus is { } navigation && Children.FirstOrDefault() is ListBoxElement<TreeModel<T>.Node> navigationList)
                {
                    CollectionItemElement? navigationTarget = navigationList.Options.FirstOrDefault(item => item.Widget.Key == navigation.Key);
                    if (navigationTarget is not null)
                    {
                        Surface?.QueueItemFocus(navigationTarget, navigation.Request);
                        _navigationFocus = null;
                    }
                }

                if (!navigating && restoreKey is not null && Children.FirstOrDefault() is ListBoxElement<TreeModel<T>.Node> adoptedList)
                {
                    CollectionItemElement? target = adoptedList.Options.FirstOrDefault(item => item.Widget.Key == restoreKey);
                    if (target is not null)
                    {
                        Surface?.QueueItemFocus(target, focusRequest);
                    }
                }
            })
            {
                PreparationState = request,
            };
        }
        finally
        {
            _preparing = false;
        }
    }

    /// <inheritdoc/>
    protected override Action? PrepareUpdate(Compose.Definitions.TreeView<T> next, UiElementUpdateContext context) => () =>
    {
        if (_notifyExpansion)
        {
            _notifyExpansion = false;
            Widget.OnExpandedChanged?.Invoke(ExpandedKeys);
        }
    };

    /// <inheritdoc/>
    protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
    {
        Vector2 size = LayoutChild(Children[0], constraints);
        SetChildOffset(Children[0], Vector2.Zero);
        return constraints.Constrain(size);
    }

    private static string[] CopyPath(IReadOnlyList<string> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        int count = path.Count;
        if (count is < 1 or > TreeModel<T>.MaximumDepth + 1)
        {
            throw new ArgumentException("Paths require 1 to 257 keys.", nameof(path));
        }

        string[] copy = new string[count];
        for (int index = 0; index < copy.Length; index++)
        {
            copy[index] = path[index];
        }

        ValidateExpansion(copy);
        return copy;
    }

    private static HashSet<string> ValidateExpansion(IReadOnlyList<string> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        int count = source.Count;
        if (count < 0 || count > TreeModel<T>.MaximumNodes)
        {
            throw new ArgumentException("Too many expansion keys.", nameof(source));
        }

        var result = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < count; index++)
        {
            string key = source[index];
            if (string.IsNullOrEmpty(key) || key.Length > 256 || !result.Add(key))
            {
                throw new ArgumentException("Expansion keys must be bounded and ordinal unique.", nameof(source));
            }
        }

        return result;
    }

    private void RequireIdleOwner(bool allowOtherThread = false)
    {
        if (_preparing || PreparationSurface?.HasUnpublishedViewportPreparation == true
            || (!allowOtherThread && _ownerThread != 0 && _ownerThread != Environment.CurrentManagedThreadId))
        {
            throw new InvalidOperationException("Tree operations require an idle owner outside unpublished surface preparation.");
        }
    }

    private string[] CopyRequestPath(IReadOnlyList<string> path)
    {
        _preparing = true;
        try
        {
            return CopyPath(path);
        }
        finally
        {
            _preparing = false;
        }
    }

    private bool QueuePath(IReadOnlyList<string> path, long? position)
    {
        RequireIdleOwner();
        string[] copy = CopyRequestPath(path);

        if (_model is null || _controller is null || Surface is null)
        {
            return false;
        }

        _requestedPath = copy;
        _pathPositionVersion = position ?? _controller.ReservePositionRequest();
        _request++;
        InvalidateLayout();
        return true;
    }

    private Widget CreateRow(Compose.Definitions.TreeView<T> declaration, TreeModel<T>.Node node)
    {
        Widget content = declaration.ItemTemplate!(node.Item) ?? throw new InvalidOperationException("A node template returned null.");
        content.ValidateFreshItemRoot();
        content.AddModifier(new(UiBoxModifierKind.Padding, (node.Depth * declaration.Indentation) + 16));
        return content;
    }

    private void ConfigureRow(Compose.Definitions.TreeView<T> declaration, TreeModel<T>.Node node, CollectionItem row)
    {
        row.Role = UiRole.TreeItem;
        row.TreeItem = new(node.Depth, node.Parent);
        row.SelectionOwner = () => AutomationId;
        row.GlyphStart = node.Depth * declaration.Indentation;
        if (node.CanExpand)
        {
            row.Expanded = () => _expanded.Contains(node.Key);
            row.Expand = expanded => SetExpanded(node.Key, expanded);
        }

        row.HierarchyNavigate = NavigateHierarchy;
    }

    private UiInputResult NavigateHierarchy(CollectionItemElement row, in UiInputContext context)
    {
        RequireIdleOwner();
        if (!context.IsFocused || context.Input is not UiKeyInput { IsDown: true, IsRepeat: false, Modifiers: UiModifiers.None } input
            || input.Key is not (Lumyte.Input.Abstractions.Key.ArrowLeft or Lumyte.Input.Abstractions.Key.ArrowRight))
        {
            return UiInputResult.Unhandled;
        }

        string key = row.Widget.Key!;
        if (_model is null || !_model.Known.TryGetValue(key, out TreeModel<T>.Node? node))
        {
            return UiInputResult.Handled;
        }

        if (input.Key == Lumyte.Input.Abstractions.Key.ArrowRight)
        {
            if (node.CanExpand && !_expanded.Contains(key))
            {
                SetExpanded(key, true);
            }
            else
            {
                FocusKey(_rows.FirstOrDefault(candidate => candidate.Parent == key && candidate.Enabled)?.Key);
            }
        }
        else if (_expanded.Contains(key))
        {
            SetExpanded(key, false);
        }
        else
        {
            FocusKey(node.Parent);
        }

        return UiInputResult.Handled;
    }

    private void FocusKey(string? key)
    {
        RequireIdleOwner();
        if (key is null)
        {
            return;
        }

        var list = Children.FirstOrDefault() as ListBoxElement<TreeModel<T>.Node>;
        CollectionItemElement? item = list?.Options.FirstOrDefault(candidate => candidate.Widget.Key == key);
        if (item is not null)
        {
            Surface?.TryFocus(item);
        }
        else if (BringIntoView(key) && Surface is { } surface)
        {
            _navigationFocus = (key, surface.ReserveItemFocusRequest());
        }
    }

    private void Select(string key)
    {
        RequireIdleOwner();
        if (SelectedKey == key)
        {
            return;
        }

        SelectedKey = key;
        InvalidateInput();
        Widget.OnSelectionChanged?.Invoke(key);
    }
}
