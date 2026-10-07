using System.Runtime.InteropServices;

if (Native.Add(20, 22) != 42)
    throw new InvalidOperationException("Native P/Invoke result mismatch.");
if (Native.VerifyVulkan() != 0)
    throw new InvalidOperationException("Native Vulkan/zlib verification failed.");
if (OperatingSystem.IsWindows() && Native.VerifyDirectX() != 0)
    throw new InvalidOperationException("Direct3D 12 WARP verification failed.");
Console.WriteLine("PASS: Native NuGet restored; C# called C++; Vulkan readback and zlib roundtrip succeeded.");

internal static class Native
{
    [DllImport("lumyte_setup_smoke", EntryPoint = "lumyte_add", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Add(int a, int b);

    [DllImport("lumyte_setup_smoke", EntryPoint = "lumyte_verify_vulkan", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int VerifyVulkan();

    [DllImport("lumyte_setup_smoke", EntryPoint = "lumyte_verify_directx", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int VerifyDirectX();
}
