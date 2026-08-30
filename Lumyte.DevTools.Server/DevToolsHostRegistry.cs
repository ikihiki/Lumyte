using System.Collections.Concurrent;
using System.Text.Json;

using Lumyte.DevTools.Agent;

namespace Lumyte.DevTools.Server;

public sealed record DevToolsRemoteHost(string HostId, string DisplayName, IReadOnlyList<DevToolsAgentDomain> Domains, bool IsConnected);
public sealed record DevToolsRemoteEvent(string HostId, string Domain, string Feature, JsonElement Params);

public sealed class DevToolsHostRegistry
{
    private readonly ConcurrentDictionary<string, Session> sessions = new(StringComparer.Ordinal);
    public event Action<DevToolsRemoteEvent>? EventReceived;
    public event Action? HostsChanged;
    public IReadOnlyList<DevToolsRemoteHost> Hosts => sessions.Values.Select(x => x.Snapshot).OrderBy(x => x.DisplayName, StringComparer.Ordinal).ToArray();

    internal void Attach(string connectionId, DevToolsHostRegistration registration, IDevToolsAgentReceiver receiver)
    {
        Session session = new(connectionId, registration, receiver);
        if (sessions.TryGetValue(registration.HostId, out Session? previous)) { previous.Disconnect(); }
        sessions[registration.HostId] = session;
        HostsChanged?.Invoke();
    }

    internal void Detach(string connectionId)
    {
        KeyValuePair<string, Session>? match = sessions.FirstOrDefault(x => x.Value.ConnectionId == connectionId);
        if (match is { } pair && sessions.TryRemove(pair)) { pair.Value.Disconnect(); HostsChanged?.Invoke(); }
    }

    internal void Complete(string connectionId, DevToolsInvocationResponse response)
    {
        Session? session = sessions.Values.FirstOrDefault(x => x.ConnectionId == connectionId);
        session?.Complete(response);
    }

    internal void Publish(string connectionId, DevToolsAgentEvent value)
    {
        Session? session = sessions.Values.FirstOrDefault(x => x.ConnectionId == connectionId);
        if (session is not null) { EventReceived?.Invoke(new(session.Snapshot.HostId, value.Domain, value.Feature, JsonSerializer.Deserialize<JsonElement>(value.Params))); }
    }

    public DevToolsRemoteHost GetHost(string hostId) => Get(hostId).Snapshot;
    public ValueTask<JsonElement> InvokeAsync(string hostId, string domain, string feature, string kind, JsonElement parameters, CancellationToken cancellationToken = default) => Get(hostId).InvokeAsync(domain, feature, kind, parameters, cancellationToken);
    private Session Get(string id) => sessions.TryGetValue(id, out Session? value) ? value : throw new KeyNotFoundException($"The DevTools host '{id}' is not connected.");

    private sealed class Session
    {
        private readonly IDevToolsAgentReceiver receiver;
        private readonly ConcurrentDictionary<string, TaskCompletionSource<DevToolsInvocationResponse>> pending = new(StringComparer.Ordinal);
        private long nextId;
        public Session(string connectionId, DevToolsHostRegistration value, IDevToolsAgentReceiver receiver) { ConnectionId=connectionId;this.receiver=receiver;Snapshot=new(value.HostId,value.DisplayName,value.Domains,true); }
        public string ConnectionId { get; }
        public DevToolsRemoteHost Snapshot { get; }
        public async ValueTask<JsonElement> InvokeAsync(string domain,string feature,string kind,JsonElement parameters,CancellationToken token)
        {
            string id=Interlocked.Increment(ref nextId).ToString(System.Globalization.CultureInfo.InvariantCulture);
            TaskCompletionSource<DevToolsInvocationResponse> completion=new(TaskCreationOptions.RunContinuationsAsynchronously);pending[id]=completion;
            try { receiver.Invoke(new(id,domain,feature,kind,JsonSerializer.SerializeToUtf8Bytes(parameters)));DevToolsInvocationResponse result=await completion.Task.WaitAsync(token);if(result.Error is not null) { throw new InvalidOperationException(result.Error); } return JsonSerializer.Deserialize<JsonElement>(result.Result!); }
            finally { pending.TryRemove(id,out _); }
        }
        public void Complete(DevToolsInvocationResponse response) { if(pending.TryRemove(response.Id,out TaskCompletionSource<DevToolsInvocationResponse>? value))
            {
                value.TrySetResult(response);
            }
        }
        public void Disconnect() { foreach(TaskCompletionSource<DevToolsInvocationResponse> value in pending.Values) { value.TrySetException(new IOException("Host disconnected.")); } pending.Clear(); }
    }
}
