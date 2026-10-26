using System.Collections.ObjectModel;
using Lumyte.Diagnostics.Transport;

namespace Lumyte.Diagnostics.Server;

internal sealed class RenderGraphInspection(Func<OperationInvocation, CancellationToken, Task<DiagnosticOperationResult>> invoke)
{
    public const string SubsystemId = "graphics.render-graph";
    public const int PageSize = 10;

    private static readonly Dictionary<string, (DiagnosticField[] Arguments, DiagnosticField[] Results)> _schemas = new(StringComparer.Ordinal)
    {
        ["get-summary"] = ([], Fields("compiles:i records:i failures:i compile-milliseconds:d compile-maximum-milliseconds:d record-milliseconds:d record-maximum-milliseconds:d live-passes:i culled-passes:i barriers:i captures:i capture-pending:b retained-bytes:i")),
        ["request-capture"] = (Fields("graph-name:s"), Fields("capture-id:s state:s")),
        ["get-capture"] = (Fields("capture-id:s"), Fields("capture-id:s graph-id:s run-id:s name:s frame-id:i state:s live-passes:i culled-passes:i resources:i transient-resources:i dependencies:i barriers:i compile-milliseconds:d record-milliseconds:d estimated-bytes:i")),
        ["get-pass"] = (Fields("capture-id:s index:i"), Fields("name:s execution-index:i has-side-effects:b")),
        ["get-resource"] = (Fields("capture-id:s index:i"), Fields("name:s kind:s transient:b output:b first-pass:i last-pass:i")),
        ["get-dependency"] = (Fields("capture-id:s index:i"), Fields("before:i after:i")),
        ["get-barrier"] = (Fields("capture-id:s index:i"), Fields("pass-index:i resource-index:i before-state:s after-state:s before-stages:i before-access:i after-stages:i after-access:i")),
    };

    public SummaryData? Summary { get; private set; }

    public CaptureData? Capture { get; private set; }

    public IReadOnlyList<Row> Rows { get; private set; } = [];

    public string Detail { get; private set; } = "passes";

    public long Offset { get; private set; }

    public long Total => Capture is null ? 0 : Count(Capture, Detail);

    public bool Busy { get; private set; }

    public string Error { get; private set; } = string.Empty;

    public string CaptureId { get; private set; } = string.Empty;

    public static bool Supports(DiagnosticSubsystemCatalog? catalog)
    {
        if (catalog?.Subsystem.Id != SubsystemId || catalog.Subsystem.SchemaVersion != 2)
        {
            return false;
        }

        foreach ((string id, (DiagnosticField[] arguments, DiagnosticField[] results)) in _schemas)
        {
            OperationDescriptor[] matches = catalog.Operations.Where(operation => operation.Id == id).ToArray();
            if (matches.Length != 1 || matches[0].RequiredPermission != DiagnosticPermission.Observe || matches[0].RequiresRevision
                || !Matches(matches[0].Arguments, arguments, exact: true) || !Matches(matches[0].Results, results, exact: false))
            {
                return false;
            }
        }

        return true;
    }

    public Task RefreshSummaryAsync(CancellationToken cancellationToken) => ExecuteAsync(
        async token =>
        {
            IReadOnlyDictionary<string, DiagnosticValue> values = await ReadAsync("get-summary", [], token);
            Summary = new SummaryData(Integer(values, "compiles"), Integer(values, "records"), Integer(values, "failures"), Number(values, "compile-milliseconds"), Number(values, "compile-maximum-milliseconds"), Number(values, "record-milliseconds"), Number(values, "record-maximum-milliseconds"), Integer(values, "live-passes"), Integer(values, "culled-passes"), Integer(values, "barriers"), Integer(values, "captures"), Boolean(values, "capture-pending"), Integer(values, "retained-bytes"));
        },
        cancellationToken);

