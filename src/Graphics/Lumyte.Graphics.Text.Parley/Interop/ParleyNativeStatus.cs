using System.Text;

namespace Lumyte.Graphics.Text.Parley.Interop;

internal static unsafe class ParleyNativeStatus
{
    public static void Check(uint status)
    {
        if (status == 0)
        {
            return;
        }

        byte* data = null;
        nuint length = 0;
        NativeMethods.lp_last_error(&data, &length);
        string message = data is not null && length <= int.MaxValue ? Encoding.UTF8.GetString(new ReadOnlySpan<byte>(data, (int)length)) : "The native text engine failed.";
        throw status switch
        {
            1 => new InvalidDataException(message),
            2 => new NotSupportedException(message),
            _ => new InvalidOperationException(message),
        };
    }
}
