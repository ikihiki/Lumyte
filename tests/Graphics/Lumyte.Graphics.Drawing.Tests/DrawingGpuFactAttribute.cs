using Xunit;

namespace Lumyte.Graphics.Drawing.Tests;

/// <summary>Enables Vulkan pixel tests only in an explicitly configured GPU test environment.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class DrawingGpuFactAttribute : FactAttribute
{
    /// <summary>Initializes a new instance of the <see cref="DrawingGpuFactAttribute"/> class.</summary>
    public DrawingGpuFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("LUMYTE_GRAPHICS_GPU_TESTS") != "1")
        {
            Skip = "Set LUMYTE_GRAPHICS_GPU_TESTS=1 with a Vulkan GPU or software driver.";
        }
    }
}
