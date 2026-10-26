using Lumyte.Diagnostics.Server;

namespace Lumyte.Diagnostics.IntegrationTests;

/// <summary>Checks bounded render graph inspection and asynchronous target isolation.</summary>
public sealed class RenderGraphInspectionTests
{
    /// <summary>Checks version, operation shape and permissions before using a dedicated page.</summary>
    [Fact]
    public void RequiresCaptureSchemaAndObservePermissions()
    {
        DiagnosticSubsystemCatalog catalog = Catalog();
        Assert.True(RenderGraphInspection.Supports(catalog));
        Assert.False(RenderGraphInspection.Supports(null));
        Assert.False(RenderGraphInspection.Supports(catalog with { Subsystem = catalog.Subsystem with { SchemaVersion = 1 } }));
        Assert.False(RenderGraphInspection.Supports(catalog with { Subsystem = catalog.Subsystem with { Id = "rendering" } }));
        Assert.False(RenderGraphInspection.Supports(catalog with { Operations = catalog.Operations.Skip(1).ToArray() }));
        foreach (OperationDescriptor replacement in new[]
        {
            catalog.Operations[0] with { RequiredPermission = DiagnosticPermission.Edit },
            catalog.Operations[0] with { RequiresRevision = true },
            catalog.Operations[0] with { Arguments = [new("unexpected", DiagnosticValueKind.String)] },
            catalog.Operations[0] with { Results = [new("compiles", DiagnosticValueKind.String)] },
        })
        {
            Assert.False(RenderGraphInspection.Supports(catalog with { Operations = [replacement, .. catalog.Operations.Skip(1)] }));
        }
    }

    /// <summary>Checks detail reads remain paged even for very large valid counts.</summary>
    /// <returns>The test completion.</returns>
    [Fact]
    public async Task PagesTenRowsWithoutEagerCaptureDownloadsAsync()
    {
        var remote = new Remote { LivePasses = int.MaxValue };
        var model = new RenderGraphInspection(remote.InvokeAsync);
        await model.OpenCaptureAsync(remote.Id, CancellationToken.None);
        Assert.Single(remote.Calls);
        Assert.Empty(model.Rows);
        await model.LoadDetailsAsync("passes", int.MaxValue - 12L, CancellationToken.None);
        Assert.Equal(10, model.Rows.Count);
        Assert.Equal(int.MaxValue - 12L, model.Offset);
        Assert.Equal(int.MaxValue, model.Total);
        Assert.Equal(11, remote.Calls.Count);
        Assert.All(remote.Calls.Skip(1), invocation =>
        {
            Assert.Equal(remote.Id, invocation.Arguments["capture-id"].String);
            Assert.Null(invocation.ExpectedRevision);
        });
        Assert.Equal(remote.Calls.Count, remote.Calls.Select(call => call.RequestId).Distinct().Count());
        await model.LoadDetailsAsync("passes", int.MaxValue - 2L, CancellationToken.None);
        Assert.Equal(2, model.Rows.Count);
        Assert.Equal(13, remote.Calls.Count);
    }

    /// <summary>Checks overlapping requests do not launch concurrent command sequences.</summary>
    /// <returns>The test completion.</returns>
    [Fact]
    public async Task RejectsOverlapUntilOutstandingReadCompletesAsync()
    {
        var response = new TaskCompletionSource<DiagnosticOperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        var model = new RenderGraphInspection((_, _) =>
        {
            calls++;
            return response.Task;
        });
        Task pending = model.RefreshSummaryAsync(CancellationToken.None);
        Assert.True(model.Busy);
        await model.RequestCaptureAsync(string.Empty, CancellationToken.None);
        await model.RefreshSummaryAsync(CancellationToken.None);
        Assert.Equal(1, calls);
        response.SetResult(Summary());
        await pending;
        Assert.False(model.Busy);
        Assert.Equal(10, model.Summary!.Compiles);
    }

