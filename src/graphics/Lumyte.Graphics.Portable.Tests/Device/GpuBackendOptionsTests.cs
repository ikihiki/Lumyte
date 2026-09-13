namespace Lumyte.Graphics.Portable.Tests.Device;

public sealed class GpuBackendOptionsTests
{
    [Fact]
    public void ReplacingRequirementsPreservesTheOriginalOptions()
    {
        var requirements = new GpuRequiredLimits
        {
            MaxBufferSize = (1ul << 40) + 256,
            MinStorageBufferOffsetAlignment = 128,
        };
        var original = new GpuBackendOptions { RequireIndirectFirstInstance = true, RequiredLimits = requirements };

        GpuBackendOptions changed = original with
        {
            RequireDualSourceBlend = true,
            RequiredLimits = original.RequiredLimits with { MaxImmediateSize = 192 },
        };

        Assert.Equal((false, true, requirements),
            (original.RequireDualSourceBlend, original.RequireIndirectFirstInstance, original.RequiredLimits));
        Assert.Equal(192u, changed.RequiredLimits.MaxImmediateSize);
    }

    [Fact]
    public void AnUnspecifiedLimitIsDifferentFromAnExplicitZero()
    {
        var unspecified = new GpuBackendOptions();

        GpuBackendOptions explicitZero = unspecified with
        {
            RequiredLimits = unspecified.RequiredLimits with { MaxStorageTexturesPerShaderStage = 0 },
        };

        Assert.Null(unspecified.RequiredLimits.MaxStorageTexturesPerShaderStage);
        Assert.Equal(0u, explicitZero.RequiredLimits.MaxStorageTexturesPerShaderStage);
    }
}
