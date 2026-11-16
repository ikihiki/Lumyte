using Xunit;

namespace Lumyte.Maps.Session.Tests;

internal sealed class MapGpuFactAttribute : FactAttribute
{
    public MapGpuFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("LUMYTE_GRAPHICS_GPU_TESTS") != "1"
            && Environment.GetEnvironmentVariable("LUMYTE_MAPS_GPU_TESTS") != "1")
        {
            Skip = "Set LUMYTE_GRAPHICS_GPU_TESTS=1 (or LUMYTE_MAPS_GPU_TESTS=1) to execute actual Vulkan readback.";
        }
    }
}
