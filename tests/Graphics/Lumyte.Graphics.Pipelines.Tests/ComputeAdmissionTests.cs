using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Pipelines.Tests;

/// <summary>Distinguishes actual Browser policy from the shared WGSL shader target.</summary>
public sealed class ComputeAdmissionTests
{
    /// <summary>Native WGSL is admitted without a public policy bypass.</summary>
    [Fact]
    public void NativeWgslComputeIsNotBrowser()
    {
        var caps = new DeviceCaps { ShaderTarget = ShaderTarget.Wgsl, MaxComputeInvocationsPerWorkgroup = 64 };
        GraphicsComputeAdmission.Validate(caps, browser: false);
        Assert.Throws<NotSupportedException>(() => GraphicsComputeAdmission.Validate(caps, browser: true));
    }

    /// <summary>Missing compute caps reject independently of target and Browser policy.</summary>
    [Fact]
    public void MissingComputeCapabilitiesStillReject()
    {
        foreach (ShaderTarget target in Enum.GetValues<ShaderTarget>())
        {
            Assert.Throws<NotSupportedException>(() => GraphicsComputeAdmission.Validate(new() { ShaderTarget = target }, browser: false));
            Assert.Throws<NotSupportedException>(() => GraphicsComputeAdmission.Validate(new() { ShaderTarget = target, MaxComputeInvocationsPerWorkgroup = 64 }, browser: true));
        }
    }
}
