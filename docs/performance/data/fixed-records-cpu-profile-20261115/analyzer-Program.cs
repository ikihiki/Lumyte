using System.Text.Json;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
string input=args[0]; string etlx=Path.ChangeExtension(input,".etlx");
if(!File.Exists(etlx)) TraceLog.CreateFromEventPipeDataFile(input,etlx,new TraceLogOptions{KeepAllEvents=true});
using var log=new TraceLog(etlx);
var inventory=new Dictionary<string,int>(); var examples=new Dictionary<string,object>();
var windows=new List<(double Start,double End,string Kind)>(); double? start=null;string kind="";
foreach(var e in log.Events){string key=e.ProviderName+"/"+e.EventName;inventory[key]=inventory.GetValueOrDefault(key)+1;
if(e.ProviderName.Contains("EngineEventSource")){if(!examples.ContainsKey(key))examples[key]=e.PayloadNames.ToDictionary(n=>n,n=>e.PayloadByName(n)?.ToString());
if(e.EventName.Contains("WorkloadActual")||e.EventName.Contains("WorkloadExtra")){if(e.EventName.EndsWith("Start")){start=e.TimeStampRelativeMSec;kind=e.EventName;}else if(e.EventName.EndsWith("Stop")&&start is double s){windows.Add((s,e.TimeStampRelativeMSec,kind));start=null;}}}
if(e.ProviderName.StartsWith("Lumyte")&&!examples.ContainsKey(key))examples[key]=e.PayloadNames.ToDictionary(n=>n,n=>e.PayloadByName(n)?.ToString());}
var begins=new Dictionary<string,(double Time,string Parent,string Stage,int Thread,string Frame,string Run)>();
var spans=new Dictionary<string,(double Start,double End,string Parent,string Stage,int Thread,string Frame,string Run)>();
var counterRows=new List<(string Key,string Parent,string Stage,string Id,long Value)>();int unmatchedEnds=0,mismatches=0,duplicateBegins=0;
foreach(var e in log.Events){if(!e.ProviderName.StartsWith("Lumyte")||e.EventName is not ("StageBegin" or "StageEnd" or "StageCounter"))continue;
string P(string n)=>e.PayloadByName(n)?.ToString()??"";
string run=P("profileRunId"),frame=P("frameId"),stage=P("stage"),parent=P("parentSpanId"),key=e.ProviderName+"/"+run+"/"+frame+"/"+P("spanId");
if(e.EventName=="StageBegin"){if(!begins.TryAdd(key,(e.TimeStampRelativeMSec,parent,stage,e.ThreadID,frame,run)))duplicateBegins++;}
else if(e.EventName=="StageCounter")counterRows.Add((key,parent,stage,P("counterId"),Convert.ToInt64(e.PayloadByName("value"))));
else if(!begins.Remove(key,out var b))unmatchedEnds++;else if(b.Parent!=parent||b.Stage!=stage)mismatches++;else spans[key]=(b.Time,e.TimeStampRelativeMSec,parent,stage,b.Thread,frame,run);
}
int joined=0,missing=0,counterMismatch=0,unassociated=0,parentMissing=0;
var counters=new Dictionary<string,long>();
foreach(var c in counterRows){if(!spans.TryGetValue(c.Key,out var b)){missing++;continue;}if(b.Run==Guid.Empty.ToString()||b.Frame=="0"){unassociated++;continue;}if(c.Parent!=b.Parent||c.Stage!=b.Stage){counterMismatch++;continue;}joined++;if(windows.Any(w=>b.Start>=w.Start&&b.End<=w.End))counters[c.Stage+"/"+c.Id]=counters.GetValueOrDefault(c.Stage+"/"+c.Id)+c.Value;}
foreach(var p in spans){if(p.Value.Parent!="0"){string parent=p.Key[..(p.Key.LastIndexOf('/')+1)]+p.Value.Parent;if(!spans.ContainsKey(parent))parentMissing++;}}
var selected=spans.Values.Where(b=>b.Run!=Guid.Empty.ToString()&&long.TryParse(b.Frame,out long f)&&f>0&&windows.Any(w=>b.Start>=w.Start&&b.End<=w.End)).ToArray();
var stageStats=selected.GroupBy(b=>b.Stage).Select(g=>new{Stage=g.Key,Count=g.Count(),MeanWallMs=g.Average(b=>b.End-b.Start),TotalInclusiveWallMs=g.Sum(b=>b.End-b.Start)}).ToArray();
var stageSamples=new Dictionary<string,long>();
var managedSelf=new Dictionary<string,long>();var managedNearest=new Dictionary<string,long>();var managedInclusive=new Dictionary<string,long>();var managedGroups=new Dictionary<string,long>();var self=new Dictionary<string,long>();var inclusive=new Dictionary<string,long>();var nearest=new Dictionary<string,long>();var stacks=new Dictionary<string,long>();var kinds=new Dictionary<string,long>();var types=new Dictionary<string,long>();var callers=new Dictionary<string,long>();
long samples=0,allocTicks=0,bytes=0;var gc=new List<object>();
foreach(var e in log.Events){if(!windows.Any(w=>e.TimeStampRelativeMSec>=w.Start&&e.TimeStampRelativeMSec<=w.End))continue;
var stack=new List<string>();for(var s=e.CallStack();s!=null&&stack.Count<160;s=s.Caller)stack.Add(s.CodeAddress.FullMethodName);
void Add(Dictionary<string,long> d,string k,long n=1)=>d[k]=d.GetValueOrDefault(k)+n;
if(e.ProviderName=="Microsoft-DotNETCore-SampleProfiler"){samples++;if(e.PayloadByName("Type")?.ToString()=="Managed"){Add(managedSelf,stack.FirstOrDefault()??"[stackless]");Add(managedNearest,stack.FirstOrDefault(n=>n.Contains("Lumyte."))??"[no Lumyte frame]");foreach(string n in stack.Distinct())Add(managedInclusive,n);string group=stack.Any(n=>n.Contains("NormalizedClip2D.Create")||n.Contains("AccumulateWinding"))?"clip normalization/winding":stack.Any(n=>n.Contains("CanAdmit"))?"capacity admission":stack.Any(n=>n.Contains("TrySharedClip")||n.Contains("DrawRecordPage2D.Clip("))?"clip stream dedup/packing":stack.Any(n=>n.Contains("OwnedBytes"))?"retained owner accounting":stack.Any(n=>n.Contains("DrawingProgram2D"))?"intermediate program compiler":stack.Any(n=>n.Contains("PreparedRecordBatch2D"))?"other fixed-record preparation/record":stack.Any(n=>n.Contains("VulkanSubmission")||n.Contains("VulkanCompletionDispatcher"))?"submission/completion":stack.Any(n=>n.Contains("Lumyte.Graphics.Vulkan"))?"other Vulkan managed":stack.Any(n=>n.Contains("DrawingProfiling")||n.Contains("DrawingProfileScope"))?"profiling overhead":"other";Add(managedGroups,group);}var active=selected.Where(b=>b.Thread==e.ThreadID&&e.TimeStampRelativeMSec>=b.Start&&e.TimeStampRelativeMSec<=b.End).OrderBy(b=>b.End-b.Start).ToArray();Add(stageSamples,active.Length>0?active[0].Stage:"[no same-thread span; not guessed]");Add(kinds,string.Join(";",e.PayloadNames.Select(n=>n+"="+e.PayloadByName(n))));Add(self,stack.FirstOrDefault()??"[stackless]");Add(nearest,stack.FirstOrDefault(n=>n.Contains("Lumyte."))??"[no Lumyte frame]");foreach(string n in stack.Distinct())Add(inclusive,n);Add(stacks,string.Join(";",stack.AsEnumerable().Reverse()));}
if(e.ProviderName=="Microsoft-Windows-DotNETRuntime"&&e.EventName.Contains("AllocationTick")){allocTicks++;long b=Convert.ToInt64(e.PayloadByName("AllocationAmount64"));bytes+=b;Add(types,e.PayloadByName("TypeName")?.ToString()??"unknown",b);Add(callers,string.Join(";",stack),b);}
if(e.ProviderName=="Microsoft-Windows-DotNETRuntime"&&(e.EventName=="GC/Start"||e.EventName.Contains("SuspendEE")||e.EventName.Contains("RestartEE")))gc.Add(new{e.EventName,Time=e.TimeStampRelativeMSec,Payload=e.PayloadNames.ToDictionary(n=>n,n=>e.PayloadByName(n)?.ToString())});}
object Top(Dictionary<string,long>d,int n=40)=>d.OrderByDescending(p=>p.Value).Take(n).Select(p=>new{Name=p.Key,Count=p.Value}).ToArray();
File.WriteAllText(args[1],JsonSerializer.Serialize(new{Trace=Path.GetFileName(input),log.EventsLost,Windows=windows.Select(w=>new{w.Start,w.End,w.Kind}),samples,allocTicks,EstimatedAllocationBytes=bytes,SampleKinds=kinds,ManagedSelf=Top(managedSelf),ManagedNearest=Top(managedNearest),ManagedInclusive=Top(managedInclusive),ManagedGroups=managedGroups,StageSamples=stageSamples,StageStats=stageStats,Counters=counters,Joins=new{Paired=spans.Count,UnmatchedBegins=begins.Count,unmatchedEnds,mismatches,duplicateBegins,joined,missing,counterMismatch,unassociated,parentMissing,ActualFrames=selected.Select(b=>b.Run+"/"+b.Frame).Distinct().Count()},Self=Top(self),Inclusive=Top(inclusive),NearestLumyte=Top(nearest),CallTree=Top(stacks,80),AllocationTypes=Top(types),AllocationStacks=Top(callers),GC=gc,Inventory=inventory,Examples=examples},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"samples={samples} windows={windows.Count} lost={log.EventsLost}");
