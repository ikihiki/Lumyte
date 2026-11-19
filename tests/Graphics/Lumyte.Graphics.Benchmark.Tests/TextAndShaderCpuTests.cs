using System.Buffers.Binary;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Benchmarks.Testing;
using Lumyte.Graphics.Generators.Benchmark;
using Lumyte.Graphics.Shaders.Benchmark;
using Lumyte.Graphics.Text;
using Lumyte.Graphics.Text.Benchmark;
using Lumyte.Graphics.Text.Parley;
using Lumyte.Graphics.Text.Parley.Benchmark;
using Xunit;

namespace Lumyte.Graphics.Benchmark.Tests;

/// <summary>Functional public-API coverage, independent of benchmark timing or allocation budgets.</summary>
public sealed class TextAndShaderCpuTests
{
    /// <summary>Snapshot copying and both real multichannel generators produce observable output.</summary>
    /// <returns>The completed staging validation.</returns>
    [Fact]
    public async Task TextSnapshotAndAtlasUseRealCpuPathsAsync()
    {
        var fixture = new TextSnapshotBenchmarks { Outlines = 16 };
        fixture.Setup();
        try
        {
            Assert.Equal(16, fixture.Snapshot().Geometry.Count);
            DistanceFieldImage2D msdf = fixture.GenerateMsdf();
            DistanceFieldImage2D mtsdf = fixture.GenerateMtsdf();
            Assert.Equal(msdf.Width * msdf.Height * 4, msdf.Pixels.Length);
            Assert.NotEqual(msdf.Pixels.ToArray(), mtsdf.Pixels.ToArray());
            CpuGraphicsCounters counters = await fixture.StageAtlasAsync();
            Assert.Equal(1, counters.TextureCreates);
            Assert.Equal(1, counters.Copies);
            Assert.True(counters.UploadedBytes > 0);
            Assert.Equal(0, counters.Submissions);
        }
        finally
        {
            fixture.Cleanup();
        }
    }

    /// <summary>Generated transfer sizes and reference counts match actual snapshots.</summary>
    /// <returns>The completed typed transfer validation.</returns>
    [Fact]
    public async Task GeneratedBulkAndBindingsUseCurrentStructAbiAsync()
    {
        var fixture = new ShaderBulkBenchmarks { Roots = 128 };
        fixture.Setup();
        try
        {
            ShaderValueSnapshot numeric = fixture.CaptureNumeric();
            ShaderValueSnapshot references = fixture.CaptureReferences();
            Assert.Equal(SizeOf<NumericArguments>(), numeric.Data.Length);
            Assert.Equal(80, numeric.Data.Length);
            Assert.Equal(SizeOf<ReferencedArguments>(), references.Data.Length);
            Assert.Equal(2, references.Values.Count);
            Assert.Equal(2, ReferencesOf<ReferencedArguments>());
            Assert.Equal(17u, BinaryPrimitives.ReadUInt32LittleEndian(numeric.Data.Span[76..]));
            Assert.NotEqual(0ul, fixture.RecordReferenceBindings());
            Assert.Equal((ulong)(128 * SizeOf<NumericArguments>()), await fixture.CopyTypedBulkAsync());
        }
        finally
        {
            fixture.Cleanup();
        }
    }

    /// <summary>Incremental reuse is equivalent, semantic edits differ, and consumer compilation emits.</summary>
    [Fact]
    public void GeneratorExecutesAndEmitsConsumer()
    {
        var fixture = new ShaderGeneratorBenchmarks { Declarations = 16 };
        fixture.Setup();
        Assert.Equal(fixture.FreshGeneration(), fixture.IncrementalWarm());
        Assert.NotEqual(fixture.FreshGeneration(), fixture.FreshChangedGeneration());
        Assert.Equal(fixture.FreshChangedGeneration(), fixture.SemanticDeclarationChange());
        Assert.Equal(fixture.FreshGeneration(), fixture.IncrementalWarm());
        Assert.True(fixture.EmitConsumer() > 0);
        Assert.True(fixture.EmitConsumer(changed: true) > 0);
    }

    /// <summary>CPU compilation yields a real target artifact which survives transport parsing.</summary>
    /// <returns>The completed external compiler verification.</returns>
    [Fact]
    [Trait("Execution", "Integration")]
    public async Task ShaderCompilerUsesRealSlangArtifactAsync()
    {
        var fixture = new ShaderCompilerBenchmarks();
        await fixture.SetupAsync();
        ShaderTargetData parsed = fixture.ReadArtifact();
        ShaderArtifact compiled = await fixture.CompileSpirVAsync();
        Assert.NotEmpty(parsed.Code);
        Assert.Equal(ShaderTarget.SpirV, parsed.Target);
        Assert.Equal(ShaderStage.Compute, parsed.Stage);
        Assert.NotEmpty(compiled.GetTarget(ShaderTarget.SpirV).ReflectionJson);
    }

    /// <summary>Native text snapshots survive cold engine disposal and contain editing geometry.</summary>
    /// <param name="paragraph">The bounded paragraph profile.</param>
    [Theory]
    [InlineData("Latin")]
    [InlineData("Multiscript")]
    [InlineData("Wrapped")]
    [Trait("Execution", "Integration")]
    public void ParleyShapesLicensedFontsAndRetainsSnapshots(string paragraph)
    {
        var fixture = new ParleyLayoutBenchmarks { Paragraph = paragraph };
        fixture.Setup();
        try
        {
            ParleyTextLayout2D warm = fixture.ShapeRetainedEngine();
            ParleyTextLayout2D cold = fixture.ShapeColdEngine();
            Assert.NotEmpty(warm.Glyphs);
            Assert.NotEmpty(warm.Layout.Geometry);
            Assert.NotEmpty(fixture.GenerateShapedOutlineMtsdf().Pixels.ToArray());
            Assert.NotEmpty(warm.Carets);
            Assert.NotEmpty(warm.SelectionRects);
            Assert.Equal(warm.Layout.Bounds, cold.Layout.Bounds);
            Assert.Equal(warm.Layout.Text, fixture.CopyCachedSnapshot().Text);
            Assert.True(fixture.ReadCachedEditingGeometry() > warm.Glyphs.Count);
            if (paragraph == "Wrapped")
            {
                Assert.True(warm.Layout.LineCount > 1);
            }
        }
        finally
        {
            fixture.Cleanup();
        }
    }

    private static int SizeOf<T>()
        where T : struct, IShaderArguments => T.TransferSize;

    private static int ReferencesOf<T>()
        where T : struct, IShaderArguments => T.ReferenceCount;
}
