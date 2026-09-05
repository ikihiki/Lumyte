using System.Net.WebSockets;
using System.Text;

using Microsoft.AspNetCore.Http;

namespace Lumyte.DevTools.Server;

public static class DevToolsWebSocketEndpoint
{
    private const int MaximumMessageBytes = 1024 * 1024;

    public static async Task HandleAsync(HttpContext context, DevToolsHub hub)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(hub);
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("A WebSocket connection is required.", context.RequestAborted);
            return;
        }

        using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync();
        using SemaphoreSlim sendLock = new(1, 1);
        await using MemoryStream message = new();

        async ValueTask SendAsync(string json, CancellationToken cancellationToken)
        {
            byte[] payload = Encoding.UTF8.GetBytes(json);
            await sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (socket.State == WebSocketState.Open)
                {
                    await socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                sendLock.Release();
            }
        }

        using DevToolsJsonSession session = new(hub, SendAsync);
        byte[] buffer = new byte[16 * 1024];
        while (socket.State == WebSocketState.Open && !context.RequestAborted.IsCancellationRequested)
        {
            WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, context.RequestAborted).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, context.RequestAborted).ConfigureAwait(false);
                break;
            }

            if (result.MessageType != WebSocketMessageType.Text)
            {
                continue;
            }

            await message.WriteAsync(buffer.AsMemory(0, result.Count), context.RequestAborted).ConfigureAwait(false);
            if (message.Length > MaximumMessageBytes)
            {
                await socket.CloseAsync(WebSocketCloseStatus.MessageTooBig, "Message exceeds 1 MiB.", context.RequestAborted).ConfigureAwait(false);
                break;
            }

            if (!result.EndOfMessage)
            {
                continue;
            }

            string request = Encoding.UTF8.GetString(message.GetBuffer(), 0, checked((int)message.Length));
            message.SetLength(0);
            string response = await session.ProcessAsync(request, context.RequestAborted).ConfigureAwait(false);
            await SendAsync(response, context.RequestAborted).ConfigureAwait(false);
        }
    }
}
