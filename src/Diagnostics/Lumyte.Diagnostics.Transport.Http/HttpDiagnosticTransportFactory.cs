using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Lumyte.Diagnostics.Transport.Http;

internal sealed class HttpDiagnosticTransportFactory(Uri address, string token) : IDiagnosticTransportFactory
{
    public async ValueTask<IDiagnosticConnection> OpenAsync(ClientHello hello, CancellationToken cancellationToken)
    {
        var client = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(35), MaxResponseContentBufferSize = 4 * 1024 * 1024 };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        try
        {
            using HttpResponseMessage response = await client.PostAsync("diagnostics/v1/sessions", JsonBody(hello, DiagnosticJson.Context.ClientHello), cancellationToken).ConfigureAwait(false);
            EnsureSuccess(response);
            SessionWelcome welcome = await ReadAsync(response, DiagnosticJson.Context.SessionWelcome, cancellationToken).ConfigureAwait(false);
            client.DefaultRequestHeaders.Add("X-Diagnostics-Session", welcome.SessionSecret);
            return new HttpDiagnosticConnection(client, welcome);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            client.Dispose();
            throw new DiagnosticTransportException("HTTP session establishment failed.", exception);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    internal static HttpContent JsonBody<T>(T value, JsonTypeInfo<T> type)
    {
        var content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(value, type));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return content;
    }

    internal static void EnsureSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new DiagnosticTransportException($"Diagnostic HTTP request was rejected ({(int)response.StatusCode}).");
        }
    }

    internal static async Task<T> ReadAsync<T>(HttpResponseMessage response, JsonTypeInfo<T> type, CancellationToken cancellationToken)
    {
        byte[] body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize(body, type) ?? throw new DiagnosticTransportException("Empty diagnostic response.");
    }

    private sealed class HttpDiagnosticConnection(HttpClient client, SessionWelcome welcome) : IDiagnosticConnection
    {
        private int _reader;
        private int _disposed;

        public SessionWelcome Welcome => welcome;

        public async IAsyncEnumerable<DiagnosticCommand> ReadCommandsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _reader, 1) != 0)
            {
                throw new InvalidOperationException("Only one command reader is supported.");
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                DiagnosticCommand[] commands;
                try
                {
                    using HttpResponseMessage response = await client.GetAsync($"diagnostics/v1/sessions/{Welcome.SessionId}/commands", cancellationToken).ConfigureAwait(false);
                    EnsureSuccess(response);
                    commands = await ReadAsync(response, DiagnosticJson.Context.DiagnosticCommandArray, cancellationToken).ConfigureAwait(false);
                }
                catch (HttpRequestException exception)
                {
                    throw new DiagnosticTransportException("HTTP command reception failed.", exception);
                }

                foreach (DiagnosticCommand command in commands)
                {
                    yield return command;
                }
            }
        }

        public async ValueTask<PublishReceipt> PublishAsync(DiagnosticMessage message, CancellationToken cancellationToken)
        {
            try
            {
                using HttpResponseMessage response = await client.PostAsync($"diagnostics/v1/sessions/{Welcome.SessionId}/messages", JsonBody(message, DiagnosticJson.Context.DiagnosticMessage), cancellationToken).ConfigureAwait(false);
                EnsureSuccess(response);
                return await ReadAsync(response, DiagnosticJson.Context.PublishReceipt, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException exception)
            {
                throw new DiagnosticTransportException("HTTP publication failed.", exception);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                using HttpResponseMessage response = await client.DeleteAsync($"diagnostics/v1/sessions/{Welcome.SessionId}", timeout.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
            {
            }
            finally
            {
                client.Dispose();
            }
        }
    }
}
