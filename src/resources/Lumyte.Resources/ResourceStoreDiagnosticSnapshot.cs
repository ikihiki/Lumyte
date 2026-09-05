namespace Lumyte.Resources;
public enum ResourceDiagnosticState { Loading, Loaded, Faulted }
public sealed record ResourceStoreDiagnosticSnapshot(IReadOnlyList<ResourceDiagnosticEntry> Resources);
public sealed record ResourceDiagnosticEntry(uint Id,string Key,string ResourceType,uint Generation,ResourceDiagnosticState State,int ReferenceCount,IReadOnlyList<ResourceMemoryCost> MemoryCosts,IReadOnlyList<uint> Dependencies,string? Error);
