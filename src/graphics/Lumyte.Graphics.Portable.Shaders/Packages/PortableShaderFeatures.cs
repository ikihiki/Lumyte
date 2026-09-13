namespace Lumyte.Graphics.Portable.Shaders;

/// <summary>Requirements whose availability is represented by the Portable device contract.</summary>
/// <remarks>This is not a list of every WGSL extension. Language and shader validation belongs to the runtime.</remarks>
[Flags]
public enum PortableShaderFeatures
{
    None = 0,
    ImmediateAddressSpace = 1,
    DualSourceBlend = 2,
}

public enum PortableShaderProgramKind { Raster, Compute }

/// <summary>An entry in the package's single WGSL module. Pixel maps to the fragment stage.</summary>
public readonly record struct PortableShaderEntryPoint(GpuShaderStage Stage, string Name);
