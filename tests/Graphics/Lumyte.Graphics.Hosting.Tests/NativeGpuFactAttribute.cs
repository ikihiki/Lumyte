using Xunit;

namespace Lumyte.Graphics.Hosting.Tests;

[AttributeUsage(AttributeTargets.Method)]
internal sealed class NativeGpuFactAttribute : FactAttribute
{
    public NativeGpuFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("LUMYTE_GRAPHICS_GPU_TESTS") != "1")
        {
            Skip = "Enable LUMYTE_GRAPHICS_GPU_TESTS with a GPU or software Vulkan driver.";
        }
    }
}
