using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Text;
using System.Text.Json;

namespace Lumyte.UI.SampleSupport;

/// <summary>Explicitly enabled, bounded sample-only capture observations.</summary>
internal sealed class UiCaptureDiagnostics : EventListener
{
    private const int TerminalArchiveBytes = 16384;
    private static UiCaptureDiagnostics? _instance;
    private readonly object _gate = new();
    private readonly Queue<string> _events = new();
    private readonly Dictionary<long, string> _requests = new();
    private readonly HashSet<long> _terminal = [];
    private readonly Dictionary<long, string[]> _terminalFrames = new();
    private readonly Dictionary<(Guid Run, long Frame), Dictionary<long, string>> _open = new();
    private readonly Dictionary<(Guid Run, long Frame), string> _last = new();
    private readonly Dictionary<long, HashSet<(Guid Run, long Frame)>> _relations = new();
    private long _dropped;
    private long _streamed;

    internal static void Enable()
    {
        try
        {
            _instance ??= new();
        }
        catch (Exception)
        {
            // Unsupported diagnostic runtimes keep the application path unchanged.
        }
    }

    internal static string GetSnapshot()
    {
        try
        {
            return _instance?.Snapshot() ?? "{\"enabled\":false}";
        }
        catch (Exception)
        {
            return "{\"enabled\":true,\"observerError\":true}";
        }
    }

