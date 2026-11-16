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
For hardware readback set LUMYTE_MAPS_GPU_TESTS=1 and an explicitly verified VK_DRIVER_FILES manifest.
The PNG artifact is a conversion of the raw /tmp/lumyte-map-gpu-rgba.bin readback, not a desktop screenshot.

Strict application NativeAOT and trimmed publish use the existing CI flags:

    dotnet publish samples/Lumyte.Maps.Sample -c Release -p:PublishAot=true -p:ILLinkTreatWarningsAsErrors=true -p:IlcTreatWarningsAsErrors=true -o /tmp/lumyte-maps-aot
    dotnet publish samples/Lumyte.Maps.Sample -c Release -r linux-x64 --self-contained true -p:PublishTrimmed=true -p:ILLinkTreatWarningsAsErrors=true -o /tmp/lumyte-maps-trim

Run each published Lumyte.Maps.Sample headless binary. The bounded smoke waits for actual tile adoption
before orderly stop. The launcher registration is exercised with --sample maps --backend headless --configuration Release.

Browser execution is not covered by these native projects. The local adapter currently requires local
FileStream inputs; no browser input source or public browser sample is registered. This is an explicit
platform limitation, not a passing Browser gate. No HTTP or M3 input/spatial demand is implemented.
No seam-free claim follows from one handmade pixel fixture. Parent clips, semitransparent edges across
different extents, DPI matrix parity and real-city fixtures still need broader visual coverage.
