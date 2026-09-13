using Lumyte.Graphics.Native.Resources;
using Lumyte.Graphics.RenderGraph;
using NativeBufferDescription = Lumyte.Graphics.Native.Resources.GpuBufferDescription;
using NativeTextureViewDescription = Lumyte.Graphics.Native.Resources.GpuTextureViewDescription;

namespace Lumyte.Graphics.Native.RenderGraph;

/// <summary>One feature build. Its references and builders belong to this execution only.</summary>
public sealed class NativePassBuildContext
{
    private readonly NativeExecutionBuild build;
    internal GpuRenderGraphPass Declaration { get; }
    internal readonly List<NativePassBuilder> Passes = [];
    private readonly HashSet<NativePassResource> allowed = [];
    private readonly HashSet<string> resourceNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> viewNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> passNames = new(StringComparer.Ordinal);
    private bool closed;
    internal NativePassBuildContext(NativeExecutionBuild build, GpuRenderGraphPass pass)
    { this.build = build; Declaration = pass; build.Features.Add(this); }
    public NativePassServices Services { get { RequireOpen(); return build.Services; } }
    public T GetInput<T>(GpuGraphValue<T> value) { RequireOpen(); return Declaration.GetInput(value, build.Bindings); }
    public NativePassTexture ImportTexture(GpuRenderGraphTexture resource)
    { RequireDeclared(resource); var value = (NativePassTexture)build.Resources[resource]; allowed.Add(value); return value; }
    public NativePassBuffer ImportBuffer(GpuRenderGraphBuffer resource)
    { RequireDeclared(resource); var value = (NativePassBuffer)build.Resources[resource]; allowed.Add(value); return value; }
    public NativePassTexture ImportTexture(GpuTextureRef reference, NativeGpuTextureDescription description)
    {
        RequireOpen(); ArgumentNullException.ThrowIfNull(reference);
        if (build.Imported.TryGetValue(reference, out NativePassResource? existing))
        {
            RequireExternalDeclaration(existing);
            if (existing is not NativePassTexture texture || texture.Description != description)
            { throw new ArgumentException("An imported resource must retain its original description.", nameof(description)); }
            allowed.Add(texture); return texture;
        }
        IDisposable hold = Services.Resources.AcquireUse(reference);
        var result = new NativePassTexture(build, Declaration.Name + "/imported-texture", description, reference);
        build.PrivateResources.Add(result); build.Imported.Add(reference, result); build.Leases.Add(hold); allowed.Add(result); return result;
    }
    public NativePassBuffer ImportBuffer(GpuBufferRef reference)
    {
        RequireOpen(); ArgumentNullException.ThrowIfNull(reference);
        if (build.Imported.TryGetValue(reference, out NativePassResource? existing))
        { RequireExternalDeclaration(existing); allowed.Add(existing); return (NativePassBuffer)existing; }
        IDisposable hold = Services.Resources.AcquireUse(reference);
        var result = new NativePassBuffer(build, Declaration.Name + "/imported-buffer", new(Services.Resources.GetBufferRange(reference).Size), reference);
        build.PrivateResources.Add(result); build.Imported.Add(reference, result); build.Leases.Add(hold); allowed.Add(result); return result;
    }
    public NativePassTexture CreateTexture(string name, NativeGpuTextureDescription description)
    {
        RequireName(name, resourceNames);
        var texture = new NativePassTexture(build, Declaration.Name + "/" + name, description);
        build.PrivateResources.Add(texture); allowed.Add(texture); return texture;
    }
    public NativePassBuffer CreateBuffer(string name, NativeBufferDescription description)
    {
        RequireName(name, resourceNames);
        var buffer = new NativePassBuffer(build, Declaration.Name + "/" + name, description);
        build.PrivateResources.Add(buffer); allowed.Add(buffer); return buffer;
    }
    public NativePassView CreateView(string name, NativePassTexture texture, NativeTextureViewDescription description)
    {
        RequireResource(texture); RequireName(name, viewNames);
        var view = new NativePassView(Declaration.Name + "/" + name, texture, description); build.Views.Add(view); return view;
    }
    public NativePassBuilder AddPass<TState>(string name, TState state, NativePassRecordAction<TState> record)
    {
        RequireOpen(); ArgumentNullException.ThrowIfNull(record); RequireName(name, passNames);
        var builder = new NativePassBuilder(this, Declaration.Name + "/" + name, context => record(context, state));
        build.Passes.Add(builder); Passes.Add(builder); return builder;
    }
    public void Instantiate<TState>(NativePassTemplate<TState> template, TState state)
    { RequireOpen(); ArgumentNullException.ThrowIfNull(template); template.Build(this, state); }
    public void Retain(IDisposable lease)
    {
        RequireOpen(); ArgumentNullException.ThrowIfNull(lease);
        if (build.Leases.Any(existing => ReferenceEquals(existing, lease)))
        { throw new ArgumentException("This execution already owns the lease.", nameof(lease)); }
        build.Leases.Add(lease);
    }
    public NativePassContentGeneration<TContent> RegisterContent<TContent>(TContent content, IDisposable lease,
        params NativePassBuilder[] writers)
    {
        RequireOpen(); ArgumentNullException.ThrowIfNull(lease); ArgumentNullException.ThrowIfNull(writers);
        if (writers.Length == 0 || writers.Distinct().Count() != writers.Length
            || writers.Any(writer => writer is null || !ReferenceEquals(writer.Feature, this)))
        { throw new ArgumentException("Provide distinct writers registered by this feature build.", nameof(writers)); }
        if (writers.Any(writer => !writer.Accesses.Values.Any(access => access != GpuRenderGraphAccess.Read)))
        { throw new ArgumentException("Every content writer must declare a resource write.", nameof(writers)); }
        NativeContentEntry entry = build.Content.Create(content, lease, build, writers.ToArray());
        build.Registered.Add(entry); build.Leases.Add(entry.Acquire());
        return new(entry);
    }
    public NativePassContentGeneration<TContent> RegisterContent<TContent>(TContent content, IDisposable lease, GpuSubmissionToken submission)
    {
        RequireOpen(); ArgumentNullException.ThrowIfNull(lease);
        if (!Services.Resources.OwnsSubmission(submission))
        { throw new ArgumentException("The token must be accepted by this runtime's main queue.", nameof(submission)); }
        Task result = submission.WaitAsync();
        if (result.IsCompleted) { result.GetAwaiter().GetResult(); }
        NativeContentEntry entry = build.Content.Create(content, lease, null, []);
        IDisposable writer = entry.Acquire();
        try { Services.Resources.RetainUntilSubmissionEnds(submission, writer); }
        catch { entry.Abandon(); writer.Dispose(); throw; }
        entry.Accept(submission, []);
        build.Leases.Add(entry.Acquire());
        build.UsedContent.Add(entry);
        return new(entry);
    }
    public bool TryUseContent<TContent>(NativePassContentGeneration<TContent>? generation, out TContent content)
    {
        RequireOpen(); content = default!;
        if (generation is null) { return false; }
        NativeContentEntry entry = generation.Entry;
        if (!ReferenceEquals(entry.Store, build.Content))
        { throw new ArgumentException("The content generation belongs to another runtime.", nameof(generation)); }
        if (!entry.TryAcquire(build, out object? value, out IDisposable? hold)) { return false; }
        build.Leases.Add(hold!); build.UsedContent.Add(entry);
        if (ReferenceEquals(entry.Origin, build))
        {
            build.ContentDependencies.Add((this, Passes.Count, entry.Writers));
        }
        content = (TContent)value!; return true;
    }
    internal void RequireResource(NativePassResource resource)
    {
        RequireOpen(); ArgumentNullException.ThrowIfNull(resource);
        if (!ReferenceEquals(resource.Owner, build) || !allowed.Contains(resource))
        { throw new ArgumentException("Import or create this resource in the current feature execution first.", nameof(resource)); }
    }
    private void RequireDeclared(GpuRenderGraphResource resource)
    {
        RequireOpen(); ArgumentNullException.ThrowIfNull(resource);
        if (!Declaration.Uses.Any(use => ReferenceEquals(use.Resource, resource)))
        { throw new ArgumentException("The feature did not declare this resource.", nameof(resource)); }
    }
    private void RequireExternalDeclaration(NativePassResource resource)
    {
        foreach ((GpuRenderGraphResource logical, NativePassResource physical) in build.Resources)
        { if (ReferenceEquals(resource, physical)) { RequireDeclared(logical); return; } }
    }
    private void RequireName(string name, HashSet<string> names)
    {
        RequireOpen(); ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!names.Add(name)) { throw new ArgumentException("A name must be unique within this feature build.", nameof(name)); }
    }
    private void RequireOpen() => ObjectDisposedException.ThrowIf(closed, this);
    internal void Close() { closed = true; foreach (NativePassBuilder builder in Passes) { builder.Seal(); } }
}
