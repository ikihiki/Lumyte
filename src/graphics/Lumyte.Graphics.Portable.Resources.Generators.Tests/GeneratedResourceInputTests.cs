using Lumyte.Graphics.Portable.Resources.Tests.Unit.Management;
using Lumyte.Graphics.Portable.Shaders;
using Xunit;
using Inputs = Consumer.@namespace.@struct;

namespace Lumyte.Graphics.Portable.Resources.Generators.Tests;

public sealed class GeneratedResourceInputTests
{
    [Theory]
    [InlineData(8ul, 16ul, 16ul)]
    [InlineData(8ul, ulong.MaxValue, 56ul)]
    public async Task PreparedBindingsUseTypedReferencesAndSelectedBufferRange(ulong offset, ulong length, ulong expectedLength)
    {
        using var backend = new ManagerTestBackend();
        using PortableShaderProgram program = new PortableShaderLoader(backend).Load(Package());
        await using var manager = new GpuResourceManager(backend);
        using var scope = manager.CreateScope();
        GpuBufferRef buffer = scope.CreateBuffer(new(64, GpuBufferUsage.Storage));
        GpuTextureRef texture = scope.CreateTexture(new(GpuTextureDimension.Texture2D, 4, 4, 1, 1, 1, 1,
            GpuFormat.Rgba8Unorm, GpuTextureUsage.Sampled));
        GpuViewRef view = scope.GetView(texture);
        var samplerDescription = new GpuSamplerDescription(AddressU: GpuSamplerAddressMode.Repeat);
        GpuSamplerRef sampler = scope.GetSampler(samplerDescription);
        IGpuBindingInputs inputs = new Inputs(buffer, view, sampler) { writerOffset = offset, writerLength = length };

        GpuBindingsRef bindings = scope.GetBindings(program, Inputs.Group, inputs);

        ManagerTestBackend.Bindings actual = Assert.Single(backend.CreatedBindings);
        Assert.Same(actual, manager.GetBindingsHandle(bindings));
        Assert.Equal(new[]
        {
            GpuBindingEntry.Texture(2, manager.GetTextureView(view)),
            GpuBindingEntry.Buffer(5, manager.GetBufferRange(buffer, offset, expectedLength)),
            GpuBindingEntry.Sampler(8, samplerDescription),
        }, actual.Entries);
    }

    private static PortableShaderPackage Package() => new(PortableShaderPackage.CurrentVersion,
        "@compute @workgroup_size(1) fn main() {}", [new(GpuShaderStage.Compute, "main")], PortableShaderFeatures.None,
        [new([new(5, GpuShaderStage.Compute, new GpuBufferBindingLayout(GpuBufferBindingType.ReadOnlyStorage)),
            new(2, GpuShaderStage.Compute, new GpuTextureBindingLayout(GpuTextureSampleType.Float)),
            new(8, GpuShaderStage.Compute, new GpuSamplerBindingLayout(GpuSamplerBindingType.Filtering))])],
        null, [], new([new("writer", 0, 5, GpuBindingLayoutKind.Buffer),
            new("event", 0, 2, GpuBindingLayoutKind.Texture), new("sampler", 0, 8, GpuBindingLayoutKind.Sampler)]), "consumer-abi");
}
