using System.Runtime.InteropServices;

namespace Lumyte.Setup.Smoke;

internal static class Native
{
    [DllImport("lumyte_setup_smoke", EntryPoint = "lumyte_add", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Add(int a, int b);

    [DllImport("lumyte_setup_smoke", EntryPoint = "lumyte_verify_vulkan", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int VerifyVulkan();

    [DllImport("lumyte_setup_smoke", EntryPoint = "lumyte_verify_directx", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int VerifyDirectX();
}
