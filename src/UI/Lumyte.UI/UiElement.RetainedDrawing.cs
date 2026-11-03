using System.Collections.Concurrent;
using System.Reflection;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.UI;

/// <summary>A persistent UI instance with independent configuration, topology and lifetime.</summary>
public partial class UiElement
{
    private static readonly ConcurrentDictionary<Type, RecordingMethods> _recordingMethods = new();

    internal bool HasDefaultRecordingOrder => Methods.Record.DeclaringType == typeof(UiElement);

    internal bool CanCacheRetainedDrawing => CacheRetainedDrawing || Methods.Self.DeclaringType == typeof(UiElement)
        || (GetType() == typeof(RectangleElement) && Methods.Self.DeclaringType == typeof(RectangleElement));

    /// <summary>Gets a value indicating whether local drawing may be reused until visual invalidation or a size change.</summary>
    /// <remarks>
    /// Custom implementations must call <see cref="InvalidateDrawing"/> when their drawing changes.
    /// RecordOverride must retain the default self-then-children order; custom orders are always opaque.
    /// Borrowed textures and other external resources must outlive every consuming prepared frame.
    /// </remarks>
    protected virtual bool CacheRetainedDrawing => false;

    private RecordingMethods Methods
    {
        get
        {
            Type type = GetType();
            if (_recordingMethods.TryGetValue(type, out RecordingMethods? methods))
            {
                return methods;
            }

            // Virtual delegate targets root their exact methods for trimmed/AOT builds;
            // no lookup of arbitrary non-public methods by name is required.
            methods = new(((Action<DrawingCanvas2D>)RecordOverride).Method, ((Action<DrawingCanvas2D>)OnRecord).Method);
            return _recordingMethods.GetOrAdd(type, methods);
        }
    }

    internal void ValidateRetainedDrawingDependencies() => ValidateRetainedDrawing();

    internal void InvokeRecordSelf(DrawingCanvas2D canvas) => RecordSelf(canvas);

    /// <summary>Invalidates this element's reusable drawing and existing ancestor visual captures.</summary>
    protected void InvalidateDrawing() => InvalidateTransitionCapture();

    /// <summary>Validates external drawing dependencies before retained layout synchronization.</summary>
    /// <remarks>Invalidate layout or drawing here when a borrowed dependency revision changed; do not record.</remarks>
    protected virtual void ValidateRetainedDrawing()
    {
    }

    private sealed record RecordingMethods(MethodInfo Record, MethodInfo Self);
}
