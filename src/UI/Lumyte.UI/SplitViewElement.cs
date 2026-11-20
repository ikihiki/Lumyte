using System.Numerics;
using SplitDeclaration = Lumyte.UI.Compose.Definitions.SplitView;
using WidgetDefinition = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

/// <summary>Owns independent accepted pane allocation and ordinary divider input.</summary>
/// <param name="widget">The initial passive declaration.</param>
public sealed class SplitViewElement(SplitDeclaration widget) : UiElement<SplitDeclaration>(widget)
{
    private Dictionary<UiElement, (float Declared, double Accepted)> _weights = new(ReferenceEqualityComparer.Instance);
    private UiElement[] _visible = [];
    private float[] _sizes = [];
    private float[] _minimums = [];
    private (UiElement[] Visible, float[] Sizes, float[] Minimums, Dictionary<UiElement, (float Declared, double Accepted)> Weights, float Length, float Gap, UiOrientation Orientation, bool Reset)? _prepared;
    private SplitDivider.DividerElement? _active;
    private Dictionary<UiElement, (float Declared, double Accepted)>? _requestedWeights;
    private float[]? _requestedSizes;
    private bool _notifyChanged;
    private bool _notifyCommitted;
    private UiElement[] _activeTopology = [];
    private UiElement[] _activeVisible = [];
    private bool[] _activeEnabled = [];
    private UiOrientation _activeOrientation;
    private bool _cancelActive;
    private UiElement[]? _commitTopology;
    private UiOrientation _commitOrientation;
    private float _acceptedLength;
    private float _acceptedGap;
    private UiOrientation _acceptedOrientation;

    /// <summary>Gets a detached copy of accepted visible pane extents.</summary>
    public IReadOnlyList<float> PaneSizes => Array.AsReadOnly((float[])_sizes.Clone());

    internal override bool RetainUninitializedSlot(int slot) => slot != Widget.Dividers.SlotIndex;

    internal override IReadOnlyList<WidgetDefinition>[]? PrepareDeclaredSlots(WidgetDefinition next, IReadOnlyList<WidgetDefinition>[] slots, bool retainUninitialized)
    {
        var declaration = (SplitDeclaration)next;
        IReadOnlyList<WidgetDefinition> panes = retainUninitialized && !declaration.Children.IsInitialized && IsMounted
            ? GetChildren(Widget.Children).Select(child => child.PreparationWidget).ToArray() : slots[declaration.Children.SlotIndex];
        var dividers = new WidgetDefinition[Math.Max(0, panes.Count - 1)];
        for (int index = 0; index < dividers.Length; index++)
        {
            string left = panes[index].Key is { } leftKey ? "k:" + leftKey : "p:" + index;
            string right = panes[index + 1].Key is { } rightKey ? "k:" + rightKey : "p:" + (index + 1);
            dividers[index] = new SplitDivider { Key = left.Length + ":" + left + right, Index = index, TestId = declaration.TestId is { } testId ? testId + ".divider." + index : null, Signature = declaration.Orientation + ":" + string.Join(";", panes.Select(pane => pane.Key + ":" + pane.IsVisible + ":" + pane.IsEnabled)), Role = UiRole.Separator, Focusable = true, AutomationName = "Resize " + left + " / " + right };
        }

        slots[declaration.Dividers.SlotIndex] = dividers;
        return slots;
    }

    internal override bool PrepareViewportContent()
    {
        if (_commitTopology is { } completed)
        {
            UiElement[] current = PreparationChildren.Where(child => child is not SplitDivider.DividerElement).ToArray();
            if (!completed.SequenceEqual(current) || ((SplitDeclaration)PreparationWidget).Orientation != _commitOrientation)
            {
                _notifyCommitted = false;
                _commitTopology = null;
            }
        }

        if (_active is not null)
        {
            UiElement[] panes = PreparationChildren.Where(child => child is not SplitDivider.DividerElement).ToArray();
            UiElement[] visible = panes.Where(child => child.PreparationWidget.IsVisible).ToArray();
            _cancelActive = !_activeTopology.SequenceEqual(panes) || !_activeVisible.SequenceEqual(visible)
                || ((SplitDeclaration)PreparationWidget).Orientation != _activeOrientation
                || !_activeEnabled.SequenceEqual(panes.Select(child => child.PreparationWidget.IsEnabled));
        }

        return false;
    }

