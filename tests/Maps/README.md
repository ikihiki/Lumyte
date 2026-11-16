# Maps verification

All fixtures are hand-authored, with no network or redistributed OSM data.

- Lumyte.Maps.Tests: coordinate normalization, layer extent, ring/hole/multiple exteriors,
  malformed/truncated protobuf and commands, independent limits, local source/stage identity,
  observed revision and manager sharing.
- Lumyte.Maps.Session.Tests: bounded admission, queue-full retry, zero viewport, stop drain,
  cache-hit accounting and generation pins; opt-in real Vulkan analytic pixels and direct versus
  MapView retained output equality. Default execution explicitly skips the GPU test.
- Lumyte.Maps.UI.Tests: record-only retained reuse, invalidation, replacement, mount/unmount.

Use the shared repository SDK/environment and slang path. Run each project with dotnet test -c Release.
For hardware readback set the existing CI flag LUMYTE_GRAPHICS_GPU_TESTS=1 (LUMYTE_MAPS_GPU_TESTS=1 remains an alias) and an explicitly verified VK_DRIVER_FILES manifest.
The PNG artifact is a conversion of the raw /tmp/lumyte-map-gpu-rgba.bin readback, not a desktop screenshot.

Strict application NativeAOT and trimmed publish use the existing CI flags:

    dotnet publish samples/Lumyte.Maps.Sample -c Release -p:PublishAot=true -p:ILLinkTreatWarningsAsErrors=true -p:IlcTreatWarningsAsErrors=true -o /tmp/lumyte-maps-aot
    dotnet publish samples/Lumyte.Maps.Sample -c Release -r linux-x64 --self-contained true -p:PublishTrimmed=true -p:ILLinkTreatWarningsAsErrors=true -o /tmp/lumyte-maps-trim

Run each published Lumyte.Maps.Sample headless binary. The bounded smoke waits for actual tile adoption
before orderly stop. The launcher registration is exercised with --sample maps --backend headless --configuration Release.

Browser execution is separately covered by Lumyte.Maps.Browser.Sample using bounded immutable in-memory
inputs. The trimmed browser smoke proves adoption, mounted UI recording, retained reuse and cleanup.
It does not prove Browser GPU pixels; optional capture remains failing with device-recovery timeout.
No HTTP or M3 input/spatial demand is implemented.
The final handmade hardware matrix covers parent clips, overlay, alpha-0.5 ownership edges across
1024/8192 extents, integer/half-pixel positions and DPI 1/1.5/2. Generic tests additionally cover
translation/reflection and explicitly reject rotation/shear. No real-city or general no-seams claim
follows from these finite fixtures.

## Existing CI discovery

Maps test projects are registered in Lumyte.slnx. build/Build.cs Test invokes dotnet test against the
canonical solution (or host-derived BuildSolution) with -m:1. Build.Ci host filtering retains these
portable projects. Existing Linux native GPU CI already exports LUMYTE_GRAPHICS_GPU_TESTS=1;
MapGpuFact honors that standard flag, so no new CI environment switch is required.

The followup hardware fixture explicitly renders both logical DPI 1 and 2, adjacent 1024/8192 extents,
semitransparent buffered polygons, road casing/interior, UI overlay and an overflowing child clipped by
its actual UI parent. It compares entire direct and retained hardware readbacks and analytic pixel
colors independently. Test artifacts remain outside Git. See IMPLEMENTATION-EVIDENCE.md for results.
