using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

// A selection probe, not a supported browser or a replacement product adapter.
return Probe.Run(args);

internal static unsafe partial class Probe
{
    private static bool _contract;
    private static byte[]? _contractCopy;
    private static bool _created;
    private static bool _painted;
    private static bool _closed;
    private static long _due;

    internal static int Run(string[] args)
    {
        try
        {
            if (Abi() != 1)
            {
                Console.Error.WriteLine("cef-bridge-abi-mismatch");
                return 3;
            }

            if (args.Any(arg => arg.StartsWith("--type=", StringComparison.Ordinal)))
            {
                return Subprocess();
            }

            var callbacks = new Callbacks { Size = (uint)sizeof(Callbacks), Version = 1, Event = &Event };
            if (Contract(&callbacks) != 0 || !_contract || _contractCopy is not [0x20, 0x40, 0x80, 0x80] || Contract(null) != 1 || Shutdown() != 4)
            {
                return 4;
            }

            callbacks.Version = 2;
            if (Contract(&callbacks) != 1)
            {
                return 4;
            }

            callbacks.Version = 1;
            Console.WriteLine("native-contract: ABI/version/null/callback/BGRA/owner-shutdown passed");
            if (!args.Contains("--browser", StringComparer.Ordinal))
            {
                return 0;
            }

            // Only a deliberately supplied local acceptance server may be opened.
            string? url = args.FirstOrDefault(arg => arg.StartsWith("http://127.0.0.1:", StringComparison.Ordinal));
            if (url is null)
            {
                Console.Error.WriteLine("local-acceptance-url-required");
                return 2;
            }

            int initialized = Initialize(&callbacks);
            Console.WriteLine($"sandbox-initialize-status: {initialized}");
            if (initialized != 0)
            {
                return 5;
            }

            byte[] address = Encoding.UTF8.GetBytes(url);
            fixed (byte* pointer = address)
            {
                if (Create(pointer, (nuint)address.Length) != 0)
                {
                    return 6;
                }
            }

            long deadline = Environment.TickCount64 + 15000;
            bool closing = false;
            while (!_closed && Environment.TickCount64 < deadline)
            {
                if (Environment.TickCount64 >= Interlocked.Read(ref _due))
                {
                    Interlocked.Exchange(ref _due, long.MaxValue);
                    if (Pump() != 0)
                    {
                        return 7;
                    }
                }

                if (_painted && !closing)
                {
                    closing = true;
                    if (Close() != 0 || Shutdown() != 2)
                    {
                        return 8;
                    }
                }

                Thread.Sleep(1);
            }

            Console.WriteLine($"OSR created={_created} painted={_painted} async-closed={_closed}");
            return _closed && _painted && Shutdown() == 0 ? 0 : 9;
        }
        catch (DllNotFoundException)
        {
            Console.Error.WriteLine("cef-native-missing");
            return 10;
        }
        catch (EntryPointNotFoundException)
        {
            Console.Error.WriteLine("cef-bridge-abi-mismatch");
            return 3;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Event(nuint context, uint kind, byte* data, nuint length, int width, int height)
    {
        try
        {
            switch (kind)
            {
                case 1 when length == 8:
                    long due = Environment.TickCount64 + Math.Clamp(*(long*)data, 0, int.MaxValue);
                    long old;
                    do
                    {
                        old = Interlocked.Read(ref _due);
                        if (old <= due)
                        {
                            return;
                        }
                    }
                    while (Interlocked.CompareExchange(ref _due, due, old) != old);
                    break;
                case 2: _created = true; break;
                case 3: _closed = true; break;
                case 4: _painted = length == 16384 && width == 64 && height == 64; break;
                case 5:
                    _contract = length == 4 && width == 1 && height == 1;
                    _contractCopy = new ReadOnlySpan<byte>(data, checked((int)length)).ToArray();
                    break;
            }
        }
        catch
        {
            // Managed exceptions must never cross the callback ABI.
        }
    }

    [LibraryImport("lumyte_ui_webview_cef", EntryPoint = "lumyte_cef_abi_version")]
    private static partial uint Abi();

    [LibraryImport("lumyte_ui_webview_cef", EntryPoint = "lumyte_cef_contract")]
    private static partial int Contract(Callbacks* callbacks);

    [LibraryImport("lumyte_ui_webview_cef", EntryPoint = "lumyte_cef_initialize")]
    private static partial int Initialize(Callbacks* callbacks);

    [LibraryImport("lumyte_ui_webview_cef", EntryPoint = "lumyte_cef_subprocess")]
    private static partial int Subprocess();

    [LibraryImport("lumyte_ui_webview_cef", EntryPoint = "lumyte_cef_create")]
    private static partial int Create(byte* url, nuint length);

    [LibraryImport("lumyte_ui_webview_cef", EntryPoint = "lumyte_cef_pump")]
    private static partial int Pump();

    [LibraryImport("lumyte_ui_webview_cef", EntryPoint = "lumyte_cef_close")]
    private static partial int Close();

    [LibraryImport("lumyte_ui_webview_cef", EntryPoint = "lumyte_cef_shutdown")]
    private static partial int Shutdown();

    [StructLayout(LayoutKind.Sequential)]
    private struct Callbacks
    {
        internal uint Size;
        internal uint Version;
        internal nuint Context;
        internal delegate* unmanaged[Cdecl]<nuint, uint, byte*, nuint, int, int, void> Event;
    }
}
