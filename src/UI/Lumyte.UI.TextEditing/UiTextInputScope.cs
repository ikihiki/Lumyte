using System.Runtime.CompilerServices;

namespace Lumyte.UI.TextEditing;

/// <summary>Associates one text input handler with all editable widgets connected to a surface.</summary>
public static class UiTextInputScope
{
    private static readonly ConditionalWeakTable<UiSurface, IUiTextInputHandler> _handlers = new();

    /// <summary>Attaches a borrowed handler once for all editable widgets in the surface.</summary>
    /// <param name="surface">The surface that owns the editable widgets.</param>
    /// <param name="handler">The borrowed text input handler.</param>
    /// <returns>A registration to release after text sessions have drained.</returns>
    public static IDisposable Attach(UiSurface surface, IUiTextInputHandler handler)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(handler);
        _ = surface.Root;
        _handlers.Add(surface, handler);
        return new Registration(surface, handler);
    }

    internal static IUiTextInputHandler? Resolve(UiSurface? surface) => surface is not null && _handlers.TryGetValue(surface, out IUiTextInputHandler? handler) ? handler : null;

    private sealed class Registration(UiSurface surface, IUiTextInputHandler handler) : IDisposable
    {
        private UiSurface? _surface = surface;

        public void Dispose()
        {
            UiSurface? current = Interlocked.Exchange(ref _surface, null);
            if (current is not null && _handlers.TryGetValue(current, out IUiTextInputHandler? active) && ReferenceEquals(active, handler))
            {
                _handlers.Remove(current);
            }
        }
    }
}