    public Task RequestCaptureAsync(string graphName, CancellationToken cancellationToken) => ExecuteAsync(
        async token =>
        {
            if (graphName.Length > 256)
            {
                throw new InspectionException("グラフ名は256文字以内で指定してください。");
            }

            IReadOnlyDictionary<string, DiagnosticValue> values = await ReadAsync("request-capture", new() { ["graph-name"] = DiagnosticValue.From(graphName) }, token);
            string id = Identifier(Text(values, "capture-id", 36));
            if (Text(values, "state", 32) != "pending")
            {
                throw new InvalidDataException();
            }

            SelectCapture(id);
            await ReadCaptureAsync(token);
        },
        cancellationToken);

    public Task OpenCaptureAsync(string id, CancellationToken cancellationToken) => ExecuteAsync(
        async token =>
        {
            string normalized;
            try
            {
                normalized = Identifier(id);
            }
            catch (InvalidDataException)
            {
                throw new InspectionException("Capture IDをUUID形式で入力してください。");
            }

            SelectCapture(normalized);
            await ReadCaptureAsync(token);
        },
        cancellationToken);

    public Task RefreshCaptureAsync(CancellationToken cancellationToken) => ExecuteAsync(ReadCaptureAsync, cancellationToken);

    public Task LoadDetailsAsync(string kind, long offset, CancellationToken cancellationToken) => ExecuteAsync(
        async token =>
        {
            string operation = kind switch
            {
                "passes" => "get-pass",
                "resources" => "get-resource",
                "dependencies" => "get-dependency",
                "barriers" => "get-barrier",
                _ => throw new InspectionException("詳細の種類が不正です。"),
            };
            Rows = [];
            Detail = kind;
            Offset = 0;
            CaptureData capture = Capture ?? throw new InspectionException("キャプチャを取得してください。");
            long total = Count(capture, kind);
            if (offset < 0 || (total > 0 && offset >= total) || (total == 0 && offset != 0))
            {
                throw new InspectionException("表示範囲がキャプチャの件数を超えています。");
            }

            if (capture.State is "pending" or "capturing" or "compile-failed" or "too-large")
            {
                throw new InspectionException(capture.State == "too-large" ? "キャプチャが保存容量を超えています。詳細は保存されていません。" : "このキャプチャの詳細はまだ取得できません。");
            }

            int length = (int)Math.Min(PageSize, total - offset);
            var rows = new List<Row>(length);
            for (int index = 0; index < length; index++)
            {
                long position = offset + index;
                IReadOnlyDictionary<string, DiagnosticValue> values = await ReadAsync(operation, new() { ["capture-id"] = DiagnosticValue.From(capture.CaptureId), ["index"] = DiagnosticValue.From(position) }, token);
                ValidateRow(values, kind, capture);
                rows.Add(new Row(position, new ReadOnlyDictionary<string, DiagnosticValue>(new Dictionary<string, DiagnosticValue>(values, StringComparer.Ordinal))));
            }

            token.ThrowIfCancellationRequested();
            Rows = rows.AsReadOnly();
            Offset = offset;
        },
        cancellationToken);

    private static DiagnosticField[] Fields(string text) => text.Split(' ').Select(field =>
    {
        string[] pair = field.Split(':');
        return new DiagnosticField(pair[0], pair[1] switch { "i" => DiagnosticValueKind.Int64, "d" => DiagnosticValueKind.Double, "b" => DiagnosticValueKind.Boolean, _ => DiagnosticValueKind.String });
    }).ToArray();

    private static bool Matches(DiagnosticField[] actual, DiagnosticField[] expected, bool exact)
        => (!exact || actual.Length == expected.Length) && expected.All(field => actual.Count(candidate => candidate.Id == field.Id && candidate.Kind == field.Kind) == 1);

    private static long Count(CaptureData capture, string kind) => kind switch
    {
        "passes" => capture.LivePasses + capture.CulledPasses,
        "resources" => capture.Resources,
        "dependencies" => capture.Dependencies,
        "barriers" => capture.Barriers,
        _ => 0,
    };