    internal override void AcceptViewportPreparation()
    {
        if (_cancelActive)
        {
            _cancelActive = false;
            _active?.CancelOwnership();
        }

        if (_prepared is { } prepared)
        {
            if (_active is not null && !_activeVisible.SequenceEqual(prepared.Visible))
            {
                _active.CancelOwnership();
            }

            bool changed = !_sizes.SequenceEqual(prepared.Sizes);
            _weights = prepared.Weights;
            _visible = prepared.Visible;
            _sizes = prepared.Sizes;
            _minimums = prepared.Minimums;
            _acceptedLength = prepared.Length;
            _acceptedGap = prepared.Gap;
            _acceptedOrientation = prepared.Orientation;
            _prepared = null;
            if (_requestedWeights is { } weights)
            {
                _requestedWeights = null;
                _requestedSizes = null;
                _notifyChanged = !prepared.Reset && changed;
            }
        }
    }

    internal override void DiscardViewportPreparation()
    {
        _prepared = null;
        _cancelActive = false;
    }

    internal override Action? TakeViewportNotification()
    {
        bool changed = _notifyChanged;
        bool committed = _notifyCommitted;
        _notifyChanged = false;
        _notifyCommitted = false;
        _commitTopology = null;
        if (!changed && !committed)
        {
            return null;
        }

        IReadOnlyList<float> sizes = PaneSizes;
        return () =>
        {
            List<Exception>? failures = null;
            if (changed && IsMounted)
            {
                try
                {
                    Widget.OnChanged?.Invoke(sizes);
                }
                catch (Exception error)
                {
                    (failures ??= []).Add(error);
                }
            }

            if (committed && IsMounted)
            {
                try
                {
                    Widget.OnCommitted?.Invoke(sizes);
                }
                catch (Exception error)
                {
                    (failures ??= []).Add(error);
                }
            }

            if (failures is { Count: > 0 })
            {
                throw new AggregateException(failures);
            }
        };
    }

    internal double Fraction(int divider)
    {
        UiElementChildren panes = GetChildren(Widget.Children);
        int index = divider < panes.Count ? Array.IndexOf(_visible, panes[divider]) : -1;
        double total = index >= 0 && index + 1 < _sizes.Length ? (double)_sizes[index] + _sizes[index + 1] : 0;
        return total > 0 ? _sizes[index] / total : 0;
    }

    internal bool ResizeFraction(int divider, double fraction)
    {
        RequireIdle();
        if (!double.IsFinite(fraction) || fraction < 0 || fraction > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(fraction));
        }

