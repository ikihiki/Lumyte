using Lumyte.Diagnostics.Transport;

#pragma warning disable SA1116, SA1117, SA1118, SA1204 // Keep operation flows and schema construction together.

namespace Lumyte.Diagnostics.Server;

internal sealed class WindowInspection(Func<OperationInvocation, CancellationToken, Task<DiagnosticOperationResult>> invoke)
{
    public const string SubsystemId = "window";
    private const int MaximumSystems = 64;
    private const int MaximumWindows = 512;

    private static readonly Dictionary<string, (DiagnosticPermission Permission, DiagnosticField[] Arguments, DiagnosticField[] Results)> _schemas = new(StringComparer.Ordinal)
    {
        ["list-systems"] = (DiagnosticPermission.Observe, [], Fields("systems:s")),
        ["list-windows"] = (DiagnosticPermission.Observe, Fields("system:s"), Fields("windows:s")),
        ["read"] = (DiagnosticPermission.Observe, Fields("system:s window:s"), SnapshotFields()),
        ["show"] = (DiagnosticPermission.Edit, Fields("system:s window:s"), RequestFields()),
        ["hide"] = (DiagnosticPermission.Edit, Fields("system:s window:s"), RequestFields()),
        ["activate"] = (DiagnosticPermission.Edit, Fields("system:s window:s"), RequestFields()),
        ["bring-to-front"] = (DiagnosticPermission.Edit, Fields("system:s window:s"), RequestFields()),
        ["close"] = (DiagnosticPermission.Edit, Fields("system:s window:s"), RequestFields()),
    };

    public IReadOnlyList<string> Systems { get; private set; } = [];

    public string? SelectedSystem { get; private set; }

    public IReadOnlyList<string> Windows { get; private set; } = [];

    public Snapshot? Selected { get; private set; }

    public bool Busy { get; private set; }

    public string Error { get; private set; } = string.Empty;

    public string Notice { get; private set; } = string.Empty;

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

    public Task RefreshSystemsAsync(CancellationToken token) => ExecuteAsync(async cancellationToken =>
    {
        IReadOnlyDictionary<string, DiagnosticValue> values = await ReadAsync("list-systems", [], cancellationToken);
        string[] systems = ParseList(Text(values, "systems", 8256), MaximumSystems, ValidSystem);
        cancellationToken.ThrowIfCancellationRequested();
        Systems = systems;
        if (SelectedSystem is not null && !systems.Contains(SelectedSystem, StringComparer.Ordinal))
        {
            SelectedSystem = null;
            Windows = [];
            Selected = null;
        }
    }, token);

    public Task SelectSystemAsync(string system, CancellationToken token) => ExecuteAsync(async cancellationToken =>
    {
        if (!Systems.Contains(system, StringComparer.Ordinal))
        {
            throw new InspectionException("一覧にあるWindowSystemを選択してください。");
        }

        IReadOnlyDictionary<string, DiagnosticValue> values = await ReadAsync("list-windows", new() { ["system"] = DiagnosticValue.From(system) }, cancellationToken);
        string[] windows = ParseList(Text(values, "windows", 16512), MaximumWindows, ValidWindow);
        cancellationToken.ThrowIfCancellationRequested();
        SelectedSystem = system;
        Windows = windows;
        if (Selected is not null && (Selected.System != system || !windows.Contains(Selected.Window, StringComparer.Ordinal)))
        {
            Selected = null;
        }
    }, token);

