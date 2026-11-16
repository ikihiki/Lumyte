using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Lumyte.Maps.Mvt;
using Lumyte.Memory;
using Lumyte.Resources;

namespace Lumyte.Maps.Http.Smoke;

internal static class Program
{
    private static async Task Main()
    {
        // Synthetic MVT 2 roads layer, no external data or public network.
        byte[] payload = [0x1a, 9, 0x0a, 5, 114, 111, 97, 100, 115, 0x78, 2];
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var scope = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/tiles/");
        Task serving = ServeAsync(listener, payload);
        var tile = new MapTileId(0, 0, 0);
        var registration = new HttpMvtRegistration("local-aot", "1", new("bounded"), new(scope, "public"), new Dictionary<MapTileId, Uri> { [tile] = new(scope, "0.pbf") });
        await using var source = new HttpMvtSource(registration);
        await using MemoryManager memory = new();
        await using ResourceManager manager = new("local-http-smoke", source, [new MvtResourceStage(registration.Mvt)], memory, new InlineRuntime());
        using ResourceReference<MapTileContent> reference = await manager.RetainAsync(registration.GetKey(tile));
        using ResourceLease<MapTileContent> lease = reference.Pin();
        if (lease.Value.Layers.Length != 1 || lease.Value.Layers[0].Name != "roads" || lease.Value.SourceRevision != Convert.ToHexString(SHA256.HashData(payload)))
        {
            throw new InvalidOperationException("Local HTTP/Resources/decode revision integration failed.");
        }

        await serving;
        Console.WriteLine("local HTTP -> bounded snapshot -> Resources -> MVT revision: PASS");
    }

    private static async Task ServeAsync(TcpListener listener, byte[] payload)
    {
        using TcpClient client = await listener.AcceptTcpClientAsync();
        await using NetworkStream stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        while (!string.IsNullOrEmpty(await reader.ReadLineAsync()))
        {
        }

        byte[] headers = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/vnd.mapbox-vector-tile\r\nContent-Length: {payload.Length}\r\nETag: \"smoke\"\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(headers);
        await stream.WriteAsync(payload);
    }

    private sealed class InlineRuntime : IResourceRuntime, IResourceExecutor
    {
        public IResourceExecutor Executor => this;

        public ValueTask BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken) => context.ExecuteAsync(cancellationToken);

        public ValueTask ReleaseAsync(IResourceStage stage, IResourceRuntime runtime, object resource) => stage.ReleaseAsync(resource, runtime);
    }
}
