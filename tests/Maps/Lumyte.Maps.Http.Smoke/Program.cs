using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Maps.Mvt;
using Lumyte.Memory;
using Lumyte.Resources;

namespace Lumyte.Maps.Http.Smoke;

internal static class Program
{
    private static async Task Main()
    {
        // Pre-encoded synthetic MVT 2 roads: one two-vertex line, no attributes.
        // This is fixture data, not a second encoder/parser or a fake decoded stage.
        byte[] first = [26, 24, 10, 5, 114, 111, 97, 100, 115, 120, 2, 18, 13, 24, 2, 34, 9, 9, 128, 16, 128, 32, 10, 128, 32, 0];
        byte[] second = (byte[])first.Clone();
        second[19] = 18;
        MapTileId a = new(2, 0, 0);
        MapTileId b = new(2, 1, 0);
        var payloads = new Dictionary<string, byte[]> { ["/tiles/2/0/0.pbf"] = first, ["/tiles/2/1/0.pbf"] = second };
        await using var server = new LocalServer(payloads);
        var profile = new MapTileLoadProfile("spatial-http", maxInputBytes: 256, maxDecodedBytes: 65536, maxAttributeBytes: 4096, maxFeatures: 8, maxVertices: 32, maxReservationBytesPerTile: 131072);
        var policy = new HttpMvtPolicy(server.Scope, "synthetic-public", maxWireBytes: 256, maxExpandedBytes: 256, maxEntries: 2, maxResidentBytes: 34 * 1024 * 1024, maxInFlight: 1, freshness: TimeSpan.FromHours(1));
        var registration = new HttpMvtRegistration("spatial-http", "1", profile, policy, new Dictionary<MapTileId, Uri>
        {
            [a] = new(server.Scope, "2/0/0.pbf"),
            [b] = new(server.Scope, "2/1/0.pbf"),
        });
        var expected = new Dictionary<MapTileId, string> { [a] = Hash(first), [b] = Hash(second) };
        Require(expected[a] != expected[b], "Fixture representations must have distinct content revisions.");
        var tiles = new Tiles(registration, expected);
        var stage = new ObservedMvtStage(registration, expected);
        await using var source = new HttpMvtSource(registration);
        await using MemoryManager memory = new();
        await using ResourceManager manager = new("spatial-http-smoke", source, [stage], memory, new InlineRuntime());
        var options = new MapSessionOptions
        {
            MaxResidentTiles = 1,
            PrefetchRadius = 0,
            MaxPrefetchTiles = 0,
            MaxFallbackDepth = 0,
            MaxPendingTiles = 1,
            MaxInFlightTiles = 1,
            MaxCompletedTiles = 1,
            MaxPublicationsPerAdvance = 1,
            MaxResidentCpuBytes = profile.MaxReservationBytesPerTile,
            MaxRecordingCpuBytes = 131072,
        };
        var style = new MapStyle(MvtDecoder.SchemaId, "1", new(0, 0, 0, 1), [new("roads", new(1, 0, 0, 1), new StrokeStyle2D { Width = 2 })]);
        await using var map = new MapSession(manager, tiles, style, options);
        map.SetViewport(new(100, 100));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        MapTileId[] route = [a, b, a];
        int records = 0;
        int maxResidentSlots = 0;
        for (int step = 0; step < route.Length; step++)
        {
            MapTileId target = route[step];
            map.SetCamera(CameraAt(target));
            await PumpAsync(map, () => map.ReadyVisibleTiles == 1 && !map.IsMaintenanceInFlight, profile, deadline.Token);
            Require(map.VisibleTiles.Count == 1 && map.VisibleTiles[0] == target, "Spatial demand must select only the current XYZ.");
            Require(map.PublishedTiles == 1 && stage.Builds == step + 1 && stage.Revisions[step] == expected[target], "Actual HTTP MVT build was not adopted with its expected revision.");
            Require(await source.GetRevisionAsync(registration.GetKey(target).Address.Id, deadline.Token) == expected[target], "Source and adopted revision differ.");
            Require(server.Requests == Math.Min(step + 1, 2), "Fresh synchronized HTTP cache must keep A-B-A to two downloads.");
            var canvas = new Canvas2D();
            canvas.Begin();
            map.Record(canvas);
            int count = canvas.End().Count;
            Require(count >= 2, "Adopted geometry must record more than the background through Canvas2D.");
            records += count;
            maxResidentSlots = Math.Max(maxResidentSlots, map.PublishedTiles + map.InFlightTiles + map.RetiringTiles);
        }

        Require(stage.Releases == 2 && map.ReleasedGenerationCount >= 2, "Both camera transitions must reclaim the previous manager generation.");
        ulong stoppedRevision = map.DisplayRevision;
        map.BeginStop();
        await PumpAsync(map, () => map.IsStopped, profile, deadline.Token);
        await map.DisposeAsync().AsTask().WaitAsync(deadline.Token);
        Require(map.IsStopped && map.PublishedTiles == 0 && map.InFlightTiles == 0 && map.PendingTiles == 0 && map.RetiringTiles == 0 && map.ResidentCpuBytes == 0 && !map.IsMaintenanceInFlight && map.MaintenanceFailure is null, "Spatial stop/disposal left owned work or storage.");
        Require(map.DisplayRevision == stoppedRevision && stage.Releases == 3, "Stopping must not publish and must release the final real decoded resource.");
        Require(await manager.TrimAsync(deadline.Token) == 0, "Final session maintenance left an unreclaimed manager generation.");
        await server.Delivered.Task.WaitAsync(deadline.Token);
        Require(server.Requests == 2 && server.RequestsFor("/tiles/2/0/0.pbf") == 1 && server.RequestsFor("/tiles/2/1/0.pbf") == 1 && server.Active == 0, "HTTP fixture terminal counts are inconsistent.");
        await manager.DisposeAsync();
        await source.DisposeAsync();
        Console.WriteLine($"local HTTP -> spatial MapSession A-B-A -> Canvas.Record -> retirement: PASS; requests={server.Requests}; builds={stage.Builds}; releases={stage.Releases}; recordings=3; draw-records={records}; max-resident-slots={maxResidentSlots}; terminal-owned=0");
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static MapCamera CameraAt(MapTileId id)
    {
        double x = (id.X + 0.5) / (1 << id.Zoom);
        double y = (id.Y + 0.5) / (1 << id.Zoom);
        return new((x * 360) - 180, Math.Atan(Math.Sinh(Math.PI * (1 - (2 * y)))) * 180 / Math.PI, id.Zoom);
    }

    private static async Task PumpAsync(MapSession map, Func<bool> complete, MapTileLoadProfile profile, CancellationToken cancellationToken)
    {
        while (!complete())
        {
            cancellationToken.ThrowIfCancellationRequested();
            map.Advance();
            Require(map.Failures.Count == 0 && map.MaintenanceFailure is null, "Spatial HTTP session reported an acquisition or maintenance failure.");
            Require(map.PublishedTiles + map.InFlightTiles + map.RetiringTiles <= 1 && map.PendingTiles <= 1 && map.ResidentCpuBytes <= profile.MaxReservationBytesPerTile, "Spatial admission or retirement exceeded its single-tile budget.");
            await Task.Delay(1, cancellationToken);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class Tiles(HttpMvtRegistration registration, IReadOnlyDictionary<MapTileId, string> expected)
        : MapTileSet("spatial-http", "1", MvtDecoder.SchemaId, 2, 2, "synthetic fixture", registration.Mvt.Profile)
    {
        public override ResourceKey<MapTileContent> GetKey(MapTileId id) => registration.GetKey(id);

        public override string? GetExpectedSourceRevision(MapTileId id) => expected[id.Normalize()];
    }

    private sealed class ObservedMvtStage : IResourceSourceStage<MapTileContent>
    {
        private readonly MvtResourceStage _inner;
        private readonly Dictionary<ResourceId, string> _expected;

        internal ObservedMvtStage(HttpMvtRegistration registration, IReadOnlyDictionary<MapTileId, string> expected)
        {
            _inner = new(registration.Mvt);
            _expected = expected.ToDictionary(pair => registration.GetKey(pair.Key).Address.Id, pair => pair.Value);
        }

        internal int Builds { get; private set; }

        internal int Releases { get; private set; }

        internal List<string> Revisions { get; } = [];

        public async ValueTask<MapTileContent> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
        {
            MapTileContent content = await _inner.BuildAsync(context, cancellationToken);
            Require(content.SourceRevision == _expected[context.Address.Id] && content.Layers.Length == 1 && content.Layers[0].Name == "roads" && content.Layers[0].Features.Length == 1, "Actual bounded MVT decode did not preserve geometry and content revision.");
            Revisions.Add(content.SourceRevision);
            Builds++;
            return content;
        }

        public ValueTask ReleaseAsync(MapTileContent resource)
        {
            Releases++;
            return _inner.ReleaseAsync(resource);
        }
    }

    private sealed class LocalServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly IReadOnlyDictionary<string, byte[]> _payloads;
        private readonly Dictionary<string, int> _counts = [];
        private readonly Task _run;
        private int _requests;
        private int _active;

        internal LocalServer(IReadOnlyDictionary<string, byte[]> payloads)
        {
            _payloads = payloads;
            _listener.Start();
            Scope = new($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/tiles/");
            _run = RunAsync();
        }

        internal Uri Scope { get; }

        internal int Requests => Volatile.Read(ref _requests);

        internal int Active => Volatile.Read(ref _active);

        internal TaskCompletionSource Delivered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Stop();
            try
            {
                await _run;
            }
            catch (OperationCanceledException)
            {
            }

            _stop.Dispose();
        }

        internal int RequestsFor(string path)
        {
            lock (_counts)
            {
                return _counts.GetValueOrDefault(path);
            }
        }

        private async Task RunAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                using TcpClient client = await _listener.AcceptTcpClientAsync(_stop.Token);
                Interlocked.Increment(ref _active);
                try
                {
                    await using NetworkStream stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                    string line = await reader.ReadLineAsync(_stop.Token) ?? throw new InvalidDataException("Missing HTTP request.");
                    string[] request = line.Split(' ');
                    Require(request.Length == 3 && request[0] == "GET" && _payloads.ContainsKey(request[1]), "Only the two explicitly registered XYZ endpoints may be requested.");
                    while (!string.IsNullOrEmpty(await reader.ReadLineAsync(_stop.Token)))
                    {
                    }

                    Require(Interlocked.Increment(ref _requests) <= 2, "HTTP request budget exceeded.");
                    lock (_counts)
                    {
                        _counts[request[1]] = _counts.GetValueOrDefault(request[1]) + 1;
                    }

                    byte[] payload = _payloads[request[1]];
                    byte[] headers = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/vnd.mapbox-vector-tile\r\nContent-Length: {payload.Length}\r\nETag: \"{Hash(payload)}\"\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(headers, _stop.Token);
                    await stream.WriteAsync(payload, _stop.Token);
                }
                finally
                {
                    Interlocked.Decrement(ref _active);
                    if (Requests == 2)
                    {
                        Delivered.TrySetResult();
                    }
                }
            }
        }
    }

    private sealed class InlineRuntime : IResourceRuntime, IResourceExecutor
    {
        public IResourceExecutor Executor => this;

        public ValueTask BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken) => context.ExecuteAsync(cancellationToken);

        public ValueTask ReleaseAsync(IResourceStage stage, IResourceRuntime runtime, object resource) => stage.ReleaseAsync(resource, runtime);
    }
}
