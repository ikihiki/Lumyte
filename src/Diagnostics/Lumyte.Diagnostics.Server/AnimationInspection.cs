using Lumyte.Diagnostics.Transport;

namespace Lumyte.Diagnostics.Server;

internal sealed class AnimationInspection(Func<OperationInvocation, CancellationToken, Task<DiagnosticOperationResult>> invoke)
{
    public const string SubsystemId = "animation";
    private const int MaximumTargets = 128;

    private static readonly Dictionary<string, (DiagnosticPermission Permission, DiagnosticField[] Arguments, DiagnosticField[] Results)> _schemas = new(StringComparer.Ordinal)
    {
        ["list"] = (DiagnosticPermission.Observe, [], Fields("targets:s")),
        ["read"] = (DiagnosticPermission.Observe, Fields("id:s"), SnapshotFields()),
        ["play"] = (DiagnosticPermission.Edit, Fields("id:s"), SnapshotFields()),
        ["pause"] = (DiagnosticPermission.Edit, Fields("id:s"), SnapshotFields()),
        ["stop"] = (DiagnosticPermission.Edit, Fields("id:s"), SnapshotFields()),
        ["seek"] = (DiagnosticPermission.Edit, Fields("id:s position-ticks:i"), SnapshotFields()),
        ["set-speed"] = (DiagnosticPermission.Edit, Fields("id:s speed:d"), SnapshotFields()),
        ["set-trigger"] = (DiagnosticPermission.Edit, Fields("id:s trigger:s"), SnapshotFields()),
        ["clear-triggers"] = (DiagnosticPermission.Edit, Fields("id:s"), SnapshotFields()),
    };

    public IReadOnlyList<string> Targets { get; private set; } = [];

    public Snapshot? Selected { get; private set; }

    public bool Busy { get; private set; }

    public string Error { get; private set; } = string.Empty;

    public static bool Supports(DiagnosticSubsystemCatalog? catalog)
    {
        if (catalog?.Subsystem.Id != SubsystemId || catalog.Subsystem.SchemaVersion != 1)
        {
            return false;
        }

        foreach ((string id, (DiagnosticPermission permission, DiagnosticField[] arguments, DiagnosticField[] results)) in _schemas)
        {
            OperationDescriptor[] matches = catalog.Operations.Where(operation => operation.Id == id).ToArray();
            if (matches.Length != 1 || matches[0].RequiredPermission != permission || matches[0].RequiresRevision
                || !Matches(matches[0].Arguments, arguments) || !Matches(matches[0].Results, results))
            {
                return false;
            }
        }

        return true;
    }

    public Task RefreshTargetsAsync(CancellationToken cancellationToken) => ExecuteAsync(
        async token =>
    {
        IReadOnlyDictionary<string, DiagnosticValue> values = await ReadAsync("list", [], token);
        string text = Text(values, "targets", 16512);
        string[] targets = text.Length == 0 ? [] : text.Split('\n');
        if (targets.Length > MaximumTargets || targets.Any(id => !ValidIdentifier(id)) || targets.Distinct(StringComparer.Ordinal).Count() != targets.Length
            || !targets.SequenceEqual(targets.Order(StringComparer.Ordinal)))
        {
            throw new InvalidDataException();
        }

        token.ThrowIfCancellationRequested();
        Targets = targets;
        if (Selected is not null && !targets.Contains(Selected.Id, StringComparer.Ordinal))
        {
            Selected = null;
        }
    },
        cancellationToken);

    public Task SelectAsync(string id, CancellationToken cancellationToken) => ExecuteAsync(
        async token =>
    {
        if (!Targets.Contains(id, StringComparer.Ordinal))
        {
            throw new InspectionException("一覧にある対象を選択してください。");
        }

        Snapshot snapshot = await ReadSnapshotAsync("read", new() { ["id"] = DiagnosticValue.From(id) }, token);
        if (snapshot.Id != id)
        {
            throw new InvalidDataException();
        }

        token.ThrowIfCancellationRequested();
        Selected = snapshot;
    },
        cancellationToken);

    public Task PlayAsync(CancellationToken token) => ChangeAsync("play", [], token);

    public Task PauseAsync(CancellationToken token) => ChangeAsync("pause", [], token);

    public Task StopAsync(CancellationToken token) => ChangeAsync("stop", [], token);

    public Task SeekAsync(long positionTicks, CancellationToken token)
        => positionTicks < 0 ? RejectAsync("再生位置は0以上で指定してください。", token) : ChangeAsync("seek", new() { ["position-ticks"] = DiagnosticValue.From(positionTicks) }, token);

    public Task SetSpeedAsync(double speed, CancellationToken token)
        => !double.IsFinite(speed) || speed < 0 ? RejectAsync("速度は0以上の有限値で指定してください。", token) : ChangeAsync("set-speed", new() { ["speed"] = DiagnosticValue.From(speed) }, token);

    public Task SetTriggerAsync(string trigger, CancellationToken token)
        => !ValidIdentifier(trigger) ? RejectAsync("Trigger名は1～128文字の小文字ASCII識別子で指定してください。", token) : ChangeAsync("set-trigger", new() { ["trigger"] = DiagnosticValue.From(trigger) }, token);

    public Task ClearTriggersAsync(CancellationToken token) => ChangeAsync("clear-triggers", [], token);

