using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Lumyte.Diagnostics.Transport;

namespace Lumyte.Diagnostics.UI.Automation.Tests;

internal sealed class LoopbackHandler : HttpMessageHandler
{
    private readonly ConcurrentQueue<(OperationInvocation? Invocation, TaskCompletionSource<HttpResponseMessage> Completion)> _requests = new();
    private readonly TaskCompletionSource<Guid> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread _thread;
    private bool _stop;
    private int _clicks;
    private int _dropStarts;

    internal LoopbackHandler()
    {
        _thread = new Thread(Run) { IsBackground = true };
        _thread.Start();
    }

    internal Task<Guid> Ready => _ready.Task;

    internal int Clicks => Volatile.Read(ref _clicks);

    internal int DropStarts { set => Volatile.Write(ref _dropStarts, value); }

    internal bool ExpireStarts { get; set; }

    internal bool CorruptHash { get; set; }

    internal bool CorruptCatalog { get; set; }

    internal ConcurrentQueue<string> ActionBodies { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        OperationInvocation? invocation = null;
        if (request.Method == HttpMethod.Post)
        {
            byte[] bytes = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            invocation = JsonSerializer.Deserialize(bytes, DiagnosticJson.Context.OperationInvocation)!;
            if (invocation.OperationId == "action-start")
            {
                ActionBodies.Enqueue(System.Text.Encoding.UTF8.GetString(bytes));
            }
        }

        var completion = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _requests.Enqueue((invocation, completion));
        HttpResponseMessage response = await completion.Task.WaitAsync(cancellationToken);
        if (invocation?.OperationId == "action-start" && Interlocked.Decrement(ref _dropStarts) >= 0)
        {
            response.Dispose();
            throw new HttpRequestException("Injected lost admission response.");
        }

        return response;
    }

    protected override void Dispose(bool disposing)
    {
        Volatile.Write(ref _stop, true);
        _thread.Join(TimeSpan.FromSeconds(5));
        base.Dispose(disposing);
    }

    private static HttpResponseMessage Json(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    private void Run()
    {
        try
        {
            using var h = new Harness(screenshots: true);
            _ready.SetResult(h.Session);
            while (!Volatile.Read(ref _stop))
            {
                while (_requests.TryDequeue(out (OperationInvocation? Invocation, TaskCompletionSource<HttpResponseMessage> Completion) request))
                {
                    if (request.Invocation is { } invocation)
                    {
                        var context = new DiagnosticOperationContext(invocation.RequestId, h.Session, new(0, 0), "operator", invocation.ExpectedRevision, CancellationToken.None);
                        DiagnosticOperationResult result = h.Operations.Invoke(invocation.OperationId, invocation.Arguments, context, new HashSet<DiagnosticPermission> { DiagnosticPermission.Observe, DiagnosticPermission.OverrideInput });
                        if (ExpireStarts && invocation.OperationId == "action-start" && result.Status == "success")
                        {
                            result = DiagnosticOperationResult.Reject("expired", "Operation result is unknown after the deadline.");
                        }

                        if (CorruptHash && invocation.OperationId == "artifact-read" && result.Values is not null)
                        {
                            var fields = result.Values.ToDictionary();
                            fields["sha256"] = DiagnosticValue.From(new string('0', 64));
                            result = result with { Values = fields };
                        }

                        request.Completion.SetResult(Json(JsonSerializer.SerializeToUtf8Bytes(result, DiagnosticJson.Context.DiagnosticOperationResult)));
                    }
                    else
                    {
                        OperationDescriptor[] operations = h.Operations.Catalog.ToArray();
                        if (CorruptCatalog)
                        {
                            operations[0] = operations[0] with { RequiredPermission = DiagnosticPermission.Edit };
                        }

                        SessionSnapshot[] sessions = [new(h.Session, Guid.NewGuid(), [new(new("ui.automation", "UI automation", 1), operations)], 0, 0, 0)];
                        request.Completion.SetResult(Json(JsonSerializer.SerializeToUtf8Bytes(sessions, DiagnosticJson.Context.SessionSnapshotArray)));
                    }
                }

                h.Step(TimeSpan.FromMilliseconds(2));
                Volatile.Write(ref _clicks, h.Clicks);
                Thread.Sleep(2);
            }
        }
        catch (Exception failure)
        {
            _ready.TrySetException(failure);
            while (_requests.TryDequeue(out (OperationInvocation? Invocation, TaskCompletionSource<HttpResponseMessage> Completion) request))
            {
                request.Completion.TrySetException(failure);
            }
        }
    }
}
