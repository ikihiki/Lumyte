using System.Text.Json;
using Lumyte.Diagnostics.Transport;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.WebUtilities;

namespace Lumyte.Diagnostics.Server.Components;

/// <summary>The resource-oriented, authenticated diagnostic workspace.</summary>
public partial class Dashboard : IAsyncDisposable
{
    private static readonly Dictionary<string, string> _titles = new(StringComparer.Ordinal)
    {
        ["overview"] = "Resources",
        ["operations"] = "Operations",
        ["input"] = "Input",
        ["metrics"] = "Metrics",
        ["logs"] = "Logs",
        ["traces"] = "Traces",
    };

    private static readonly DiagnosticJsonContext _prettyJson = new(new JsonSerializerOptions(DiagnosticJson.Context.Options) { WriteIndented = true });
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _subscription;
    private CancellationTokenRegistration _authenticationRegistration;
    private Task _updates = Task.CompletedTask;
    private SessionSnapshot[] _resources = [];
    private DiagnosticUiSession[] _summaries = [];
    private DiagnosticEvent[] _events = [];
    private Guid? _selectedId;
    private long _selectionVersion;
    private DiagnosticEvent? _inspected;
    private bool _paused;
    private bool _expired;
    private bool _confirmDisconnect;
    private bool _ready;
    private bool _disposed;
    private string _view = "overview";
    private string _resourceFilter = string.Empty;
    private string _operationFilter = string.Empty;
    private string _eventFilter = string.Empty;
    private string _traceFilter = string.Empty;
    private string _level = string.Empty;
    private string _error = string.Empty;
    private string _updatedAt = "更新待ち";
    private string _operationStatus = "操作を実行すると結果を表示します。";
    private string _operationResult = string.Empty;

    [Inject]
    private DiagnosticDashboardSession Session { get; set; } = default!;