    private static DiagnosticValue Value(IReadOnlyDictionary<string, DiagnosticValue> values, string field, DiagnosticValueKind kind)
    {
        if (!values.TryGetValue(field, out DiagnosticValue value) || value.Kind != kind)
        {
            throw new InvalidDataException();
        }

        return value;
    }

    private static long Integer(IReadOnlyDictionary<string, DiagnosticValue> values, string field, long minimum = 0, long maximum = long.MaxValue)
    {
        long value = Value(values, field, DiagnosticValueKind.Int64).Int64;
        return value >= minimum && value <= maximum ? value : throw new InvalidDataException();
    }

    private static double Number(IReadOnlyDictionary<string, DiagnosticValue> values, string field)
    {
        double value = Value(values, field, DiagnosticValueKind.Double).Double;
        return double.IsFinite(value) && value >= 0 ? value : throw new InvalidDataException();
    }

    private static bool Boolean(IReadOnlyDictionary<string, DiagnosticValue> values, string field) => Value(values, field, DiagnosticValueKind.Boolean).Boolean;

    private static string Text(IReadOnlyDictionary<string, DiagnosticValue> values, string field, int maximum = 256)
    {
        string? value = Value(values, field, DiagnosticValueKind.String).String;
        return value is not null && value.Length <= maximum ? value : throw new InvalidDataException();
    }

    private static string Identifier(string id) => Guid.TryParseExact(id, "D", out Guid parsed) && parsed != Guid.Empty ? parsed.ToString("D") : throw new InvalidDataException();

    private static void ValidateRow(IReadOnlyDictionary<string, DiagnosticValue> values, string kind, CaptureData capture)
    {
        switch (kind)
        {
            case "passes":
                Text(values, "name");
                Integer(values, "execution-index", -1, capture.LivePasses - 1);
                Boolean(values, "has-side-effects");
                break;
            case "resources":
                Text(values, "name");
                string resourceKind = Text(values, "kind");
                if (resourceKind is not ("Texture" or "Buffer" or "ShaderDataBuffer"))
                {
                    throw new InvalidDataException();
                }

                Boolean(values, "transient");
                Boolean(values, "output");
                long first = Integer(values, "first-pass", -1, capture.LivePasses - 1);
                long last = Integer(values, "last-pass", -1, capture.LivePasses - 1);
                if (last < first || (first == -1) != (last == -1))
                {
                    throw new InvalidDataException();
                }

                break;
            case "dependencies":
                long before = Integer(values, "before", 0, capture.LivePasses - 1);
                long after = Integer(values, "after", 0, capture.LivePasses - 1);
                if (before >= after)
                {
                    throw new InvalidDataException();
                }

                break;
            case "barriers":
                Integer(values, "pass-index", 0, capture.LivePasses);
                Integer(values, "resource-index", 0, capture.Resources - 1);
                Text(values, "before-state");
                Text(values, "after-state");
                Integer(values, "before-stages");
                Integer(values, "before-access");
                Integer(values, "after-stages");
                Integer(values, "after-access");
                break;
        }
    }

