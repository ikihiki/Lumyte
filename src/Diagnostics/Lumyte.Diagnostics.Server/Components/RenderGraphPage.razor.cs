using System.Globalization;
using Microsoft.AspNetCore.Components;

namespace Lumyte.Diagnostics.Server.Components;

/// <summary>Inspects bounded render graph captures through the authenticated diagnostic session.</summary>
public partial class RenderGraphPage
{
    private static readonly (string Id, string Title)[] _details = [("passes", "Passes"), ("resources", "Resources"), ("dependencies", "Dependencies"), ("barriers", "Barriers")];
    private static readonly (long Bit, string Name)[] _stages = [(1, "Host"), (2, "Copy"), (4, "DrawIndirect"), (8, "IndexInput"), (16, "VertexShader"), (32, "FragmentShader"), (64, "ComputeShader"), (128, "ColorOutput"), (256, "DepthStencil")];
    private static readonly (long Bit, string Name)[] _accesses = [(1, "HostRead"), (2, "HostWrite"), (4, "CopyRead"), (8, "CopyWrite"), (16, "IndexRead"), (32, "IndirectRead"), (64, "ShaderRead"), (128, "ShaderWrite"), (256, "ColorRead"), (512, "ColorWrite"), (1024, "DepthStencilRead"), (2048, "DepthStencilWrite")];
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _operation;
    private RenderGraphInspection? _model;
    private Task _updates = Task.CompletedTask;
    private bool _supported;
    private bool _working;
    private bool _live;
    private bool _polling;
    private bool _disposed;
    private CancellationToken _connectionToken;
    private int _pollsRemaining;
    private long _jumpIndex;
    private string _graphName = string.Empty;
    private string _captureInput = string.Empty;
    private string _loadedCapture = string.Empty;
    private string _notice = string.Empty;

    /// <summary>Gets or sets the selected game's immutable catalog and shell context.</summary>
    [CascadingParameter]
    public DiagnosticPageContext Context { get; set; } = default!;

    [Inject]
    private DiagnosticDashboardSession Session { get; set; } = default!;

    private bool Busy => _working || _model?.Busy == true || !Session.Connected;

    private string Error => _notice.Length > 0 ? _notice : _model?.Error ?? string.Empty;

    private string DetailTitle => _details.FirstOrDefault(item => item.Id == _model?.Detail).Title ?? string.Empty;

