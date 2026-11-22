using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shaders;
using Lumyte.Graphics.Wgpu;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Verifies that a real Wgpu device does not advertise or emulate Mesh/Task execution.</summary>
public sealed class WgpuMeshUnsupportedGpuTests
{
    /// <summary>Existing native device creation remains usable, while Mesh modules and programs are explicitly rejected.</summary>
    /// <returns>The capability and rejection verification.</returns>
    [Trait("Execution", "Gpu")]
    [GpuFact]
    public async Task WgpuExplicitlyRejectsMeshAsync()
    {
        using var device = WgpuDevice.Create();
        Assert.Equal(GraphicsFeatures.None, device.Caps.Features & (GraphicsFeatures.MeshShader | GraphicsFeatures.TaskShader));
        Assert.Null(device.Caps.MeshShaderLimits);
        Assert.Null(device.Caps.TaskShaderLimits);
        Assert.Throws<NotSupportedException>(() => device.CreateMeshPipeline(new() { MeshShader = null! }));
        const string Source = """
            struct Vertex { float4 position : SV_Position; };
            [shader("mesh")] [numthreads(1,1,1)] [outputtopology("triangle")]
            void main(out vertices Vertex vertices[3], out indices uint3 indices[1])
            {
                SetMeshOutputCounts(0,0);
            }
            """;
        ShaderArtifact artifact = await new SlangShaderCompiler().CompileAsync(new() { Source = Source, Stage = ShaderStage.Mesh, Target = ShaderTarget.SpirV });
        Assert.Throws<NotSupportedException>(() => device.CreateShader(artifact));
    }
}
