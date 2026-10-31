using System.Reflection;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Samples;
using Lumyte.Graphics.Shaders;
using Lumyte.Graphics.Shared;
using Xunit;

namespace Lumyte.Graphics.Direct3D12.Tests;

/// <summary>Uses the same exercises as Vulkan; GPU tests are explicitly opt-in.</summary>
public sealed class ParityTests
{
    /// <summary>Compiles every shared sample shader to DXIL without loading D3D12.</summary>
    [Fact]
    public void OfflineSamplesContainDxil()
    {
        Assembly assembly = typeof(PipelineExercise).Assembly;
        string[] shaders = assembly.GetManifestResourceNames().Where(n => n.EndsWith(".lshader", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(shaders);
        foreach (string name in shaders)
        {
            ShaderTargetData target = ShaderArtifact.LoadEmbedded(assembly, name).GetTarget(ShaderTarget.Dxil);
            Assert.Equal("DXBC"u8.ToArray(), target.Code[..4]);
        }
    }

    /// <summary>Checks DXIL helper reflection and root binding without GPU execution.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task OnlineHelperCompilesAsync()
    {
        using Stream source = typeof(ShaderBindingExercise).Assembly.GetManifestResourceStream("Lumyte.Shaders.binding-compute.slang")!;
        using var reader = new StreamReader(source);
        ShaderArtifact artifact = await new SlangShaderCompiler().CompileAsync(new() { Source = await reader.ReadToEndAsync(), Target = ShaderTarget.Dxil });
        ShaderTargetData target = artifact.GetTarget(ShaderTarget.Dxil);
        Assert.Contains("lumyteAbi", target.ReflectionJson);
        Assert.Contains("BindingNode", target.ReflectionJson);
        Assert.NotEmpty(target.Code.ToArray());
        ShaderTargetData offline = ShaderArtifact.LoadEmbedded(typeof(ShaderBindingExercise).Assembly, "Lumyte.Shaders.binding-compute.lshader").GetTarget(ShaderTarget.Dxil);
        Assert.True(ShaderDataLayout.Root(target, "arguments").Matches(ShaderDataLayout.Root(offline, "arguments")));
        Assert.True(ShaderDataLayout.Data(target, "BindingNode").Matches(ShaderDataLayout.Data(offline, "BindingNode")));
    }

    /// <summary>Checks DXIL constant packing, descriptor bindings and cross-stage schema rejection.</summary>
    [Fact]
    public void DxilLayoutAndProgramContracts()
    {
        ShaderTargetData Target(string name) => ShaderArtifact.LoadEmbedded(typeof(ShaderBindingExercise).Assembly, "Lumyte.Shaders." + name + ".lshader").GetTarget(ShaderTarget.Dxil);
        ShaderTargetData compute = Target("binding-compute");
        var root = ShaderDataLayout.Root(compute, "arguments");
        Assert.Equal(112, root.Size);
        Assert.Equal(20, ShaderDataLayout.Data(compute, "BindingNode").Size);
        using var json = System.Text.Json.JsonDocument.Parse(compute.ReflectionJson);
        System.Text.Json.JsonElement binding = json.RootElement.GetProperty("parameters").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "arguments").GetProperty("binding");
        Assert.Equal("constantBuffer", binding.GetProperty("kind").GetString());
        Assert.Equal(8, binding.GetProperty("index").GetInt32());
        Assert.Throws<ArgumentException>(() => ShaderDataLayout.ValidateProgram(Target("binding-mismatch-vertex"), Target("binding-mismatch-fragment")));
        ShaderDataLayout.ValidateProgram(Target("binding-cycle-vertex"), Target("binding-cycle-fragment"));
    }

    /// <summary>Checks transfer, texture, sampler, argument, render, compute, indirect and semaphore parity.</summary>
    /// <returns>The asynchronous verification.</returns>
    [AgilityFact]
    public async Task SharedVulkanExercisesAsync()
    {
        using var device = Direct3D12Device.Create(new() { UseWarp = true, EnableDebugLayer = true });
        _ = await BufferExercise.RunAsync(device);
        _ = TextureExercise.Run(device);
        _ = SamplerExercise.Run(device);
        _ = ArgumentTableExercise.Run(device);
        _ = ShaderExercise.Run(device);
        _ = await CommandExercise.RunAsync(device);
        _ = await PipelineExercise.RunAsync(device);
        _ = await ShaderBindingExercise.RunAsync(device);
        _ = await AdvancedCommandExercise.RunAsync(device);
        _ = await SemaphoreExercise.RunAsync(device);
        _ = await RenderGraphExercise.RunAsync(device);
    }

    /// <summary>Exercises partial compilation and state variants only on explicitly enabled hardware.</summary>
    /// <returns>The asynchronous verification.</returns>
    [PartialProgramsFact]
    public async Task PreviewPartialProgramsAsync()
    {
        using var device = Direct3D12Device.Create(new() { EnablePartialGraphicsPrograms = true, EnableDebugLayer = true });
        Assert.True(device.PartialGraphicsProgramsEnabled);
        _ = await PipelineExercise.RunAsync(device);
        _ = await AdvancedCommandExercise.RunAsync(device);
    }
}
