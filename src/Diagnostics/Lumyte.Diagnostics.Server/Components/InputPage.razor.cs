using System.Text.Json;
using Microsoft.AspNetCore.Components;

namespace Lumyte.Diagnostics.Server.Components;

/// <summary>Renders device state and effective action mappings from the Input snapshot operation.</summary>
#pragma warning disable SA1204 // Parsing helpers stay next to their model declarations for this component.
public partial class InputPage
{
    private readonly CancellationTokenSource _lifetime = new();
    private SnapshotModel? _snapshot;
    private Task? _updates;
    private Guid? _session;
    private string _view = "devices";
    private string _status = "Inputスナップショットを読み込んでいます…";
    private bool _busy;

    [CascadingParameter]
    private DiagnosticPageContext Context { get; set; } = default!;

    private IEnumerable<(SubsystemDescriptor Subsystem, OperationDescriptor Operation)> InputOperations
        => (Context.Resource?.Catalog ?? []).Where(item => item.Subsystem.Id == "input")
            .SelectMany(item => item.Operations.Where(operation => operation.Id != "get-snapshot")
                .Select(operation => (item.Subsystem, operation)));

    private IEnumerable<(SubsystemDescriptor Subsystem, OperationDescriptor Operation)> MappingOperations
        => (Context.Resource?.Catalog ?? []).Where(item => item.Subsystem.Id == "settings.input-actions")
            .SelectMany(item => item.Operations.Where(operation => operation.Id is "read-tree" or "save-tree" or "save-result")
                .Select(operation => (item.Subsystem, operation)));

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await _lifetime.CancelAsync();
        if (_updates != null)
        {
            try
            {
                await _updates;
            }
            catch (OperationCanceledException)
            {
            }
        }

