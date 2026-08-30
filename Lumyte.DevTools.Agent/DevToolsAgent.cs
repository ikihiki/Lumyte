using System.IO.Pipes;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Channels;
using Grpc.Net.Client;
using MagicOnion.Client;

namespace Lumyte.DevTools.Agent;

public sealed class DevToolsAgent
{
    public const string DefaultPipeName = "Lumyte.DevTools";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly DevToolsHub hub;
    private readonly DevToolsHostRegistration registration;
    private readonly string pipeName;
    private readonly TimeSpan reconnectDelay;

    public DevToolsAgent(DevToolsHub hub, string hostId, string displayName, string pipeName = DefaultPipeName, TimeSpan? reconnectDelay = null)
    {
        this.hub = hub;
        this.pipeName = pipeName;
        this.reconnectDelay = reconnectDelay ?? TimeSpan.FromSeconds(1);
        registration = new DevToolsHostRegistration(hostId, displayName, GetDomains());
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try { await RunConnectionAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception exception) when (exception is IOException or HttpRequestException or Grpc.Core.RpcException) { }
            await Task.Delay(reconnectDelay, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    private async Task RunConnectionAsync(CancellationToken cancellationToken)
    {
        SocketsHttpHandler handler = new()
        {
            ConnectCallback = async (_, token) =>
            {
                NamedPipeClientStream pipe = new(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                try { await pipe.ConnectAsync(token).ConfigureAwait(false); return pipe; }
                catch { pipe.Dispose(); throw; }
            },
        };
        using GrpcChannel channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions { HttpHandler = handler });
        using SemaphoreSlim outbound = new(1, 1);
        Receiver receiver = new(hub, outbound);
        IDevToolsAgentHub client = await StreamingHubClient.ConnectAsync<IDevToolsAgentHub, IDevToolsAgentReceiver>(channel, receiver, cancellationToken: cancellationToken);
        receiver.Attach(client);
        List<IDisposable> subscriptions = [];
        Channel<DevToolsAgentEvent> events = Channel.CreateUnbounded<DevToolsAgentEvent>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        Task eventSender = Task.Run(async () =>
        {
            await foreach (DevToolsAgentEvent value in events.Reader.ReadAllAsync(cancellationToken))
            {
                await outbound.WaitAsync(cancellationToken);
                try { await client.PublishEventAsync(value); }
                finally { outbound.Release(); }
            }
        }, cancellationToken);
        try
        {
            await client.RegisterAsync(registration);
            foreach (DevToolsAgentDomain domain in registration.Domains)
            {
                foreach (DevToolsAgentFeature feature in domain.Features.Where(static x => x.Kind == "event"))
                {
                    subscriptions.Add(hub.Subscribe(new DevToolsDomain(domain.Name), feature.Name, (value, token) =>
                    {
                        _ = client.PublishEventAsync(new DevToolsAgentEvent(domain.Name, feature.Name, JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions)));
                        return ValueTask.CompletedTask;
                    }));
                }
            }
            await client.WaitForDisconnectAsync();
        }
        finally
        {
            foreach (IDisposable subscription in subscriptions) { subscription.Dispose(); }
            await client.DisposeAsync();
        }
    }

    private DevToolsAgentDomain[] GetDomains() => hub.Domains.Select(domain => new DevToolsAgentDomain(domain.Name, hub.GetFeatures(domain).Select(feature => new DevToolsAgentFeature(feature.Name, feature.Kind.ToString().ToLowerInvariant(), feature.RequestType.AssemblyQualifiedName!, feature.ResponseType?.AssemblyQualifiedName)).ToArray())).ToArray();

    private sealed class Receiver(DevToolsHub hub, SemaphoreSlim outbound) : IDevToolsAgentReceiver
    {
        private IDevToolsAgentHub? client;
        public void Attach(IDevToolsAgentHub value) => client = value;
        public void Invoke(DevToolsInvocation invocation) => _ = InvokeAsync(invocation);
        private async Task InvokeAsync(DevToolsInvocation invocation)
        {
            DevToolsInvocationResponse response;
            try
            {
                DevToolsFeatureKind kind = invocation.Kind == "query" ? DevToolsFeatureKind.Query : DevToolsFeatureKind.Command;
                DevToolsDomain domain = new(invocation.Domain);
                DevToolsFeature feature = hub.GetFeatures(domain).Single(x => x.Kind == kind && x.Name == invocation.Feature);
                object? parameter = JsonSerializer.Deserialize(invocation.Params, feature.RequestType, JsonOptions);
                object? result = await hub.InvokeAsync(domain, kind, invocation.Feature, parameter).ConfigureAwait(false);
                response = new(invocation.Id, JsonSerializer.SerializeToUtf8Bytes(result, feature.ResponseType!, JsonOptions), null);
            }
            catch (Exception exception) { response = new(invocation.Id, null, exception.Message); }
            if (client is not null)
            {
                await outbound.WaitAsync();
                try { await client.CompleteInvocationAsync(response); }
                finally { outbound.Release(); }
            }
        }
    }
}
