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

    /// <summary>Checks duplicate scope identity is accepted while ambiguous identities never format or route logs.</summary>
    [Fact]
    public void NestedLogScopesRequireOneDistinctIdentity()
    {
        using ServiceProvider provider = Build();
        using IServiceScope first = provider.CreateScope();
        using IServiceScope second = provider.CreateScope();
        DiagnosticTelemetry one = first.ServiceProvider.GetRequiredService<DiagnosticTelemetry>();
        DiagnosticTelemetry two = second.ServiceProvider.GetRequiredService<DiagnosticTelemetry>();
        one.Start();
        two.Start();
        string firstId = first.ServiceProvider.GetRequiredService<IGameExecutionIdentity>().InstanceId.ToString("D");
        string secondId = second.ServiceProvider.GetRequiredService<IGameExecutionIdentity>().InstanceId.ToString("D");
        ILogger logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Lumyte.Test.Component");
        using (logger.BeginScope(new Dictionary<string, object?> { ["lumyte.instance.id"] = firstId }))
        {
            using (logger.BeginScope(new Dictionary<string, object?> { ["lumyte.instance.id"] = firstId }))
            {
                logger.LogInformation("Repeated identity");
            }

            bool formatted = false;
            using (logger.BeginScope(new Dictionary<string, object?> { ["lumyte.instance.id"] = secondId }))
            {
                logger.Log(LogLevel.Information, new EventId(1), 0, null, (_, _) =>
                {
                    formatted = true;
                    return "Ambiguous identity";
                });
            }

            Assert.False(formatted);
            ILogger unrelated = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Lumyte.Testing.Component");
            unrelated.LogInformation("Unrelated category");
        }

        Assert.Equal("Repeated identity", Assert.Single(Drain(one)).Value.String);
        Assert.Empty(Drain(two));
    }

    /// <summary>Checks structured log fields remain detached and bounded across nested scopes.</summary>
    [Fact]
    public void LogFieldsAreBoundedBeforeScopeIdentity()
    {
        using ServiceProvider provider = Build();
        using IServiceScope game = provider.CreateScope();
        DiagnosticTelemetry collector = game.ServiceProvider.GetRequiredService<DiagnosticTelemetry>();
        collector.Start();
        string instanceId = game.ServiceProvider.GetRequiredService<IGameExecutionIdentity>().InstanceId.ToString("D");
        ILogger logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Lumyte.Test.Component");
        var state = Enumerable.Range(0, 10).ToDictionary(index => $"state-{index}", index => (object?)index);
        var outer = Enumerable.Range(0, 32).ToDictionary(index => $"outer-{index}", index => (object?)index);
        using (logger.BeginScope(outer))
        using (logger.BeginScope(new Dictionary<string, object?> { ["lumyte.instance.id"] = instanceId }))
        {
            logger.Log(LogLevel.Warning, new EventId(7), state, new InvalidOperationException("Error"), static (_, _) => "Message");
            state["state-0"] = 123;
            outer.Clear();
        }

        DiagnosticEvent log = Assert.Single(Drain(collector));
        Assert.Equal(37, log.Fields.Count);
        Assert.Equal(0, log.Fields["state-0"].Int64);
        Assert.Equal(21, log.Fields["outer-21"].Int64);
        Assert.False(log.Fields.ContainsKey("outer-22"));
        Assert.False(log.Fields.ContainsKey("lumyte.instance.id"));
        Assert.Equal("Warning", log.Fields["log.level"].String);
        Assert.Equal("Error", log.Fields["exception.message"].String);
        Assert.Equal(typeof(InvalidOperationException).FullName, log.Fields["exception.type"].String);
        Assert.Equal("System.InvalidOperationException: Error", log.Fields["exception.stacktrace"].String);
    }

    /// <summary>Checks metric span snapshots keep their field limit, reject nonfinite values and detach source tags.</summary>
    [Fact]
    public void MetricTagsAreBoundedAndDetached()
    {
        using ServiceProvider provider = Build();
        using IServiceScope game = provider.CreateScope();
        DiagnosticTelemetry collector = game.ServiceProvider.GetRequiredService<DiagnosticTelemetry>();
        collector.Start();
        string instanceId = game.ServiceProvider.GetRequiredService<IGameExecutionIdentity>().InstanceId.ToString("D");
        Meter meter = provider.GetRequiredService<IMeterFactory>().Create(new MeterOptions("Lumyte.Test"));
        Counter<double> counter = meter.CreateCounter<double>("requests");
        KeyValuePair<string, object?>[] tags = Enumerable.Range(0, 64)
            .Select(index => new KeyValuePair<string, object?>($"tag-{index}", index))
            .ToArray();
        tags[63] = new("lumyte.instance.id", instanceId);
        counter.Add(double.NaN, tags.AsSpan());
        counter.Add(double.PositiveInfinity, tags.AsSpan());
        counter.Add(1, tags.AsSpan());
        tags[0] = new("tag-0", 123);

        DiagnosticEvent metric = Assert.Single(Drain(collector));
        Assert.Equal(32, metric.Fields.Count);
        Assert.Equal(0, metric.Fields["tag-0"].Int64);
        Assert.False(metric.Fields.ContainsKey("tag-32"));
        Assert.Equal(1, metric.Value.Double);
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