    [Inject]
    private AuthenticationStateProvider Authentication { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    private SessionSnapshot? Selected => _resources.FirstOrDefault(resource => resource.SessionId == _selectedId);

    private string EventKind => _view == "metrics" ? "metric" : _view == "logs" ? "log" : "span";

    private DiagnosticEvent[] FilteredEvents => DiagnosticTelemetryModel.Filter(_events, EventKind, _eventFilter, _view == "metrics" ? string.Empty : _traceFilter, _level);

    private SessionSnapshot[] FilteredResources => _resources.Where(resource => string.Join(' ', resource.InstanceId, resource.SessionId, string.Join(' ', resource.Catalog.Select(item => item.Subsystem.DisplayName))).Contains(_resourceFilter, StringComparison.OrdinalIgnoreCase)).ToArray();

    private (SubsystemDescriptor Subsystem, OperationDescriptor Operation)[] Operations => (Selected?.Catalog ?? []).SelectMany(item => item.Operations.Select(operation => (item.Subsystem, operation))).ToArray();

    private IEnumerable<(SubsystemDescriptor Subsystem, OperationDescriptor Operation)> FilteredOperations => Operations.Where(item => (_view != "input" || item.Operation.RequiredPermission == DiagnosticPermission.OverrideInput)
        && string.Join(' ', item.Subsystem.Id, item.Subsystem.DisplayName, item.Operation.Id, item.Operation.DisplayName).Contains(_operationFilter, StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        Navigation.LocationChanged -= RouteChanged;
        Session.ConnectionChanged -= ConnectionChanged;
        _authenticationRegistration.Dispose();
        await _lifetime.CancelAsync();
        await StopSubscriptionAsync();
        _lifetime.Dispose();
    }

    /// <inheritdoc/>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        try
        {
            Session.Attach((await Authentication.GetAuthenticationStateAsync()).User);
            _authenticationRegistration = Session.AuthenticationToken.Register(() => _ = InvokeAsync(Expire));
            Navigation.LocationChanged += RouteChanged;
            Session.ConnectionChanged += ConnectionChanged;
            _ready = true;
            ReadResources();
            ApplyRoute();
            await RestartAsync();
        }
        catch (UnauthorizedAccessException)
        {
            Expire();
        }
    }

    private void ReadResources() => _resources = Session.Resources();

    private void ApplyRoute(string? uri = null)
    {
        Dictionary<string, Microsoft.Extensions.Primitives.StringValues> query = QueryHelpers.ParseQuery(Navigation.ToAbsoluteUri(uri ?? Navigation.Uri).Query);
        string view = query.GetValueOrDefault("view").ToString();
        _view = _titles.ContainsKey(view) ? view : "overview";
        Guid? requested = Guid.TryParse(query.GetValueOrDefault("session"), out Guid id) ? id : null;
        Guid? selected = query.ContainsKey("session") ? requested.HasValue && _resources.Any(resource => resource.SessionId == requested) ? requested : null : _selectedId.HasValue && Selected != null ? _selectedId : _resources.FirstOrDefault()?.SessionId;
        if (selected != _selectedId)
        {
            _selectedId = selected;
            _selectionVersion++;
            _events = [];
            _paused = false;
            _inspected = null;
            _operationStatus = "操作を実行すると結果を表示します。";
            _operationResult = string.Empty;
            _confirmDisconnect = false;
        }

        _traceFilter = requested.HasValue && selected == null ? string.Empty : query.GetValueOrDefault("trace").ToString();
        _error = requested.HasValue && selected == null ? "リンク先のゲームは接続されていません。診断対象を選択してください。" : string.Empty;
        if (!_paused && _selectedId.HasValue)
        {
            _events = Session.Events(_selectedId.Value);
        }
    }

    private void Refresh()
    {
        try
        {
            ReadResources();
            ApplyRoute();
        }
        catch (UnauthorizedAccessException)
        {
            Expire();
        }
    }

    private void RefreshState()
    {
        DiagnosticUiSession[] summaries = Session.Summaries();
        bool topology = !summaries.Select(item => item.SessionId).SequenceEqual(_resources.Select(item => item.SessionId));
        DiagnosticUiSession? previous = _summaries.FirstOrDefault(item => item.SessionId == _selectedId);
        DiagnosticUiSession? current = summaries.FirstOrDefault(item => item.SessionId == _selectedId);
        if (topology)
        {
            ReadResources();
            ApplyRoute();
        }
        else
        {
            Dictionary<Guid, DiagnosticUiSession> byId = summaries.ToDictionary(item => item.SessionId);
            _resources = _resources.Select(resource => byId.TryGetValue(resource.SessionId, out DiagnosticUiSession? summary) ? resource with { TelemetryReceived = summary.TelemetryReceived, TelemetryDropped = summary.TelemetryDropped, PendingCommands = summary.PendingCommands } : resource).ToArray();
            if (!_paused && _selectedId.HasValue && current != previous)
            {
                _events = Session.Events(_selectedId.Value);
            }
        }

        _summaries = summaries;
        _updatedAt = "更新 " + DateTimeOffset.Now.ToString("T", System.Globalization.CultureInfo.CurrentCulture);
        StateHasChanged();
    }

    private void ConnectionChanged()
    {
        if (_ready)
        {
            _ = InvokeAsync(RestartAsync);
        }
    }

    private async Task RestartAsync()
    {
        await StopSubscriptionAsync();
        if (_expired || _lifetime.IsCancellationRequested || !Session.Connected)
        {
            return;
        }

        try
        {
            _subscription = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, Session.ConnectionToken, Session.AuthenticationToken);
            _updates = UpdateAsync(_subscription.Token);
        }
        catch (UnauthorizedAccessException)
        {
            Expire();
        }
    }

    private async Task StopSubscriptionAsync()
    {
        if (_subscription != null)
        {
            await _subscription.CancelAsync();
            await _updates;
            _subscription.Dispose();
            _subscription = null;
        }
    }

