using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using Lumyte.Animation;
using Lumyte.Core.Time;
using Lumyte.Diagnostics;
using Lumyte.Diagnostics.Animation;
using Lumyte.Diagnostics.Remote.Sample;
using Lumyte.Diagnostics.RenderGraph;
using Lumyte.Diagnostics.Sample;
using Lumyte.Diagnostics.Settings;
using Lumyte.Diagnostics.Transport;
using Lumyte.Diagnostics.Transport.Http;
using Lumyte.Diagnostics.Transport.MagicOnion;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Settings;
using Lumyte.StateMachines;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

string transport = args.Length > 0 ? args[0] : "magiconion";
var address = new Uri(args.Length > 1 ? args[1] : transport == "http" ? "http://127.0.0.1:5000" : "http://127.0.0.1:5001");
int seconds = int.Parse(args.Length > 2 ? args[2] : "60", CultureInfo.InvariantCulture);
string token = Environment.GetEnvironmentVariable("LUMYTE_DIAGNOSTICS_GAME_TOKEN") ?? throw new InvalidOperationException("Set LUMYTE_DIAGNOSTICS_GAME_TOKEN.");
bool renderGraphEnabled = Environment.GetEnvironmentVariable("LUMYTE_DIAGNOSTICS_RENDER_GRAPH") == "1";
bool animationEnabled = Environment.GetEnvironmentVariable("LUMYTE_DIAGNOSTICS_ANIMATION") == "1";
var services = new ServiceCollection();
services.AddLumyteDiagnostics(options =>
{
    options.Enabled = true;
    options.AllowedMeterNames = ["Lumyte.Remote.Sample", SettingsTelemetry.MeterName];
    options.AllowedActivitySourceNames = ["Lumyte.Remote.Sample", SettingsTelemetry.ActivitySourceName];
    options.AllowedLogCategoryPrefixes = ["Lumyte.Remote.Sample", SettingsTelemetry.LogCategoryName];
    if (renderGraphEnabled)
    {
        options.AllowedMeterNames = [.. options.AllowedMeterNames, RenderGraphDiagnostics.TelemetryName];
        options.AllowedActivitySourceNames = [.. options.AllowedActivitySourceNames, RenderGraphDiagnostics.TelemetryName];
        options.AllowedLogCategoryPrefixes = [.. options.AllowedLogCategoryPrefixes, RenderGraphDiagnostics.TelemetryName];
    }

    options.TraceSampleRatio = 1;
});
services.AddScoped<InputOverrideService>();
services.AddDiagnosticExecutionPoint<BeforeInputProcessing>("input.before-processing");
services.AddDiagnosticSubsystem<InputDiagnostics, BeforeInputProcessing>(new("input", "Input", 1));
services.AddDiagnosticAgent<BeforeInputProcessing>();
if (renderGraphEnabled)
{
    services.AddRenderGraphDiagnostics<BeforeInputProcessing>();
}

if (animationEnabled)
{
    services.AddAnimationDiagnostics<BeforeInputProcessing>();
}

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
    var animationClock = new ManualClock();
    AnimationPlayback? playback = null;
    AnimationStateMachine<SampleMotion, string>? animationMachine = null;
    AnimationOutput? animationOutput = null;
    List<AnimationEventOccurrence>? animationEvents = null;
    List<AnimationStateEvent<SampleMotion>>? stateEvents = null;
    List<AnimationStateTransition<SampleMotion>>? stateTransitions = null;
    AnimationDiagnosticRegistration? playbackRegistration = null;
    AnimationDiagnosticRegistration? machineRegistration = null;
    if (animationEnabled)
    {
        AnimationTimeline playbackTimeline = ComposeAnimation.Timeline()[ComposeAnimation.Delay(Duration.FromSeconds(120))].Build();
        playback = new AnimationPlayback(animationClock, playbackTimeline);
        playback.Play();
        var action = StateMachineTrigger.Create();
        var builder = new AnimationStateMachineBuilder<SampleMotion, string>();
        builder.AddState(SampleMotion.Idle, ComposeAnimation.Timeline()[ComposeAnimation.Delay(Duration.FromSeconds(120))].Build());
        builder.AddState(SampleMotion.Action, ComposeAnimation.Timeline()[ComposeAnimation.Delay(Duration.FromSeconds(120))].Build());
        builder.AddTransition(SampleMotion.Idle, SampleMotion.Action, trigger: action);
        animationMachine = builder.Build(animationClock, SampleMotion.Idle, "sample");
        animationOutput = new AnimationOutput();
        animationEvents = [];
        stateEvents = [];
        stateTransitions = [];
        AnimationDiagnosticRegistry registry = game.GetRequiredService<AnimationDiagnosticRegistry>();
        playbackRegistration = registry.Register("sample.playback", playback);
        machineRegistration = registry.Register(
            "sample.motion",
            animationMachine,
            static state => state.ToString(),
            new Dictionary<string, StateMachineTrigger> { ["action"] = action });
    }

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
        long graphFrame = 0;
        long lastGraphCompile = 0;
        RenderGraph? graph = renderGraphEnabled ? RenderGraphSample.Create(game.GetRequiredService<RenderGraphDiagnostics>()) : null;
        bool previous = false;
        if (graph is not null)
        {
            Console.WriteLine("Render graph: sample-frame (compile-only; no GPU commands or allocations)");
        }

        Console.WriteLine("Input state: Jump=False");
        while (!run.IsCompleted && Stopwatch.GetElapsedTime(started).TotalSeconds < seconds)
        {
            pump.Pump(new(frame++, Stopwatch.GetTimestamp()), new(TimeSpan.FromMilliseconds(2), 8));
            if (playback is not null && animationMachine is not null)
            {
                animationClock.Advance(Duration.FromTimeSpan(TimeSpan.FromMilliseconds(5)));
                animationOutput!.Clear();
                animationEvents!.Clear();
                playback.Update(animationOutput, animationEvents);
                animationOutput.Clear();
                stateEvents!.Clear();
                stateTransitions!.Clear();
                animationMachine.Update("sample", animationOutput, stateEvents, stateTransitions);
            }

            if (graph is not null && (lastGraphCompile == 0 || Stopwatch.GetElapsedTime(lastGraphCompile).TotalMilliseconds >= 16))
            {
                lastGraphCompile = Stopwatch.GetTimestamp();
                using CompiledRenderGraph plan = graph.Compile(frameId: ++graphFrame);
            }

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
            machineRegistration?.Dispose();
            playbackRegistration?.Dispose();
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
