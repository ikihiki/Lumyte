using System.Numerics;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.UI;

/// <summary>Adapts mounted UI instances to a mutable logical drawing scene with reusable local content.</summary>
/// <remarks>
/// Explicitly opt in; this does not replace UiSurface.Record or the hosted visual composition path.
/// Synchronize access with the surface. Dispose every read lease before Update.
/// This adapter owns its scene, not the surface or resources borrowed by recorded commands.
/// Callers must keep borrowed resources alive through preparation and consuming GPU submissions.
/// </remarks>
public sealed class UiDrawingScene2D : IDisposable
{
    private readonly UiSurface _surface;
    private readonly DrawingScene2D _scene = new();
    private readonly Dictionary<UiElement, Placement> _placements = new(ReferenceEqualityComparer.Instance);
    private DrawingNodeId2D? _opaque;
    private Vector2? _viewport;
    private bool _disposed;
    private bool _updating;

    /// <summary>Initializes a new instance of the <see cref="UiDrawingScene2D"/> class.Creates an adapter without taking ownership of the surface.</summary>
    /// <param name="surface">The existing mounted UI surface.</param>
    public UiDrawingScene2D(UiSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        _ = surface.RootElement;
        _surface = surface;
    }

    /// <summary>Gets the adapter-owned scene for borrowing, snapshots and renderer preparation.</summary>
    /// <remarks>Do not mutate or dispose this scene directly; topology belongs to this adapter.</remarks>
    public DrawingScene2D Scene => _scene;

    /// <summary>Gets the number of local or opaque recordings performed by the last successful update.</summary>
    public int LastRecordingCount { get; private set; }

