using System.Security.Claims;

namespace Lumyte.Diagnostics.Server;

internal sealed class DiagnosticBrowserTickets(TimeProvider clock) : IDisposable
{
    internal const string TicketClaim = "diagnostics.ticket";

    private readonly object _gate = new();
    private readonly Dictionary<string, Ticket> _tickets = new(StringComparer.Ordinal);

    public ClaimsPrincipal Create()
    {
        lock (_gate)
        {
            foreach (string expired in _tickets.Where(pair => pair.Value.Expires <= clock.GetUtcNow()).Select(pair => pair.Key).ToArray())
            {
                _tickets.Remove(expired, out Ticket? ticket);
                ticket!.Cancellation.Cancel();
                ticket.Cancellation.Dispose();
            }

            if (_tickets.Count >= 128)
            {
                throw new InvalidOperationException("Too many browser logins.");
            }

            string id = Guid.NewGuid().ToString("N");
            DateTimeOffset expires = clock.GetUtcNow().AddMinutes(30);
            _tickets.Add(id, new(expires, new CancellationTokenSource(TimeSpan.FromMinutes(30), clock)));
            return new(new ClaimsIdentity([new(ClaimTypes.Name, "diagnostic-operator"), new("diagnostics.role", "operator"), new(TicketClaim, id)], DiagnosticBrowserUi.Scheme));
        }
    }

    public CancellationToken Require(ClaimsPrincipal user)
    {
        lock (_gate)
        {
            string? id = user.FindFirst(TicketClaim)?.Value;
            if (user.Identity?.IsAuthenticated != true || !user.HasClaim("diagnostics.role", "operator") || id == null
                || !_tickets.TryGetValue(id, out Ticket? ticket) || ticket.Expires <= clock.GetUtcNow() || ticket.Cancellation.IsCancellationRequested)
            {
                throw new UnauthorizedAccessException("Browser login expired. Sign in again.");
            }

            return ticket.Cancellation.Token;
        }
    }

    public void Revoke(ClaimsPrincipal user)
    {
        Ticket? ticket;
        lock (_gate)
        {
            string? id = user.FindFirst(TicketClaim)?.Value;
            if (id == null || !_tickets.Remove(id, out ticket))
            {
                return;
            }
        }

        ticket.Cancellation.Cancel();
        ticket.Cancellation.Dispose();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (Ticket ticket in _tickets.Values)
            {
                ticket.Cancellation.Cancel();
                ticket.Cancellation.Dispose();
            }

            _tickets.Clear();
        }
    }

    private sealed record Ticket(DateTimeOffset Expires, CancellationTokenSource Cancellation);
}
