using System.Diagnostics;
using System.Diagnostics.Metrics;
using Lumyte.Diagnostics.Sample;
using Lumyte.Diagnostics.Transport;
using Lumyte.Diagnostics.Transport.Http;
using Lumyte.Diagnostics.Transport.MagicOnion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lumyte.Diagnostics.IntegrationTests;

internal sealed class RemoteGame : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource<Guid> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _pressed;
    private int _disposed;

    public RemoteGame(Uri address, string gameToken, bool magicOnion, Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLumyteDiagnostics(options =>
        {
            options.Enabled = true;
            options.AllowedMeterNames = ["Lumyte.Remote.Test"];
            options.AllowedActivitySourceNames = ["Lumyte.Remote.Test"];
            options.AllowedLogCategoryPrefixes = ["Lumyte.Remote.Test"];
            options.TraceSampleRatio = 1;
        });
        services.AddScoped<InputOverrideService>();
        services.AddDiagnosticExecutionPoint<BeforeInputProcessing>("input.before-processing");
        services.AddDiagnosticSubsystem<InputDiagnostics, BeforeInputProcessing>(new("input", "Input", 1));
        services.AddDiagnosticAgent<BeforeInputProcessing>();
        if (magicOnion)
        {
            services.AddMagicOnionDiagnosticTransport(options =>
            {
                options.Endpoint = address;
                options.GameToken = gameToken;
            });
        }
        else
        {
            services.AddHttpDiagnosticTransport(options =>
            {
                options.BaseAddress = address;
                options.GameToken = gameToken;
            });
        }

        configure?.Invoke(services);
        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        new Thread(Run) { IsBackground = true, Name = "diagnostic-test-game" }.Start();
    }

    public bool Pressed => Volatile.Read(ref _pressed);

    public Task Completion => _completed.Task;

    public Task<Guid> ReadyAsync() => _ready.Task.WaitAsync(TimeSpan.FromSeconds(15));

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _stop.CancelAsync();
        await _completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await _provider.DisposeAsync();
        _stop.Dispose();
    }

    private static void ProduceTelemetry(IServiceProvider game)
    {
        IGameExecutionIdentity identity = game.GetRequiredService<IGameExecutionIdentity>();
        Meter meter = game.GetRequiredService<IMeterFactory>().Create(new MeterOptions("Lumyte.Remote.Test"));
        Counter<long> counter = meter.CreateCounter<long>("remote.frames");
        var tag = new KeyValuePair<string, object?>("lumyte.instance.id", identity.InstanceId.ToString("D"));
        using var source = new ActivitySource("Lumyte.Remote.Test");
        ILogger logger = game.GetRequiredService<ILoggerFactory>().CreateLogger("Lumyte.Remote.Test");
        using (logger.BeginScope(new Dictionary<string, object?> { [tag.Key] = tag.Value }))
        using (source.StartActivity("remote.start", ActivityKind.Internal, default(ActivityContext), [tag]))
        {
            counter.Add(9007199254740993L, tag);
            logger.LogInformation("Processed {Count} operations", 9007199254740993L);
        }
    }

    private void Run()
    {
        try
        {
            AsyncServiceScope scope = _provider.CreateAsyncScope();
            try
            {
                IServiceProvider game = scope.ServiceProvider;
                IDiagnosticPump<BeforeInputProcessing> pump = game.GetRequiredService<IDiagnosticPump<BeforeInputProcessing>>();
                InputOverrideService input = game.GetRequiredService<InputOverrideService>();
                DiagnosticAgent<BeforeInputProcessing> agent = game.GetRequiredService<DiagnosticAgent<BeforeInputProcessing>>();
                game.GetRequiredService<DiagnosticTelemetry>().Start();
                pump.Activate();
                try
                {
                    using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                    connectTimeout.CancelAfter(TimeSpan.FromSeconds(10));
                    agent.ConnectAsync(connectTimeout.Token).GetAwaiter().GetResult();
                    Task run = agent.RunAsync();
                    _ready.TrySetResult(agent.SessionId);
                    ProduceTelemetry(game);
                    long frame = 0;
                    while (!_stop.IsCancellationRequested && !run.IsCompleted)
                    {
                        pump.Pump(new(frame++, Stopwatch.GetTimestamp()), new(TimeSpan.FromMilliseconds(2), 8));
                        Volatile.Write(ref _pressed, input.Read("Jump", false));
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
                        Volatile.Write(ref _pressed, input.Read("Jump", false));
                        pump.Deactivate();
                    }
                }
            }
            finally
            {
                scope.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }

            _completed.TrySetResult();
        }
        catch (Exception exception)
        {
            _ready.TrySetException(exception);
            _completed.TrySetException(exception);
        }
    }
}
