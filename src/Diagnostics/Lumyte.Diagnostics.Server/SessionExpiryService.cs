namespace Lumyte.Diagnostics.Server;

internal sealed class SessionExpiryService(DiagnosticSessionRegistry sessions) : BackgroundService
{
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        sessions.CloseAll();
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            sessions.ExpireIdleSessions();
        }
    }
}
