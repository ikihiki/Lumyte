using Lumyte.Graphics.Portable.Resources;
using Lumyte.Graphics.Portable.Shaders;

namespace Lumyte.Graphics.Portable.RenderGraph;

public enum PortablePassUsage
{
    SampledRead, UniformRead, StorageRead, StorageWrite, ColorAttachment, DepthStencilAttachment,
    CopySource, CopyDestination, IndexRead, IndirectRead,
}

public abstract class PortablePassResource
{
    internal PortablePassResource(PortableExecutionBuild owner, string name) { Owner = owner; Name = name; }
    internal PortableExecutionBuild Owner { get; }
    public string Name { get; }
}
public sealed class PortablePassTexture : PortablePassResource
{
    internal PortablePassTexture(PortableExecutionBuild owner, string name, GpuTextureDescription description, GpuTextureRef? reference = null)
        : base(owner, name) { Description = description; Reference = reference; }
    public GpuTextureDescription Description { get; internal set; }
    internal GpuTextureRef? Reference { get; set; }
}
public sealed class PortablePassBuffer : PortablePassResource
{
    internal PortablePassBuffer(PortableExecutionBuild owner, string name, GpuBufferDescription description, GpuBufferRef? reference = null)
        : base(owner, name) { Description = description; Reference = reference; }
    public GpuBufferDescription Description { get; internal set; }
    internal GpuBufferRef? Reference { get; set; }
}
public sealed class PortablePassView
{
    internal PortablePassView(PortablePassTexture texture, GpuTextureViewDescription description)
    { Texture = texture; Description = description; }
    public PortablePassTexture Texture { get; }
    public GpuTextureViewDescription Description { get; }
    internal GpuViewRef? Reference { get; set; }
}
public sealed class PortablePassBindings
{
    internal PortablePassBindings(PortableExecutionBuild owner, PortableShaderProgram program, uint group,
        IReadOnlyList<Action<GpuBindingWriter>> entries)
    { Owner = owner; Program = program; Group = group; Entries = entries; }
    internal PortableExecutionBuild Owner { get; }
    internal PortableShaderProgram Program { get; }
    internal uint Group { get; }
    internal IReadOnlyList<Action<GpuBindingWriter>> Entries { get; }
    internal GpuBindingsRef? Reference { get; set; }
}

/// <summary>Declares immutable bindings in terms of internal graph resources.</summary>
public interface IPortablePassBindingInputs { void Write(PortablePassBindingWriter writer); }

public sealed class PortablePassBindingWriter
{
    private readonly PortableExecutionBuild owner;
    private readonly List<Action<GpuBindingWriter>> entries = [];
    private bool ended;
    internal PortablePassBindingWriter(PortableExecutionBuild owner) { this.owner = owner; }
    public void Texture(uint binding, PortablePassView view)
    {
        ArgumentNullException.ThrowIfNull(view); Check(view.Texture);
        entries.Add(writer => writer.Texture(binding, view.Reference!));
    }
    public void Buffer(uint binding, PortablePassBuffer buffer, ulong offset = 0, ulong length = ulong.MaxValue)
    { Check(buffer); entries.Add(writer => writer.Buffer(binding, buffer.Reference!, offset, length)); }
    private void Check(PortablePassResource resource)
    {
        ObjectDisposedException.ThrowIf(ended, this);
        if (!ReferenceEquals(resource.Owner, owner)) { throw new ArgumentException("The resource belongs to another execution.", nameof(resource)); }
    }
    internal IReadOnlyList<Action<GpuBindingWriter>> Finish() { ended = true; return entries.ToArray(); }
}