    internal string Snapshot()
    {
        lock (_gate)
        {
            using var output = new MemoryStream();
            using (var writer = new Utf8JsonWriter(output))
            {
                writer.WriteStartObject();
                writer.WriteBoolean("enabled", true);
                writer.WriteNumber("dropped", _dropped);
                writer.WriteNumber("streamed", _streamed);
                writer.WriteStartArray("events");
                foreach (string item in _events)
                {
                    writer.WriteRawValue(item);
                }

                writer.WriteEndArray();
                writer.WriteStartArray("requests");
                foreach (string item in _requests.Values)
                {
                    writer.WriteRawValue(item);
                }

                writer.WriteEndArray();
                writer.WriteStartArray("relations");
                foreach ((long request, HashSet<(Guid Run, long Frame)> frames) in _relations)
                {
                    foreach ((Guid run, long frame) in frames)
                    {
                        writer.WriteStartObject();
                        writer.WriteNumber("requestId", request);
                        writer.WriteString("profileRunId", run);
                        writer.WriteNumber("frameId", frame);
                        writer.WriteEndObject();
                    }
                }

                writer.WriteEndArray();
                writer.WriteStartArray("terminalFrames");
                foreach (string[] frames in _terminalFrames.Values)
                {
                    foreach (string frame in frames)
                    {
                        writer.WriteRawValue(frame);
                    }
                }

                writer.WriteEndArray();
                writer.WriteStartArray("lastStages");
                foreach (string item in _last.Values)
                {
                    writer.WriteRawValue(item);
                }

                writer.WriteEndArray();
                writer.WriteStartArray("openStages");
                foreach (Dictionary<long, string> stages in _open.Values)
                {
                    foreach (string item in stages.Values)
                    {
                        writer.WriteRawValue(item);
                    }
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(output.ToArray());
        }
    }

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if (eventSource.Name == "Lumyte-UI-Application-Profiling")
        {
            EnableEvents(eventSource, EventLevel.Informational, (EventKeywords)19);
        }
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        try
        {
            Observe(eventData);
        }
        catch (Exception)
        {
            // Sample diagnostics cannot affect application work, exceptions or deadlines.
        }
    }

    private static string Record(EventWrittenEventArgs item)
    {
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStartObject();
            writer.WriteNumber("eventId", item.EventId);
            writer.WriteString("name", item.EventName);
            writer.WriteNumber("ticks", Stopwatch.GetTimestamp());
            writer.WriteNumber("frequency", Stopwatch.Frequency);
            writer.WriteStartObject("data");
            for (int i = 0; i < item.Payload!.Count; i++)
            {
                string name = item.PayloadNames![i];
                switch (item.Payload![i])
                {
                    case Guid value:
                        writer.WriteString(name, value);
                        break;
                    case long value:
                        writer.WriteNumber(name, value);
                        break;
                    case int value:
                        writer.WriteNumber(name, value);
                        break;
                    case bool value:
                        writer.WriteBoolean(name, value);
                        break;
                    default:
                        writer.WriteNull(name);
                        break;
                }
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(output.ToArray());
    }

    private string TerminalFrame(long request, (Guid Run, long Frame) frame)
    {
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStartObject();
            writer.WriteNumber("requestId", request);
            writer.WriteString("profileRunId", frame.Run);
            writer.WriteNumber("frameId", frame.Frame);
            writer.WriteString("observation", "atTerminal");
            writer.WriteStartArray("openStages");
            if (_open.TryGetValue(frame, out Dictionary<long, string>? stages))
            {
                foreach (string stage in stages.Values)
                {
                    writer.WriteRawValue(stage);
                }
            }

            writer.WriteEndArray();
            writer.WritePropertyName("lastStage");
            if (_last.TryGetValue(frame, out string? last))
            {
                writer.WriteRawValue(last);
            }
            else
            {
                writer.WriteNullValue();
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(output.ToArray());
    }

    private void Observe(EventWrittenEventArgs item)
    {
        if (item.EventId is not (1 or 2 or 5 or 7 or 12) || item.Payload is null)
        {
            return;
        }

        lock (_gate)
        {
            if (item.EventId == 12)
            {
                CaptureRecord(item);
                return;
            }

            ObserveFrame(item);
        }
    }

    private void CaptureRecord(EventWrittenEventArgs item)
    {
        string record = Record(item);
        long request = (long)item.Payload![0]!;
        int phase = (int)item.Payload![4]!;
        bool terminal = phase is 8 or 9 or 10;
        if (!_requests.ContainsKey(request) && _requests.Count >= 16)
        {
            long evicted = _requests.Keys.First();
            _requests.Remove(evicted);
            _terminal.Remove(evicted);
            _terminalFrames.Remove(evicted);
            _dropped++;
        }

        if (!_terminal.Contains(request) || terminal)
        {
            _requests[request] = record;
        }

        if (!_relations.TryGetValue(request, out HashSet<(Guid Run, long Frame)>? frames))
        {
            if (_relations.Count >= 16)
            {
                _relations.Remove(_relations.Keys.First());
                _dropped++;
            }

            _relations[request] = frames = [];
        }

        var key = ((Guid)item.Payload![1]!, (long)item.Payload![2]!);
        if (key.Item2 != 0)
        {
            if (frames.Count >= 4)
            {
                frames.Clear();
                _dropped++;
            }

            frames.Add(key);
        }

        Add(record, terminal);
        if (phase == 3 && _open.TryGetValue(key, out Dictionary<long, string>? stages))
        {
            foreach (string stage in stages.Values)
            {
                Add(stage, false);
            }
        }

        if (terminal && _terminal.Add(request))
        {
            string[] archive = frames.Select(frame => TerminalFrame(request, frame)).ToArray();
            while (_terminalFrames.Count != 0 && _terminalFrames.Values.Sum(items => items.Sum(value => value.Length)) + archive.Sum(value => value.Length) > TerminalArchiveBytes)
            {
                _terminalFrames.Remove(_terminalFrames.Keys.First());
                _dropped++;
            }

            if (archive.Sum(value => value.Length) <= TerminalArchiveBytes)
            {
                _terminalFrames[request] = archive;
            }
            else
            {
                _dropped++;
            }
        }

        if (_terminal.Contains(request))
        {
            _relations.Remove(request);
        }
    }

    private void ObserveFrame(EventWrittenEventArgs item)
    {
        var frame = ((Guid)item.Payload![0]!, (long)item.Payload![1]!);
        if (frame.Item2 == 0)
        {
            return;
        }

        if (item.EventId is 5 or 7)
        {
            if (_relations.Values.Any(frames => frames.Contains(frame)))
            {
                Add(Record(item), false);
            }

            return;
        }

        string record = Record(item);
        if (!_last.ContainsKey(frame) && _last.Count >= 4)
        {
            (Guid Run, long Frame) evicted = _last.Keys.FirstOrDefault(key => !_relations.Values.Any(frames => frames.Contains(key)));
            if (evicted == default)
            {
                _dropped++;
                return;
            }

            _last.Remove(evicted);
        }

        _last[frame] = record;
        long span = (long)item.Payload![2]!;
        if (!_open.TryGetValue(frame, out Dictionary<long, string>? open))
        {
            if (_open.Count >= 4)
            {
                (Guid Run, long Frame) evicted = _open.Keys.FirstOrDefault(key => !_relations.Values.Any(frames => frames.Contains(key)));
                if (evicted == default)
                {
                    _dropped++;
                    return;
                }

                _open.Remove(evicted);
                _dropped++;
            }

            _open[frame] = open = [];
        }

        if (item.EventId == 1)
        {
            if (open.Count >= 8)
            {
                open.Remove(open.Keys.First());
                _dropped++;
            }

            open[span] = record;
        }
        else
        {
            open.Remove(span);
            if (open.Count == 0)
            {
                _open.Remove(frame);
            }
        }

        if (_relations.Values.Any(frames => frames.Contains(frame)))
        {
            Add(record, false);
        }
    }

    private void Add(string record, bool terminal)
    {
        if (_events.Count >= 64)
        {
            _events.Dequeue();
            _dropped++;
        }

        _events.Enqueue(record);
        if (_streamed < 2048 || terminal)
        {
            _streamed++;
            Console.WriteLine("UI Capture trace: " + record);
        }
        else
        {
            _dropped++;
        }
    }
}
