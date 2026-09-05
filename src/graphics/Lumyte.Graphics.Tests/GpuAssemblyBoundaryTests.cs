using System.Reflection;
using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Tests;

public sealed class GpuAssemblyBoundaryTests
{
    [Fact]
    public void CoreRuntimeDoesNotDependOnContainerOrCompilerPackages()
    {
        string[] references = typeof(IGpuBackend).Assembly.GetReferencedAssemblies()
            .Select(static reference => reference.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain("MessagePack", references);
        Assert.DoesNotContain("Lumyte.Graphics.Shader", references);
        Assert.DoesNotContain("Lumyte.Graphics.Shader.Offline", references);
    }

    [Fact]
    public void RenderGraphDependsOnCoreWithoutOwningShaderPackaging()
    {
        Assembly assembly = typeof(GpuRenderGraph).Assembly;
        string[] references = assembly.GetReferencedAssemblies()
            .Select(static reference => reference.Name ?? string.Empty)
            .ToArray();

        Assert.NotEqual(typeof(IGpuBackend).Assembly, assembly);
        Assert.Contains("Lumyte.Graphics", references);
        Assert.DoesNotContain("Lumyte.Graphics.Shader", references);
        Assert.DoesNotContain("Lumyte.Graphics.Shader.Offline", references);
    }

    [Fact]
    public void BackendContractConsumesReadyShaderIr()
    {
        MethodInfo raster = typeof(IGpuBackend).GetMethod(nameof(IGpuBackend.CreateRasterPipeline))!;
        MethodInfo compute = typeof(IGpuBackend).GetMethod(nameof(IGpuBackend.CreateComputePipeline))!;

        Assert.Equal(
            [typeof(GpuRasterPipelineDescription), typeof(GpuShaderBinary), typeof(GpuShaderBinary)],
            raster.GetParameters().Select(static parameter => parameter.ParameterType));
        Assert.Equal([typeof(GpuShaderBinary)], compute.GetParameters().Select(static parameter => parameter.ParameterType));
    }
}
