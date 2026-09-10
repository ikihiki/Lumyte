namespace Lumyte.Graphics.Native;

/// <summary>A failed native GPU API call, preserving its HRESULT or VkResult.</summary>
public sealed class NativeGpuException : Exception
{
    public NativeGpuException(string message, long nativeErrorCode)
        : base(message)
        => NativeErrorCode = nativeErrorCode;

    public long NativeErrorCode { get; }
}