    public Task SelectWindowAsync(string window, CancellationToken token) => ExecuteAsync(async cancellationToken =>
    {
        string system = SelectedSystem ?? throw new InspectionException("WindowSystemを選択してください。");
        if (!Windows.Contains(window, StringComparer.Ordinal))
        {
            throw new InspectionException("一覧にあるWindowを選択してください。");
        }

        Snapshot snapshot = await ReadSnapshotAsync(system, window, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        Selected = snapshot;
    }, token);

    public Task ShowAsync(CancellationToken token) => CommandAsync("show", token);

    public Task HideAsync(CancellationToken token) => CommandAsync("hide", token);

    public Task ActivateAsync(CancellationToken token) => CommandAsync("activate", token);

    public Task BringToFrontAsync(CancellationToken token) => CommandAsync("bring-to-front", token);

    public Task CloseAsync(CancellationToken token) => CommandAsync("close", token);

    private static DiagnosticField[] SnapshotFields() => Fields("system:s window:s parent:s presentation-root:s kind:s role:s ownership:s native-window:s x:d y:d width:d height:d client-width:d client-height:d pixel-width:i pixel-height:i scale-x:d scale-y:d is-visible:b is-effectively-visible:b lifetime:s");

    private static DiagnosticField[] RequestFields() => Fields("system:s window:s request:i");

    private static DiagnosticField[] Fields(string shape) => shape.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(field =>
    {
        string[] pair = field.Split(':');
        return new DiagnosticField(pair[0], pair[1] switch
        {
            "b" => DiagnosticValueKind.Boolean,
            "i" => DiagnosticValueKind.Int64,
            "d" => DiagnosticValueKind.Double,
            _ => DiagnosticValueKind.String,
        });
    }).ToArray();

    private static bool Matches(DiagnosticField[] actual, DiagnosticField[] expected)
        => actual.Length == expected.Length && expected.All(field => actual.Count(candidate => candidate.Id == field.Id && candidate.Kind == field.Kind) == 1);

    private static bool ValidSystem(string value)
        => value.Length is >= 1 and <= 128 && value.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_' or '.');

    private static bool ValidWindow(string value)
        => value.StartsWith("window-", StringComparison.Ordinal) && ulong.TryParse(value.AsSpan(7), out ulong number) && number > 0;

    private static string[] ParseList(string text, int maximum, Func<string, bool> valid)
    {
        string[] entries = text.Length == 0 ? [] : text.Split('\n');
        if (entries.Length > maximum || entries.Any(entry => !valid(entry)) || entries.Distinct(StringComparer.Ordinal).Count() != entries.Length
            || !entries.SequenceEqual(entries.Order(StringComparer.Ordinal)))
        {
            throw new InvalidDataException();
        }

        return entries;
    }

    private Task CommandAsync(string operation, CancellationToken token) => ExecuteAsync(async cancellationToken =>
    {
        Snapshot selected = Selected ?? throw new InspectionException("Windowを選択してください。");
        IReadOnlyDictionary<string, DiagnosticValue> values = await ReadAsync(operation, Target(selected.System, selected.Window), cancellationToken);
        string system = Text(values, "system", 128);
        string window = Text(values, "window", 64);
        long request = Integer(values, "request", 1);
        if (system != selected.System || window != selected.Window)
        {
            throw new InvalidDataException();
        }

        cancellationToken.ThrowIfCancellationRequested();
        Notice = $"要求 {request} を受け付けました。状態を更新して完了結果を確認してください。";
    }, token);

    private async Task<Snapshot> ReadSnapshotAsync(string system, string window, CancellationToken token)
    {
        IReadOnlyDictionary<string, DiagnosticValue> values = await ReadAsync("read", Target(system, window), token);
        var snapshot = new Snapshot(
            Text(values, "system", 128), Text(values, "window", 64), Text(values, "parent", 64), Text(values, "presentation-root", 64),
            Text(values, "kind", 16), Text(values, "role", 32), Text(values, "ownership", 16), Text(values, "native-window", 128),
            Number(values, "x"), Number(values, "y"), Number(values, "width", 0, true), Number(values, "height", 0, true),
            Number(values, "client-width", 0, true), Number(values, "client-height", 0, true), Integer(values, "pixel-width", 0), Integer(values, "pixel-height", 0),
            Number(values, "scale-x", 0, true), Number(values, "scale-y", 0, true), Boolean(values, "is-visible"), Boolean(values, "is-effectively-visible"), Text(values, "lifetime", 16));
        if (snapshot.System != system || snapshot.Window != window || !ValidWindow(snapshot.PresentationRoot)
            || (snapshot.Parent.Length != 0 && !ValidWindow(snapshot.Parent)) || snapshot.Kind is not ("Native" or "Hosted")
            || snapshot.Role is not ("Normal" or "Tool" or "Popup" or "ContextMenu" or "ImeCandidates")
            || snapshot.Ownership is not ("Owned" or "Borrowed") || snapshot.Lifetime is not ("Open" or "Closing" or "Closed")
            || (snapshot.Kind == "Native") == (snapshot.NativeWindow.Length == 0))
        {
            throw new InvalidDataException();
        }

        return snapshot;
    }

    private static Dictionary<string, DiagnosticValue> Target(string system, string window)
        => new(StringComparer.Ordinal) { ["system"] = DiagnosticValue.From(system), ["window"] = DiagnosticValue.From(window) };

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
                "invalid-state" => "現在のWindow状態では操作を実行できません。",
                "forbidden" => "この診断操作を実行する権限がありません。",
                "expired" => "操作の期限を超えました。結果は不明で、自動再送は行いません。",
                _ => "診断操作が拒否されました。",
            };
            throw new InspectionException((result.Code ?? result.Status) + " · " + message);
        }

        return result.Values is { Count: <= 24 } values ? values : throw new InvalidDataException();
    }

    private async Task ExecuteAsync(Func<CancellationToken, Task> action, CancellationToken token)
    {
        if (Busy || token.IsCancellationRequested)
        {
            return;
        }

        Busy = true;
        Error = string.Empty;
        Notice = string.Empty;
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

    private static DiagnosticValue Value(IReadOnlyDictionary<string, DiagnosticValue> values, string field, DiagnosticValueKind kind)
        => values.TryGetValue(field, out DiagnosticValue value) && value.Kind == kind ? value : throw new InvalidDataException();

    private static string Text(IReadOnlyDictionary<string, DiagnosticValue> values, string field, int maximum)
        => Value(values, field, DiagnosticValueKind.String).String is { } text && text.Length <= maximum ? text : throw new InvalidDataException();

    private static long Integer(IReadOnlyDictionary<string, DiagnosticValue> values, string field, long minimum)
    {
        long value = Value(values, field, DiagnosticValueKind.Int64).Int64;
        return value >= minimum ? value : throw new InvalidDataException();
    }

    private static double Number(IReadOnlyDictionary<string, DiagnosticValue> values, string field, double minimum, bool exclusive)
    {
        double value = Value(values, field, DiagnosticValueKind.Double).Double;
        return double.IsFinite(value) && (exclusive ? value > minimum : value >= minimum) ? value : throw new InvalidDataException();
    }

    private static double Number(IReadOnlyDictionary<string, DiagnosticValue> values, string field)
    {
        double value = Value(values, field, DiagnosticValueKind.Double).Double;
        return double.IsFinite(value) ? value : throw new InvalidDataException();
    }

    private static bool Boolean(IReadOnlyDictionary<string, DiagnosticValue> values, string field)
        => Value(values, field, DiagnosticValueKind.Boolean).Boolean;

    public sealed record Snapshot(
        string System, string Window, string Parent, string PresentationRoot, string Kind, string Role, string Ownership, string NativeWindow,
        double X, double Y, double Width, double Height, double ClientWidth, double ClientHeight, long PixelWidth, long PixelHeight,
        double ScaleX, double ScaleY, bool IsVisible, bool IsEffectivelyVisible, string Lifetime);

    private sealed class InspectionException(string message) : Exception(message);
}
