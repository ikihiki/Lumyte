using Microsoft.AspNetCore.Components;

namespace Lumyte.Diagnostics.Server.Components;

/// <summary>Controls explicitly published animation executors through bounded diagnostic operations.</summary>
public partial class AnimationPage
{
    private readonly CancellationTokenSource _lifetime = new();
    private AnimationInspection? _model;
    private bool _supported;
    private bool _working;
    private bool _disposed;
    private Guid? _target;
    private long _position;
    private double _speed = 1;
    private string _trigger = string.Empty;
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

        _supported = AnimationInspection.Supports(Context.Resource.Catalog.FirstOrDefault(item => item.Subsystem.Id == AnimationInspection.SubsystemId));
        if (!_supported)
        {
            return;
        }

        Guid target = Context.Resource.SessionId;
        _target = target;
        _model = new AnimationInspection((invocation, token) => Session.InvokeAsync(target, invocation, token));
        await RefreshAsync();
    }

    private static string KindName(string kind) => kind == "playback" ? "PLAYBACK" : "STATE MACHINE";

    private Task RefreshAsync() => RunAsync(token => _model!.RefreshTargetsAsync(token));

    private Task SelectAsync(string id) => RunAsync(async token =>
    {
        await _model!.SelectAsync(id, token);
        AdoptSelectedValues();
    });

    private Task ReadSelectedAsync() => _model?.Selected is { } selected ? SelectAsync(selected.Id) : Task.CompletedTask;

    private Task PlayAsync() => ChangeAsync(token => _model!.PlayAsync(token));

    private Task PauseAsync() => ChangeAsync(token => _model!.PauseAsync(token));

    private Task StopAsync() => ChangeAsync(token => _model!.StopAsync(token));

    private Task SeekAsync() => ChangeAsync(token => _model!.SeekAsync(_position, token));

    private Task SetSpeedAsync() => ChangeAsync(token => _model!.SetSpeedAsync(_speed, token));

    private Task SetTriggerAsync() => ChangeAsync(token => _model!.SetTriggerAsync(_trigger.Trim(), token));

    private Task ClearTriggersAsync() => ChangeAsync(token => _model!.ClearTriggersAsync(token));

    private Task ChangeAsync(Func<CancellationToken, Task> action) => RunAsync(async token =>
    {
        await action(token);
        AdoptSelectedValues();
    });

    private void AdoptSelectedValues()
    {
        if (_model?.Selected is { } selected)
        {
            _position = selected.PositionTicks;
            _speed = selected.Speed;
        }
    }

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
