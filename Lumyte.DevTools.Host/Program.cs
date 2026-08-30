using Lumyte.DevTools;
using Lumyte.DevTools.Agent;
using Lumyte.DevTools.Host;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton<DevToolsHub>();
builder.Services.AddSingleton(new DiagnosticsCollectorOptions(SourcePrefix: Environment.GetEnvironmentVariable("LUMYTE_DEVTOOLS_DIAGNOSTICS_PREFIX") ?? "Lumyte.", Enabled: !StringComparer.OrdinalIgnoreCase.Equals(Environment.GetEnvironmentVariable("LUMYTE_DEVTOOLS_DIAGNOSTICS_ENABLED"), "false")));
builder.Services.AddSingleton<DiagnosticsCollector>();
builder.Services.AddSingleton<DiagnosticsDomain>();
builder.Services.AddSingleton<DemoCounterDomain>();
builder.Services.AddSingleton<DemoInputDomain>();
builder.Services.AddSingleton<DemoResourcesDomain>();
builder.Services.AddSingleton<IDemoWindowRunner, WindowsDemoWindowRunner>();
builder.Services.AddSingleton(provider =>
{
    _ = provider.GetRequiredService<DiagnosticsDomain>();
    _ = provider.GetRequiredService<DemoCounterDomain>();
    _ = provider.GetRequiredService<DemoInputDomain>();
    _ = provider.GetRequiredService<DemoResourcesDomain>();
    return new DevToolsAgent(
        provider.GetRequiredService<DevToolsHub>(),
        Environment.GetEnvironmentVariable("LUMYTE_DEVTOOLS_HOST_ID") ?? "demo-game",
        Environment.GetEnvironmentVariable("LUMYTE_DEVTOOLS_DISPLAY_NAME") ?? "Demo Game",
        Environment.GetEnvironmentVariable("LUMYTE_DEVTOOLS_PIPE_NAME") ?? DevToolsAgent.DefaultPipeName);
});
builder.Services.AddHostedService<DemoWindowHostedService>();
builder.Services.AddHostedService<DevToolsAgentHostedService>();
await builder.Build().RunAsync();
