using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Validation;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Declarations are live ranges; only an explicit CPU transfer captures application values.</summary>
public sealed class ExplicitTransferBoundaryTests
{
    /// <summary>Generated capture owns numeric bytes even when its source is a mutable class.</summary>
    [Fact]
    public void GeneratedClassSnapshotsOwnPrivateNumericBacking()
    {
        var value = new ExplicitTransferValue { Value = 7 };
        ShaderValueSnapshot before = IShaderArguments.Capture(in value);
        value.Value = 999;
        ShaderValueSnapshot after = IShaderArguments.Capture(in value);
        Assert.Equal(7u, MemoryMarshal.Read<uint>(before.Data.Span));
        Assert.Equal(999u, MemoryMarshal.Read<uint>(after.Data.Span));
        Assert.Equal(20, before.Data.Length);
        Assert.Contains(before.GetDiagnosticFields(), field => field.Path == "Target" && field.Offset == 4 && field.IsReference);
    }

    /// <summary>Vulkan observes mutations only at explicit CopyFrom boundaries, in both validation modes.</summary>
    /// <returns>The completed native readback checks.</returns>
    [GpuFact]
    public async Task VulkanDeclarationsStayLiveUntilExplicitTransferAsync()
    {
        ShaderArtifact artifact = await ExplicitTransferExercise.CompileAsync(ShaderTarget.SpirV);
        foreach (bool validation in new[] { false, true })
        {
            using var raw = VulkanDevice.Create();
            using ValidationDevice? layer = validation ? new(raw) : null;
            IGraphicDevice device = layer ?? (IGraphicDevice)raw;

            await ExplicitTransferExercise.CheckAsync(device, artifact, validation);
        }
    }

    /// <summary>Wgpu observes mutations only at explicit CopyFrom boundaries, in both validation modes.</summary>
    /// <returns>The completed native readback checks.</returns>
    [GpuFact]
    public async Task WgpuDeclarationsStayLiveUntilExplicitTransferAsync()
    {
        ShaderArtifact artifact = await ExplicitTransferExercise.CompileAsync(ShaderTarget.Wgsl);
        foreach (bool validation in new[] { false, true })
        {
            using var raw = WgpuDevice.Create();
            using ValidationDevice? layer = validation ? new(raw) : null;
            IGraphicDevice device = layer ?? (IGraphicDevice)raw;

            await ExplicitTransferExercise.CheckAsync(device, artifact, validation);
        }
    }
}
