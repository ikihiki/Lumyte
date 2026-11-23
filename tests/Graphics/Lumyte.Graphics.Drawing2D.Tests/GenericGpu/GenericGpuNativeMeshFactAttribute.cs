using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Requires a separately opted-in Vulkan device with native mesh shader support.</summary>
public sealed class GenericGpuNativeMeshFactAttribute : FactAttribute
{
    /// <summary>Initializes a new instance of the <see cref="GenericGpuNativeMeshFactAttribute"/> class.</summary>
    public GenericGpuNativeMeshFactAttribute()
    {
        if (!OperatingSystem.IsLinux() || Environment.GetEnvironmentVariable("LUMYTE_GENERIC_MESH_TESTS") != "1")
        {
            Skip = "Set LUMYTE_GENERIC_MESH_TESTS=1 only on a native mesh-capable Vulkan lane; mesh assertions remain mandatory there.";
        }
    }
}
