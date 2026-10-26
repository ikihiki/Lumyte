using System.Globalization;
using Microsoft.AspNetCore.Components;

namespace Lumyte.Diagnostics.Server.Components;

/// <summary>Observes and controls explicitly registered WindowSystem instances.</summary>
public partial class WindowPage
{
    private readonly CancellationTokenSource _lifetime = new();
    private WindowInspection? _model;
    private bool _supported;
    private bool _working;
    private bool _disposed;
    private Guid? _target;
    private string _notice = string.Empty;

    /// <summary>Gets or sets the selected game's immutable catalog and shell context.</summary>
    [CascadingParameter]
    public DiagnosticPageContext Context { get; set; } = default!;

    [Inject]
    private DiagnosticDashboardSession Session { get; set; } = default!;

    private bool Busy => _working || _model?.Busy == true || !Session.Connected;

    private string Error => _notice.Length > 0 ? _notice : _model?.Error ?? string.Empty;

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _lifetime.CancelAsync();
        _lifetime.Dispose();
    }

    /// <inheritdoc/>
    protected override async Task OnParametersSetAsync()
    {
        if (_disposed || Context.Resource is null || (_model is not null && _target == Context.Resource.SessionId))
        {
            return;
        }

        _supported = WindowInspection.Supports(Context.Resource.Catalog.FirstOrDefault(item => item.Subsystem.Id == WindowInspection.SubsystemId));
        if (!_supported)
        {
            return;
        }

        Guid target = Context.Resource.SessionId;
        _target = target;
        _model = new WindowInspection((invocation, token) => Session.InvokeAsync(target, invocation, token));
        await RefreshAsync();
    }

    private static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private Task RefreshAsync() => RunAsync(token => _model!.RefreshSystemsAsync(token));

    private Task SelectSystemAsync(string id) => RunAsync(token => _model!.SelectSystemAsync(id, token));

    private Task SelectWindowAsync(string id) => RunAsync(token => _model!.SelectWindowAsync(id, token));

    private Task ReadSelectedAsync() => _model?.Selected is { } selected ? SelectWindowAsync(selected.Window) : Task.CompletedTask;

    private Task ShowAsync() => RunAsync(token => _model!.ShowAsync(token));

    private Task HideAsync() => RunAsync(token => _model!.HideAsync(token));

    private Task ActivateAsync() => RunAsync(token => _model!.ActivateAsync(token));

    private Task BringToFrontAsync() => RunAsync(token => _model!.BringToFrontAsync(token));

    private Task CloseAsync() => RunAsync(token => _model!.CloseAsync(token));

    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (Busy || _model is null || _disposed)
        {
            return;
        }

        _working = true;
        _notice = string.Empty;
        StateHasChanged();
        try
        {
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, Session.ConnectionToken, Session.AuthenticationToken);
            await action(operation.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (UnauthorizedAccessException)
        {
            _notice = "認証が失効しました。ログインし直してください。";
        }
        finally
        {
            _working = false;
            if (!_disposed)
            {
                StateHasChanged();
            }
        }
    }
}
