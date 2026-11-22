using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Runs only when actual Vulkan generic-stage verification is explicitly enabled.</summary>
public sealed class GenericGpuNativeFactAttribute : FactAttribute
{
    /// <summary>Initializes a new instance of the <see cref="GenericGpuNativeFactAttribute"/> class.</summary>
    public GenericGpuNativeFactAttribute()
    {
        if (!OperatingSystem.IsLinux() || (Environment.GetEnvironmentVariable("LUMYTE_GENERIC_GPU_TESTS") != "1" && Environment.GetEnvironmentVariable("LUMYTE_GRAPHICS_GPU_TESTS") != "1"))
        {
            Skip = "Set LUMYTE_GENERIC_GPU_TESTS=1 with a built Vulkan assembly and completed stage artifacts.";
        }
    }
}
