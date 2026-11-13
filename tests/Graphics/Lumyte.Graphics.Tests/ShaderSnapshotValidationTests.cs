using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shaders;
using Lumyte.Graphics.Shared;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks that public snapshots cannot bypass compiled root reference schemas.</summary>
public sealed class ShaderSnapshotValidationTests
{
    private const string Source = """
        #include "lumyte.slang"

        struct SnapshotArguments
        {
            float Value;
            GpuTextureRef Texture;
            GpuSamplerRef Sampler;
            GpuRWBufferRef<uint> Output;
        };

        ConstantBuffer<SnapshotArguments> arguments;

        [shader("compute")]
        [numthreads(1, 1, 1)]
        void main()
        {
            float4 color = LumyteSampleGrad(arguments.Texture, arguments.Sampler,
                float2(0.5, 0.5), float2(0, 0), float2(0, 0));
            arguments.Output.Store(0, uint(arguments.Value + color.x));
        }
        """;

    /// <summary>Rejects malformed resource metadata even when every supplied payload fits its wire size.</summary>
    /// <returns>The completion of shader schema compilation and packing checks.</returns>
    [Fact]
    public async Task PackRejectsMalformedRootSnapshotsAsync()
    {
        ShaderArtifact artifact = await CompileAsync(ShaderTarget.Wgsl);
        var layout = ShaderDataLayout.Root(artifact.GetTarget(ShaderTarget.Wgsl), "arguments");
        ShaderValueSnapshot valid = Snapshot(new object(), new object(), new object());
        Assert.Equal(layout.Size, layout.Pack(valid, _ => new byte[16]).Length);

        var errors = new List<(string Name, Exception? Error)>();
        foreach ((string name, ShaderValueSnapshot invalid) in InvalidSnapshots(valid, new object()).Take(4))
        {
            errors.Add((name, Record.Exception(() => layout.Pack(invalid, _ => new byte[16]))));
        }

#if DEBUG
        AssertRejected(errors);
#else
        AssertRejected(errors.Where(error => error.Name is "duplicate resource slot" or "reference slot outside wire bytes"));
        Assert.All(errors.Where(error => error.Name is not ("duplicate resource slot" or "reference slot outside wire bytes")), error => Assert.Null(error.Error));
#endif
    }

    /// <summary>Checks all five malformed snapshots through the public Vulkan setter and dispatch boundary.</summary>
    /// <returns>The completion of shader compilation and native command recording checks.</returns>
    [GpuFact]
    public async Task VulkanRejectsMalformedRootSnapshotsAsync()
    {
        using var device = VulkanDevice.Create();
        await RejectNativeSnapshotsAsync(device);
    }

    /// <summary>Checks all five malformed snapshots through the public wgpu setter and dispatch boundary.</summary>
    /// <returns>The completion of shader compilation and native command recording checks.</returns>
    [GpuFact]
    public async Task WgpuRejectsMalformedRootSnapshotsAsync()
    {
        using var device = WgpuDevice.Create();
        await RejectNativeSnapshotsAsync(device);
    }

    private static Task<ShaderArtifact> CompileAsync(ShaderTarget target)
        => new SlangShaderCompiler().CompileAsync(new() { Source = Source, Stage = ShaderStage.Compute, Target = target });

