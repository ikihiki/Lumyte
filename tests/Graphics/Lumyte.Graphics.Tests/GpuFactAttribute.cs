using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Runs a real-device test only when the GPU test environment is explicitly enabled.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class GpuFactAttribute : FactAttribute
{
    /// <summary>Initializes a new instance of the <see cref="GpuFactAttribute"/> class.</summary>
    public GpuFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("LUMYTE_GRAPHICS_GPU_TESTS") != "1")
        {
            Skip = "Set LUMYTE_GRAPHICS_GPU_TESTS=1 with a working GPU or software driver.";
        }
    }
}
