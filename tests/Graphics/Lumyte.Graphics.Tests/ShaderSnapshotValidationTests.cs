using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shaders;
using Lumyte.Graphics.Shared;
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
    public async Task DiagnosticsRejectMalformedRootSnapshotsAsync()
    {
        ShaderArtifact artifact = await CompileAsync(ShaderTarget.Wgsl);
        var layout = ShaderDataLayout.Root(artifact.GetTarget(ShaderTarget.Wgsl), "arguments");
        ShaderValueSnapshot valid = Snapshot(new object(), new object(), new object());
        ShaderDataLayout.ValidateSnapshot(valid);

        var errors = new List<(string Name, Exception? Error)>();
        foreach ((string name, ShaderValueSnapshot invalid) in InvalidSnapshots(valid, new object()).Take(4))
        {
            errors.Add((name, Record.Exception(() => ShaderDataLayout.ValidateSnapshot(invalid))));
        }

        foreach ((string name, ShaderValueSnapshot invalid) in InvalidSnapshots(valid, new object()).Take(4))
        {
            Assert.Throws<ArgumentException>(() => layout.ValidateArguments(invalid, validateReferences: true));
        }

        AssertRejected(errors.Where(error => error.Name is "duplicate resource slot" or "reference slot outside wire bytes"));
        Assert.All(errors.Where(error => error.Name is not ("duplicate resource slot" or "reference slot outside wire bytes")), error => Assert.Null(error.Error));
    }

    private static Task<ShaderArtifact> CompileAsync(ShaderTarget target)
        => new SlangShaderCompiler().CompileAsync(new() { Source = Source, Stage = ShaderStage.Compute, Target = target });

    private static ShaderValueSnapshot Snapshot(object texture, object sampler, object output)
        => new("arguments", "SnapshotArguments", new byte[52], [new(typeof(IGraphicsTextureView), texture, 4), new(typeof(IGraphicsSampler), sampler, 20), new(typeof(uint), output, 36)], [new("Value", 0, 4, typeof(float)), new("Texture", 4, 16, typeof(ShaderReferenceSlot), true), new("Sampler", 20, 16, typeof(ShaderReferenceSlot), true), new("Output", 36, 16, typeof(ShaderReferenceSlot), true)]);

    private static IEnumerable<(string Name, ShaderValueSnapshot Snapshot)> InvalidSnapshots(ShaderValueSnapshot valid, object sampler)
    {
        yield return ("missing resource slot", valid with { Values = valid.Values.Skip(1).ToArray() });
        yield return ("duplicate resource slot", valid with { Values = [.. valid.Values, valid.Values[0]] });

        // Host-safe and disjoint, but inconsistent with the shader field at offset 4.
        yield return ("unknown reference offset", valid with { Values = [valid.Values[0] with { Offset = 0 }, .. valid.Values.Skip(1)] });
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
