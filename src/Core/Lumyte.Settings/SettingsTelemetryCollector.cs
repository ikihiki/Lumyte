using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace Lumyte.Settings;

internal sealed class SettingsTelemetryCollector : IDisposable
{
    private static readonly Action<ILogger, string, string, Exception?> _started = LoggerMessage.Define<string, string>(LogLevel.Debug, new(2000, "SettingsStarted"), "Settings {Operation} started for {Section}");
    private static readonly Action<ILogger, string, string, Exception?> _completed = LoggerMessage.Define<string, string>(LogLevel.Information, new(2001, "SettingsCompleted"), "Settings {Operation} completed with {Status}");
    private static readonly Action<ILogger, string, string, Exception?> _warning = LoggerMessage.Define<string, string>(LogLevel.Warning, new(2002, "SettingsRejected"), "Settings {Operation} completed with {Status}");
    private static readonly Action<ILogger, string, string, Exception?> _failed = LoggerMessage.Define<string, string>(LogLevel.Error, new(2003, "SettingsFailed"), "Settings {Operation} completed with {Status}");
    private readonly ActivitySource _activities = new(SettingsTelemetry.ActivitySourceName);
    private readonly ILogger _logger;
    private readonly Counter<long> _operations;
    private readonly Histogram<double> _duration;
    private readonly Histogram<double> _waitDuration;
    private readonly UpDownCounter<long> _active;

    public SettingsTelemetryCollector(IMeterFactory factory, ILoggerFactory loggers)
    {
        Meter meter = factory.Create(new MeterOptions(SettingsTelemetry.MeterName));
        _operations = meter.CreateCounter<long>("settings.operations", description: "Completed settings operations.");
        _duration = meter.CreateHistogram<double>("settings.operation.duration", "ms", "Settings operation duration.");
        _waitDuration = meter.CreateHistogram<double>("settings.write.wait.duration", "ms", "Time waiting for settings write ownership.");
        _active = meter.CreateUpDownCounter<long>("settings.writes.active", description: "Active settings save and reset operations.");
        _logger = loggers.CreateLogger(SettingsTelemetry.LogCategoryName);
    }

    public Operation Begin(string operation, string section) => new(this, operation, section);

    public void DocumentLoaded(SettingsLoadStatus status, double milliseconds)
    {
        using Operation operation = Begin("document-load", "$document");
        operation.Complete(status.ToString());
        operation.DurationOverride = milliseconds;
    }

    public void Dispose() => _activities.Dispose();

    private static bool IsFailure(string status) => status is "Failed" or "StorageFailure" or "ValidationFailed" or "InvalidData" or "UnsupportedVersion";

    internal sealed class Operation : IDisposable
    {
        private readonly SettingsTelemetryCollector _owner;
        private readonly string _name;
        private readonly TagList _tags;
        private readonly long _startedAt = Stopwatch.GetTimestamp();
        private readonly bool _write;
        private readonly Activity? _activity;
        private readonly IDisposable? _scope;
        private string _status = "Failed";
        private long? _revision;
        private int _disposed;

        public Operation(SettingsTelemetryCollector owner, string name, string section)
        {
            _owner = owner;
            _name = name;
            _write = name is "save" or "reset" or "document-reset";
            _tags = new() { { "settings.operation", name }, { "settings.section", section } };
            if (SettingsTelemetry.InstanceId is Guid instance)
            {
                _tags.Add("lumyte.instance.id", instance.ToString("D"));
            }

            try
            {
                _scope = owner._logger.BeginScope(_tags.ToArray());
                if (name != "document-load")
                {
                    _activity = owner._activities.StartActivity("Settings." + name, ActivityKind.Internal, default(ActivityContext), _tags);
                }

                _started(owner._logger, name, section, null);
            }
            catch (Exception)
            {
                // Instrumentation must never change persistence semantics.
            }

            if (_write)
            {
                try
                {
                    owner._active.Add(1, _tags);
                }
                catch (Exception)
                {
                    // Consumers can fail while receiving a measurement.
                }
            }
        }

        public double? DurationOverride { get; set; }

        public void Complete(string status, long? revision = null)
        {
            _status = status;
            _revision = revision;
        }

        public async Task WaitAsync(SemaphoreSlim semaphore, CancellationToken cancellationToken)
        {
            long started = Stopwatch.GetTimestamp();
            string outcome = "Failed";
            try
            {
                await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                outcome = "Acquired";
            }
            catch (OperationCanceledException)
            {
                outcome = "Cancelled";
                throw;
            }
            finally
            {
                try
                {
                    TagList tags = _tags;
                    tags.Add("settings.wait.outcome", outcome);
                    _owner._waitDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds, tags);
                }
                catch (Exception)
                {
                    // A completed wait remains acquired even if a consumer fails.
                }
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            TagList tags = _tags;
            tags.Add("settings.status", _status);
            try
            {
                _owner._operations.Add(1, tags);
                _owner._duration.Record(DurationOverride ?? Stopwatch.GetElapsedTime(_startedAt).TotalMilliseconds, tags);
            }
            catch (Exception)
            {
                // Keep the caller's result independent of telemetry consumers.
            }

            if (_write)
            {
                try
                {
                    _owner._active.Add(-1, _tags);
                }
                catch (Exception)
                {
                    // Balancing uses exactly the start tags, without result status.
                }
            }

            try
            {
                _activity?.SetTag("settings.status", _status);
                if (_revision.HasValue)
                {
                    _activity?.SetTag("settings.revision", _revision.Value);
                }

                if (IsFailure(_status))
                {
                    _activity?.SetStatus(ActivityStatusCode.Error, _status);
                    _failed(_owner._logger, _name, _status, null);
                }
                else if (_status is "Conflict" or "RecoveryRequired")
                {
                    _warning(_owner._logger, _name, _status, null);
                }
                else
                {
                    _completed(_owner._logger, _name, _status, null);
                }
            }
            catch (Exception)
            {
                // Do not attach exception or validation text to diagnostic output.
            }
            finally
            {
                try
                {
                    _activity?.Dispose();
                }
                catch (Exception)
                {
                    // ActivityStopped callbacks are external consumers too.
                }

                try
                {
                    _scope?.Dispose();
                }
                catch (Exception)
                {
                    // Logger scopes are owned only for this operation.
                }
            }
        }
    }
}
