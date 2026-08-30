using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Lumyte.DevTools.Host;

public sealed record DiagnosticsCollectorOptions(int ActivityCapacity = 512, int SampleCapacity = 2048, int SeriesCapacity = 256, int HistogramWindow = 256, string SourcePrefix = "Lumyte.", bool Enabled = true);
public sealed record DiagnosticTag(string Key, string Value);
public sealed record DiagnosticActivityEvent(string Name, DateTimeOffset Timestamp, IReadOnlyList<DiagnosticTag> Tags);
public sealed record DiagnosticActivity(string TraceId, string ActivityId, string? ParentId, string Source, string Operation, string Kind, DateTimeOffset Start, double DurationMilliseconds, string Status, string? StatusDescription, IReadOnlyList<DiagnosticTag> Tags, IReadOnlyList<DiagnosticTag> Baggage, IReadOnlyList<DiagnosticActivityEvent> Events, bool IsActive);
public sealed record MetricSample(DateTimeOffset Timestamp, double Measurement);
public sealed record MetricSeriesSnapshot(string Key, string Meter, string Instrument, string Kind, string? Unit, string? Description, IReadOnlyList<DiagnosticTag> Tags, double Current, double Delta, double? RatePerSecond, long Count, double Sum, double? Min, double? Max, double? P50, double? P95, IReadOnlyList<MetricSample> Samples);
public sealed record DiagnosticsCatalog(IReadOnlyList<string> ActivitySources, IReadOnlyList<DiagnosticInstrument> Instruments);
public sealed record DiagnosticInstrument(string Meter, string Name, string Kind, string? Unit, string? Description);
public sealed record DiagnosticsStatus(bool Enabled, bool Paused, int ActivityCount, int ActivityCapacity, int MetricSampleCount, int MetricSampleCapacity, int SeriesCount, int SeriesCapacity, long DroppedActivities, long DroppedMetricSamples, long DroppedSeries);
public sealed record DiagnosticsSnapshot(DiagnosticsStatus Status, IReadOnlyList<DiagnosticActivity> Activities, IReadOnlyList<MetricSeriesSnapshot> Metrics);

