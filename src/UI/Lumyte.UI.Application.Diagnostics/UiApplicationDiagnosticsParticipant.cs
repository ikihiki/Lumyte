using Lumyte.Diagnostics;
using Lumyte.Diagnostics.Transport;
using Lumyte.Diagnostics.UI.Automation;
using Lumyte.UI.Automation;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.UI.Application.Diagnostics;

internal sealed class UiApplicationDiagnosticsParticipant : IUiApplicationParticipant
{
    private readonly AsyncServiceScope _scope;
    private readonly UiApplicationDiagnosticsOptions _options;
    private readonly Dictionary<Guid, UiAutomationDiagnosticRegistration> _windows = [];
    private readonly Dictionary<Guid, UiAutomationDiagnosticRegistration> _popups = [];
    private readonly Dictionary<Guid, List<UiAutomationDiagnosticRegistration>> _chrome = [];
    private readonly HashSet<Guid> _unregisteredPopups = [];
    private UiAutomationDiagnosticsRuntime? _runtime;
    private IDiagnosticPump<UiApplicationDiagnosticsPoint>? _pump;
    private DiagnosticAgent<UiApplicationDiagnosticsPoint>? _agent;
    private Task? _agentRun;
    private UiApplicationWindowContext? _primary;
    private long _frame;
    private bool _disposed;

    internal UiApplicationDiagnosticsParticipant(IServiceProvider services, UiApplicationDiagnosticsOptions options)
    {
        _options = options;
        _scope = services.CreateAsyncScope();
    }

    public bool IsStopped => _runtime?.IsStopped ?? true;

    private UiAutomationDiagnosticsRuntime Runtime => _runtime ?? throw new InvalidOperationException("Diagnostics has not started.");

    public async ValueTask StartAsync(CancellationToken cancellationToken)
    {
        _runtime = _scope.ServiceProvider.GetRequiredService<UiAutomationDiagnosticsRuntime>();
        _pump = _scope.ServiceProvider.GetRequiredService<IDiagnosticPump<UiApplicationDiagnosticsPoint>>();
        _pump.Activate();
        _agent = _scope.ServiceProvider.GetService<DiagnosticAgent<UiApplicationDiagnosticsPoint>>();
        if (_agent is not null)
        {
            _scope.ServiceProvider.GetRequiredService<DiagnosticTelemetry>().Start();
            await _agent.ConnectAsync(cancellationToken);
            _agentRun = _agent.RunAsync();
        }
    }

