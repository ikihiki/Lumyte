namespace Lumyte.Graphics.Native.Shaders.Tests;

internal static class ShaderFixtures
{
    internal static readonly NativeGpuDescriptorLimits UnifiedLayout = new(64, 32, 64, 32, 32, 32, 16, 16);

    internal static NativeShaderStageArtifact Stage(GpuShaderStage stage = GpuShaderStage.Compute,
        byte[]? code = null, string entry = "kernel") => new(stage, entry, code ?? [7, 11, 19]);

    internal static NativeShaderInputLayout Layout(string id = "root") => new(id, 16, 4,
        [new("value", NativeShaderInputFieldKind.Scalar, 0, 4), new("output", NativeShaderInputFieldKind.GpuAddress, 8, 8)]);

    internal static NativeShaderArtifact Artifact(NativeShaderTarget target = NativeShaderTarget.Vulkan,
        NativeShaderCapabilities required = NativeShaderCapabilities.None,
        NativeShaderDescriptorHeapAbi? abi = null, NativeShaderStageArtifact[]? stages = null, string hash = "native-v1")
        => new(target, target == NativeShaderTarget.Vulkan ? GpuShaderCodeFormat.SpirV : GpuShaderCodeFormat.Dxil,
            stages ?? [Stage()], required, abi ?? NativeShaderDescriptorHeapAbi.None, Layout(), [], hash);

    internal static NativeShaderPackage Package(params NativeShaderArtifact[] artifacts)
        => new(NativeShaderPackage.CurrentVersion, artifacts);
}
