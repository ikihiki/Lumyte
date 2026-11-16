# Fixed local map sample

Run the real launcher:

    dotnet run --project samples/Lumyte.Samples -c Release -- --sample maps --backend headless --configuration Release
    dotnet run --project samples/Lumyte.Samples -c Release -- --sample maps --backend desktop --configuration Release

Headless is a bounded CPU/record-only application smoke and waits for actual decoded tile adoption;
it does not claim a GPU screenshot. Desktop presents the same public IUiApplicationContent consumer.
The window title identifies the entirely hand-authored fixture (no OSM data). No public tile endpoint
is contacted. The sample writes a small temporary MVT then removes that owned directory after stop.

Content owns MapSession but borrows ResourceManager. Initialize publishes MapView, Advance drives
bounded completion adoption, BeginStop quiesces, RetireAsync returns the session after host GPU drain,
and ReleaseAsync leaves service ownership to the application scope. CPU-only graphics recovery is
explicitly acknowledged through IUiApplicationGraphicsContent; no old GPU view is retained.

The fixture exercises water transparency, a building hole and separate road casing/interior passes.
It is a fixed local set, not pan/zoom interaction or spatial streaming. Native and browser validation
boundaries and current limits are documented under tests/Maps/README.md.
