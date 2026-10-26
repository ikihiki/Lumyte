using System.Runtime.InteropServices;

namespace Lumyte.Audio.OpenAL;

internal sealed class OpenALNativeContext : INativeContext
{
    private nint _library;

    private OpenALNativeContext()
    {
        string[] names = OperatingSystem.IsWindows()
            ? ["openal32.dll", "soft_oal.dll"]
            : OperatingSystem.IsMacOS() || OperatingSystem.IsIOS() || OperatingSystem.IsTvOS() || OperatingSystem.IsMacCatalyst()
                ? ["/System/Library/Frameworks/OpenAL.framework/OpenAL", "libopenal.dylib"]
                : ["libopenal.so.1", "libopenal.so"];
        foreach (string name in names)
        {
            if (NativeLibrary.TryLoad(name, typeof(OpenALNativeContext).Assembly, null, out _library))
            {
                return;
            }
        }

        throw new DllNotFoundException($"OpenAL could not be loaded from the application or system library search path. Tried: {string.Join(", ", names)}.");
    }

    public nint GetProcAddress(string proc, int? slot = null)
    {
        nint library = Volatile.Read(ref _library);
        ObjectDisposedException.ThrowIf(library == 0, this);
        return NativeLibrary.GetExport(library, proc);
    }

    public bool TryGetProcAddress(string proc, out nint addr, int? slot = null)
    {
        nint library = Volatile.Read(ref _library);
        ObjectDisposedException.ThrowIf(library == 0, this);
        return NativeLibrary.TryGetExport(library, proc, out addr);
    }

    public void Dispose()
    {
        nint library = Interlocked.Exchange(ref _library, 0);
        if (library != 0)
        {
            NativeLibrary.Free(library);
        }
    }

    internal static AL CreateApi()
    {
        var context = new OpenALNativeContext();
        try
        {
            return new(context);
        }
        catch
        {
            context.Dispose();
            throw;
        }
    }

    internal static ALContext CreateContextApi()
    {
        var context = new OpenALNativeContext();
        try
        {
            return new(context);
        }
        catch
        {
            context.Dispose();
            throw;
        }
    }
}
