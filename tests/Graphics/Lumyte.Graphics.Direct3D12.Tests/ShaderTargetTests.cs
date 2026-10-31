using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shaders;
using Xunit;

namespace Lumyte.Graphics.Direct3D12.Tests;

/// <summary>Checks DXIL compilation without creating a GPU device.</summary>
public sealed class ShaderTargetTests
{
    /// <summary>Checks that default artifacts contain all supported backend targets.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task DxilIsIncludedInPortableCompilationAsync()
    {
        var source = new ShaderCompilationDesc { Source = "[numthreads(1, 1, 1)] void main() {}" };
        ShaderArtifact artifact = await new SlangShaderCompiler().CompileAsync(source);
        Assert.NotEmpty(artifact.GetTarget(ShaderTarget.Wgsl).Code.ToArray());
        Assert.NotEmpty(artifact.GetTarget(ShaderTarget.SpirV).Code.ToArray());
        Assert.Equal("DXBC"u8.ToArray(), artifact.GetTarget(ShaderTarget.Dxil).Code[..4]);
    }
}