        _lifetime.Dispose();
    }

    /// <inheritdoc/>
    protected override async Task OnParametersSetAsync()
    {
        if (_session != Context.Resource?.SessionId)
        {
            _session = Context.Resource?.SessionId;
            await LoadAsync();
            _updates ??= UpdateAsync();
        }
    }

    private async Task UpdateAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        while (await timer.WaitForNextTickAsync(_lifetime.Token))
        {
            await InvokeAsync(LoadAsync);
        }
    }

    private async Task LoadAsync()
    {
        if (_busy || Context.Resource == null || Context.Invoke == null)
        {
            return;
        }

        OperationDescriptor? operation = Context.Resource.Catalog.FirstOrDefault(item => item.Subsystem.Id == "input")?.Operations.FirstOrDefault(item => item.Id == "get-snapshot");
        if (operation == null)
        {
            _status = "ゲームはInputスナップショット操作を公開していません。";
            return;
        }

        _busy = true;
        try
        {
            var invocation = new OperationInvocation(Guid.NewGuid(), "input", operation.Id, [], TimeoutMilliseconds: operation.Traffic == DiagnosticOperationTraffic.Stream ? 1000 : 5000);
            DiagnosticOperationResult? result = await Context.Invoke(invocation);
            if (result?.Status == "success" && result.Values?.TryGetValue("json", out DiagnosticValue value) == true && value.String != null)
            {
                _snapshot = Parse(value.String);
                _status = $"revision {_snapshot.Revision} · 500 msごとに更新";
            }
            else if (result != null)
            {
                _status = $"{result.Status} · {result.Code ?? "snapshot-unavailable"}";
            }
        }
        catch (JsonException)
        {
            _status = "Inputスナップショットの形式が正しくありません。";
        }
        finally
        {
            _busy = false;
        }
    }

    private static SnapshotModel Parse(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 24 });
        JsonElement root = document.RootElement;
        InputSystemModel[] inputs = root.GetProperty("inputSystems").EnumerateArray().Select(input => new InputSystemModel(
            input.GetProperty("id").GetGuid(),
            input.GetProperty("sources").EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray(),
            input.GetProperty("devices").EnumerateArray().Select(ParseDevice).ToArray())).ToArray();
        ActionSystemModel[] actions = root.GetProperty("actionSystems").EnumerateArray().Select(action => new ActionSystemModel(
            action.GetProperty("id").GetGuid(),
            action.GetProperty("timeMs").GetDouble(),
            action.GetProperty("contexts").EnumerateArray().Select(ParseContext).ToArray(),
            action.GetProperty("recognized").EnumerateArray().Select(item => new RecognitionModel(item.GetProperty("id").GetString()!, item.GetProperty("context").GetString()!, item.GetProperty("atMs").GetDouble(), Point(item.GetProperty("value")))).ToArray())).ToArray();
        return new(root.GetProperty("revision").GetInt64(), inputs, actions);
    }

    private static DeviceModel ParseDevice(JsonElement device)
    {
        string kind = device.GetProperty("kind").GetString()!;
        JsonElement state = device.GetProperty("state");
        string[] pressed = state.TryGetProperty(kind == "Keyboard" ? "keys" : "buttons", out JsonElement buttons) ? buttons.EnumerateArray().Select(item => item.GetString()!).ToArray() : [];
        PointModel? position = state.TryGetProperty("position", out JsonElement point) ? Point(point) : null;
        Dictionary<string, PointModel> sticks = state.TryGetProperty("sticks", out JsonElement stickObject) ? stickObject.EnumerateObject().ToDictionary(item => item.Name, item => Point(item.Value), StringComparer.Ordinal) : [];
        Dictionary<string, double> triggers = state.TryGetProperty("triggers", out JsonElement triggerObject) ? triggerObject.EnumerateObject().ToDictionary(item => item.Name, item => item.Value.GetDouble(), StringComparer.Ordinal) : [];
        Dictionary<string, Guid> targets = device.TryGetProperty("targets", out JsonElement targetObject) ? targetObject.EnumerateObject().ToDictionary(item => item.Name, item => item.Value.GetGuid(), StringComparer.Ordinal) : [];
        JsonElement pointers = state.TryGetProperty("contacts", out JsonElement contacts) ? contacts : state.TryGetProperty("pointers", out JsonElement pens) ? pens : default;
        PointerModel[] pointerModels = pointers.ValueKind == JsonValueKind.Array ? pointers.EnumerateArray().Select(item => new PointerModel(item.GetProperty("id").GetString()!, item.GetProperty("phase").GetString()!, Point(item.GetProperty("position")), item.GetProperty("pressure").ValueKind == JsonValueKind.Null ? null : item.GetProperty("pressure").GetDouble())).ToArray() : [];
        return new(device.GetProperty("id").GetUInt64(), kind, device.GetProperty("name").GetString()!, device.GetProperty("identity").GetString()!, device.GetProperty("sequence").GetUInt64(), device.GetProperty("connected").GetBoolean(), device.GetProperty("focused").GetBoolean(), pressed, position, sticks, triggers, pointerModels, targets);
    }

    private static ContextModel ParseContext(JsonElement context) => new(
        context.GetProperty("id").GetString()!,
        context.GetProperty("parent").ValueKind == JsonValueKind.Null ? null : context.GetProperty("parent").GetString(),
        context.GetProperty("priority").GetInt32(),
        context.GetProperty("exclusive").GetBoolean(),
        context.GetProperty("actions").EnumerateArray().Select(action => new ActionModel(
            action.GetProperty("id").GetString()!,
            action.GetProperty("target").GetGuid(),
            action.GetProperty("kind").GetString()!,
            Point(action.GetProperty("value")),
            action.GetProperty("phase").GetString()!,
            action.GetProperty("sensitivity").GetDouble(),
            action.GetProperty("smoothingMs").GetDouble(),
            action.GetProperty("bindings").EnumerateArray().Select(binding => new BindingModel(
                binding.GetProperty("id").GetString()!,
                binding.GetProperty("control").GetString()!,
                Point(binding.GetProperty("scale")))).ToArray())).ToArray());

    private static PointModel Point(JsonElement point) => new(point.GetProperty("x").GetDouble(), point.GetProperty("y").GetDouble());

    private static string ViewTitle(string view) => view switch { "devices" => "Devices", "actions" => "Actions", _ => "Tape / Live" };

    private static string Short(Guid value) => value.ToString("N", System.Globalization.CultureInfo.InvariantCulture)[..8];

    private static string Vector(PointModel? value) => value == null ? "—" : $"({value.X.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}, {value.Y.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)})";

    private static string List(string[] values) => values.Length == 0 ? "—" : string.Join(", ", values);

    private static string Percent(double value) => Math.Clamp(value, 0, 100).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "%";

    private static double StickX(PointModel value) => 50 + (Math.Clamp(value.X, -1, 1) * 40);

    private static double StickY(PointModel value) => 50 - (Math.Clamp(value.Y, -1, 1) * 40);

    private static string Strength(PointModel value) => (Math.Clamp(Math.Sqrt((value.X * value.X) + (value.Y * value.Y)), 0, 1) * 100).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "%";

    private sealed record SnapshotModel(long Revision, InputSystemModel[] Inputs, ActionSystemModel[] Actions);

    private sealed record InputSystemModel(Guid Id, string[] Sources, DeviceModel[] Devices);

    private sealed record DeviceModel(ulong Id, string Kind, string Name, string Identity, ulong Sequence, bool Connected, bool Focused, string[] Pressed, PointModel? Position, Dictionary<string, PointModel> Sticks, Dictionary<string, double> Triggers, PointerModel[] Pointers, Dictionary<string, Guid> Targets);

    private sealed record PointerModel(string Id, string Phase, PointModel Position, double? Pressure);

    private sealed record PointModel(double X, double Y);

    private sealed record ActionSystemModel(Guid Id, double TimeMs, ContextModel[] Contexts, RecognitionModel[] Recognized);

    private sealed record ContextModel(string Id, string? Parent, int Priority, bool Exclusive, ActionModel[] Actions);

    private sealed record ActionModel(string Id, Guid Target, string Kind, PointModel Value, string Phase, double Sensitivity, double SmoothingMs, BindingModel[] Bindings);

    private sealed record BindingModel(string Id, string Control, PointModel Scale);

    private sealed record RecognitionModel(string Id, string Context, double AtMs, PointModel Value);
}
#pragma warning restore SA1204
