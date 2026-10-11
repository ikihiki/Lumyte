using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Lumyte.Settings.Tests;

/// <summary>Observes standard telemetry without coupling persistence to a diagnostic transport.</summary>
public sealed class SettingsTelemetryTests
{
    /// <summary>Measures real outcomes, durations and balanced write ownership without publishing settings values.</summary>
    /// <returns>The asynchronous test.</returns>
    [Fact]
    public async Task RecordsOutcomesAndCorrelatedExceptionsAsync()
    {
        var instance = Guid.NewGuid();
        using var capture = new Capture(instance);
        using IDisposable correlation = SettingsTelemetry.BeginScope(instance);
        var store = new Store();
        using ServiceProvider provider = await CreateAsync(store, capture);
        IEditableOptions<SampleSettings> settings = provider.GetRequiredService<IEditableOptions<SampleSettings>>();
        _ = settings.Current;
        int initialCount = capture.Measurements.Count;
        _ = settings.Current;
        _ = settings.BeginEdit();
        _ = settings.LoadResult;
        Assert.Equal(initialCount, capture.Measurements.Count);
        SettingsEdit<SampleSettings> stale = settings.BeginEdit();
        Assert.Equal(SettingsSaveStatus.Saved, (await settings.SaveAsync(settings.BeginEdit())).Status);
        Assert.Equal(SettingsSaveStatus.Conflict, (await settings.SaveAsync(stale)).Status);
        SettingsEdit<SampleSettings> invalid = settings.BeginEdit();
        invalid.Value.PrimaryRange.Minimum = 2;
        Assert.Equal(SettingsSaveStatus.ValidationFailed, (await settings.SaveAsync(invalid)).Status);
        store.Fail = true;
        Assert.Equal(SettingsSaveStatus.StorageFailure, (await settings.SaveAsync(settings.BeginEdit())).Status);
        store.Fail = false;
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => settings.SaveAsync(settings.BeginEdit(), cancelled.Token));
        Assert.Equal(SettingsSaveStatus.Saved, (await settings.ResetAsync(settings.Revision)).Status);
        Assert.Equal(SettingsSaveStatus.Saved, (await provider.GetRequiredService<ISettingsDocument>().ResetAsync()).Status);
        foreach (string status in new[] { "Saved", "Conflict", "ValidationFailed", "StorageFailure", "Cancelled" })
        {
            Assert.Contains(capture.Measurements, item => item.Name == "settings.operations" && item.Tags["settings.operation"]?.ToString() == "save" && item.Tags["settings.status"]?.ToString() == status);
        }

