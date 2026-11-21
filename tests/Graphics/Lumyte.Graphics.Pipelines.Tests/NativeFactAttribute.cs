using Xunit;

namespace Lumyte.Graphics.Pipelines.Tests;

/// <summary>Requires an explicitly enabled real native device test.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class NativeFactAttribute : FactAttribute
{
    /// <summary>Initializes a new instance of the <see cref="NativeFactAttribute"/> class.</summary>
    public NativeFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("LUMYTE_GRAPHICS_GPU_TESTS") != "1")
        {
            Skip = "Set LUMYTE_GRAPHICS_GPU_TESTS=1 with an actual native Vulkan device.";
        }
    }
}