        UiElementChildren panes = GetChildren(Widget.Children);
        int index = divider < panes.Count ? Array.IndexOf(_visible, panes[divider]) : -1;
        return index >= 0 && index + 1 < _sizes.Length && Resize(divider, (fraction * ((double)_sizes[index] + _sizes[index + 1])) - _sizes[index]);
    }

    internal bool Begin(SplitDivider.DividerElement divider)
    {
        RequireIdle();
        if (_active is not null && !ReferenceEquals(_active, divider))
        {
            return false;
        }

        UiElementChildren panes = GetChildren(Widget.Children);
        int index = divider.Widget.Index < panes.Count ? Array.IndexOf(_visible, panes[divider.Widget.Index]) : -1;
        if (index < 0 || index + 1 >= _visible.Length || !_visible[index].Widget.IsEnabled || !_visible[index + 1].Widget.IsEnabled)
        {
            return false;
        }

        _notifyCommitted = false;
        _commitTopology = null;
        _active = divider;
        _activeTopology = panes.ToArray();
        _activeVisible = (UiElement[])_visible.Clone();
        _activeEnabled = panes.Select(child => child.Widget.IsEnabled).ToArray();
        _activeOrientation = Widget.Orientation;
        return true;
    }

    internal void End(SplitDivider.DividerElement divider, bool commit)
    {
        if (!ReferenceEquals(_active, divider))
        {
            return;
        }

        if (commit)
        {
            _commitTopology = _activeTopology;
            _commitOrientation = _activeOrientation;
        }

        _active = null;
        _activeTopology = [];
        _activeVisible = [];
        _activeEnabled = [];
        if (commit && IsMounted)
        {
            _notifyCommitted = true;
            InvalidateLayout();
        }
    }

    internal bool Resize(int divider, double delta)
    {
        RequireIdle();
        UiElementChildren panes = GetChildren(Widget.Children);
        if (divider < 0 || divider + 1 >= panes.Count)
        {
            return false;
        }

        int index = Array.IndexOf(_visible, panes[divider]);
        if (index < 0 || index + 1 >= _sizes.Length || !_visible[index].Widget.IsEnabled || !_visible[index + 1].Widget.IsEnabled)
        {
            return false;
        }

        float[] currentSizes = _requestedSizes ?? _sizes;
        double total = (double)currentSizes[index] + currentSizes[index + 1];
        double minimum = Math.Min(_minimums[index], _sizes[index]);
        double maximum = Math.Max(minimum, total - Math.Min(_minimums[index + 1], _sizes[index + 1]));
        float first = (float)Math.Clamp(currentSizes[index] + delta, minimum, maximum);
        float second = (float)Math.Max(0, total - first);
        if (currentSizes[index] == first && currentSizes[index + 1] == second)
        {
            return false;
        }

        float[] accepted = (float[])currentSizes.Clone();
        accepted[index] = first;
        accepted[index + 1] = second;
        double sum = Enumerable.Range(0, accepted.Length).Sum(index => Math.Max(0, (double)accepted[index] - _minimums[index]));
        double totalWeight = _visible.Sum(child => (_requestedWeights ?? _weights).TryGetValue(child, out (float Declared, double Accepted) current) ? current.Accepted : Metadata(child.Widget).Weight);
        var requested = new Dictionary<UiElement, (float Declared, double Accepted)>(_weights, ReferenceEqualityComparer.Instance);
        for (int pane = 0; pane < accepted.Length; pane++)
        {
            UiElement child = _visible[pane];
            float declared = Metadata(child.Widget).Weight;
            double weight = sum > 0 ? (Math.Max(0, (double)accepted[pane] - _minimums[pane]) / sum) * totalWeight : Metadata(child.Widget).Weight;
            requested[child] = (declared, weight);
        }

        _requestedSizes = accepted;
        _requestedWeights = requested;
        InvalidateLayout();
        return true;
    }

    /// <inheritdoc/>
    protected override void OnUnmounted()
    {
        _active = null;
        _activeTopology = [];
        _activeVisible = [];
        _activeEnabled = [];
        _commitTopology = null;
        _requestedWeights = null;
        _requestedSizes = null;
        _prepared = null;
        _weights.Clear();
        _visible = [];
        _sizes = [];
        _minimums = [];
        _notifyChanged = false;
        _notifyCommitted = false;
        _cancelActive = false;
    }

    /// <inheritdoc/>
    protected override IEnumerable<UiElement> GetAutomationChildren() => Children.Where(child => child is not SplitDivider.DividerElement || (child.Size.X > 0 && child.Size.Y > 0));

    /// <inheritdoc/>
    protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
    {
        if (!constraints.HasBoundedWidth || !constraints.HasBoundedHeight)
        {
            throw new InvalidOperationException("SplitView requires finite constraints on both axes.");
        }

        bool vertical = Widget.Orientation == UiOrientation.Vertical;
        Vector2 result = new(constraints.MaxWidth, constraints.MaxHeight);
        UiElementChildren panes = GetChildren(Widget.Children);
        UiElement[] visible = panes.Where(child => child.LayoutVisible).ToArray();
        float[] minimums = visible.Select(child => Metadata(child.Widget).Minimum).ToArray();
        bool reset = panes.Any(child => _weights.TryGetValue(child, out (float Declared, double Accepted) old) && old.Declared != Metadata(child.Widget).Weight);
        double[] weights = visible.Select(child => !reset && (_requestedWeights ?? _weights).TryGetValue(child, out (float Declared, double Accepted) old) ? old.Accepted : Metadata(child.Widget).Weight).ToArray();
        float length = C4Layout.Main(result, vertical);
        float[] sizes = SplitViewLayout.Allocate(length, Widget.DividerThickness, weights, minimums, out float gap);
        if (!reset && _acceptedLength == length && _acceptedGap == gap && _acceptedOrientation == Widget.Orientation
            && _visible.SequenceEqual(visible) && _minimums.SequenceEqual(minimums)
            && visible.All(child => _weights.TryGetValue(child, out (float Declared, double Accepted) previous) && previous.Declared == Metadata(child.Widget).Weight))
        {
            sizes = (float[])(_requestedSizes ?? _sizes).Clone();
        }

        double offset = 0;
        int position = 0;
        foreach (UiElement pane in panes)
        {
            float extent = pane.LayoutVisible ? sizes[position++] : 0;
            Vector2 size = vertical ? new(result.X, extent) : new(extent, result.Y);
            LayoutChild(pane, UiBoxConstraints.Tight(size));
            SetChildOffset(pane, pane.LayoutVisible ? C4Layout.Vector(Math.Min(length, offset), 0, vertical) : Vector2.Zero);
            if (pane.LayoutVisible)
            {
                offset = (double)(float)Math.Min(length, offset) + extent + gap;
            }
        }

        UiElementChildren dividers = GetChildren(Widget.Dividers);
        foreach (UiElement divider in dividers)
        {
            int index = ((SplitDivider)divider.Widget).Index;
            UiElement left = panes[index];
            int visibleIndex = Array.IndexOf(visible, left);
            bool active = visibleIndex >= 0 && visibleIndex + 1 < visible.Length;
            double boundary = active ? sizes.Take(visibleIndex + 1).Sum(value => (double)value) + (gap * visibleIndex) : 0;
            float hit = active ? Math.Min(length, Widget.MinimumHitSize) : 0;
            float start = (float)Math.Clamp(boundary + (gap / 2) - (hit / 2), 0, Math.Max(0, length - hit));
            LayoutChild(divider, UiBoxConstraints.Tight(vertical ? new(result.X, hit) : new(hit, result.Y)));
            SetChildOffset(divider, C4Layout.Vector(start, 0, vertical));
        }

        if (PreparationSurface?.IsPreparingViewportLayout == true)
        {
            var proposedWeights = new Dictionary<UiElement, (float Declared, double Accepted)>(ReferenceEqualityComparer.Instance);
            foreach (UiElement pane in panes)
            {
                float declared = Metadata(pane.Widget).Weight;
                proposedWeights.Add(pane, !reset && (_requestedWeights ?? _weights).TryGetValue(pane, out (float Declared, double Accepted) old) && old.Declared == declared ? old : (declared, declared));
            }

            _prepared = (visible, sizes, minimums, proposedWeights, length, gap, Widget.Orientation, reset);
        }
        else
        {
            _visible = visible;
            _sizes = sizes;
            _minimums = minimums;
        }

        return result;
    }

    private static UiParentData Metadata(WidgetDefinition declaration) => declaration.ParentData is [{ Kind: UiParentDataKind.SplitView } data] ? data : new(UiParentDataKind.SplitView);

    private void RequireIdle()
    {
        if (PreparationSurface?.HasUnpublishedViewportPreparation == true)
        {
            throw new InvalidOperationException("SplitView input cannot mutate unpublished preparation.");
        }
    }
}
