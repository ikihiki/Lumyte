using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Lumyte.Diagnostics;

/// <summary>Collects standard metrics, completed spans and structured logs into a bounded queue.</summary>
public sealed class DiagnosticTelemetry : IDisposable
{
    private readonly IGameExecutionIdentity _identity;
    private readonly DiagnosticOptions _options;
    private readonly IMeterFactory _meterFactory;
    private readonly TelemetryRouter _router;
    private readonly Channel<DiagnosticEvent> _events;
    private readonly string _instanceTag;
    private readonly string _traceKey = Guid.NewGuid().ToString("N");
    private MeterListener? _metrics;
    private ActivityListener? _traces;
    private long _dropped;
    private volatile bool _running;
    private bool _disposed;

    internal DiagnosticTelemetry(IGameExecutionIdentity identity, DiagnosticOptions options, IMeterFactory meterFactory, TelemetryRouter router)
    {
        _identity = identity;
        _instanceTag = identity.InstanceId.ToString("D");
        _options = options;
        _meterFactory = meterFactory;
        _router = router;
        _events = Channel.CreateBounded<DiagnosticEvent>(new BoundedChannelOptions(options.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
        });
    }

    /// <summary>Gets the count of events dropped at the collection boundary.</summary>
    public long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>Begins collection; must be called before starting work.</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_running || !_options.Enabled)
        {
            return;
        }

        _running = true;
        _router.Register(_identity.InstanceId, this);
        _metrics = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (ReferenceEquals(instrument.Meter.Scope, _meterFactory)
                    && _options.AllowedMeterNames.Contains(instrument.Meter.Name, StringComparer.Ordinal)
                    && !instrument.GetType().Name.StartsWith("Observable", StringComparison.Ordinal))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            },
        };
        _metrics.SetMeasurementEventCallback<long>((instrument, value, tags, _) => OnMetric(instrument, DiagnosticValue.From(value), tags));
        _metrics.SetMeasurementEventCallback<double>((instrument, value, tags, _) => OnMetric(instrument, DiagnosticValue.From(value), tags));
        _metrics.SetMeasurementEventCallback<int>((instrument, value, tags, _) => OnMetric(instrument, DiagnosticValue.From((long)value), tags));
        _metrics.SetMeasurementEventCallback<float>((instrument, value, tags, _) => OnMetric(instrument, DiagnosticValue.From((double)value), tags));
        _metrics.SetMeasurementEventCallback<short>((instrument, value, tags, _) => OnMetric(instrument, DiagnosticValue.From((long)value), tags));
        _metrics.SetMeasurementEventCallback<byte>((instrument, value, tags, _) => OnMetric(instrument, DiagnosticValue.From((long)value), tags));
        _metrics.Start();
        _traces = new ActivityListener
        {
            ShouldListenTo = source => _options.AllowedActivitySourceNames.Contains(source.Name, StringComparer.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> options) => Sample(options.Tags, options.TraceId, options.Parent),
            SampleUsingParentId = (ref ActivityCreationOptions<string> options) =>
            {
                ActivityContext.TryParse(options.Parent, null, out ActivityContext parent);
                return Sample(options.Tags, options.TraceId, parent);
            },
            ActivityStarted = activity =>
            {
                ActivityContext.TryParse(activity.ParentId, null, out ActivityContext parent);
                if (Sample(activity.TagObjects, activity.TraceId, parent) == ActivitySamplingResult.AllDataAndRecorded)
                {
                    activity.SetCustomProperty(_traceKey, true);
                }
            },
            ActivityStopped = activity =>
            {
                if (activity.GetCustomProperty(_traceKey) is true)
                {
                    Write(new("span", Stopwatch.GetTimestamp(), activity.OperationName, DiagnosticValue.From(activity.Status.ToString()), activity.TraceId.ToString(), activity.SpanId.ToString(), activity.ParentSpanId.ToString(), activity.Duration.Ticks, CopyFields(activity.TagObjects)));
                }
            },
        };
        ActivitySource.AddActivityListener(_traces);
    }

    /// <summary>Reads one detached event without blocking.</summary>
    /// <param name="item">The item argument.</param>
    /// <returns>The computed result.</returns>
    public bool TryRead(out DiagnosticEvent? item) => _events.Reader.TryRead(out item);

    /// <inheritdoc/>
    public void Dispose()
    {
        _running = false;
        _disposed = true;
        _router.Remove(_identity.InstanceId);
        _metrics?.Dispose();
        _traces?.Dispose();
        _metrics = null;
        _traces = null;
        _events.Writer.TryComplete();
    }

    internal void Drop() => Interlocked.Increment(ref _dropped);

    internal bool LogEnabled(string category, LogLevel level) => _running && level >= _options.MinimumLogLevel && level != LogLevel.None
        && _options.AllowedLogCategoryPrefixes.Any(prefix => category == prefix || category.StartsWith(prefix + ".", StringComparison.Ordinal));

    internal void WriteLog(string category, LogLevel level, EventId eventId, string message, IEnumerable<KeyValuePair<string, object?>> state, IEnumerable<KeyValuePair<string, object?>> scopes, Exception? exception)
    {
        if (!LogEnabled(category, level))
        {
            return;
        }

        Dictionary<string, DiagnosticValue> fields = CopyFields(state.Concat(scopes));
        fields["log.level"] = DiagnosticValue.From(level.ToString());
        fields["log.event-id"] = DiagnosticValue.From((long)eventId.Id);
        if (exception != null)
        {
            fields["exception.type"] = DiagnosticValue.From(exception.GetType().FullName ?? "Exception");
            fields["exception.message"] = DiagnosticValue.From(Trim(exception.Message));
        }

        Activity? activity = Activity.Current;
        Write(new("log", Stopwatch.GetTimestamp(), category, DiagnosticValue.From(Trim(message)), activity?.TraceId.ToString(), activity?.SpanId.ToString(), null, 0, fields));
    }

    private static string Trim(string value) => value.Length <= 4096 ? value : value[..4096];

    private static Dictionary<string, DiagnosticValue> CopyFields(IEnumerable<KeyValuePair<string, object?>>? values)
    {
        var result = new Dictionary<string, DiagnosticValue>(StringComparer.Ordinal);
        if (values == null)
        {
            return result;
        }

        foreach (KeyValuePair<string, object?> pair in values.Take(32))
        {
            DiagnosticValue? value = pair.Value switch
            {
                string text => DiagnosticValue.From(Trim(text)),
                bool boolean => DiagnosticValue.From(boolean),
                int integer => DiagnosticValue.From((long)integer),
                long integer => DiagnosticValue.From(integer),
                double number when double.IsFinite(number) => DiagnosticValue.From(number),
                _ => null,
            };
            if (value is DiagnosticValue scalar && pair.Key.Length <= 128)
            {
                result[pair.Key] = scalar;
            }
        }

        return result;
    }

    private bool Matches(IEnumerable<KeyValuePair<string, object?>>? tags)
        => tags != null && tags.Any(pair => pair.Key == "lumyte.instance.id" && pair.Value is string id && id == _instanceTag);

    private ActivitySamplingResult Sample(IEnumerable<KeyValuePair<string, object?>>? tags, ActivityTraceId traceId, ActivityContext parent)
    {
        if (!Matches(tags))
        {
            return ActivitySamplingResult.None;
        }

        bool sampled;
        if (parent != default)
        {
            sampled = (parent.TraceFlags & ActivityTraceFlags.Recorded) != 0;
        }
        else
        {
            Span<byte> bytes = stackalloc byte[16];
            traceId.CopyTo(bytes);
            uint value = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes);
            sampled = value / 4294967296.0 < _options.TraceSampleRatio;
        }

        return sampled ? ActivitySamplingResult.AllDataAndRecorded : ActivitySamplingResult.None;
    }

    private void OnMetric(Instrument instrument, DiagnosticValue value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        bool matched = false;
        foreach (KeyValuePair<string, object?> tag in tags)
        {
            if (tag.Key == "lumyte.instance.id" && tag.Value is string id && id == _instanceTag)
            {
                matched = true;
            }
        }

        if (matched && (value.Kind != DiagnosticValueKind.Double || double.IsFinite(value.Double)))
        {
            Write(new("metric", Stopwatch.GetTimestamp(), instrument.Name, value, null, null, null, 0, CopyFields(tags.ToArray())));
        }
    }

    private void Write(DiagnosticEvent item)
    {
        if (_running && !_events.Writer.TryWrite(item))
        {
            Interlocked.Increment(ref _dropped);
        }
    }
}
