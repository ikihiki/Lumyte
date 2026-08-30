using Lumyte.DevTools.Agent;

using Microsoft.Extensions.Hosting;

namespace Lumyte.DevTools.Host;

internal sealed class DevToolsAgentHostedService(
    DevToolsAgent agent,
    DiagnosticsDomain diagnosticsDomain,
    DemoCounterDomain demoDomain,
    DemoInputDomain inputDomain,
    DemoResourcesDomain resourcesDomain) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _ = diagnosticsDomain;
        _ = demoDomain;
        _ = inputDomain;
        _ = resourcesDomain;
        return agent.RunAsync(stoppingToken);
    }
}
