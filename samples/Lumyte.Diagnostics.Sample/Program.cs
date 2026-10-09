using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Lumyte.Diagnostics;
using Lumyte.Diagnostics.Sample;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var services = new ServiceCollection();
services.AddLumyteDiagnostics(options =>
{
    options.Enabled = true;
    options.AllowedMeterNames = ["Lumyte.Input.Sample"];
    options.AllowedActivitySourceNames = ["Lumyte.Input.Sample"];
    options.AllowedLogCategoryPrefixes = ["Lumyte.Input.Sample"];
    options.TraceSampleRatio = 1;
});
services.AddScoped<InputOverrideService>();
services.AddDiagnosticExecutionPoint<BeforeInputProcessing>("input.before-processing");
services.AddDiagnosticSubsystem<InputDiagnostics, BeforeInputProcessing>(new("input", "Input", 1));
using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
using IServiceScope game = provider.CreateScope();
IDiagnosticPump<BeforeInputProcessing> pump = game.ServiceProvider.GetRequiredService<IDiagnosticPump<BeforeInputProcessing>>();
InputOverrideService input = game.ServiceProvider.GetRequiredService<InputOverrideService>();
DiagnosticTelemetry collector = game.ServiceProvider.GetRequiredService<DiagnosticTelemetry>();
IGameExecutionIdentity identity = game.ServiceProvider.GetRequiredService<IGameExecutionIdentity>();
collector.Start();
pump.Activate();
var context = new DiagnosticOperationContext(Guid.NewGuid(), Guid.NewGuid(), default, "developer", null, default);
var arguments = new Dictionary<string, DiagnosticValue>
{
    ["button"] = DiagnosticValue.From("Jump"),
    ["pressed"] = DiagnosticValue.From(true),
    ["duration-ms"] = DiagnosticValue.From(200L),
};
Task<DiagnosticOperationResult> task = pump.SubmitAsync(new("input", "override-button", context, arguments, new HashSet<DiagnosticPermission> { DiagnosticPermission.OverrideInput }, Stopwatch.GetTimestamp() + Stopwatch.Frequency));
pump.Pump(new(1, Stopwatch.GetTimestamp()), new(TimeSpan.FromMilliseconds(10), 8));
Console.WriteLine(JsonSerializer.Serialize(task.GetAwaiter().GetResult()));
Console.WriteLine($"Jump overridden: {input.Read("Jump", physicalState: false)}");
Meter meter = game.ServiceProvider.GetRequiredService<IMeterFactory>().Create(new MeterOptions("Lumyte.Input.Sample"));
Counter<long> counter = meter.CreateCounter<long>("input.overrides");
var tag = new KeyValuePair<string, object?>("lumyte.instance.id", identity.InstanceId.ToString("D"));
using var source = new ActivitySource("Lumyte.Input.Sample");
ILogger logger = game.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Lumyte.Input.Sample");
using (logger.BeginScope(new Dictionary<string, object?> { [tag.Key] = tag.Value }))
using (source.StartActivity("input.override", ActivityKind.Internal, default(ActivityContext), [tag]))
{
    counter.Add(1, tag);
    logger.LogInformation("Button {Button} overridden", "Jump");
}

while (collector.TryRead(out DiagnosticEvent? item))
{
    Console.WriteLine(JsonSerializer.Serialize(item));
}

input.ReleaseSession(context.SessionId);
pump.Deactivate();
