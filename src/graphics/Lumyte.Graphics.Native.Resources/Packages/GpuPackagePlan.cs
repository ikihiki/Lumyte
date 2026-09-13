using System.Collections.ObjectModel;

namespace Lumyte.Graphics.Native.Resources;

public enum GpuPackagePlacement { Pools, SingleAllocation }
public sealed record GpuPackageExport(string Id, string ResourceId);

public abstract class GpuPackageResource
{
    private protected GpuPackageResource(string id, IEnumerable<string>? dependencies, string allocationGroup)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(allocationGroup);
        Id = id; AllocationGroup = allocationGroup;
        Dependencies = Array.AsReadOnly(dependencies?.ToArray() ?? []);
    }
    public string Id { get; }
    public string AllocationGroup { get; }
    public IReadOnlyList<string> Dependencies { get; }
}

public sealed class GpuPackageBuffer : GpuPackageResource
{
    private readonly byte[] data;
    public GpuPackageBuffer(string id, GpuBufferDescription description, ReadOnlySpan<byte> data = default,
        ulong destinationOffset = 0, IEnumerable<string>? dependencies = null, string allocationGroup = "default")
        : base(id, dependencies, allocationGroup)
    {
        if (destinationOffset > description.Size || (ulong)data.Length > description.Size - destinationOffset)
        { throw new ArgumentOutOfRangeException(nameof(destinationOffset)); }
        Description = description; DestinationOffset = destinationOffset; this.data = data.ToArray();
    }
    public GpuBufferDescription Description { get; }
    public ulong DestinationOffset { get; }
    public ReadOnlySpan<byte> Data => data;
}

public sealed class GpuPackageTexture : GpuPackageResource
{
    public GpuPackageTexture(string id, NativeGpuTextureDescription description,
        IEnumerable<GpuTextureUpload>? uploads = null, IEnumerable<string>? dependencies = null,
        NativeGpuMemoryKind memoryKind = NativeGpuMemoryKind.GpuOnly,
        GpuTextureViewDescription? defaultView = null, string allocationGroup = "default")
        : base(id, dependencies, allocationGroup)
    {
        Description = description; MemoryKind = memoryKind; DefaultView = defaultView;
        Uploads = Array.AsReadOnly(uploads?.ToArray() ?? []);
        if (Uploads.Any(upload => upload is null)) { throw new ArgumentException("An upload cannot be null.", nameof(uploads)); }
    }
    public NativeGpuTextureDescription Description { get; }
    public NativeGpuMemoryKind MemoryKind { get; }
    public GpuTextureViewDescription? DefaultView { get; }
    public IReadOnlyList<GpuTextureUpload> Uploads { get; }
}

/// <summary>Prepared bytes with explicit CPU block-row layout and Native texture state transitions.</summary>
public sealed class GpuTextureUpload
{
    private readonly byte[] data;
    public GpuTextureUpload(ReadOnlySpan<byte> data, NativeGpuTextureCopyFootprint footprint,
        uint rowCount, ulong rowBytes, GpuTextureLayout beforeLayout = GpuTextureLayout.Undefined,
        GpuTextureLayout afterLayout = GpuTextureLayout.General, ulong stagingAlignment = 512)
    {
        if (rowCount == 0 || rowBytes == 0 || stagingAlignment == 0) { throw new ArgumentOutOfRangeException(nameof(rowCount)); }
        if (rowBytes > footprint.RowPitch) { throw new ArgumentException("CPU row bytes exceed row pitch.", nameof(rowBytes)); }
        ulong imageBytes = checked((rowCount - 1ul) * footprint.RowPitch + rowBytes);
        uint images = Math.Max(footprint.LayerCount, footprint.Extent.Depth);
        if (images == 0 || images > 1 && footprint.ImagePitch < imageBytes)
        { throw new ArgumentException("CPU image pitch does not cover an image.", nameof(footprint)); }
        ulong required = checked((images - 1ul) * footprint.ImagePitch + imageBytes);
        if (required > (ulong)data.Length) { throw new ArgumentException("CPU upload data does not cover the footprint.", nameof(data)); }
        this.data = data.ToArray(); Footprint = footprint; BeforeLayout = beforeLayout;
        AfterLayout = afterLayout; StagingAlignment = stagingAlignment;
    }
    public ReadOnlySpan<byte> Data => data;
    public NativeGpuTextureCopyFootprint Footprint { get; }
    public GpuTextureLayout BeforeLayout { get; }
    public GpuTextureLayout AfterLayout { get; }
    public ulong StagingAlignment { get; }
}

public sealed class GpuPackagePlan
{
    public GpuPackagePlan(IEnumerable<GpuPackageResource> resources, IEnumerable<GpuPackageExport> exports)
    {
        ArgumentNullException.ThrowIfNull(resources); ArgumentNullException.ThrowIfNull(exports);
        GpuPackageResource[] items = resources.ToArray();
        Dictionary<string, GpuPackageResource> names = new(StringComparer.Ordinal);
        foreach (GpuPackageResource item in items)
        {
            if (item is null || !names.TryAdd(item.Id, item)) { throw new ArgumentException("Resource IDs must be unique and non-null.", nameof(resources)); }
        }
        foreach (GpuPackageResource item in items)
        {
            foreach (string dependency in item.Dependencies)
            { if (dependency is null || !names.ContainsKey(dependency)) { throw new ArgumentException("A dependency resource is missing.", nameof(resources)); } }
        }
        HashSet<string> visiting = [], visited = [];
        void Visit(string id)
        {
            if (visited.Contains(id)) { return; }
            if (!visiting.Add(id)) { throw new ArgumentException("Package dependencies contain a cycle.", nameof(resources)); }
            foreach (string dependency in names[id].Dependencies) { Visit(dependency); }
            visiting.Remove(id); visited.Add(id);
        }
        foreach (string id in names.Keys) { Visit(id); }
        Dictionary<string, string> exportNames = new(StringComparer.Ordinal);
        foreach (GpuPackageExport export in exports)
        {
            if (export is null || string.IsNullOrWhiteSpace(export.Id) || !names.ContainsKey(export.ResourceId) || !exportNames.TryAdd(export.Id, export.ResourceId))
            { throw new ArgumentException("Exports must have unique IDs and refer to package resources.", nameof(exports)); }
        }
        Resources = Array.AsReadOnly(items); Exports = new ReadOnlyDictionary<string, string>(exportNames);
    }
    public IReadOnlyList<GpuPackageResource> Resources { get; }
    public IReadOnlyDictionary<string, string> Exports { get; }
}
