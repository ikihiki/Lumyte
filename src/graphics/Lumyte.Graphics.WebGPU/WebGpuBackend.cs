namespace Lumyte.Graphics.WebGPU;

/// <summary>Creates WebGPU implementations behind the common graphics backend contract.</summary>
public static class WebGpuBackend
{
    public static IGpuBackend Create() => WebGpuDevice.Create();
}
