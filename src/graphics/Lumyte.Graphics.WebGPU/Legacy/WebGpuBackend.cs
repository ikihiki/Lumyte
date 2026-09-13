namespace Lumyte.Graphics.WebGPU.Legacy;

/// <summary>Creates the legacy WebGPU implementation of the original graphics contract.</summary>
public static class WebGpuBackend
{
    public static IGpuBackend Create() => WebGpuDevice.Create();
}