    private async Task ExecuteAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        if (Busy || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        Busy = true;
        Error = string.Empty;
        try
        {
            await action(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Cancellation never retries a command or commits a late response.
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            // A transport can finish with an error after its selected target was abandoned.
        }
        catch (Exception exception) when (exception is InspectionException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        {
            Error = exception switch
            {
                InspectionException => exception.Message,
                InvalidDataException => "受信した診断データが公開スキーマと一致しません。",
                UnauthorizedAccessException => "診断の認証が失効しました。ログインし直してください。",
                KeyNotFoundException => "診断対象のゲームは接続されていません。",
                _ => "接続を確認できません。操作は自動で再送されません。",
            };
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task<IReadOnlyDictionary<string, DiagnosticValue>> ReadAsync(string operation, Dictionary<string, DiagnosticValue> arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DiagnosticOperationResult result = await invoke(new(Guid.NewGuid(), SubsystemId, operation, arguments), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (result.Status != "success")
        {
            if (result.Code == "not-found" && operation != "get-summary" && operation != "request-capture")
            {
                Capture = null;
                Rows = [];
                Offset = 0;
            }

            string message = result.Code switch
            {
                "not-found" => "キャプチャが見つかりません。期限切れ、保存容量による削除、またはIDを確認してください。",
                "not-ready" => "キャプチャの詳細はまだ準備できていません。",
                "too-large" => "キャプチャが保存容量を超えています。詳細は保存されていません。",
                "busy" => "診断要求またはキャプチャが処理中です。しばらく待ってから操作してください。",
                "forbidden" => "この診断操作を実行する権限がありません。",
                "disabled" => "ゲーム側のレンダーグラフ診断が無効です。",
                "expired" => "操作の期限を超えました。実行結果は不明です。自動再送は行いません。",
                _ => "診断操作が拒否されました。",
            };
            throw new InspectionException((result.Code ?? result.Status) + " · " + message + (string.IsNullOrEmpty(result.Message) ? string.Empty : " " + result.Message));
        }

        return result.Values is { Count: <= 32 } values ? values : throw new InvalidDataException();
    }

    private async Task ReadCaptureAsync(CancellationToken cancellationToken)
    {
        if (CaptureId.Length == 0)
        {
            throw new InspectionException("Capture IDを指定してください。");
        }

        IReadOnlyDictionary<string, DiagnosticValue> values = await ReadAsync("get-capture", new() { ["capture-id"] = DiagnosticValue.From(CaptureId) }, cancellationToken);
        string id = Identifier(Text(values, "capture-id", 36));
        string state = Text(values, "state", 32);
        if (id != CaptureId || state is not ("pending" or "capturing" or "compiled" or "recorded" or "compile-failed" or "record-failed" or "too-large"))
        {
            throw new InvalidDataException();
        }

        string graphId = Text(values, "graph-id", 36);
        string runId = Text(values, "run-id", 36);
        if (state == "pending")
        {
            if (graphId.Length != 0 || runId.Length != 0)
            {
                throw new InvalidDataException();
            }
        }
        else
        {
            graphId = Identifier(graphId);
            runId = Identifier(runId);
        }

        long live = Integer(values, "live-passes", maximum: int.MaxValue);
        long culled = Integer(values, "culled-passes", maximum: int.MaxValue - live);
        long resources = Integer(values, "resources", maximum: int.MaxValue);
        long frame = state == "pending" ? Integer(values, "frame-id", -1, -1) : Integer(values, "frame-id");
        var capture = new CaptureData(id, graphId, runId, Text(values, "name"), frame, state, live, culled, resources, Integer(values, "transient-resources", maximum: resources), Integer(values, "dependencies", maximum: int.MaxValue), Integer(values, "barriers", maximum: int.MaxValue), Number(values, "compile-milliseconds"), Number(values, "record-milliseconds"), Integer(values, "estimated-bytes"));
        if (Capture is { RunId.Length: > 0 } previous && (previous.RunId != capture.RunId || previous.GraphId != capture.GraphId || previous.FrameId != capture.FrameId))
        {
            throw new InvalidDataException();
        }

        Capture = capture;
    }

    private void SelectCapture(string id)
    {
        CaptureId = id;
        Capture = null;
        Rows = [];
        Offset = 0;
    }

    public sealed record SummaryData(long Compiles, long Records, long Failures, double CompileMilliseconds, double CompileMaximumMilliseconds, double RecordMilliseconds, double RecordMaximumMilliseconds, long LivePasses, long CulledPasses, long Barriers, long Captures, bool CapturePending, long RetainedBytes);

    public sealed record CaptureData(string CaptureId, string GraphId, string RunId, string Name, long FrameId, string State, long LivePasses, long CulledPasses, long Resources, long TransientResources, long Dependencies, long Barriers, double CompileMilliseconds, double RecordMilliseconds, long EstimatedBytes);

    public sealed record Row(long Index, IReadOnlyDictionary<string, DiagnosticValue> Values);

    private sealed class InspectionException(string message) : Exception(message);
}
