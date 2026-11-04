using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Ahjo.Wgpu.Native;
using Lumyte.Graphics.Wgpu;
using A = Ahjo.Wgpu;

namespace Lumyte.Graphics.Presentation.Benchmarks;

// Benchmark-only version-sensitive access, matching Primitives/RecoveryRunner.Identity.
// No production assembly friendship or production API changes.
internal static class NativeIdentity
{
    internal static unsafe object Capture(WgpuDevice device)
    {
        A.Adapter adapter = GetAdapter(device);
        WGPUAdapterInfo info = default;
        if (WGPU.wgpuAdapterGetInfo(adapter.Handle, &info) != WGPUStatus.Success)
        {
            throw new InvalidOperationException("Exact selected native adapter query failed.");
        }

        try
        {
            if (info.adapterType is WGPUAdapterType.CPU or WGPUAdapterType.Unknown)
            {
                throw new InvalidOperationException("Identified physical GPU required; CPU/unknown adapter rejected.");
            }

            return new
            {
                vendor = NativeString(info.vendor),
                architecture = NativeString(info.architecture),
                device = NativeString(info.device),
                driverDescription = NativeString(info.description),
                backendType = info.backendType.ToString(),
                adapterType = info.adapterType.ToString(),
                vendorId = info.vendorID,
                deviceId = info.deviceID,
                shaderTarget = device.Caps.ShaderTarget.ToString(),
                source = "this WgpuDevice's exact selected Ahjo adapter via benchmark-only UnsafeAccessor",
            };
        }
        finally
        {
            WGPU.wgpuAdapterInfoFreeMembers(info);
        }
    }

    private static unsafe string NativeString(WGPUStringView value) => value.data == null ? string.Empty : value.length == nuint.MaxValue ? Marshal.PtrToStringUTF8((nint)value.data) ?? string.Empty : Encoding.UTF8.GetString(new ReadOnlySpan<byte>(value.data, checked((int)value.length)));

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_adapter")]
    private static extern ref A.Adapter GetAdapter(WgpuDevice device);
}