        Assert.Single(capture.Measurements, item => item.Name == "settings.operations" && item.Tags["settings.operation"]?.ToString() == "document-load");
        Assert.Single(capture.Measurements, item => item.Name == "settings.operations" && item.Tags["settings.operation"]?.ToString() == "load");
        Assert.All(capture.Measurements.Where(item => item.Name.EndsWith("duration", StringComparison.Ordinal)), item => Assert.True(double.IsFinite(item.Value) && item.Value >= 0));
        Measurement[] active = capture.Measurements.Where(item => item.Name == "settings.writes.active").ToArray();
        Assert.Equal(0, active.Sum(item => item.Value));
        Assert.All(active, item => Assert.False(item.Tags.ContainsKey("settings.status")));
        Assert.Contains(capture.Measurements, item => item.Name == "settings.write.wait.duration" && item.Tags["settings.wait.outcome"]?.ToString() == "Acquired");
        Activity save = Assert.Single(capture.Spans, span => span.OperationName == "Settings.save" && span.GetTagItem("settings.status")?.ToString() == "StorageFailure");
        Assert.Equal(ActivityStatusCode.Error, save.Status);
        Assert.Contains(capture.Logs, log => log.TraceId == save.TraceId.ToString() && log.Level == LogLevel.Error);
        Assert.Contains(capture.Logs, log => log.Level == LogLevel.Warning && log.Message.Contains("Conflict", StringComparison.Ordinal));
        Assert.DoesNotContain(capture.Spans, span => span.OperationName == "Settings.document-load");
        Assert.Contains(capture.Logs, log => log.TraceId == save.TraceId.ToString() && log.Exception is IOException);
        Assert.Equal(typeof(IOException).FullName, save.GetTagItem("exception.type"));
        Assert.Equal("secret-storage-path", save.GetTagItem("exception.message"));
        Assert.Contains(nameof(Store.WriteAtomicallyAsync), save.GetTagItem("exception.stacktrace")?.ToString(), StringComparison.Ordinal);
        Assert.Contains(save.Events, item => item.Name == "exception");
        Assert.All(capture.Measurements, item => Assert.False(item.Tags.ContainsKey("settings.revision")));
    }

    /// <summary>Cancellation during storage and queue waits leaves active measurements balanced.</summary>
    /// <returns>The asynchronous test.</returns>
    [Fact]
    public async Task RecordsCancelledWaitAndRestoresNestedCorrelationAsync()
    {
        var instance = Guid.NewGuid();
        using var capture = new Capture(instance);
        using IDisposable correlation = SettingsTelemetry.BeginScope(instance);
        var store = new Store { Pause = true };
        using ServiceProvider provider = await CreateAsync(store, capture);
        IEditableOptions<SampleSettings> settings = provider.GetRequiredService<IEditableOptions<SampleSettings>>();
        using var firstCancellation = new CancellationTokenSource();
        Task<SettingsSaveResult<SampleSettings>> first = settings.SaveAsync(settings.BeginEdit(), firstCancellation.Token);
        await store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var secondCancellation = new CancellationTokenSource();
        Task<SettingsSaveResult<SampleSettings>> second = settings.SaveAsync(settings.BeginEdit(), secondCancellation.Token);
        secondCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        firstCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.Contains(capture.Measurements, item => item.Name == "settings.write.wait.duration" && item.Tags["settings.wait.outcome"]?.ToString() == "Cancelled");
        Assert.Equal(0, capture.Measurements.Where(item => item.Name == "settings.writes.active").Sum(item => item.Value));
        store.Pause = false;
        int before = capture.Measurements.Count;
        using (SettingsTelemetry.BeginScope(Guid.NewGuid()))
        {
            await settings.SaveAsync(settings.BeginEdit());
        }

        Assert.Equal(before, capture.Measurements.Count);
        await settings.SaveAsync(settings.BeginEdit());
        Assert.True(capture.Measurements.Count > before);
    }

    /// <summary>A broken telemetry consumer must not turn a committed save into a failure.</summary>
    /// <returns>The asynchronous test.</returns>
    [Fact]
    public async Task ConsumerFailureDoesNotChangePersistenceAsync()
    {
        var instance = Guid.NewGuid();
        using var capture = new Capture(instance, fail: true);
        using IDisposable correlation = SettingsTelemetry.BeginScope(instance);
        var store = new Store();
        using ServiceProvider provider = await CreateAsync(store, capture);
        IEditableOptions<SampleSettings> settings = provider.GetRequiredService<IEditableOptions<SampleSettings>>();
        Assert.Equal(SettingsSaveStatus.Saved, (await settings.SaveAsync(settings.BeginEdit())).Status);
        Assert.Equal(1, settings.Revision);
        Assert.NotNull(store.Data);
    }

    /// <summary>Reports broken physical loads and recovery without overwriting protected data.</summary>
    /// <param name="status">The physical load outcome.</param>
    /// <returns>The asynchronous test.</returns>
    [Theory]
    [InlineData(SettingsLoadStatus.InvalidData)]
    [InlineData(SettingsLoadStatus.UnsupportedVersion)]
    [InlineData(SettingsLoadStatus.StorageFailure)]
    public async Task RecordsLoadFailureAndRecoveryAsync(SettingsLoadStatus status)
    {
        var instance = Guid.NewGuid();
        using var capture = new Capture(instance);
        using IDisposable correlation = SettingsTelemetry.BeginScope(instance);
        var store = new Store
        {
            Data = System.Text.Encoding.UTF8.GetBytes(status == SettingsLoadStatus.UnsupportedVersion ? "{\"documentVersion\":99}" : "invalid-json"),
            FailReads = status == SettingsLoadStatus.StorageFailure,
        };
        using ServiceProvider provider = await CreateAsync(store, capture);
        IEditableOptions<SampleSettings> settings = provider.GetRequiredService<IEditableOptions<SampleSettings>>();
        Assert.Equal(status, settings.LoadResult.Status);
        Assert.Equal(SettingsSaveStatus.RecoveryRequired, (await settings.SaveAsync(settings.BeginEdit())).Status);
        Assert.Contains(capture.Measurements, item => item.Name == "settings.operations" && item.Tags["settings.operation"]?.ToString() == "document-load" && item.Tags["settings.status"]?.ToString() == status.ToString());
        Assert.Contains(capture.Spans, span => span.OperationName == "Settings.load" && span.Status == ActivityStatusCode.Error);
        if (status != SettingsLoadStatus.UnsupportedVersion)
        {
            Assert.Contains(capture.Logs, log => log.Exception != null && log.Message.Contains("document-load", StringComparison.Ordinal));
        }

        Assert.Equal(SettingsSaveStatus.Saved, (await provider.GetRequiredService<ISettingsDocument>().ResetAsync()).Status);
        Assert.Contains(capture.Measurements, item => item.Name == "settings.operations" && item.Tags["settings.operation"]?.ToString() == "document-reset" && item.Tags["settings.status"]?.ToString() == "Saved");
    }

    /// <summary>Unexpected errors preserve the original exception while producing failure telemetry.</summary>
    /// <returns>The asynchronous test.</returns>
    [Fact]
    public async Task RecordsUnexpectedExceptionsAndPreservesPropagationAsync()
    {
        var instance = Guid.NewGuid();
        using var capture = new Capture(instance);
        using IDisposable correlation = SettingsTelemetry.BeginScope(instance);
        var failure = new InvalidOperationException("Unexpected storage failure", new ArgumentException("Inner failure"));
        var store = new Store { Unexpected = failure };
        using ServiceProvider provider = await CreateAsync(store, capture);
        IEditableOptions<SampleSettings> settings = provider.GetRequiredService<IEditableOptions<SampleSettings>>();
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => settings.SaveAsync(settings.BeginEdit())));
        Assert.Equal(0, settings.Revision);
        Assert.Null(store.Data);
        Activity span = Assert.Single(capture.Spans, item => item.OperationName == "Settings.save");
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Equal("Failed", span.GetTagItem("settings.status"));
        Assert.Contains("Inner failure", span.GetTagItem("exception.stacktrace")?.ToString(), StringComparison.Ordinal);
        Assert.Contains(capture.Logs, log => ReferenceEquals(log.Exception, failure) && log.TraceId == span.TraceId.ToString());
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => settings.ResetAsync(settings.Revision)));
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetRequiredService<ISettingsDocument>().ResetAsync()));
        Assert.Contains(capture.Spans, item => item.OperationName == "Settings.reset" && item.Status == ActivityStatusCode.Error && item.GetTagItem("exception.type")?.ToString() == typeof(InvalidOperationException).FullName);
        Assert.Contains(capture.Spans, item => item.OperationName == "Settings.document-reset" && item.Status == ActivityStatusCode.Error && item.GetTagItem("exception.type")?.ToString() == typeof(InvalidOperationException).FullName);
        Assert.Equal(0, capture.Measurements.Where(item => item.Name == "settings.writes.active").Sum(item => item.Value));
    }

    private static async Task<ServiceProvider> CreateAsync(Store store, Capture capture)
    {
        PersistedSettingsSource source = await PersistedSettingsSource.LoadAsync(store);
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddPersistedSettings(source).Build());
        services.AddLogging(builder => builder.AddProvider(capture));
        services.UseSampleModule();
        services.AddSettings(source);
        return services.BuildServiceProvider();
    }

    private sealed record Measurement(string Name, double Value, Dictionary<string, object?> Tags);

    private sealed record Log(LogLevel Level, string Message, string? TraceId, Exception? Exception);

    private sealed class Store : ISettingsStore
    {
        public byte[]? Data { get; set; }

        public Exception? Unexpected { get; set; }

        public bool FailReads { get; set; }

        public bool Fail { get; set; }

        public bool Pause { get; set; }

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<byte[]?> ReadAsync(CancellationToken cancellationToken = default)
        {
            if (FailReads)
            {
                throw new IOException("secret-storage-path");
            }

            return ValueTask.FromResult(Data);
        }

        public async ValueTask WriteAtomicallyAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        {
            if (Unexpected != null)
            {
                throw Unexpected;
            }

            if (Fail)
            {
                throw new IOException("secret-storage-path");
            }

            if (Pause)
            {
                Entered.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            Data = data.ToArray();
        }
    }

    private sealed class Capture : ILoggerProvider, ILogger
    {
        private readonly Guid _instance;
        private readonly bool _fail;
        private readonly MeterListener _meter = new();
        private readonly ActivityListener _activities;

        public Capture(Guid instance, bool fail = false)
        {
            _instance = instance;
            _fail = fail;
            _meter.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == SettingsTelemetry.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _meter.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags));
            _meter.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags));
            _meter.Start();
            _activities = new()
            {
                ShouldListenTo = source => source.Name == SettingsTelemetry.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity =>
                {
                    if (activity.GetTagItem("lumyte.instance.id")?.ToString() == instance.ToString("D"))
                    {
                        Spans.Enqueue(activity);
                    }
                },
            };
            ActivitySource.AddActivityListener(_activities);
        }

        public ConcurrentQueue<Measurement> Measurements { get; } = new();

        public ConcurrentQueue<Activity> Spans { get; } = new();

        public ConcurrentQueue<Log> Logs { get; } = new();

        public ILogger CreateLogger(string categoryName) => this;

        public bool IsEnabled(LogLevel logLevel) => true;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (_fail)
            {
                throw new InvalidOperationException("Broken logger");
            }

            Logs.Enqueue(new(logLevel, formatter(state, exception), Activity.Current?.TraceId.ToString(), exception));
        }

        public void Dispose()
        {
            _meter.Dispose();
            _activities.Dispose();
        }

        private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            Dictionary<string, object?> data = tags.ToArray().ToDictionary(pair => pair.Key, pair => pair.Value);
            if (data.GetValueOrDefault("lumyte.instance.id")?.ToString() != _instance.ToString("D"))
            {
                return;
            }

            if (_fail)
            {
                throw new InvalidOperationException("Broken metrics consumer");
            }

            Measurements.Enqueue(new(instrument.Name, value, data));
        }
    }
}