    /// <summary>Applies pending layout and synchronizes visible elements in their existing painter order.</summary>
    /// <remarks>A live scene borrow rejects this operation before layout or custom drawing runs.</remarks>
    public void Update()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_updating)
        {
            throw new InvalidOperationException("Retained drawing updates cannot be reentered.");
        }

        // Even an unchanged root transform validates exclusive write access before user callbacks.
        _scene.SetTransform(_scene.Root, Matrix3x2.Identity);
        _updating = true;
        try
        {
            _surface.UpdateTree();
            foreach (UiElement element in UiSurface.CollectElements(_surface.RootElement))
            {
                element.ValidateRetainedDrawingDependencies();
            }

            _surface.UpdateLayout();
            UiElement root = _surface.RootElement;
            int recordings = 0;
            if (_surface.RequiresOpaqueDrawing)
            {
                ClearPlacements();
                _scene.SetClip(_scene.Root, null);
                _viewport = null;
                var canvas = new Canvas2D();
                canvas.Begin();
                _surface.Record(canvas);
                DrawList2D content = canvas.End();
                if (_opaque is { } opaque)
                {
                    _scene.ReplaceContent(opaque, content);
                }
                else
                {
                    _opaque = _scene.Insert(_scene.Root, 0, content);
                }

                LastRecordingCount = 1;
                return;
            }

            if (_opaque is { } previous)
            {
                _scene.Remove(previous);
                _opaque = null;
            }

            Vector2 viewport = _surface.DrawingViewport;
            if (_viewport != viewport)
            {
                _scene.SetClip(_scene.Root, RectangleClip(viewport));
                _viewport = viewport;
            }

            var visited = new HashSet<UiElement>(ReferenceEqualityComparer.Instance);
            if (viewport.X > 0 && viewport.Y > 0)
            {
                Synchronize(root, _scene.Root, 0, visited, ref recordings);
            }

            // Detach stale groups only after all surviving identities have moved to their new parents.
            foreach (UiElement stale in _placements.Keys.Where(element => !visited.Contains(element)).ToArray())
            {
                RemovePlacement(stale);
            }

            LastRecordingCount = recordings;
        }
        finally
        {
            _updating = false;
        }
    }

    /// <summary>Updates this scene, then borrows its latest CPU content until renderer preparation finishes.</summary>
    /// <returns>A caller-owned read lease; dispose before the next update.</returns>
    public DrawingSceneReadLease2D Borrow()
    {
        Update();
        return _scene.Borrow();
    }

    /// <summary>Disposes the owned scene without disposing the surface or borrowed drawing resources.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_updating)
        {
            throw new InvalidOperationException("Cannot dispose during a retained drawing update.");
        }

        _scene.Dispose();
        _placements.Clear();
        _disposed = true;
    }

    private static Path2D RectangleClip(Vector2 size)
    {
        var path = new PathBuilder2D();
        path.MoveTo(Vector2.Zero);
        path.LineTo(new(size.X, 0));
        path.LineTo(size);
        path.LineTo(new(0, size.Y));
        path.Close();
        return path.Build();
    }

    private void Synchronize(UiElement element, DrawingNodeId2D parent, int index, HashSet<UiElement> visited, ref int recordings)
    {
        if (!element.LayoutVisible)
        {
            return;
        }

        visited.Add(element);
        bool split = element.HasDefaultRecordingOrder;
        if (!_placements.TryGetValue(element, out Placement? placement))
        {
            placement = new(_scene.AddGroup(parent, index)) { Parent = parent };
            _placements.Add(element, placement);
        }
        else
        {
            // Move also accounts for an earlier sibling insertion/removal in the current pass.
            _scene.Move(placement.Group, parent, index);
        }

        placement.Parent = parent;
        Vector2 offset = new(element.Bounds.X, element.Bounds.Y);
        if (placement.Offset != offset)
        {
            _scene.SetTransform(placement.Group, Matrix3x2.CreateTranslation(offset));
            placement.Offset = offset;
        }

        if (placement.ClipSize != element.Size || placement.Clipped != element.Widget.ClipToBounds)
        {
            _scene.SetClip(placement.Group, element.Widget.ClipToBounds ? RectangleClip(element.Size) : null);
            placement.ClipSize = element.Size;
            placement.Clipped = element.Widget.ClipToBounds;
        }

        if (!placement.Recorded || !split || !element.CanCacheRetainedDrawing
            || placement.VisualRevision != element.VisualRevision || placement.Size != element.Size
            || !ReferenceEquals(placement.Configuration, element.Widget)
            || (element is RectangleElement && placement.Fill != ((Compose.Definitions.Rectangle)element.Widget).Fill))
        {
            DrawList2D recording = _surface.RecordDrawingFragment(element, split);
            DrawList2D content = recording;
            if (placement.Content is { } node)
            {
                _scene.ReplaceContent(node, content);
            }
            else
            {
                placement.Content = _scene.Insert(placement.Group, 0, content);
            }

            placement.Configuration = element.Widget;
            if (element.Widget is Compose.Definitions.Rectangle rectangle)
            {
                placement.Fill = rectangle.Fill;
            }

            placement.VisualRevision = element.VisualRevision;
            placement.Size = element.Size;
            placement.Recorded = true;
            recordings++;
        }

        if (split)
        {
            int childIndex = 1;
            foreach (UiElement child in element.Children)
            {
                if (child.LayoutVisible)
                {
                    Synchronize(child, placement.Group, childIndex++, visited, ref recordings);
                }
            }
        }
    }

    private void ClearPlacements()
    {
        foreach (UiElement element in _placements.Keys.ToArray())
        {
            RemovePlacement(element);
        }
    }

    private void RemovePlacement(UiElement element)
    {
        if (!_placements.TryGetValue(element, out Placement? placement))
        {
            return;
        }

        // Groups can have moved independently from the current UI parent relationships.
        foreach (UiElement descendant in _placements.Keys.Where(candidate => IsPlacedUnder(candidate, placement.Group)).ToArray())
        {
            _placements.Remove(descendant);
        }

        _placements.Remove(element);
        _scene.Remove(placement.Group);
    }

    private bool IsPlacedUnder(UiElement element, DrawingNodeId2D group)
    {
        DrawingNodeId2D parent = _placements[element].Parent;
        while (parent != _scene.Root)
        {
            if (parent == group)
            {
                return true;
            }

            Placement? ancestor = _placements.Values.FirstOrDefault(candidate => candidate.Group == parent);
            if (ancestor is null)
            {
                break;
            }

            parent = ancestor.Parent;
        }

        return false;
    }

    private sealed class Placement(DrawingNodeId2D group)
    {
        internal DrawingNodeId2D Group { get; } = group;

        internal DrawingNodeId2D Parent { get; set; }

        internal Vector2 Offset { get; set; }

        internal DrawingNodeId2D? Content { get; set; }

        internal bool Recorded { get; set; }

        internal Compose.Definitions.Widget? Configuration { get; set; }

        internal Color2D Fill { get; set; }

        internal ulong VisualRevision { get; set; }

        internal Vector2 Size { get; set; }

        internal Vector2 ClipSize { get; set; }

        internal bool Clipped { get; set; }
    }
}
