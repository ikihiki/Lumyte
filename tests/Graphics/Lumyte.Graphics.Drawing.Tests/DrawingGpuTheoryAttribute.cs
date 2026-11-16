using Xunit;

namespace Lumyte.Graphics.Drawing.Tests;

/// <summary>Enables parameterized Vulkan checks only with explicit GPU opt-in.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class DrawingGpuTheoryAttribute : TheoryAttribute
{
    /// <summary>Initializes a new instance of the <see cref="DrawingGpuTheoryAttribute"/> class.</summary>
    public DrawingGpuTheoryAttribute()
    {
        if (Environment.GetEnvironmentVariable("LUMYTE_GRAPHICS_GPU_TESTS") != "1")
        {
            Skip = "Set LUMYTE_GRAPHICS_GPU_TESTS=1 with an explicitly selected Vulkan driver.";
        }
    }
}
