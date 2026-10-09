using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using Lumyte.Diagnostics;
using Lumyte.Diagnostics.Remote.Sample;
using Lumyte.Diagnostics.Sample;
using Lumyte.Diagnostics.Settings;
using Lumyte.Diagnostics.Transport;
using Lumyte.Diagnostics.Transport.Http;
using Lumyte.Diagnostics.Transport.MagicOnion;
using Lumyte.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

string transport = args.Length > 0 ? args[0] : "magiconion";
var address = new Uri(args.Length > 1 ? args[1] : transport == "http" ? "http://127.0.0.1:5000" : "http://127.0.0.1:5001");
int seconds = int.Parse(args.Length > 2 ? args[2] : "60", CultureInfo.InvariantCulture);
string token = Environment.GetEnvironmentVariable("LUMYTE_DIAGNOSTICS_GAME_TOKEN") ?? throw new InvalidOperationException("Set LUMYTE_DIAGNOSTICS_GAME_TOKEN.");
var services = new ServiceCollection();
services.AddLumyteDiagnostics(options =>
{
    options.Enabled = true;
    options.AllowedMeterNames = ["Lumyte.Remote.Sample", SettingsTelemetry.MeterName];
    options.AllowedActivitySourceNames = ["Lumyte.Remote.Sample", SettingsTelemetry.ActivitySourceName];
    options.AllowedLogCategoryPrefixes = ["Lumyte.Remote.Sample", SettingsTelemetry.LogCategoryName];
    options.TraceSampleRatio = 1;
});
services.AddScoped<InputOverrideService>();
services.AddDiagnosticExecutionPoint<BeforeInputProcessing>("input.before-processing");
services.AddDiagnosticSubsystem<InputDiagnostics, BeforeInputProcessing>(new("input", "Input", 1));
services.AddDiagnosticAgent<BeforeInputProcessing>();
string? settingsPath = Environment.GetEnvironmentVariable("LUMYTE_DIAGNOSTICS_SETTINGS_PATH");
using IDisposable? configuration = settingsPath == null ? null : RegisterSettings(services, settingsPath);
if (transport == "http")
{
    services.AddHttpDiagnosticTransport(options =>
    {
        options.BaseAddress = address;
        options.GameToken = token;
    });
}
else if (transport == "magiconion")
{
    services.AddMagicOnionDiagnosticTransport(options =>
    {
        options.Endpoint = address;
        options.GameToken = token;
    });
}
else
{
    throw new ArgumentException("Choose http or magiconion.");
}

using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
AsyncServiceScope scope = provider.CreateAsyncScope();
try
{
    IServiceProvider game = scope.ServiceProvider;
    InputOverrideService input = game.GetRequiredService<InputOverrideService>();
    game.GetRequiredService<DiagnosticTelemetry>().Start();
    IDiagnosticPump<BeforeInputProcessing> pump = game.GetRequiredService<IDiagnosticPump<BeforeInputProcessing>>();
    DiagnosticAgent<BeforeInputProcessing> agent = game.GetRequiredService<DiagnosticAgent<BeforeInputProcessing>>();
    pump.Activate();
    try
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        agent.ConnectAsync(timeout.Token).GetAwaiter().GetResult();
        Console.WriteLine($"Connected: {transport}, session={agent.SessionId}");
        Task run = agent.RunAsync();
        IGameExecutionIdentity identity = game.GetRequiredService<IGameExecutionIdentity>();
        Meter meter = game.GetRequiredService<IMeterFactory>().Create(new MeterOptions("Lumyte.Remote.Sample"));
        Counter<long> counter = meter.CreateCounter<long>("remote.frames");
        var tag = new KeyValuePair<string, object?>("lumyte.instance.id", identity.InstanceId.ToString("D"));
        using var source = new ActivitySource("Lumyte.Remote.Sample");
        ILogger logger = game.GetRequiredService<ILoggerFactory>().CreateLogger("Lumyte.Remote.Sample");
        using (logger.BeginScope(new Dictionary<string, object?> { [tag.Key] = tag.Value }))
        using (source.StartActivity("remote.start", ActivityKind.Internal, default(ActivityContext), [tag]))
        {
            counter.Add(9007199254740993L, tag);
            logger.LogInformation("Game instance connected");
        }

        long started = Stopwatch.GetTimestamp();
        long frame = 0;
        bool previous = false;
        Console.WriteLine("Input state: Jump=False");
        while (!run.IsCompleted && Stopwatch.GetElapsedTime(started).TotalSeconds < seconds)
        {
            pump.Pump(new(frame++, Stopwatch.GetTimestamp()), new(TimeSpan.FromMilliseconds(2), 8));
            bool pressed = input.Read("Jump", false);
            if (pressed != previous)
            {
                Console.WriteLine($"Input state: Jump={pressed}");
                previous = pressed;
            }

            if (frame % 100 == 0)
            {
                counter.Add(1, tag);
            }

            Thread.Sleep(5);
        }
    }
    finally
    {
        try
        {
            agent.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        finally
        {
            input.ReleaseSession(agent.SessionId);
            pump.Deactivate();
            Console.WriteLine($"Disconnected: Jump={input.Read("Jump", false)}");
        }
    }
}
finally
{
    scope.DisposeAsync().AsTask().GetAwaiter().GetResult();
}

static IDisposable RegisterSettings(IServiceCollection services, string path)
{
    var source = new PersistedJsonFileSource(path);
    IConfigurationRoot configuration = new ConfigurationBuilder().AddPersistedJsonFile(source).Build();
    services.AddSettings(source);
    services.AddPersistedOptions<AudioSettings>("audio")
        .Validate(value => value.Volume is >= 0 and <= 1, "Volume must be between zero and one.")
        .UseJsonTypeInfo(AudioSettingsJsonContext.Default.AudioSettings);
    services.AddSettingsDiagnostics<AudioSettings, BeforeInputProcessing>("audio", fields => fields
        .Double("volume", value => value.Volume, (value, volume) => value.Volume = volume)
        .Boolean("muted", value => value.Muted, (value, muted) => value.Muted = muted)
        .String("device", value => value.Device));
    return (IDisposable)configuration;
}
