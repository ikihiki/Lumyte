namespace Lumyte.Graphics.Portable;

/// <summary>Stages that may access a binding. Pixel maps to the fragment stage.</summary>
[Flags]
public enum GpuShaderStage { None = 0, Vertex = 1, Pixel = 2, Compute = 4 }