    /// <summary>Checks immutable captures remain selected while aggregate monitoring advances.</summary>
    /// <returns>The test completion.</returns>
    [Fact]
    public async Task RequestsOnceAndKeepsCapturedRunAcrossLaterMonitoringAsync()
    {
        var remote = new Remote { State = "pending" };
        var model = new RenderGraphInspection(remote.InvokeAsync);
        await model.RequestCaptureAsync("main", CancellationToken.None);
        Assert.Equal("pending", model.Capture!.State);
        Assert.Equal(remote.Id, model.CaptureId);
        Assert.Equal(new[] { "request-capture", "get-capture" }, remote.Calls.Select(call => call.OperationId));
        remote.State = "compiled";
        await model.RefreshCaptureAsync(CancellationToken.None);
        string run = model.Capture!.RunId;
        await model.RefreshSummaryAsync(CancellationToken.None);
        remote.State = "recorded";
        await model.RefreshCaptureAsync(CancellationToken.None);
        Assert.Equal(run, model.Capture!.RunId);
        Assert.Equal(100, model.Capture.FrameId);
        Assert.Single(remote.Calls, call => call.OperationId == "request-capture");
        remote.Frame = 101;
        await model.RefreshCaptureAsync(CancellationToken.None);
        Assert.Contains("スキーマ", model.Error, StringComparison.Ordinal);
        Assert.Equal(100, model.Capture.FrameId);
    }

    /// <summary>Checks a result for a different capture cannot replace the selected identity.</summary>
    /// <returns>The test completion.</returns>
    [Fact]
    public async Task RejectsDifferentCaptureIdentityAsync()
    {
        var remote = new Remote();
        var model = new RenderGraphInspection(remote.InvokeAsync);
        await model.OpenCaptureAsync(Guid.NewGuid().ToString("D"), CancellationToken.None);
        Assert.Null(model.Capture);
        Assert.Contains("スキーマ", model.Error, StringComparison.Ordinal);
    }

    /// <summary>Checks expiry midway through a page discards both old and partial details.</summary>
    /// <returns>The test completion.</returns>
    [Fact]
    public async Task ExpiryDiscardsPartialAndPreviousDetailsAsync()
    {
        var remote = new Remote();
        var model = new RenderGraphInspection(remote.InvokeAsync);
        await model.OpenCaptureAsync(remote.Id, CancellationToken.None);
        await model.LoadDetailsAsync("passes", 0, CancellationToken.None);
        Assert.Equal(10, model.Rows.Count);
        remote.MissingIndex = 12;
        await model.LoadDetailsAsync("passes", 10, CancellationToken.None);
        Assert.Empty(model.Rows);
        Assert.Null(model.Capture);
        Assert.Equal(remote.Id, model.CaptureId);
        Assert.Contains("期限切れ", model.Error, StringComparison.Ordinal);
        Assert.Equal(3, remote.Calls.Count(call => call.OperationId == "get-pass" && call.Arguments["index"].Int64 >= 10));
    }

    /// <summary>Checks cancellation prevents late responses even when the transport ignores it.</summary>
    /// <returns>The test completion.</returns>
    [Fact]
    public async Task CanceledReadCannotCommitLateResultAsync()
    {
        var response = new TaskCompletionSource<DiagnosticOperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var model = new RenderGraphInspection((_, _) => response.Task);
        using var lifetime = new CancellationTokenSource();
        Task pending = model.RefreshSummaryAsync(lifetime.Token);
        lifetime.Cancel();
        response.SetResult(Summary());
        await pending;
        Assert.Null(model.Summary);
        Assert.Empty(model.Error);
        Assert.False(model.Busy);
    }

    /// <summary>Checks a canceled transport error cannot fail the retained browser circuit.</summary>
    /// <returns>The test completion.</returns>
    [Fact]
    public async Task CanceledReadSuppressesLateTransportFailureAsync()
    {
        var response = new TaskCompletionSource<DiagnosticOperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var model = new RenderGraphInspection((_, _) => response.Task);
        using var lifetime = new CancellationTokenSource();
        Task pending = model.RefreshSummaryAsync(lifetime.Token);
        lifetime.Cancel();
        response.SetException(new InvalidOperationException("Disconnected after cancellation."));
        await pending;
        Assert.Null(model.Summary);
        Assert.Empty(model.Error);
        Assert.False(model.Busy);
    }

    /// <summary>Checks unknown frame sentinels are confined to pending captures.</summary>
    /// <returns>The test completion.</returns>
    [Fact]
    public async Task RejectsNegativeCapturedFrameAsync()
    {
        var remote = new Remote { Frame = -1 };
        var model = new RenderGraphInspection(remote.InvokeAsync);
        await model.OpenCaptureAsync(remote.Id, CancellationToken.None);
        Assert.Null(model.Capture);
        Assert.Contains("スキーマ", model.Error, StringComparison.Ordinal);
    }

