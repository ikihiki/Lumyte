using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Lumyte.Diagnostics.Tests;

/// <summary>Verifies collection from standard .NET instrumentation and scope isolation.</summary>
public sealed class TelemetryTests
{
    /// <summary>Checks metric routing, correlated structured logs, completed spans and bounded loss.</summary>
    [Fact]
    public void StandardInstrumentationIsScopedAndCorrelated()
    {
        using ServiceProvider provider = Build(capacity: 8);
        using IServiceScope first = provider.CreateScope();
        using IServiceScope second = provider.CreateScope();
        DiagnosticTelemetry one = first.ServiceProvider.GetRequiredService<DiagnosticTelemetry>();
        DiagnosticTelemetry two = second.ServiceProvider.GetRequiredService<DiagnosticTelemetry>();
        IGameExecutionIdentity identity = first.ServiceProvider.GetRequiredService<IGameExecutionIdentity>();
        one.Start();
        two.Start();
        Meter meter = provider.GetRequiredService<IMeterFactory>().Create(new MeterOptions("Lumyte.Test"));
        Counter<long> counter = meter.CreateCounter<long>("requests");
        var tag = new KeyValuePair<string, object?>("lumyte.instance.id", identity.InstanceId.ToString("D"));
        using var source = new ActivitySource("Lumyte.Test");
        ILogger logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Lumyte.Test.Component");
        string traceId;
        using (logger.BeginScope(new Dictionary<string, object?> { [tag.Key] = tag.Value }))
        using (Activity? activity = source.StartActivity("step", ActivityKind.Internal, default(ActivityContext), [tag]))
        {
            Assert.NotNull(activity);
            traceId = activity.TraceId.ToString();
            counter.Add(9007199254740993L, tag);
            logger.LogInformation("Processed {Count} operations", 7);
        }

        List<DiagnosticEvent> events = Drain(one);
        Assert.Equal(3, events.Count);
        Assert.Empty(Drain(two));
        Assert.Equal(9007199254740993L, Assert.Single(events, item => item.Kind == "metric").Value.Int64);
        DiagnosticEvent log = Assert.Single(events, item => item.Kind == "log");
        Assert.Equal(traceId, log.TraceId);
        Assert.Equal(7, log.Fields["Count"].Int64);
        Assert.Equal(traceId, Assert.Single(events, item => item.Kind == "span").TraceId);
        for (int i = 0; i < 16; i++)
        {
            counter.Add(1, tag);
        }

        Assert.Equal(8, Drain(one).Count);
        Assert.Equal(8, one.Dropped);
    }

    /// <summary>Checks another ActivityListener cannot force diagnostics to export unsampled traces.</summary>
    [Fact]
    public void OtherListenersDoNotOverrideDiagnosticSampling()
    {
        using ServiceProvider provider = Build(ratio: 0);
        using IServiceScope game = provider.CreateScope();
        DiagnosticTelemetry collector = game.ServiceProvider.GetRequiredService<DiagnosticTelemetry>();
        collector.Start();
        using var other = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Lumyte.Test",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(other);
        using var source = new ActivitySource("Lumyte.Test");
        IGameExecutionIdentity identity = game.ServiceProvider.GetRequiredService<IGameExecutionIdentity>();
        using (Activity? activity = source.StartActivity(
            "step",
            ActivityKind.Internal,
            default(ActivityContext),
            [new("lumyte.instance.id", identity.InstanceId.ToString("D"))]))
        {
            Assert.NotNull(activity);
        }

        Assert.Empty(Drain(collector));
    }

    /// <summary>Checks logs without a unique execution scope and disabled collection are excluded.</summary>
    [Fact]
    public void LogsRequireIdentityAndDisposedSinksAreNotReused()
    {
        using ServiceProvider provider = Build();
        using IServiceScope game = provider.CreateScope();
        DiagnosticTelemetry collector = game.ServiceProvider.GetRequiredService<DiagnosticTelemetry>();
        collector.Start();
        ILogger logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Lumyte.Test.Component");
        logger.LogInformation("No game scope");
        Assert.Empty(Drain(collector));
        collector.Dispose();
        Assert.Throws<ObjectDisposedException>(collector.Start);
        IGameExecutionIdentity identity = game.ServiceProvider.GetRequiredService<IGameExecutionIdentity>();
        using (logger.BeginScope(new Dictionary<string, object?> { ["lumyte.instance.id"] = identity.InstanceId.ToString("D") }))
        {
            logger.LogInformation("Stopped game scope");
        }

        Assert.Empty(Drain(collector));
    }

    private static ServiceProvider Build(int capacity = 1024, double ratio = 1)
    {
        var services = new ServiceCollection();
        services.AddLumyteDiagnostics(options =>
        {
            options.Enabled = true;
            options.QueueCapacity = capacity;
            options.TraceSampleRatio = ratio;
            options.AllowedMeterNames = ["Lumyte.Test"];
            options.AllowedActivitySourceNames = ["Lumyte.Test"];
            options.AllowedLogCategoryPrefixes = ["Lumyte.Test"];
        });
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private static List<DiagnosticEvent> Drain(DiagnosticTelemetry collector)
    {
        var events = new List<DiagnosticEvent>();
        while (collector.TryRead(out DiagnosticEvent? item))
        {
            events.Add(item!);
        }

        return events;
    }
}
