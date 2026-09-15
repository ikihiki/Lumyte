using System.Collections.Concurrent;
using System.Diagnostics;

using Lumyte.Graphics.RenderGraph.Conformance;
using Lumyte.Graphics.Tests;

namespace Lumyte.Graphics.Vulkan.Tests;

[Collection("GpuBackend")]
[Trait("Category", "VulkanNativeConformance")]
[Trait("Category", "VulkanValidation")]
[Trait("Category", "TwoDConformance")]
public sealed class VulkanTwoDTests
{
    public static IEnumerable<object[]> ModelCases => ModelRenderConsumer.Cases.Select(name => new object[] { name });
    [VulkanNativeTheory]
    [MemberData(nameof(ModelCases))]
    [Trait("Category","ModelConformance")]
    public Task ModelsMatchReference(string scenario) => WithValidationAsync(() => NativeTwoDConformance.ModelAsync("vulkan",CreateBackend,scenario));
    [VulkanNativeFact]
    [Trait("Category", "WindowPresentation")]
    public Task PresentsAndResizesARealWindow() => WithValidationAsync(() => NativeTwoDConformance.PresentWindowAsync(CreateBackend,
        static (backend, hwnd) => ((VulkanBackend)backend).CreateWindowSurface(hwnd)));
    public static IEnumerable<object[]> FilterCases => ImageFilterConsumer.Cases.Select(name => new object[] { name });
    [VulkanNativeTheory]
    [MemberData(nameof(FilterCases))]
    [Trait("Category", "ImageFilterConformance")]
    public Task StandardImageFiltersMatchReference(string scenario) => WithValidationAsync(() => NativeTwoDConformance.FilterAsync("vulkan", CreateBackend, scenario));
    public static IEnumerable<object[]> Cases => TwoDScenarios.Names.Select(name => new object[] { name });
    [VulkanNativeTheory]
    [MemberData(nameof(Cases))]
    public Task MatchesSkiaReference(string scenario) => WithValidationAsync(() => NativeTwoDConformance.CompareAsync("vulkan", CreateBackend, scenario));
    [VulkanNativeTheory]
    [InlineData(GpuFormat.Bgra8Unorm)]
    [InlineData(GpuFormat.Rgba16Float)]
    public Task TargetFormatsPreserveLinearColor(GpuFormat format) => WithValidationAsync(() => NativeTwoDConformance.CompareAsync("vulkan", CreateBackend, "shapes", format));
    [VulkanNativeFact]
    public Task HalfFloatLayersPreserveHdrColor() => WithValidationAsync(() => NativeTwoDConformance.CompareAsync("vulkan", CreateBackend, "hdr-layer", GpuFormat.Rgba16Float));
    [VulkanNativeFact]
    public Task SamplesATextureProducedByAnEarlierGraphPass() => WithValidationAsync(() => NativeTwoDConformance.GraphTextureAsync("vulkan", CreateBackend));
    [VulkanNativeFact]
    public Task RetainedSnapshotsRemainIndependentAcrossSubmissions() => WithValidationAsync(() => NativeTwoDConformance.RetainedSnapshotsAsync("vulkan", CreateBackend));
    private static VulkanBackend CreateBackend() => VulkanBackend.Create(new() { EnableValidation = true });
    private static async Task WithValidationAsync(Func<Task> run)
    {
        using var validation = new ValidationMessages();
        Trace.Listeners.Add(validation);
        try
        { await run(); }
        finally { Trace.Listeners.Remove(validation); }
        Assert.Empty(validation.Messages);
    }
    private sealed class ValidationMessages : TraceListener
    {
        internal ConcurrentQueue<string> Messages { get; } = new();
        public override void Write(string? message) { }
        public override void WriteLine(string? message) { }
        public override void WriteLine(string? message, string? category)
        { if (category == "Vulkan validation") { Messages.Enqueue(message ?? string.Empty); } }
    }
    private sealed class VulkanNativeTheoryAttribute : TheoryAttribute
    { public VulkanNativeTheoryAttribute() => Skip = VulkanTestSupport.UnavailableReason; }
}