    private async Task UpdateAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                long version = Session.Version;
                await InvokeAsync(RefreshState);
                await Session.WaitForChangeAsync(version, cancellationToken);
                await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Disconnect, logout and disposal never replay an operation.
        }
        catch (UnauthorizedAccessException)
        {
            await InvokeAsync(Expire);
        }
        catch (InvalidOperationException) when (!Session.Connected || _lifetime.IsCancellationRequested)
        {
            // A connection can disappear between a notification and its read.
        }
    }

    private void RouteChanged(object? sender, LocationChangedEventArgs args)
    {
        _ = InvokeAsync(() =>
        {
            if (!_ready || _expired)
            {
                return;
            }

            try
            {
                ApplyRoute();
                StateHasChanged();
            }
            catch (UnauthorizedAccessException)
            {
                Expire();
            }
        });
    }

    private void Navigate(string view, Guid? session = null, string trace = "")
    {
        var values = new Dictionary<string, string?> { ["view"] = view, ["session"] = (session ?? _selectedId)?.ToString(), ["trace"] = trace.Length > 0 ? trace : null };
        string uri = QueryHelpers.AddQueryString("/", values);
        Navigation.NavigateTo(uri);
        ApplyRoute(uri);
    }

    private void SelectGame(ChangeEventArgs args)
    {
        if (Guid.TryParse(args.Value?.ToString(), out Guid id))
        {
            Navigate(_view, id);
        }
        else
        {
            string uri = QueryHelpers.AddQueryString("/", new Dictionary<string, string?> { ["view"] = _view, ["session"] = string.Empty });
            Navigation.NavigateTo(uri);
            ApplyRoute(uri);
        }
    }

    private void Trace(string trace) => Navigate("traces", _selectedId, trace);

    private void Inspect(DiagnosticEvent item) => _inspected = item;

    private void ClearFilters()
    {
        _eventFilter = string.Empty;
        _traceFilter = string.Empty;
        _level = string.Empty;
        Navigate(_view);
    }

    private void TogglePause()
    {
        _paused = !_paused;
        if (!_paused && _selectedId.HasValue)
        {
            try
            {
                _events = Session.Events(_selectedId.Value);
            }
            catch (UnauthorizedAccessException)
            {
                Expire();
            }
        }
    }

    private void AskDisconnect() => _confirmDisconnect = true;

    private void Disconnect()
    {
        try
        {
            if (_selectedId.HasValue)
            {
                Session.Close(_selectedId.Value);
                _confirmDisconnect = false;
                Refresh();
            }
        }
        catch (UnauthorizedAccessException)
        {
            Expire();
        }
    }

    private async Task ExecuteAsync(OperationInvocation invocation)
    {
        Guid? target = _selectedId;
        long version = _selectionVersion;
        if (!target.HasValue)
        {
            return;
        }

        _operationStatus = "ゲームの実行結果を待っています…";
        try
        {
            DiagnosticOperationResult result = await Session.InvokeAsync(target.Value, invocation, _lifetime.Token);
            if (target == _selectedId && version == _selectionVersion && !_expired)
            {
                _operationStatus = result.Status + (result.Code == null ? string.Empty : " · " + result.Code);
                _operationResult = JsonSerializer.Serialize(result, _prettyJson.DiagnosticOperationResult);
            }
        }
        catch (UnauthorizedAccessException)
        {
            Expire();
        }
        catch (Exception exception) when (exception is OperationCanceledException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        {
            if (target == _selectedId && version == _selectionVersion && !_expired)
            {
                _operationStatus = "結果を確認できません。操作は自動で再送されません。";
                _error = exception.Message;
            }
        }
    }

    private void Expire()
    {
        if (_disposed)
        {
            return;
        }

        _expired = true;
        _resources = [];
        _events = [];
        _summaries = [];
        _selectedId = null;
        _inspected = null;
        _operationResult = string.Empty;
        _lifetime.Cancel();
        StateHasChanged();
    }
}
