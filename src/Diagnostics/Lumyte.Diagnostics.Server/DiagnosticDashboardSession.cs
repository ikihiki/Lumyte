using System.Security.Claims;
using Lumyte.Diagnostics.Transport;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace Lumyte.Diagnostics.Server;

internal sealed class DiagnosticDashboardSession(DiagnosticBrowserTickets tickets, DiagnosticDashboardLimits limits, IDiagnosticDashboardReader reader, DiagnosticSessionRegistry registry) : CircuitHandler, IDisposable
{
    private ClaimsPrincipal _user = new(new ClaimsIdentity());
    private CancellationTokenSource _connection = new();
    private bool _connected;
    private bool _hasSlot;
    private bool _disposed;

    public event Action? ConnectionChanged;

    public CancellationToken ConnectionToken => _connection.Token;

    public CancellationToken AuthenticationToken => tickets.Require(_user);

    public bool Connected => _connected;

    public long Version => reader.Version;

    public void Attach(ClaimsPrincipal user)
    {
        tickets.Require(user);
        _user = user;
    }

    public SessionSnapshot[] Resources()
    {
        RequireActive();
        return reader.Resources();
    }

    public DiagnosticUiSession[] Summaries()
    {
        RequireActive();
        return reader.Summaries();
    }

    public DiagnosticEvent[] Events(Guid id)
    {
        RequireActive();
        return reader.Events(id);
    }

    public async Task WaitForChangeAsync(long version, CancellationToken cancellationToken)
    {
        RequireActive();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(AuthenticationToken, ConnectionToken, cancellationToken);
        await reader.WaitForChangeAsync(version, lifetime.Token).ConfigureAwait(false);
    }

    public async Task<DiagnosticOperationResult> InvokeAsync(Guid id, OperationInvocation invocation, CancellationToken cancellationToken)
    {
        RequireActive();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(AuthenticationToken, ConnectionToken, cancellationToken);
        lifetime.Token.ThrowIfCancellationRequested();
        return await registry.InvokeAsync(id, invocation, _user.Identity!.Name!, lifetime.Token).ConfigureAwait(false);
    }

    public void Close(Guid id)
    {
        RequireActive();
        registry.Close(id);
    }

    public override async Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        if (!await limits.Slots.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Too many diagnostic browser connections.");
        }

        _hasSlot = true;
    }

    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _connection.Dispose();
        _connection = new();
        _connected = true;
        ConnectionChanged?.Invoke();
        return Task.CompletedTask;
    }

    public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _connected = false;
        _connection.Cancel();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _connected = false;
        _connection.Cancel();
        _connection.Dispose();
        if (_hasSlot)
        {
            limits.Slots.Release();
        }
    }

    private void RequireActive()
    {
        tickets.Require(_user);
        if (!_connected || _disposed)
        {
            throw new InvalidOperationException("Browser connection is unavailable. Operations are not replayed.");
        }
    }
}
