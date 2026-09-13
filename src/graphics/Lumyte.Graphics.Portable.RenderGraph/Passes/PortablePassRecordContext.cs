namespace Lumyte.Graphics.Portable.RenderGraph;

/// <summary>Prepared objects borrowed only for the current synchronous recording callback.</summary>
public sealed class PortablePassRecordContext
{
    private readonly PortableExecutionBuild build;
    private readonly PortablePassBuilder pass;
    private readonly GpuCommandBuffer commands;
    private bool ended;
    internal PortablePassRecordContext(PortableExecutionBuild build, PortablePassBuilder pass, GpuCommandBuffer commands)
    { this.build = build; this.pass = pass; this.commands = commands; }
    internal void End() => ended = true;
    private void Check() => ObjectDisposedException.ThrowIf(ended, this);
    public GpuCommandBuffer Commands { get { Check(); return commands; } }
    private void Require(PortablePassResource resource)
    {
        Check(); build.Check(resource);
        if (!pass.Uses.ContainsKey(resource)) { throw new ArgumentException($"Pass '{pass.Name}' did not declare '{resource.Name}'.", nameof(resource)); }
    }
    public GpuTextureHandle GetTexture(PortablePassTexture texture)
    { Require(texture); return build.Services.Resources.GetTextureHandle(texture.Reference!); }
    public GpuBufferRange GetBufferRange(PortablePassBuffer buffer, ulong offset = 0, ulong? length = null)
    { Require(buffer); return build.Services.Resources.GetBufferRange(buffer.Reference!, offset, length); }
    public GpuTextureView GetTextureView(PortablePassView view)
    { ArgumentNullException.ThrowIfNull(view); Require(view.Texture); return build.Services.Resources.GetTextureView(view.Reference!); }
    public GpuBindingsHandle GetBindings(PortablePassBindings bindings)
    {
        Check(); ArgumentNullException.ThrowIfNull(bindings);
        if (!ReferenceEquals(bindings.Owner, build)) { throw new ArgumentException("The bindings belong to another execution.", nameof(bindings)); }
        foreach (PortablePassResource resource in bindings.Resources) { Require(resource); }
        return build.Services.Resources.GetBindingsHandle(bindings.Reference!);
    }
}