    private static DiagnosticField[] SnapshotFields() => Fields("id:s kind:s status:s state:s position-ticks:i speed:d");

    private static DiagnosticField[] Fields(string shape) => shape.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(field =>
    {
        string[] pair = field.Split(':');
        return new DiagnosticField(pair[0], pair[1] switch { "i" => DiagnosticValueKind.Int64, "d" => DiagnosticValueKind.Double, _ => DiagnosticValueKind.String });
    }).ToArray();

    private static bool Matches(DiagnosticField[] actual, DiagnosticField[] expected)
        => actual.Length == expected.Length && expected.All(field => actual.Count(candidate => candidate.Id == field.Id && candidate.Kind == field.Kind) == 1);

    private Task ChangeAsync(string operation, Dictionary<string, DiagnosticValue> arguments, CancellationToken cancellationToken) => ExecuteAsync(
        async token =>
    {
        Snapshot selected = Selected ?? throw new InspectionException("対象を選択してください。");
        arguments.Add("id", DiagnosticValue.From(selected.Id));
        Snapshot snapshot = await ReadSnapshotAsync(operation, arguments, token);
        if (snapshot.Id != selected.Id)
        {
            throw new InvalidDataException();
        }

        token.ThrowIfCancellationRequested();
        Selected = snapshot;
    },
        cancellationToken);

    private Task RejectAsync(string message, CancellationToken cancellationToken)
        => ExecuteAsync(_ => throw new InspectionException(message), cancellationToken);

    private async Task<Snapshot> ReadSnapshotAsync(string operation, Dictionary<string, DiagnosticValue> arguments, CancellationToken token)
    {
        IReadOnlyDictionary<string, DiagnosticValue> values = await ReadAsync(operation, arguments, token);
        string id = Text(values, "id", 128);
        string kind = Text(values, "kind", 32);
        string status = Text(values, "status", 32);
        string state = Text(values, "state", 4096);
        long position = Integer(values, "position-ticks", 0);
        double speed = Number(values, "speed", 0);
        bool validStatus = kind == "playback"
            ? status is "Stopped" or "Playing" or "Paused" or "Completed" or "Cancelled"
            : status is "Stopped" or "Running" or "Paused";
        if (!ValidIdentifier(id) || kind is not ("playback" or "state-machine") || !validStatus
            || (kind == "playback" && state.Length != 0) || (kind == "state-machine" && status == "Stopped" && state.Length != 0))
        {
            throw new InvalidDataException();
        }

        return new(id, kind, status, state, position, speed);
    }

    private async Task<IReadOnlyDictionary<string, DiagnosticValue>> ReadAsync(string operation, Dictionary<string, DiagnosticValue> arguments, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        DiagnosticOperationResult result = await invoke(new(Guid.NewGuid(), SubsystemId, operation, arguments), token);
        token.ThrowIfCancellationRequested();
        if (result.Status != "success")
        {
            string message = result.Code switch
            {
                "not-found" => "対象が登録解除されました。一覧を更新してください。",
                "unsupported" => "対象の種類または現在状態ではこの操作を実行できません。",
                "invalid-value" => "入力値がAnimationの契約外です。",
                "invalid-state" => "現在のAnimation状態では操作を実行できません。",
                "forbidden" => "この診断操作を実行する権限がありません。",
                "expired" => "操作の期限を超えました。結果は不明で、自動再送は行いません。",
                _ => "診断操作が拒否されました。",
            };
            throw new InspectionException((result.Code ?? result.Status) + " · " + message);
        }

        return result.Values is { Count: <= 8 } values ? values : throw new InvalidDataException();
    }

    private async Task ExecuteAsync(Func<CancellationToken, Task> action, CancellationToken token)
    {
        if (Busy || token.IsCancellationRequested)
        {
            return;
        }

        Busy = true;
        Error = string.Empty;
        try
        {
            await action(token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception) when (token.IsCancellationRequested)
        {
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

    private DiagnosticValue Value(IReadOnlyDictionary<string, DiagnosticValue> values, string field, DiagnosticValueKind kind)
        => values.TryGetValue(field, out DiagnosticValue value) && value.Kind == kind ? value : throw new InvalidDataException();

    private string Text(IReadOnlyDictionary<string, DiagnosticValue> values, string field, int maximum)
        => Value(values, field, DiagnosticValueKind.String).String is { } text && text.Length <= maximum ? text : throw new InvalidDataException();

    private long Integer(IReadOnlyDictionary<string, DiagnosticValue> values, string field, long minimum)
    {
        long value = Value(values, field, DiagnosticValueKind.Int64).Int64;
        return value >= minimum ? value : throw new InvalidDataException();
    }

    private double Number(IReadOnlyDictionary<string, DiagnosticValue> values, string field, double minimum)
    {
        double value = Value(values, field, DiagnosticValueKind.Double).Double;
        return double.IsFinite(value) && value >= minimum ? value : throw new InvalidDataException();
    }

    private bool ValidIdentifier(string value)
        => value.Length is >= 1 and <= 128 && value.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_' or '.');

    public sealed record Snapshot(string Id, string Kind, string Status, string State, long PositionTicks, double Speed);

    private sealed class InspectionException(string message) : Exception(message);
}
