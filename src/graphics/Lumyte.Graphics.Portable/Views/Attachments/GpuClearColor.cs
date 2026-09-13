namespace Lumyte.Graphics.Portable;

/// <summary>Color clear components, converted to the attachment's format by the runtime.</summary>
public readonly record struct GpuClearColor(double Red, double Green, double Blue, double Alpha);
