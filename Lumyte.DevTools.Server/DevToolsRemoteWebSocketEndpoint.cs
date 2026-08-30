using System.Net.WebSockets;
using System.Text;

using Microsoft.AspNetCore.Http;

namespace Lumyte.DevTools.Server;

public static class DevToolsRemoteWebSocketEndpoint
{
    public static async Task HandleAsync(HttpContext context, DevToolsHostRegistry registry)
    {
        if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = 400; return; }
        using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync();
        using SemaphoreSlim sendLock = new(1, 1);
        async ValueTask Send(string json, CancellationToken token)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            await sendLock.WaitAsync(token);
            try { if (socket.State == WebSocketState.Open)
                {
                    await socket.SendAsync(bytes, WebSocketMessageType.Text, true, token);
                }
            }
            finally { sendLock.Release(); }
        }
        using DevToolsRemoteJsonSession session = new(registry, Send);
        byte[] buffer = new byte[1024 * 1024];
        while (socket.State == WebSocketState.Open && !context.RequestAborted.IsCancellationRequested)
        {
            WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, context.RequestAborted);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }

            if (result.MessageType != WebSocketMessageType.Text || !result.EndOfMessage) { await socket.CloseAsync(WebSocketCloseStatus.InvalidMessageType, "Text frames must not be fragmented.", context.RequestAborted); break; }
            string response = await session.ProcessAsync(Encoding.UTF8.GetString(buffer, 0, result.Count), context.RequestAborted);
            await Send(response, context.RequestAborted);
        }
    }
}