    private static async Task RejectNativeSnapshotsAsync(IGraphicDevice device)
    {
        ShaderArtifact artifact = await CompileAsync(device.Caps.ShaderTarget);
        using IGraphicsShader shader = device.CreateShader(artifact);
        using IGraphicsComputePipeline pipeline = device.CreateComputePipeline(new() { ComputeShader = shader });
        using IGraphicsTexture texture = device.CreateTexture(new() { Width = 1, Height = 1, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.Sampled });
        using IGraphicsTextureView view = texture.CreateView();
        using IGraphicsSampler sampler = device.CreateSampler(new());
        using IGraphicsBuffer<uint> output = device.CreateBuffer<uint>(new() { Count = 1, Usage = BufferUsage.ShaderWrite });
        using IArgumentTable table = device.CreateArgumentTable(new() { TextureCapacity = 1, SamplerCapacity = 1, BufferCapacity = 1 });
        IGpuRef<IGraphicsTextureView> textureRef = table.WriteTexture(0, view);
        IGpuRef<IGraphicsSampler> samplerRef = table.WriteSampler(0, sampler);
        IGpuRef<uint> outputRef = table.WriteBuffer(0, output.SliceElements(0, 1), ShaderBufferAccess.ReadWrite);
        ShaderValueSnapshot valid = Snapshot(textureRef, samplerRef, outputRef);

        // A complete, correctly typed root must pass the identical native recording path.
        Assert.Null(RecordDispatch(device, pipeline, table, valid));

        var errors = new List<(string Name, Exception? Error)>();
        foreach ((string name, ShaderValueSnapshot invalid) in InvalidSnapshots(valid, samplerRef))
        {
            errors.Add((name, RecordDispatch(device, pipeline, table, invalid)));
        }

        // Collect every result before asserting so one accepted root cannot hide the other cases.
#if DEBUG
        AssertRejected(errors);
#else
        AssertRejected(errors.Where(error => error.Name is "duplicate resource slot" or "reference slot outside wire bytes"));
        Assert.All(errors.Where(error => error.Name is not ("duplicate resource slot" or "reference slot outside wire bytes")), error => Assert.Null(error.Error));
#endif
    }

    private static Exception? RecordDispatch(IGraphicDevice device, IGraphicsComputePipeline pipeline, IArgumentTable table, ShaderValueSnapshot snapshot)
    {
        // Never submit malformed arguments, even on the unfixed implementation.
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        IComputeEncoder compute = commands.BeginComputePass(new());
        try
        {
            compute.SetPipeline(pipeline);
            compute.SetArgumentTable(table);
            return Record.Exception(() =>
            {
                compute.SetArguments(snapshot);
                compute.Dispatch(1);
            });
        }
        finally
        {
            compute.End();
        }
    }

    private static ShaderValueSnapshot Snapshot(object texture, object sampler, object output)
        => new("arguments", "SnapshotArguments", new byte[52], [new(typeof(IGraphicsTextureView), texture, 4), new(typeof(IGraphicsSampler), sampler, 20), new(typeof(uint), output, 36)], [new("Value", 0, 4, typeof(float)), new("Texture", 4, 16, typeof(ShaderReferenceSlot), true), new("Sampler", 20, 16, typeof(ShaderReferenceSlot), true), new("Output", 36, 16, typeof(ShaderReferenceSlot), true)]);

    private static IEnumerable<(string Name, ShaderValueSnapshot Snapshot)> InvalidSnapshots(ShaderValueSnapshot valid, object sampler)
    {
        yield return ("missing resource slot", valid with { Values = valid.Values.Skip(1).ToArray() });
        yield return ("duplicate resource slot", valid with { Values = [.. valid.Values, valid.Values[0]] });
        yield return ("unknown reference offset", valid with { Values = [valid.Values[0] with { Offset = 5 }, .. valid.Values.Skip(1)] });
        yield return ("reference slot outside wire bytes", valid with { Values = [valid.Values[0] with { Offset = 52 }, .. valid.Values.Skip(1)] });
        ShaderValue[] wrongResource = valid.Values.ToArray();
        wrongResource[0] = wrongResource[0] with { ValueType = typeof(IGraphicsSampler), Reference = sampler };
        yield return ("real sampler reference for texture slot in the selected table", valid with { Values = wrongResource });
    }

    private static void AssertRejected(IEnumerable<(string Name, Exception? Error)> errors)
        => Assert.All(errors, result => Assert.True(
            result.Error is ArgumentException,
            $"{result.Name}: expected ArgumentException before dispatch returns; got {result.Error?.ToString() ?? "no exception"}."));
}
