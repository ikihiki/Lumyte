using System.Runtime.InteropServices;

namespace Lumyte.Graphics.Shader.Offline.Tests;

public sealed class SlangReleaseTests
{
    [Fact]
    public void WgslModuleDefinesItsTargetSpecificShaderBranch()
    {
        IReadOnlyList<string> wgsl = SlangPackageCompiler.CreateModuleArguments(
            "shader.slang",
            "shader.wgsl",
            "wgsl");
        IReadOnlyList<string> spirv = SlangPackageCompiler.CreateModuleArguments(
            "shader.slang",
            "shader.spv",
            "spirv");

        Assert.Contains("-DLUMYTE_SHADER_TARGET_WGSL=1", wgsl);
        Assert.DoesNotContain("-DLUMYTE_SHADER_TARGET_WGSL=1", spirv);
    }

    [Theory]
    [InlineData("windows", Architecture.X64, "slang-2026.7.1-windows-x86_64.zip")]
    [InlineData("linux", Architecture.Arm64, "slang-2026.7.1-linux-aarch64.zip")]
    [InlineData("macos", Architecture.X64, "slang-2026.7.1-macos-x86_64.zip")]
    public void ReleaseSelectsPinnedHostArchive(string operatingSystem, Architecture architecture, string expected)
    {
        OSPlatform platform = operatingSystem switch
        {
            "windows" => OSPlatform.Windows,
            "linux" => OSPlatform.Linux,
            _ => OSPlatform.OSX,
        };

        SlangReleaseAsset asset = SlangRelease.GetAsset(platform, architecture);

        Assert.Equal(expected, asset.ArchiveName);
        Assert.Equal(128, asset.Sha512.Length);
    }
}
