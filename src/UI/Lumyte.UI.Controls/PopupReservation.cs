using System.Numerics;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Controls;

// Reserve before calling an arbitrary borrowed host. Reentry can cancel this exact
// generation before Open returns; a late token must never become the new owner.
internal sealed class PopupReservation
{
    private IDisposable? _token;
    private object? _generation;

    internal bool IsOpen => _generation is not null;

    internal static void ValidateSize(Vector2 size)
    {
        if (!float.IsFinite(size.X) || !float.IsFinite(size.Y) || size.X <= 0 || size.Y <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }
    }

    internal void Open(IMenuPopupHost host, UiElement anchor, Vector2 size, Func<Widget> factory, bool passive)
    {
        if (IsOpen || !anchor.IsMounted)
        {
            return;
        }

        object generation = new();
        _generation = generation;
        try
        {
            IDisposable token = host.Open(
                anchor,
                size,
                () =>
            {
                if (!ReferenceEquals(_generation, generation) || !anchor.IsMounted)
                {
                    throw new InvalidOperationException("Popup opening was cancelled.");
                }

                return factory();
            },
                passive,
                () =>
            {
                if (ReferenceEquals(_generation, generation))
                {
                    _generation = null;
                    _token = null;
                }
            });
            if (ReferenceEquals(_generation, generation) && anchor.IsMounted)
            {
                _token = token;
            }
            else
            {
                token.Dispose();
            }
        }
        catch
        {
            if (ReferenceEquals(_generation, generation))
            {
                Close();
            }

            throw;
        }
    }

    internal void Close()
    {
        _generation = null;
        IDisposable? token = _token;
        _token = null;
        token?.Dispose();
    }
}