    /// <summary>Checks invalid scalar kinds and overflowing counts cannot trigger detail downloads.</summary>
    /// <returns>The test completion.</returns>
    [Fact]
    public async Task RejectsMalformedCaptureBeforeFetchingDetailsAsync()
    {
        var remote = new Remote { LivePasses = long.MaxValue };
        var model = new RenderGraphInspection(remote.InvokeAsync);
        await model.OpenCaptureAsync(remote.Id, CancellationToken.None);
        Assert.Null(model.Capture);
        Assert.Contains("スキーマ", model.Error, StringComparison.Ordinal);
        await model.LoadDetailsAsync("passes", 0, CancellationToken.None);
        Assert.Single(remote.Calls);
        model = new RenderGraphInspection((_, _) => Task.FromResult(DiagnosticOperationResult.Success(new Dictionary<string, DiagnosticValue> { ["compiles"] = DiagnosticValue.From("ten") })));
        await model.RefreshSummaryAsync(CancellationToken.None);
        Assert.Null(model.Summary);
        Assert.Contains("スキーマ", model.Error, StringComparison.Ordinal);
    }

    /// <summary>Checks failures are exposed without automatic resubmission.</summary>
    /// <param name="code">The remote rejection.</param>
    /// <param name="expected">The user-visible explanation.</param>
    /// <returns>The test completion.</returns>
    [Theory]
    [InlineData("busy", "処理中")]
    [InlineData("forbidden", "権限")]
    [InlineData("disabled", "無効")]
    [InlineData("expired", "自動再送")]
    [InlineData("too-large", "保存容量")]
    public async Task ReportsRejectedRequestsWithoutRetryAsync(string code, string expected)
    {
        int calls = 0;
        var model = new RenderGraphInspection((_, _) =>
        {
            calls++;
            return Task.FromResult(DiagnosticOperationResult.Reject(code, "remote explanation"));
        });
        await model.RequestCaptureAsync(string.Empty, CancellationToken.None);
        Assert.Equal(1, calls);
        Assert.Contains(expected, model.Error, StringComparison.Ordinal);
        Assert.Empty(model.CaptureId);
        Assert.False(model.Busy);
    }

    /// <summary>Checks invalid inputs do not reach the selected game.</summary>
    /// <returns>The test completion.</returns>
    [Fact]
    public async Task RejectsInvalidInputsLocallyAsync()
    {
        var remote = new Remote();
        var model = new RenderGraphInspection(remote.InvokeAsync);
        await model.OpenCaptureAsync("not-an-id", CancellationToken.None);
        await model.RequestCaptureAsync(new string('a', 257), CancellationToken.None);
        Assert.Empty(remote.Calls);
        await model.OpenCaptureAsync(remote.Id, CancellationToken.None);
        await model.LoadDetailsAsync("passes", long.MaxValue, CancellationToken.None);
        await model.LoadDetailsAsync("unknown", 0, CancellationToken.None);
        Assert.Single(remote.Calls);
        Assert.Empty(model.Rows);
    }

    private static DiagnosticOperationResult Summary() => DiagnosticOperationResult.Success(new Dictionary<string, DiagnosticValue>
    {
        ["compiles"] = DiagnosticValue.From(10L),
        ["records"] = DiagnosticValue.From(8L),
        ["failures"] = DiagnosticValue.From(2L),
        ["compile-milliseconds"] = DiagnosticValue.From(5.0),
        ["compile-maximum-milliseconds"] = DiagnosticValue.From(1.0),
        ["record-milliseconds"] = DiagnosticValue.From(2.0),
        ["record-maximum-milliseconds"] = DiagnosticValue.From(0.5),
        ["live-passes"] = DiagnosticValue.From(50L),
        ["culled-passes"] = DiagnosticValue.From(10L),
        ["barriers"] = DiagnosticValue.From(20L),
        ["captures"] = DiagnosticValue.From(1L),
        ["capture-pending"] = DiagnosticValue.From(false),
        ["retained-bytes"] = DiagnosticValue.From(2048L),
    });

