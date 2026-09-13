namespace Lumyte.Graphics.Portable.Resources;

/// <summary>Typed managed inputs, normally emitted by the Portable resource source generator.</summary>
public interface IGpuBindingInputs { void Write(GpuBindingWriter writer); }

/// <summary>Resolves managed inputs against one prepared program group during GetBindings.</summary>
public sealed class GpuBindingWriter
{
    private readonly GpuResourceManager manager;
    private bool ended;
    private readonly List<ManagedBindingEntry> entries = [];
    internal GpuBindingWriter(GpuResourceManager manager) { this.manager = manager; }
    public void Buffer(uint binding, GpuBufferRef buffer, ulong offset = 0, ulong length = ulong.MaxValue)
    {
        ResourceRecord record = manager.Check(buffer);
        GpuBufferRange range = manager.GetBufferRange(buffer, offset, length == ulong.MaxValue ? null : length);
        Add(record, GpuBindingEntry.Buffer(binding, range));
    }
    public void Texture(uint binding, GpuViewRef view)
    {
        ResourceRecord record = manager.Check(view);
        Add(record, GpuBindingEntry.Texture(binding, view.View));
    }
    public void Sampler(uint binding, GpuSamplerRef sampler)
    {
        ResourceRecord record = manager.Check(sampler);
        Add(record, GpuBindingEntry.Sampler(binding, sampler.Description));
    }
    private void Add(ResourceRecord record, GpuBindingEntry entry)
    {
        if (ended) { throw new InvalidOperationException("The binding writer is no longer active."); }
        entries.Add(new(record, entry));
    }
    internal ManagedBindingEntry[] Finish()
    {
        ended = true;
        return entries.OrderBy(item => item.Entry.Binding).ToArray();
    }
}

internal readonly record struct ManagedBindingEntry(ResourceRecord Resource, GpuBindingEntry Entry);

internal sealed class BindingCacheKey(GpuBindingLayoutHandle layout, ManagedBindingEntry[] entries) : IEquatable<BindingCacheKey>
{
    internal GpuBindingLayoutHandle Layout { get; } = layout;
    internal ManagedBindingEntry[] Entries { get; } = entries;
    public bool Equals(BindingCacheKey? other) => other is not null && ReferenceEquals(Layout, other.Layout)
        && Entries.AsSpan().SequenceEqual(other.Entries);
    public override bool Equals(object? obj) => obj is BindingCacheKey other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode(); hash.Add(Layout, ReferenceEqualityComparer.Instance);
        foreach (ManagedBindingEntry entry in Entries) { hash.Add(entry); }
        return hash.ToHashCode();
    }
}