    public void OpenWindow(UiApplicationWindowContext context)
    {
        _primary ??= context;
        var host = new RegistrationHost(context, context.View.CurrentSurface!, context.View.GetAutomation(_options.Target.AllowTextInspection));
        UiAutomationDiagnosticRegistration token;
        try
        {
            token = _windows.Count == 0 ? Runtime.Register(_options.TargetName, context.View.CurrentSurface!, host, _options.Target) : Runtime.RegisterPopup(_windows[_primary.Id], context.Id, Name(context.Id), context.View.CurrentSurface!, host, _options.Target);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        _windows.Add(context.Id, token);
        if (context.Chrome is { } chrome)
        {
            var registrations = new List<UiAutomationDiagnosticRegistration>();
            _chrome.Add(context.Id, registrations);
            try
            {
                registrations.Add(Runtime.RegisterPopup(token, Guid.NewGuid(), Name(context.Id) + ".header", chrome.HeaderSurface!, new RegistrationHost(context, chrome.HeaderSurface!, chrome.Header), _options.Target));
                registrations.Add(Runtime.RegisterPopup(token, Guid.NewGuid(), Name(context.Id) + ".resize", chrome.ResizeSurface!, new RegistrationHost(context, chrome.ResizeSurface!, chrome.ResizeGrip), _options.Target));
            }
            catch (InvalidOperationException)
            {
                // Target capacity limits automation publication, not the lifetime of the displayed window.
            }
        }
    }

    public void CloseWindow(UiApplicationWindowContext context)
    {
        if (_windows.TryGetValue(context.Id, out UiAutomationDiagnosticRegistration? registration))
        {
            registration.Dispose();
        }

        if (_chrome.TryGetValue(context.Id, out List<UiAutomationDiagnosticRegistration>? chrome))
        {
            foreach (UiAutomationDiagnosticRegistration token in chrome)
            {
                token.Dispose();
            }
        }
    }

    public bool IsWindowStopped(UiApplicationWindowContext context) => (!_windows.TryGetValue(context.Id, out UiAutomationDiagnosticRegistration? registration) || registration.IsReleased)
        && (!_chrome.TryGetValue(context.Id, out List<UiAutomationDiagnosticRegistration>? chrome) || chrome.All(token => token.IsReleased));

    public void Advance()
    {
        if (_runtime is null || _pump is null)
        {
            return;
        }

        if (_agentRun?.IsCompleted == true && _agent is { } agent)
        {
            _ = _agentRun.Exception;
            Runtime.NotifySessionClosed(agent.SessionId);
            _agentRun = null;
        }

        if (_primary is { } primary)
        {
            IReadOnlyList<Lumyte.UI.Windowing.UiPopupInfo> popups = primary.Popups.CapturePopups();
            var live = popups.Select(popup => popup.Id).ToHashSet();
            _unregisteredPopups.RemoveWhere(id => !live.Contains(id));
            foreach (Lumyte.UI.Windowing.UiPopupInfo popup in popups)
            {
                if (!_popups.ContainsKey(popup.Id) && !_unregisteredPopups.Contains(popup.Id) && primary.Popups.TryGetSurface(popup.Id, out UiSurface? surface) && surface is not null && _windows.TryGetValue(primary.Id, out UiAutomationDiagnosticRegistration? parent) && !parent.IsReleased)
                {
                    var host = new RegistrationHost(primary, surface, primary.GetPopupAutomationForDiagnostics(popup.Id, _options.Target.AllowTextInspection));
                    try
                    {
                        UiAutomationDiagnosticRegistration registration = Runtime.RegisterPopup(parent, popup.Id, Name(popup.Id), surface, host, _options.Target);
                        _popups.Add(popup.Id, registration);
                        primary.Popups.BindLifetime(popup.Id, registration);
                    }
                    catch (InvalidOperationException)
                    {
                        _unregisteredPopups.Add(popup.Id);
                    }
                }
            }
        }

        var frame = new DiagnosticFrame(_frame++, _primary?.Automation.TimeProvider.GetTimestamp() ?? 0);
        _pump.Pump(frame, new(TimeSpan.FromMilliseconds(2), 32));
        _runtime.Advance(frame);
        foreach (Guid id in _windows.Where(pair => pair.Value.IsReleased).Select(pair => pair.Key).ToArray())
        {
            _windows.Remove(id);
            _chrome.Remove(id);
            if (_primary?.Id == id)
            {
                _primary = null;
            }
        }

        foreach (Guid id in _popups.Where(pair => pair.Value.IsReleased).Select(pair => pair.Key).ToArray())
        {
            _popups.Remove(id);
        }
    }

    public void BeginStop()
    {
        if (_agent is { } agent)
        {
            _runtime?.NotifySessionClosed(agent.SessionId);
        }

        _runtime?.BeginStop();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _runtime?.Dispose();
        _pump?.Deactivate();
        if (_agent is not null)
        {
            await _agent.DisposeAsync();
        }

        _disposed = true;
        await _scope.DisposeAsync();
    }

    private string Name(Guid id) => _options.TargetName + "." + id.ToString("N");

    private sealed class RegistrationHost(UiApplicationWindowContext context, UiSurface surface, IUiAutomationHost host) : IUiAutomationHost, IUiAutomationRoutingHost, IUiAutomationTextHost, IUiAutomationImeHost, IUiAutomationScreenshotHost
    {
        public TimeProvider TimeProvider => host.TimeProvider;

        public ValueTask<T> RunOnUiThreadAsync<T>(Func<UiSurface, T> action, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return context.Dispatcher.CheckAccess() ? new(action(surface)) : host.RunOnUiThreadAsync(action, cancellationToken);
        }

        public ValueTask NextFrameAsync(CancellationToken cancellationToken = default) => host.NextFrameAsync(cancellationToken);

        public ValueTask<IUiAutomationInputLease> AcquireInputAsync(CancellationToken cancellationToken = default) => host.AcquireInputAsync(cancellationToken);

        public UiPointerProbe ProbePointer(UiSurface surface, System.Numerics.Vector2 position) => ((IUiAutomationRoutingHost)host).ProbePointer(surface, position);

        public bool CanReceiveKeys(UiSurface surface) => ((IUiAutomationRoutingHost)host).CanReceiveKeys(surface);

        public bool Dispatch(UiSurface surface, UiInput input) => ((IUiAutomationRoutingHost)host).Dispatch(surface, input);

        public ValueTask<IUiAutomationInputLease> AcquireTextInputAsync(CancellationToken cancellationToken = default) => (host as IUiAutomationTextHost ?? throw new NotSupportedException()).AcquireTextInputAsync(cancellationToken);

        public void Fill(UiSurface surface, UiAutomationId editor, string text) => (host as IUiAutomationTextHost ?? throw new NotSupportedException()).Fill(surface, editor, text);

        public string ReadValue(UiSurface surface, UiAutomationId editor) => (host as IUiAutomationTextHost ?? throw new NotSupportedException()).ReadValue(surface, editor);

        public ValueTask<IUiAutomationInputLease> AcquireImeCandidateInputAsync(UiImeCandidateToken token, CancellationToken cancellationToken = default) => (host as IUiAutomationImeHost ?? throw new NotSupportedException()).AcquireImeCandidateInputAsync(token, cancellationToken);

        public UiImeCandidateSnapshot? CaptureImeCandidates(UiSurface surface) => (host as IUiAutomationImeHost ?? throw new NotSupportedException()).CaptureImeCandidates(surface);

        public ValueTask<UiImeCandidateResult> ExecuteImeCandidateAsync(UiImeCandidateCommand command, CancellationToken cancellationToken = default) => (host as IUiAutomationImeHost ?? throw new NotSupportedException()).ExecuteImeCandidateAsync(command, cancellationToken);

        public ValueTask<byte[]> CapturePngAsync(CancellationToken cancellationToken = default) => (host as IUiAutomationScreenshotHost ?? throw new NotSupportedException()).CapturePngAsync(cancellationToken);
    }
}