    private static DiagnosticSubsystemCatalog Catalog()
    {
        (string Id, string Arguments, string Results)[] shapes =
        [
            ("get-summary", string.Empty, "compiles:i records:i failures:i compile-milliseconds:d compile-maximum-milliseconds:d record-milliseconds:d record-maximum-milliseconds:d live-passes:i culled-passes:i barriers:i captures:i capture-pending:b retained-bytes:i"),
            ("request-capture", "graph-name:s", "capture-id:s state:s"),
            ("get-capture", "capture-id:s", "capture-id:s graph-id:s run-id:s name:s frame-id:i state:s live-passes:i culled-passes:i resources:i transient-resources:i dependencies:i barriers:i compile-milliseconds:d record-milliseconds:d estimated-bytes:i"),
            ("get-pass", "capture-id:s index:i", "name:s execution-index:i has-side-effects:b"),
            ("get-resource", "capture-id:s index:i", "name:s kind:s transient:b output:b first-pass:i last-pass:i"),
            ("get-dependency", "capture-id:s index:i", "before:i after:i"),
            ("get-barrier", "capture-id:s index:i", "pass-index:i resource-index:i before-state:s after-state:s before-stages:i before-access:i after-stages:i after-access:i"),
        ];
        return new(new(RenderGraphInspection.SubsystemId, "Render graph", 2), shapes.Select(shape => new OperationDescriptor(shape.Id, shape.Id, DiagnosticPermission.Observe, Fields(shape.Arguments), Fields(shape.Results))).ToArray());
    }

    private static DiagnosticField[] Fields(string shape) => shape.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(field =>
    {
        string[] pair = field.Split(':');
        return new DiagnosticField(pair[0], pair[1] switch { "i" => DiagnosticValueKind.Int64, "d" => DiagnosticValueKind.Double, "b" => DiagnosticValueKind.Boolean, _ => DiagnosticValueKind.String });
    }).ToArray();

    private sealed class Remote
    {
        private readonly string _graphId = Guid.NewGuid().ToString("D");
        private readonly string _runId = Guid.NewGuid().ToString("D");

        public string Id { get; } = Guid.NewGuid().ToString("D");

        public List<OperationInvocation> Calls { get; } = [];

        public long LivePasses { get; set; } = 25;

        public long Frame { get; set; } = 100;

        public long? MissingIndex { get; set; }

        public string State { get; set; } = "recorded";

        public Task<DiagnosticOperationResult> InvokeAsync(OperationInvocation invocation, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(invocation);
            DiagnosticOperationResult result = invocation.OperationId switch
            {
                "get-summary" => Summary(),
                "request-capture" => DiagnosticOperationResult.Success(new Dictionary<string, DiagnosticValue> { ["capture-id"] = DiagnosticValue.From(Id), ["state"] = DiagnosticValue.From("pending") }),
                "get-capture" => Capture(),
                "get-pass" when invocation.Arguments["index"].Int64 == MissingIndex => DiagnosticOperationResult.Reject("not-found", "Capture expired."),
                "get-pass" => DiagnosticOperationResult.Success(new Dictionary<string, DiagnosticValue>
                {
                    ["name"] = DiagnosticValue.From("Pass " + invocation.Arguments["index"].Int64),
                    ["execution-index"] = DiagnosticValue.From(invocation.Arguments["index"].Int64),
                    ["has-side-effects"] = DiagnosticValue.From(false),
                }),
                _ => throw new InvalidOperationException("Unexpected operation."),
            };
            return Task.FromResult(result);
        }

        private DiagnosticOperationResult Capture() => DiagnosticOperationResult.Success(new Dictionary<string, DiagnosticValue>
        {
            ["capture-id"] = DiagnosticValue.From(Id),
            ["graph-id"] = DiagnosticValue.From(State == "pending" ? string.Empty : _graphId),
            ["run-id"] = DiagnosticValue.From(State == "pending" ? string.Empty : _runId),
            ["name"] = DiagnosticValue.From("main"),
            ["frame-id"] = DiagnosticValue.From(State == "pending" ? -1 : Frame),
            ["state"] = DiagnosticValue.From(State),
            ["live-passes"] = DiagnosticValue.From(LivePasses),
            ["culled-passes"] = DiagnosticValue.From(0L),
            ["resources"] = DiagnosticValue.From(1L),
            ["transient-resources"] = DiagnosticValue.From(0L),
            ["dependencies"] = DiagnosticValue.From(0L),
            ["barriers"] = DiagnosticValue.From(0L),
            ["compile-milliseconds"] = DiagnosticValue.From(0.2),
            ["record-milliseconds"] = DiagnosticValue.From(0.1),
            ["estimated-bytes"] = DiagnosticValue.From(4096L),
        });
    }
}
