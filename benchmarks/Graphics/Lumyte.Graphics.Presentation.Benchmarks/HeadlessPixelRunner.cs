extern alias UiBenchmarks;

using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Wgpu;
using UiProfile = UiBenchmarks::Lumyte.Graphics.UiScenarios.UiProfile;

namespace Lumyte.Graphics.Presentation.Benchmarks;

internal static class HeadlessPixelRunner
{
    internal static int Run(Options options)
    {
        // Explicit GPU correctness entry point. No LinuxPlatform/window/surface is created.
        object provenance = Lumyte.Graphics.Gpu.Benchmarks.Provenance.Capture();
        using var ownership = new RetirementScope();
        WgpuDevice device = ownership.Own(WgpuDevice.Create());
        object nativeAdapter = NativeIdentity.Capture(device);
        UiProfile profile = UiProfile.Standard.Single(p => p.Id == "U03" && p.Variant == "forced-draw");
        object first = UiPixelGate.Verify(device, profile, TextureFormat.Bgra8Srgb, 0, ownership);
        object last = UiPixelGate.Verify(device, profile, TextureFormat.Bgra8Srgb, options.Frames - 1, ownership);
        Program.Emit(new
        {
            status = "headless-u03-pixels-passed",
            provenance,
            nativeAdapter,
            scenario = "UiProfile.Standard U03/forced-draw",
            format = "Bgra8Srgb",
            width = ClearFixture.Width,
            height = ClearFixture.Height,
            dpr = 1,
            checks = new[] { first, last },
            exactEquality = true,
            intermediateFormat = "Rgba8Srgb",
            compositeRoute = "same product present shaders as UiHostPresenter, RGBA8-sRGB intermediate -> BGRA8-sRGB headless output",
            analyticOracle = "raw BGRA readback compared to exact channel-swizzled red/blue clip/order endpoints",
            nativeFontHash = "recorded in each check from actual UiScenario native font asset",
            windowCreated = false,
            surfaceCreated = false,
            presentationProof = false,
            productHostProof = false,
            gpuSpanMs = (double?)null,
            scope = "explicit GPU correctness gate, no performance timing or presentation claim",
        });
        return 0;
    }
}
