using Lumyte.Graphics.RenderGraph.Conformance;
using Lumyte.Graphics.Tests;

namespace Lumyte.Graphics.DirectX12.Tests;

[Collection("GpuBackend")]
[Trait("Category", "DirectX12Conformance")]
[Trait("Category", "DirectX12Validation")]
[Trait("Category", "TwoDConformance")]
public sealed class DirectX12TwoDTests
{
    public static IEnumerable<object[]> Cases => TwoDScenarios.Names.Select(name => new object[] { name });
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task MatchesSkiaReference(string scenario)
    {
        using var validation = new DirectX12ValidationScope();
        await NativeTwoDConformance.CompareAsync("dx12", validation.CreateBackend, scenario);
        validation.AssertNoWarningsOrErrors();
    }
    [Theory]
    [InlineData(GpuFormat.Bgra8Unorm)]
    [InlineData(GpuFormat.Rgba16Float)]
    public async Task TargetFormatsPreserveLinearColor(GpuFormat format)
    {
        using var validation = new DirectX12ValidationScope();
        await NativeTwoDConformance.CompareAsync("dx12", validation.CreateBackend, "shapes", format);
        validation.AssertNoWarningsOrErrors();
    }
    [Fact]
    public async Task HalfFloatLayersPreserveHdrColor()
    {
        using var validation = new DirectX12ValidationScope();
        await NativeTwoDConformance.CompareAsync("dx12", validation.CreateBackend, "hdr-layer", GpuFormat.Rgba16Float);
        validation.AssertNoWarningsOrErrors();
    }
    [Fact]
    public async Task SamplesATextureProducedByAnEarlierGraphPass()
    {
        using var validation = new DirectX12ValidationScope();
        await NativeTwoDConformance.GraphTextureAsync("dx12", validation.CreateBackend);
        validation.AssertNoWarningsOrErrors();
    }
    [Fact]
    public async Task RetainedSnapshotsRemainIndependentAcrossSubmissions()
    {
        using var validation = new DirectX12ValidationScope();
        await NativeTwoDConformance.RetainedSnapshotsAsync("dx12", validation.CreateBackend);
        validation.AssertNoWarningsOrErrors();
    }
}
