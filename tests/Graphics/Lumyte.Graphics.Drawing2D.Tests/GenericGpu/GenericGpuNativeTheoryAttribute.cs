using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Runs structural native theories on explicitly enabled Linux Vulkan lanes.</summary>
public sealed class GenericGpuNativeTheoryAttribute : TheoryAttribute
{
    /// <summary>Initializes a new instance of the <see cref="GenericGpuNativeTheoryAttribute"/> class.</summary>
    public GenericGpuNativeTheoryAttribute()
    {
        if (!OperatingSystem.IsLinux() || (Environment.GetEnvironmentVariable("LUMYTE_GENERIC_GPU_TESTS") != "1" && Environment.GetEnvironmentVariable("LUMYTE_GRAPHICS_GPU_TESTS") != "1"))
        {
            Skip = "Enable actual Linux Vulkan tests with LUMYTE_GRAPHICS_GPU_TESTS=1.";
        }
    }
}
