# Lumyte.Maps

CPU-only XYZ identifiers, double-precision Web Mercator coordinates and immutable MVT geometry.
World width is 512 logical pixels at zoom zero. X wraps; Y does not. Each layer retains its own extent,
source ordering, typed attributes, buffered coordinates and polygon ring winding.

Local MVT source/pipeline usage is documented in ../Lumyte.Maps.Mvt/README.md.
This package does not interpret OSM XML or .osm.pbf, select arbitrary online sources, or promise
external style compatibility. The supported application schema is explicit.

## Fixed-set UI session (M2)

MapSession borrows ResourceManager and owns an explicit normalized fixed tile set. It does not select
new tiles when the camera changes and has no pointer interaction, HTTP, parent fallback or prefetch.
M2 explicitly rejects mixed-zoom fixed sets to prevent overlapping parent/child transparency.
The constructor bounds the fixed list before copying. SetViewport only changes logical demand;
Advance dispatches when both dimensions are positive. Each dispatch reserves a completion slot and the
full profile reservation until its noncancelled RetainAsync reaches a collected terminal result.
Adoption pins the exact Resources generation, checks its revision and byte charge, and retains both
reference and lease. A cache hit pays the same full retained size as an uncached load.

If the fixed set exceeds resident capacity, the unadmitted remainder stays pending. The session does
not evict displayed fixed tiles or falsely report all tiles loaded. Failures are stable diagnostics;
executor queue-full failures return to pending rather than becoming network retries.

BeginStop prevents dispatch/publication; Advance continues collecting terminal work. IsStopped is not
a GPU drain. The host drains GPU work before DisposeAsync. Dispose attempts share active completion
and keep unfinished reference/lease cleanup fields for a later retry. The borrowed manager remains
alive until the session and host frames have retired. All owner operations must be serialized.

Record uses an ordinary antialiased viewport clip and device-center whole-pixel ownership clips for
internal tile boundaries, preserving buffered paths and polygon winding. It paints background then global style passes then normalized tile/input-feature order (equal sort
key). It restores canvas state and never loads or submits. Coordinates subtract a double camera origin
before float conversion. Host DPI is not multiplied again. A separate MaxRecordingCpuBytes preflight
bounds conservative path/command conversion allocation for one recording; host snapshots and renderer
prepared GPU resources need their own bounds. This is not a process memory bound or a measured speedup.
No seam-free or arbitrary real-city compatibility claim is made from the handmade fixture.

MapView mounts one borrowed session, subscribes/unsubscribes changes, and enables ordinary UI retained
local recording. Unmount does not dispose the session. Changing the borrowed session on the same
mounted identity is rejected before publication; replace the element identity instead.

### Validation

Use the repository environment and SDK, then run Release tests under tests/Maps. Opt-in GPU readback:

LUMYTE_MAPS_GPU_TESTS=1 VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json dotnet test tests/Maps/Lumyte.Maps.Session.Tests -c Release

CPU tests are record-only. Native GPU execution, trimmed publishing, NativeAOT publishing and Browser
execution are distinct gates; consult the implementation evidence rather than inferring one from another.

### Ownership boundaries and fixed-set repetitions

The inner tile ownership predicate uses ClipOwnershipRect; it partitions half-open device-pixel center
membership after target DPI and accumulated axis-aligned transforms. Each owned pixel keeps the shape's
antialias samples. This avoids multiplying independent fractional clip coverages for adjacent buffered
semi-transparent tiles without snapping camera coordinates or path vertices. Translation, axis scaling
and reflection are supported; rotation or shear, including retained/replay transforms, is explicitly
rejected during preparation. The outer UI boundary retains ordinary antialiased clipping.

Already-adopted normalized X tiles repeat at visible world offsets, including near both sides of the
dateline. This does not change fixed acquisition demand or start M3 selection. Visible repetitions are
preflighted against the same recording budget; each copy is charged. The actual recorder and budget
preflight share projection visibility and paint/geometry eligibility, so dense offscreen or ineligible
features do not reject an otherwise small visible map. Extreme repetition demand rejects before paths
are allocated rather than iterating an unbounded viewport.
