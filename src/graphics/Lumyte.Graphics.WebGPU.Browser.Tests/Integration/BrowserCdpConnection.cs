using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lumyte.Graphics.WebGPU.Browser.Tests;

internal sealed class BrowserCdpConnection : IAsyncDisposable
{
    private static readonly JsonSerializerOptions jsonOptions = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    private readonly ClientWebSocket socket = new();
    private readonly CancellationTokenSource stopping = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> pending = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource> events = new();
    private readonly SemaphoreSlim sending = new(1);
    private Task receiver = Task.CompletedTask;
    private int nextId;

    public async Task ConnectAsync(Uri endpoint)
    {
        await socket.ConnectAsync(endpoint, stopping.Token);
        receiver = ReceiveAsync();
    }

    public Task ExpectEventAsync(string method, string sessionId)
    {
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!events.TryAdd(sessionId + ":" + method, completion)) { throw new InvalidOperationException("An event is already being awaited."); }
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(90));
    }

    public async Task<JsonElement> SendAsync(string method, object? parameters = null, string? sessionId = null)
    {
        int id = Interlocked.Increment(ref nextId);
        TaskCompletionSource<JsonElement> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = completion;
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new { id, method, @params = parameters ?? new { }, sessionId }, jsonOptions);
        try
        {
            await sending.WaitAsync(stopping.Token);
            try { await socket.SendAsync(payload.AsMemory(), WebSocketMessageType.Text, true, stopping.Token); }
            finally { sending.Release(); }
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(90));
        }
        finally { pending.TryRemove(id, out _); }
    }

    private async Task ReceiveAsync()
    {
        byte[] chunk = new byte[16384];
        try
        {
            while (!stopping.IsCancellationRequested)
            {
                using MemoryStream message = new();
                ValueWebSocketReceiveResult part;
                do
                {
                    part = await socket.ReceiveAsync(chunk.AsMemory(), stopping.Token);
                    if (part.MessageType == WebSocketMessageType.Close) { throw new IOException("The browser closed its DevTools connection."); }
                    message.Write(chunk, 0, part.Count);
                } while (!part.EndOfMessage);

                using JsonDocument document = JsonDocument.Parse(message.ToArray());
                JsonElement response = document.RootElement;
                if (response.TryGetProperty("id", out JsonElement id) && pending.TryRemove(id.GetInt32(), out var completion))
                {
                    if (response.TryGetProperty("error", out JsonElement error)) { completion.TrySetException(new InvalidOperationException(error.GetRawText())); }
                    else { completion.TrySetResult(response.GetProperty("result").Clone()); }
                }
                else if (response.TryGetProperty("method", out JsonElement method) && response.TryGetProperty("sessionId", out JsonElement session))
                {
                    if (events.TryRemove(session.GetString() + ":" + method.GetString(), out TaskCompletionSource? signal)) { signal.TrySetResult(); }
                }
            }
        }
        catch (Exception error)
        {
            foreach (var item in pending.Values) { item.TrySetException(error); }
            foreach (var item in events.Values) { item.TrySetException(error); }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await stopping.CancelAsync();
        socket.Abort();
        await receiver;
        socket.Dispose();
        stopping.Dispose();
        sending.Dispose();
    }
}
