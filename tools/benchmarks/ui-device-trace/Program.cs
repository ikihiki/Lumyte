using System.Text.Json;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;

string input = Path.GetFullPath(args[0]);
string output = Path.GetFullPath(args[1]);
string converted = Path.ChangeExtension(input, ".etlx");
if (!File.Exists(converted) || File.GetLastWriteTimeUtc(converted) < File.GetLastWriteTimeUtc(input))
    TraceLog.CreateFromEventPipeDataFile(input, converted, new TraceLogOptions { KeepAllEvents = true });
using var log = new TraceLog(converted);
var inventory = new Dictionary<string, int>();
var examples = new Dictionary<string, Dictionary<string, string>>();
var markers = new List<Marker>();
foreach (TraceEvent ev in log.Events)
{
    string key = ev.ProviderName + "/" + ev.EventName;
    inventory[key] = inventory.GetValueOrDefault(key) + 1;
    if ((key.Contains("SampleProfiler") || key.Contains("AllocationTick") || key.Contains("SuspendEE") || key.Contains("RestartEE")) && !examples.ContainsKey(key))
        examples[key] = ev.PayloadNames.ToDictionary(n => n, n => ev.PayloadByName(n)?.ToString() ?? "null");
    if (ev.ProviderName == "Lumyte-UI-Device-Benchmark" && ev.EventName == "Frame")
        markers.Add(new(Convert.ToInt64(ev.PayloadValue(0)), ev.TimeStampRelativeMSec, ev.ThreadID));
}
if (markers.Count < 2) throw new InvalidOperationException("No measurement frame markers.");
double start = markers.Min(m => m.Time);
double end = markers.Max(m => m.Time);
var allocations = new Dictionary<string, Allocation>();
var callers = new Dictionary<string, Allocation>();
var samples = new Dictionary<string, int>();
var sampleKinds = new Dictionary<string, int>();
var inclusive = new Dictionary<string, int>();
var external = new Dictionary<string, int>();
var pauses = new List<Pause>();
var collections = new List<object>();
double? suspended = null;
string reason = "";
long allocationBytes = 0;
int allocationTicks = 0;
int sampleCount = 0;
int stackless = 0;
int otherSuspensions = 0;
double otherSuspensionMilliseconds = 0;
foreach (TraceEvent ev in log.Events)
{
    double time = ev.TimeStampRelativeMSec;
    if (time < start || time > end) continue;
    if (ev.ProviderName == "Microsoft-Windows-DotNETRuntime")
    {
        if (ev.EventName.Contains("SuspendEEStart"))
        {
            suspended = time;
            reason = ev.PayloadNames.Contains("Reason") ? ev.PayloadByName("Reason").ToString()! : "";
        }
        else if (ev.EventName.Contains("RestartEEStop") && suspended is { } began)
        {
            if (reason == "SuspendForGC") pauses.Add(new(began, time, reason));
            else { otherSuspensions++; otherSuspensionMilliseconds += time - began; }
            suspended = null;
        }
        else if (ev.EventName == "GC/Start")
            collections.Add(new { Time = time, Payload = ev.PayloadNames.ToDictionary(n => n, n => ev.PayloadByName(n)?.ToString()) });
        else if (ev.EventName.Contains("AllocationTick"))
        {
            long bytes = Convert.ToInt64(ev.PayloadByName("AllocationAmount64"));
            string type = ev.PayloadByName("TypeName")?.ToString() ?? "unknown";
            allocationBytes += bytes;
            allocationTicks++;
            AddAllocation(allocations, type, bytes);
            var stack = Stack(ev);
            string caller = stack.FirstOrDefault(n => n.Contains("Lumyte.")) ?? "[no Lumyte frame]";
            AddAllocation(callers, caller, bytes);
        }
    }
    else if (ev.ProviderName == "Microsoft-DotNETCore-SampleProfiler")
    {
        sampleCount++;
        string kind = string.Join(";", ev.PayloadNames.Select(n => n + "=" + ev.PayloadByName(n)));
        sampleKinds[kind] = sampleKinds.GetValueOrDefault(kind) + 1;
        var stack = Stack(ev);
        if (stack.Count == 0) stackless++;
        string leaf = stack.FirstOrDefault(n => n.Contains("Lumyte.")) ?? "[no Lumyte frame]";
        if (ev.ThreadID != markers[0].Thread) continue;
        if (kind != "Type=Managed")
        {
            external[leaf] = external.GetValueOrDefault(leaf) + 1;
            continue;
        }
        samples[leaf] = samples.GetValueOrDefault(leaf) + 1;
        foreach (string name in stack.Where(n => n.Contains("Lumyte.")).Distinct())
            inclusive[name] = inclusive.GetValueOrDefault(name) + 1;
    }
}
var framePauses = markers.OrderBy(m => m.Index).Zip(markers.OrderBy(m => m.Index).Skip(1)).Select(pair =>
{
    double pause = pauses.Sum(p => Math.Max(0, Math.Min(pair.Second.Time, p.End) - Math.Max(pair.First.Time, p.Start)));
    return new { Frame = pair.Second.Index, Start = pair.First.Time, End = pair.Second.Time, IntervalMilliseconds = pair.Second.Time - pair.First.Time, GcPauseMilliseconds = pause };
}).ToArray();
var result = new
{
    Trace = Path.GetFileName(input), log.EventsLost, StartMilliseconds = start, EndMilliseconds = end,
    OwnerThread = markers[0].Thread, MarkerCount = markers.Count, SampleCount = sampleCount, StacklessSamples = stackless,
    SampleKinds = sampleKinds, EstimatedAllocationBytes = allocationBytes, AllocationTickCount = allocationTicks,
    AllocatedTypes = allocations.OrderByDescending(p => p.Value.Bytes).Take(40).Select(p => new { p.Key, p.Value.Bytes, p.Value.Count }).ToArray(),
    AllocationCallers = callers.OrderByDescending(p => p.Value.Bytes).Take(40).Select(p => new { p.Key, p.Value.Bytes, p.Value.Count }).ToArray(),
    NearestLumyteSampleFrames = samples.OrderByDescending(p => p.Value).Take(40).ToArray(),
    InclusiveLumyteSampleFrames = inclusive.OrderByDescending(p => p.Value).Take(60).ToArray(),
    ExternalOwnerSampleFrames = external.OrderByDescending(p => p.Value).Take(30).ToArray(),
    GcSuspensions = pauses, Collections = collections,
    OtherSuspensions = new { Count = otherSuspensions, Milliseconds = otherSuspensionMilliseconds },
    SlowFrames = framePauses.OrderByDescending(f => f.IntervalMilliseconds).Take(30).ToArray(),
    GcAssociatedFrames = framePauses.Where(f => f.GcPauseMilliseconds > 0).ToArray(),
    Inventory = inventory, PayloadExamples = examples,
};
File.WriteAllText(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"frames={markers.Count - 1}, samples={sampleCount}, allocation ticks={allocationTicks}, lost={log.EventsLost}, suspensions={pauses.Count}");

static List<string> Stack(TraceEvent ev)
{
    var frames = new List<string>();
    for (TraceCallStack? stack = ev.CallStack(); stack is not null && frames.Count < 160; stack = stack.Caller)
        frames.Add(stack.CodeAddress.FullMethodName);
    return frames;
}

static void AddAllocation(Dictionary<string, Allocation> map, string key, long bytes)
{
    Allocation value = map.GetValueOrDefault(key);
    value.Bytes += bytes;
    value.Count++;
    map[key] = value;
}

internal readonly record struct Marker(long Index, double Time, int Thread);
internal readonly record struct Pause(double Start, double End, string Reason);
internal struct Allocation { public long Bytes; public int Count; }
