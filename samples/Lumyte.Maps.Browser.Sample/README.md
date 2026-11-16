# Bounded MVT browser adoption sample

Hand-authored fixture bytes are linked from the native sample; no OSM download,
local filesystem tile access, HTTP tile source or M3 streaming behavior is used.
The browser host serves its application assets over localhost, not map tiles.

The content owns MapSession through public IUiApplicationContent hooks, while
Resources owns the decoded CPU generation. A private InMemoryMvtRegistration
copies each tile after validating input/reservation limits and pins its SHA256
revision. The source shares private immutable storage across read-only snapshots.
The registration's preloaded tile storage is application-owned and is additional
to each session's decoder/resident budget; the profile is not a process-wide cap.

From the repository root with the existing .NET 10 SDK, wasm-tools, slangc and
Chromium available (no installation by this sample):

    dotnet publish samples/Lumyte.Maps.Browser.Sample -c Release -p:PublishTrimmed=true
    node samples/Lumyte.Maps.Browser.Sample/verify-browser.mjs

The default smoke requires actual tile adoption, records the mounted MapView
through UiSurface.Record and UiDrawingScene2D, checks retained reuse, then stops
and drains the owning application. Set LUMYTE_MAPS_GPU_CAPTURE=1 to additionally
require the host GPU PNG and map pixel colors. Set CHROME to an existing browser
executable when chromium is not on PATH. This is one fixed fixture smoke,
not general MVT/Browser/AOT compatibility, performance or seamlessness evidence.
