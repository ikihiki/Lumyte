using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shaders;
using Xunit;

namespace Lumyte.Graphics.Direct3D12.Tests;

/// <summary>Prevents reserved DXIL metadata from changing existing shader compilation.</summary>
public sealed class ShaderTargetTests
{
    /// <summary>Rejects DXIL before invoking a compiler and preserves the default portable targets.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task ReservedDxilDoesNotChangePortableCompilationAsync()
    {
        var source = new ShaderCompilationDesc { Source = "[numthreads(1, 1, 1)] void main() {}" };
        await Assert.ThrowsAsync<NotSupportedException>(() => new SlangShaderCompiler("missing-compiler").CompileAsync(source with { Target = ShaderTarget.Dxil }));
        ShaderArtifact artifact = await new SlangShaderCompiler().CompileAsync(source);
        Assert.NotEmpty(artifact.GetTarget(ShaderTarget.Wgsl).Code.ToArray());
        Assert.NotEmpty(artifact.GetTarget(ShaderTarget.SpirV).Code.ToArray());
        Assert.Throws<NotSupportedException>(() => artifact.GetTarget(ShaderTarget.Dxil));
    }
}
