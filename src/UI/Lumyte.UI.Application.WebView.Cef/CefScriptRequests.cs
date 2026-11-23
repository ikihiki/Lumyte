using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Lumyte.UI.WebView;

namespace Lumyte.UI.Application.WebView.Cef;

// Thread safe, finite waiters; completions run asynchronously and never execute user code in IPC reader.
internal sealed class CefScriptRequests
{
    internal static readonly UTF8Encoding Utf8 = new(false, true);

    private readonly object _gate = new();
    private readonly Dictionary<long, (long Epoch, TaskCompletionSource<string> Source)> _pending = [];
    private long _next;

    // Validate all syntax even after crossing the data limit: malformed protocol still fails closed.
    // At most 4096 UTF8 bytes arrive; depth cannot exceed that finite byte budget.
    internal static bool ValidateJson(string value)
    {
        var reader = new Utf8JsonReader(Utf8.GetBytes(value), new JsonReaderOptions { MaxDepth = 4096 });
        bool allowed = true;
        bool any = false;
        while (reader.Read())
        {
            any = true;
            if (reader.TokenType is JsonTokenType.StartArray or JsonTokenType.StartObject && reader.CurrentDepth >= 64)
            {
                allowed = false;
            }
        }

        if (!any)
        {
            throw new JsonException("Empty JSON payload.");
        }

        return allowed;
    }

    internal async Task<string> EvaluateAsync(string script, long epoch, TimeSpan timeout, CancellationToken token, Action<byte[]> send)
    {
        TaskCompletionSource<string> source = new(TaskCreationOptions.RunContinuationsAsynchronously);
        long id;
        byte[] expression = Utf8.GetBytes(script);
        if (expression.Length is 0 or > 3072)
        {
            throw new ArgumentOutOfRangeException(nameof(script));
        }

        lock (_gate)
        {
            if (_pending.Count >= 16)
            {
                throw new UiWebViewScriptException("cef-script-pending-limit");
            }

            id = checked(++_next);
            _pending.Add(id, (epoch, source));
        }

        try
        {
            token.ThrowIfCancellationRequested();
            byte[] body = new byte[20 + expression.Length];
            BinaryPrimitives.WriteInt64LittleEndian(body, id);
            BinaryPrimitives.WriteInt64LittleEndian(body.AsSpan(8), epoch);
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(16), checked((uint)Math.Ceiling(timeout.TotalMilliseconds)));
            expression.CopyTo(body, 20);
            send(body);
            try
            {
                return await source.Task.WaitAsync(timeout, token).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                throw new UiWebViewScriptException("cef-script-timeout");
            }
        }
        finally
        {
            lock (_gate)
            {
                _pending.Remove(id);
            }
        }
    }

    internal void Complete(byte[] body)
    {
        if (body.Length is < 20 or > 3092)
        {
            throw new InvalidDataException("cef-script-response-size");
        }

        long id = BinaryPrimitives.ReadInt64LittleEndian(body);
        long epoch = BinaryPrimitives.ReadInt64LittleEndian(body.AsSpan(8));
        uint status = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(16));
        if (id <= 0 || epoch < 0 || status > 4)
        {
            throw new InvalidDataException("cef-script-response-header");
        }

        string value = Utf8.GetString(body.AsSpan(20));
        if (status == 0)
        {
            if (!ValidateJson(value))
            {
                status = 4;
            }
        }

        lock (_gate)
        {
            if (!_pending.TryGetValue(id, out (long Epoch, TaskCompletionSource<string> Source) pending) || pending.Epoch != epoch)
            {
                return;
            }

            _pending.Remove(id);
            if (status == 0)
            {
                pending.Source.TrySetResult(value);
            }
            else
            {
                pending.Source.TrySetException(new UiWebViewScriptException(status switch { 1 => "cef-script-exception", 2 => "cef-script-aborted", 4 => "cef-script-result-depth-limit", _ => "cef-script-timeout" }, status == 1 ? value : null));
            }
        }
    }

    internal void Abort(string reason)
    {
        lock (_gate)
        {
            foreach ((long Epoch, TaskCompletionSource<string> Source) pending in _pending.Values)
            {
                pending.Source.TrySetException(new UiWebViewScriptException(reason));
            }

            _pending.Clear();
        }
    }
}