    private string DetailHint => _model?.Detail switch
    {
        "passes" => "Indexは宣言順、実行順はculling後の順序です。除去されたPassも表示します。",
        "resources" => "使用範囲はgraph内の実行Pass番号です。GPU allocationの寿命やサイズを表しません。",
        "dependencies" => "番号は宣言順ではなく、culling後の実行順です。先行Passから後続Passへの依存関係を表示します。",
        _ => "Pass番号は実行順、Resource indexはResourcesのIndexです。Exportはgraph末尾の外部利用への同期です。",
    };

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _lifetime.CancelAsync();
        await _updates;
        _lifetime.Dispose();
    }

    /// <inheritdoc/>
    protected override async Task OnParametersSetAsync()
    {
        if (_model is not null || _disposed || Context.Resource is null)
        {
            return;
        }

        _supported = RenderGraphInspection.Supports(Context.Resource.Catalog.FirstOrDefault(item => item.Subsystem.Id == RenderGraphInspection.SubsystemId));
        if (!_supported)
        {
            return;
        }

        Guid target = Context.Resource.SessionId;
        _model = new RenderGraphInspection((invocation, token) => Session.InvokeAsync(target, invocation, token));
        _connectionToken = Session.ConnectionToken;
        _updates = UpdateAsync(_lifetime.Token);
        await RefreshSummaryAsync();
    }

    private static string Milliseconds(double value) => value.ToString("0.000", CultureInfo.InvariantCulture) + " ms";

    private static string Average(double total, long count) => count == 0 ? "—" : Milliseconds(total / count);

    private static string Bytes(long value) => value.ToString("N0", CultureInfo.CurrentCulture) + " B";

    private static bool HasDetails(string state) => state is "compiled" or "recorded" or "record-failed";

    private static string StateName(string? state) => state switch
    {
        "pending" => "Pending · 対象のCompileを待機",
        "capturing" => "Capturing · 構造を取得中",
        "compiled" => "Compiled · 構造を保存済み",
        "recorded" => "Recorded · CPU記録完了",
        "compile-failed" => "Compile failed · 構築失敗",
        "record-failed" => "Record failed · 記録失敗",
        "too-large" => "Too large · 保存容量の上限超過",
        _ => "取得できません",
    };

    private static string Text(RenderGraphInspection.Row row, string name) => row.Values[name].String ?? string.Empty;

    private static long Number(RenderGraphInspection.Row row, string name) => row.Values[name].Int64;

    private static bool Boolean(RenderGraphInspection.Row row, string name) => row.Values[name].Boolean;

    private static string Index(RenderGraphInspection.Row row, string name) => Number(row, name) < 0 ? "—" : Number(row, name).ToString(CultureInfo.InvariantCulture);

    private static string Flags(long flags, (long Bit, string Name)[] names)
    {
        if (flags == 0)
        {
            return "None";
        }

        var selected = new List<string>();
        foreach ((long bit, string name) in names)
        {
            if ((flags & bit) != 0)
            {
                selected.Add(name);
                flags &= ~bit;
            }
        }

        if (flags != 0)
        {
            selected.Add("0x" + flags.ToString("X", CultureInfo.InvariantCulture));
        }

        return string.Join(" | ", selected);
    }

    private string LifetimeStart(RenderGraphInspection.Row row) => (200d * Number(row, "first-pass") / Math.Max(1, _model!.Capture!.LivePasses)).ToString("0.###", CultureInfo.InvariantCulture);

    private string LifetimeWidth(RenderGraphInspection.Row row) => Math.Max(1, 200d * (Number(row, "last-pass") - Number(row, "first-pass") + 1) / Math.Max(1, _model!.Capture!.LivePasses)).ToString("0.###", CultureInfo.InvariantCulture);

    private Task RefreshSummaryAsync() => RunAsync(token => _model!.RefreshSummaryAsync(token));

    private Task RequestCaptureAsync() => RunAsync(async token =>
    {
        _pollsRemaining = 0;
        await _model!.RequestCaptureAsync(_graphName, token);
        if (!token.IsCancellationRequested && !_disposed && _model.CaptureId.Length > 0)
        {
            _captureInput = _model.CaptureId;
        }

        await CaptureSelectedAsync(token);
    });

    private Task OpenCaptureAsync() => RunAsync(async token =>
    {
        _pollsRemaining = 0;
        await _model!.OpenCaptureAsync(_captureInput.Trim(), token);
        await CaptureSelectedAsync(token);
    });

    private Task RefreshCaptureAsync() => RunAsync(async token =>
    {
        await _model!.RefreshCaptureAsync(token);
        await LoadFirstPageAsync(token);
    });

    private Task SelectDetailsAsync(string detail) => RunAsync(token => _model!.LoadDetailsAsync(detail, 0, token));

    private Task PreviousAsync() => RunAsync(token => _model!.LoadDetailsAsync(_model.Detail, Math.Max(0, _model.Offset - RenderGraphInspection.PageSize), token));

    private Task NextAsync() => RunAsync(token => _model!.LoadDetailsAsync(_model.Detail, _model.Offset + RenderGraphInspection.PageSize, token));

    private Task JumpAsync() => RunAsync(token => _model!.LoadDetailsAsync(_model.Detail, _jumpIndex, token));

    private async Task CaptureSelectedAsync(CancellationToken token)
    {
        if (token.IsCancellationRequested || _disposed || _model!.Error.Length > 0)
        {
            return;
        }

        _captureInput = _model.CaptureId;
        _loadedCapture = string.Empty;
        _pollsRemaining = _model.Capture?.State is "pending" or "capturing" or "compiled" ? 30 : 0;
        await LoadFirstPageAsync(token);
    }

    private async Task LoadFirstPageAsync(CancellationToken token)
    {
        if (!token.IsCancellationRequested && !_disposed && _model!.Error.Length == 0 && _model.Capture is { } capture && HasDetails(capture.State) && _loadedCapture != capture.CaptureId)
        {
            await _model.LoadDetailsAsync("passes", 0, token);
            if (_model.Error.Length == 0 && !token.IsCancellationRequested)
            {
                _loadedCapture = capture.CaptureId;
            }
        }
    }

    private void StopPolling()
    {
        _pollsRemaining = 0;
        if (_polling)
        {
            _operation?.Cancel();
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
            _operation = operation;
            await action(operation.Token);
        }
        catch (OperationCanceledException)
        {
            // A canceled wait never resends the original operation.
        }
        catch (UnauthorizedAccessException)
        {
            _notice = "認証が失効しました。ログインし直してください。";
        }
        finally
        {
            _operation = null;
            _working = false;
            if (Error.Length > 0 || !Session.Connected)
            {
                _live = false;
                _pollsRemaining = 0;
            }

            if (!_disposed)
            {
                StateHasChanged();
            }
        }
    }

    private async Task UpdateAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                await InvokeAsync(async () =>
                {
                    if (_connectionToken.IsCancellationRequested || !Session.Connected)
                    {
                        _live = false;
                        _pollsRemaining = 0;
                        if (Session.Connected)
                        {
                            _connectionToken = Session.ConnectionToken;
                        }

                        if (!_disposed)
                        {
                            StateHasChanged();
                        }

                        return;
                    }

                    if (_disposed || Busy)
                    {
                        return;
                    }

                    if (_pollsRemaining > 0)
                    {
                        _pollsRemaining--;
                        _polling = true;
                        try
                        {
                            await RefreshCaptureAsync();
                        }
                        finally
                        {
                            _polling = false;
                        }

                        if (_model?.Capture?.State is not ("pending" or "capturing" or "compiled"))
                        {
                            _pollsRemaining = 0;
                        }
                    }

                    if (_live)
                    {
                        await RefreshSummaryAsync();
                    }
                });
            }
        }
        catch (OperationCanceledException)
        {
            // The component's lifetime owns all background reads.
        }
    }
}
