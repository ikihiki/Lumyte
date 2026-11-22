using Xunit;

namespace Lumyte.Graphics.Pipelines.RenderGraph.Tests;

/// <summary>Runs an explicitly enabled actual native Vulkan fixture.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class NativeFactAttribute : FactAttribute
{
    /// <summary>Initializes a new instance of the <see cref="NativeFactAttribute"/> class.</summary>
    public NativeFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("LUMYTE_GRAPHICS_GPU_TESTS") != "1")
        {
            Skip = "Set LUMYTE_GRAPHICS_GPU_TESTS=1 for actual native execution.";
        }
    }
}
