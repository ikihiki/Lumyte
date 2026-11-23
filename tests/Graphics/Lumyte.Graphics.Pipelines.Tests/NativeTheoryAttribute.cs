using Xunit;

namespace Lumyte.Graphics.Pipelines.Tests;

/// <summary>Requires an explicitly enabled actual Vulkan execution lane.</summary>
public sealed class NativeTheoryAttribute : TheoryAttribute
{
    /// <summary>Initializes a new instance of the <see cref="NativeTheoryAttribute"/> class.</summary>
    public NativeTheoryAttribute()
    {
        if (Environment.GetEnvironmentVariable("LUMYTE_GRAPHICS_GPU_TESTS") != "1")
        {
            Skip = "Set LUMYTE_GRAPHICS_GPU_TESTS=1 with an actual native Vulkan device.";
        }
    }
}
