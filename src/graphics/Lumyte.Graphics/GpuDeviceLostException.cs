namespace Lumyte.Graphics;

/// <summary>The backend reported device loss. This exception alone does not prove GPU use has ended or resources were released.</summary>
public sealed class GpuDeviceLostException(string message) : InvalidOperationException(message);