public sealed class DiagnosticsCollector : IDisposable
{
    private readonly DiagnosticsCollectorOptions options; private readonly TimeProvider timeProvider; private readonly Lock sync = new(); private readonly List<DiagnosticActivity> activities = []; private readonly Dictionary<string, Activity> active = []; private readonly Dictionary<string, MetricSeries> series = []; private readonly HashSet<string> sources = []; private readonly Dictionary<string, DiagnosticInstrument> instruments = []; private readonly ActivityListener activityListener; private readonly MeterListener meterListener; private bool paused; private bool disposed; private long droppedActivities, droppedMetricSamples, droppedSeries; private int sampleCount; private long version;
    public DiagnosticsCollector(DiagnosticsCollectorOptions? options = null, TimeProvider? timeProvider = null)
    {
        this.options = options ?? new();
        this.timeProvider = timeProvider ?? TimeProvider.System;
        activityListener = new ActivityListener { ShouldListenTo = source => Accept(source.Name), Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded, SampleUsingParentId = (ref _) => ActivitySamplingResult.AllDataAndRecorded, ActivityStarted = OnStarted, ActivityStopped = OnStopped };
        ActivitySource.AddActivityListener(activityListener);
        meterListener = new MeterListener { InstrumentPublished = OnInstrumentPublished };
        meterListener.SetMeasurementEventCallback<byte>((i, v, t, s) => Record(i, v, t));
        meterListener.SetMeasurementEventCallback<short>((i, v, t, s) => Record(i, v, t));
        meterListener.SetMeasurementEventCallback<int>((i, v, t, s) => Record(i, v, t));
        meterListener.SetMeasurementEventCallback<long>((i, v, t, s) => Record(i, v, t));
        meterListener.SetMeasurementEventCallback<float>((i, v, t, s) => Record(i, v, t));
        meterListener.SetMeasurementEventCallback<double>((i, v, t, s) => Record(i, v, t));
        meterListener.SetMeasurementEventCallback<decimal>((i, v, t, s) => Record(i, (double)v, t));
        meterListener.Start();
    }
    public long Version => Interlocked.Read(ref version);
    public DiagnosticsCatalog GetCatalog() { lock (sync)
        {
            return new([.. sources.Order()], [.. instruments.Values.OrderBy(x => x.Meter).ThenBy(x => x.Name)]);
        }
    }
    public DiagnosticsSnapshot GetSnapshot(string? source = null, string? name = null, string? status = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        meterListener.RecordObservableInstruments();
        lock (sync)
        {
            DiagnosticActivity[] activityView = [.. activities.Concat(active.Values.Select(ToActive)).Where(a => Match(a.Source, source) && Match(a.Operation, name) && Match(a.Status, status)).OrderByDescending(a => a.Start)];
            MetricSeriesSnapshot[] metrics = [.. series.Values.Select(x => x.Snapshot()).Where(m => Match(m.Meter, source) && Match(m.Instrument, name)).OrderBy(m => m.Meter).ThenBy(m => m.Instrument)];
            return new(StatusCore(), activityView, metrics);
        }
    }
    public DiagnosticsStatus GetStatus() { lock (sync)
        {
            return StatusCore();
        }
    }
    public void Pause() { lock (sync) { paused = true; version++; } }
    public void Resume() { lock (sync) { paused = false; version++; } }
    public void Clear() { lock (sync) { activities.Clear(); series.Clear(); sampleCount = 0; droppedActivities = droppedMetricSamples = droppedSeries = 0; version++; } }
    public void Dispose() { if (disposed) { return; } disposed = true; activityListener.Dispose(); meterListener.Dispose(); }
    private bool Accept(string name) => options.Enabled && name.StartsWith(options.SourcePrefix, StringComparison.Ordinal);
    private void OnStarted(Activity activity) { lock (sync) { if (paused) { return; } sources.Add(activity.Source.Name); active[activity.Id!] = activity; version++; } }
    private void OnStopped(Activity activity) { lock (sync) { active.Remove(activity.Id!); if (paused) { return; } sources.Add(activity.Source.Name); if (activities.Count == options.ActivityCapacity) { activities.RemoveAt(0); droppedActivities++; } activities.Add(ToStopped(activity)); version++; } }
    private void OnInstrumentPublished(Instrument instrument, MeterListener listener) { if (!Accept(instrument.Meter.Name)) { return; } lock (sync) { instruments[$"{instrument.Meter.Name}/{instrument.Name}"] = new(instrument.Meter.Name, instrument.Name, Kind(instrument), instrument.Unit, instrument.Description); } listener.EnableMeasurementEvents(instrument); }
    private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        lock (sync)
        { if (paused) { return; } DiagnosticTag[] safe = SafeTags(tags); string key = $"{instrument.Meter.Name}/{instrument.Name}|{string.Join(';', safe.Select(x => $"{x.Key}={x.Value}"))}"; if (!series.TryGetValue(key, out MetricSeries? item)) { if (series.Count >= options.SeriesCapacity) { droppedSeries++; return; } item = new(key, instrument, Kind(instrument), safe, options.HistogramWindow, timeProvider); series.Add(key, item); } if (sampleCount >= options.SampleCapacity) { MetricSeries? oldest = series.Values.Where(x => x.Samples.Count > 0).OrderBy(x => x.Samples[0].Timestamp).FirstOrDefault(); if (oldest is not null) { oldest.Samples.RemoveAt(0); sampleCount--; droppedMetricSamples++; } } item.Add(value, timeProvider.GetUtcNow()); sampleCount++; version++; }
    }
    private DiagnosticsStatus StatusCore() => new(options.Enabled && !disposed, paused, activities.Count, options.ActivityCapacity, sampleCount, options.SampleCapacity, series.Count, options.SeriesCapacity, droppedActivities, droppedMetricSamples, droppedSeries);
    private static bool Match(string value, string? filter) => string.IsNullOrWhiteSpace(filter) || value.Contains(filter, StringComparison.OrdinalIgnoreCase);
    private static string Kind(Instrument i) => i.GetType().Name.Split('`')[0];
    private static DiagnosticActivity ToStopped(Activity a) => new(a.TraceId.ToString(), a.Id!, a.ParentId, a.Source.Name, a.OperationName, a.Kind.ToString(), a.StartTimeUtc, a.Duration.TotalMilliseconds, a.Status.ToString(), a.StatusDescription, SafeTags(a.TagObjects), SafeTags(a.Baggage.Select(x => new KeyValuePair<string, object?>(x.Key, x.Value))), [.. a.Events.Select(e => new DiagnosticActivityEvent(e.Name, e.Timestamp, SafeTags(e.Tags)))], false);
    private static DiagnosticActivity ToActive(Activity a) => new(a.TraceId.ToString(), a.Id!, a.ParentId, a.Source.Name, a.OperationName, a.Kind.ToString(), a.StartTimeUtc, 0, a.Status.ToString(), a.StatusDescription, SafeTags(a.TagObjects), SafeTags(a.Baggage.Select(x => new KeyValuePair<string, object?>(x.Key, x.Value))), [.. a.Events.Select(e => new DiagnosticActivityEvent(e.Name, e.Timestamp, SafeTags(e.Tags)))], true);
    private static DiagnosticTag[] SafeTags(IEnumerable<KeyValuePair<string, object?>> tags) => [.. tags.Select(x => new DiagnosticTag(x.Key, Safe(x.Value))).OrderBy(x => x.Key, StringComparer.Ordinal)];
    private static DiagnosticTag[] SafeTags(ReadOnlySpan<KeyValuePair<string, object?>> tags) { var result = new DiagnosticTag[tags.Length]; for (int i = 0; i < tags.Length; i++) { result[i] = new(tags[i].Key, Safe(tags[i].Value)); } Array.Sort(result, (a, b) => StringComparer.Ordinal.Compare(a.Key, b.Key)); return result; }
    private static string Safe(object? value) => value switch { null => "null", string s => s.Length <= 256 ? s : s[..256], bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal or Guid or DateTime or DateTimeOffset or TimeSpan or Enum => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "", _ => value.GetType().Name };
    private sealed class MetricSeries(string key, Instrument instrument, string kind, IReadOnlyList<DiagnosticTag> tags, int window, TimeProvider timeProvider)
    {
        private readonly bool histogram = kind.Contains("Histogram", StringComparison.Ordinal); private readonly int window = window; private readonly TimeProvider timeProvider = timeProvider; private double previous; private DateTimeOffset? previousTime; private readonly List<double> histogramValues = [];

        public string Key { get; } = key; public string Meter { get; } = instrument.Meter.Name; public string Instrument { get; } = instrument.Name; public string Kind { get; } = kind; public string? Unit { get; } = instrument.Unit; public string? Description { get; } = instrument.Description; public IReadOnlyList<DiagnosticTag> Tags { get; } = tags; public List<MetricSample> Samples { get; } = []; public double Current { get; private set; }
        public double Delta { get; private set; }
        public double? Rate { get; private set; }
        public long Count { get; private set; }
        public double Sum { get; private set; }
        public double? Min { get; private set; }
        public double? Max { get; private set; }
        public void Add(double value, DateTimeOffset now) { Delta = value; Count++; Sum += value; Min = Min is null ? value : Math.Min(Min.Value, value); Max = Max is null ? value : Math.Max(Max.Value, value); if (histogram) { Current = value; histogramValues.Add(value); if (histogramValues.Count > window) { histogramValues.RemoveAt(0); } } else { Current += value; if (previousTime is DateTimeOffset last) { double seconds = (now - last).TotalSeconds; Rate = seconds > 0 ? (Current - previous) / seconds : null; } previous = Current; previousTime = now; } Samples.Add(new(now, histogram ? value : Current)); }
        public MetricSeriesSnapshot Snapshot() { double[] sorted = [.. histogramValues.Order()]; return new(Key, Meter, Instrument, Kind, Unit, Description, Tags, Current, Delta, Rate, Count, Sum, Min, Max, histogram ? Percentile(sorted, .5) : null, histogram ? Percentile(sorted, .95) : null, [.. Samples]); }
        private static double? Percentile(double[] values, double p) => values.Length == 0 ? null : values[(int)Math.Ceiling((values.Length - 1) * p)];
    }
}
