using System.Reflection;
using BenchmarkDotNet.Attributes;
using Lumyte.Graphics.Abstractions;
using Lumyte.UI.Application.Desktop.Benchmark.BackendCpu;
using Xunit;
using Xunit.Abstractions;

namespace Lumyte.UI.Benchmark.Tests;

/// <summary>Separates native CPU command discard from untimed actual GPU correctness validation.</summary>
/// <param name="output">The retained functional proof output.</param>
public sealed class NativeUiCpuTests(ITestOutputHelper output)
{
    /// <summary>Checks that only the two explicitly named small-scene backend CPU methods are registered.</summary>
    [Fact]
    public void NativeCpuCasesAreBoundedAndDoNotClaimPresentation()
    {
        Type type = typeof(NativeUiRecordingBenchmarks);
        MethodInfo[] methods = type.GetMethods().Where(method => method.IsDefined(typeof(BenchmarkAttribute))).ToArray();
        Assert.Equal(2, methods.Length);
        Assert.All(methods, method => Assert.Equal(typeof(Task<CommandBufferState>), method.ReturnType));
        Assert.Equal(new[] { "Comparison" }, type.GetCustomAttribute<BenchmarkCategoryAttribute>()!.Categories);
        Assert.DoesNotContain(type.GetProperties(), property => property.IsDefined(typeof(ParamsAttribute)));
        using var fixture = new NativeUiRecordingFixture();
        Assert.Null(fixture.AdapterIdentity);
        Assert.Equal(0, fixture.ValidationSubmissions);
    }

    /// <summary>Checks warm reuse, fresh renderer recording, native unsubmitted disposal, slot release and full analytic GPU pixels.</summary>
    /// <returns>The bounded hardware functional proof; no timing campaign is performed.</returns>
    [NativeCpuGpuFact]
    public async Task NativeCpuDiscardAndEquivalentHardwarePixelsAsync()
    {
        using var fixture = new NativeUiRecordingFixture();
        await fixture.StartAsync();
        output.WriteLine(fixture.AdapterIdentity);
        int validated = fixture.ValidationSubmissions;
        long reused = fixture.WarmStatistics.ReusedRuns;
        Assert.Equal(2, validated);
        for (int iteration = 0; iteration < 4; iteration++)
        {
            Assert.Equal(CommandBufferState.Disposed, await fixture.WarmRecordAsync());
            Assert.Equal(CommandBufferState.Disposed, await fixture.ColdRendererRecordAsync());
            Assert.Equal(0, fixture.WarmStatistics.ActiveFrames);
            Assert.Equal(validated, fixture.ValidationSubmissions);
        }

        Assert.True(fixture.WarmStatistics.ReusedRuns > reused);
        await fixture.ValidatePixelsAsync();
        Assert.Equal(validated + 2, fixture.ValidationSubmissions);
        Assert.Equal(0, fixture.WarmStatistics.ActiveFrames);
        output.WriteLine($"Supported small-scene profile: 100 UI rectangles; target=128x128; warm/cold discarded native recordings=4 each; explicit validation submissions={fixture.ValidationSubmissions}; active retained frames=0; every warm/cold GPU pixel matched analytic expected output.");
    }

    /// <summary>Skips native work unless the caller explicitly opted in; runtime device audit independently rejects software fallback.</summary>
    private sealed class NativeCpuGpuFactAttribute : FactAttribute
    {
        /// <summary>Initializes a new instance of the <see cref="NativeCpuGpuFactAttribute"/> class.</summary>
        public NativeCpuGpuFactAttribute()
        {
            Skip = NativeUiRecordingFixture.UnavailableReason();
        }
    }
}
