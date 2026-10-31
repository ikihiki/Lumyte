using Xunit;

namespace Lumyte.Graphics.Direct3D12.Tests;

/// <summary>Runs parameterized native SDK tests only in an explicitly enabled Windows GPU environment.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AgilityTheoryAttribute : TheoryAttribute
{
    /// <summary>Initializes a new instance of the <see cref="AgilityTheoryAttribute"/> class.</summary>
    public AgilityTheoryAttribute()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("LUMYTE_GRAPHICS_GPU_TESTS") != "1")
        {
            Skip = "Requires Windows and LUMYTE_GRAPHICS_GPU_TESTS=1; Preview additionally requires Developer Mode.";
        }
    }
}
