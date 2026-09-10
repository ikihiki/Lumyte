namespace Lumyte.Graphics.Native;

public sealed record NativeGpuBackendOptions
{
    /// <summary>Requests the native API's validation facility; unavailable validation is reported.</summary>
    public bool EnableValidation { get; init; }
}
