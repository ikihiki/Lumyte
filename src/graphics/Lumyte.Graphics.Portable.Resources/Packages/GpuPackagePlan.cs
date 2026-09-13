using System.Collections.ObjectModel;

namespace Lumyte.Graphics.Portable.Resources;

/// <summary>Prepared CPU resources and explicit local dependency identities. Does not perform asset or file loading.</summary>
public sealed class GpuPackagePlan
{
    public GpuPackagePlan(IEnumerable<GpuPackageResource> resources, IEnumerable<GpuPackageExport> exports)
    {
        ArgumentNullException.ThrowIfNull(resources); ArgumentNullException.ThrowIfNull(exports);
        GpuPackageResource[] items = resources.ToArray();
        GpuPackageExport[] names = exports.ToArray();
        var ids = new Dictionary<string, GpuPackageResource>(StringComparer.Ordinal);
        foreach (GpuPackageResource item in items)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (!ids.TryAdd(item.Id, item)) { throw new ArgumentException($"Duplicate resource id '{item.Id}'.", nameof(resources)); }
        }
        var exportIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (GpuPackageExport export in names)
        {
            ArgumentNullException.ThrowIfNull(export);
            if (string.IsNullOrWhiteSpace(export.Id) || !exportIds.Add(export.Id) || !ids.ContainsKey(export.ResourceId))
            { throw new ArgumentException($"Export '{export.Id}' must have a unique name and reference an existing resource.", nameof(exports)); }
        }
        var active = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        void Visit(GpuPackageResource resource)
        {
            if (visited.Contains(resource.Id)) { return; }
            if (!active.Add(resource.Id)) { throw new ArgumentException("Package resource dependencies contain a cycle.", nameof(resources)); }
            foreach (string dependency in resource.Dependencies)
            {
                if (!ids.TryGetValue(dependency, out GpuPackageResource? target))
                { throw new ArgumentException($"Missing dependency '{dependency}' of resource '{resource.Id}'.", nameof(resources)); }
                Visit(target);
            }
            active.Remove(resource.Id); visited.Add(resource.Id);
        }
        foreach (GpuPackageResource item in items) { Visit(item); }
        Resources = Array.AsReadOnly(items); Exports = Array.AsReadOnly(names);
    }
    public IReadOnlyList<GpuPackageResource> Resources { get; }
    public IReadOnlyList<GpuPackageExport> Exports { get; }
}

public abstract class GpuPackageResource
{
    private protected GpuPackageResource(string id, IEnumerable<string>? dependencies)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id; Dependencies = Array.AsReadOnly((dependencies ?? []).Distinct(StringComparer.Ordinal).ToArray());
    }
    public string Id { get; }
    public IReadOnlyList<string> Dependencies { get; }
}

public sealed class GpuPackageBuffer : GpuPackageResource
{
    private readonly byte[] data;
    public GpuPackageBuffer(string id, GpuBufferDescription description, ReadOnlySpan<byte> data = default,
        ulong destinationOffset = 0, IEnumerable<string>? dependencies = null) : base(id, dependencies)
    {
        if (destinationOffset > description.Size || (ulong)data.Length > description.Size - destinationOffset)
        { throw new ArgumentOutOfRangeException(nameof(data), "The prepared bytes exceed the described buffer range."); }
        Description = description; this.data = data.ToArray(); DestinationOffset = destinationOffset;
    }
    public GpuBufferDescription Description { get; }
    public ReadOnlySpan<byte> Data => data;
    public ulong DestinationOffset { get; }
}

public sealed class GpuPackageTexture : GpuPackageResource
{
    public GpuPackageTexture(string id, GpuTextureDescription description,
        IEnumerable<GpuTextureUpload>? uploads = null, IEnumerable<string>? dependencies = null) : base(id, dependencies)
    {
        Description = description;
        GpuTextureUpload[] copied = (uploads ?? []).ToArray();
        foreach (GpuTextureUpload upload in copied)
        {
            ArgumentNullException.ThrowIfNull(upload);
            if (upload.Footprint.RequiredBytes(description.Format) > (ulong)upload.Data.Length)
            { throw new ArgumentException("Texture upload data does not cover its CPU footprint.", nameof(uploads)); }
        }
        Uploads = Array.AsReadOnly(copied);
    }
    public GpuTextureDescription Description { get; }
    public IReadOnlyList<GpuTextureUpload> Uploads { get; }
}

public sealed record GpuPackageExport(string Id, string ResourceId);

/// <summary>Owned prepared transfer bytes and buffer row/image pitches; no GPU allocation layout is implied.</summary>
public sealed class GpuTextureUpload
{
    private readonly byte[] data;
    public GpuTextureUpload(ReadOnlySpan<byte> data, GpuTextureCopyFootprint footprint)
    { this.data = data.ToArray(); Footprint = footprint; }
    public ReadOnlySpan<byte> Data => data;
    public GpuTextureCopyFootprint Footprint { get; }
}
