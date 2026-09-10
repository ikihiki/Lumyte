namespace Lumyte.Graphics;

/// <summary>The native device has stopped executing work. Pending recording resources have been released.</summary>
public sealed class GpuDeviceLostException(string message) : InvalidOperationException(message);
