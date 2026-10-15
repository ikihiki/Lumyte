using System.Runtime.InteropServices;

namespace Lumyte.Platform.Linux;

internal static class X11Errors
{
    private static readonly object _gate = new();

    internal static IReadOnlyList<Xlib.ProtocolError> Run(nint display, Action action)
    {
        lock (_gate)
        {
            var errors = new List<Xlib.ProtocolError>();
            nint previous = 0;
            Xlib.ErrorHandler handler = (nint source, ref Xlib.ProtocolError error) =>
            {
                if (source == display)
                {
                    errors.Add(error);
                    return 0;
                }

                return previous == 0 ? 0 : Marshal.GetDelegateForFunctionPointer<Xlib.ErrorHandler>(previous)(source, ref error);
            };
            previous = Xlib.SetErrorHandler(handler);
            try
            {
                action();
            }
            finally
            {
                // Xlib errors are asynchronous. Force their delivery before restoring the
                // process-wide handler, without keeping it installed across user callbacks.
                try
                {
                    Xlib.XSync(display, 0);
                }
                finally
                {
                    Xlib.RestoreErrorHandler(previous);
                    GC.KeepAlive(handler);
                }
            }

            return errors;
        }
    }

    internal static Exception Exception(Xlib.ProtocolError error) => new InvalidOperationException($"X11 error {error.Code}, request {error.Request}, resource {error.Resource}.");
}
