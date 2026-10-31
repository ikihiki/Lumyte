using Xunit;

namespace Lumyte.Graphics.Direct3D12.Tests;

/// <summary>Requires explicit opt-in on a Preview-capable driver, separately from ordinary GPU tests.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class PartialProgramsFactAttribute : FactAttribute
{
    /// <summary>Initializes a new instance of the <see cref="PartialProgramsFactAttribute"/> class.</summary>
    public PartialProgramsFactAttribute()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("LUMYTE_GRAPHICS_GPU_TESTS") != "1" || Environment.GetEnvironmentVariable("LUMYTE_D3D12_PARTIAL_TESTS") != "1")
        {
            Skip = "Requires Preview, a supporting driver, and both LUMYTE_GRAPHICS_GPU_TESTS=1 and LUMYTE_D3D12_PARTIAL_TESTS=1.";
        }
    }
}
