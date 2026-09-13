using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.Portable.Resources;
using Lumyte.Graphics.Portable.Shaders;

namespace Lumyte.Graphics.Portable.RenderGraph;

public delegate void PortablePassRecordAction<TState>(PortablePassRecordContext context, TState state);

/// <summary>One feature build. Resource allocation and recording occur after every feature has declared its uses.</summary>
public sealed class PortablePassBuildContext
{
    private readonly PortableExecutionBuild build;
    private readonly GpuRenderGraphPass pass;
    private bool ended;
    internal PortablePassBuildContext(PortableExecutionBuild build, GpuRenderGraphPass pass) { this.build = build; this.pass = pass; }
    public PortablePassServices Services { get { Check(); return build.Services; } }
    internal GpuRenderGraphPass Feature => pass;
    internal void Check() { ObjectDisposedException.ThrowIf(ended, this); }
    internal void End() { ended = true; }
    public T GetInput<T>(GpuGraphValue<T> value) { Check(); return pass.GetInput(value, build.Bindings); }
    public PortablePassTexture ImportTexture(GpuRenderGraphTexture texture)
    { Check(); RequireDeclared(texture); return (PortablePassTexture)build.Resources[texture]; }
    public PortablePassBuffer ImportBuffer(GpuRenderGraphBuffer buffer)
    { Check(); RequireDeclared(buffer); return (PortablePassBuffer)build.Resources[buffer]; }
    private void RequireDeclared(GpuRenderGraphResource resource)
    {
        if (!pass.Uses.Any(use => ReferenceEquals(use.Resource, resource)))
        { throw new ArgumentException("The feature contract did not declare this resource.", nameof(resource)); }
    }
    public PortablePassTexture ImportTexture(GpuTextureRef reference)
    { Check(); return build.ImportTexture(reference); }
    public PortablePassBuffer ImportBuffer(GpuBufferRef reference)
    { Check(); return build.ImportBuffer(reference); }
    /// <summary>Imports a raw object with transferred lifetime and an optional explicitly preceding manager submission.</summary>
    public PortablePassBuffer ImportBuffer(string name, GpuBufferHandle handle, GpuBufferDescription description,
        IDisposable lease, GpuSubmissionToken? precedingSubmission = null)
    {
        Check(); build.Name(pass, name);
        if (precedingSubmission is { } token) { Services.Resources.RequireAcceptedSubmission(token); }
        using GpuResourceScope scope = Services.Resources.CreateScope();
        GpuBufferRef reference = scope.ImportBuffer(handle, description, lease);
        PortablePassBuffer result = build.ImportBuffer(reference);
        if (precedingSubmission is { } preceding)
        {
            Services.Resources.RetainUntilSubmissionEnds(preceding, Services.Resources.AcquireUse(reference));
            build.ExternalResults.Add(preceding.WaitAsync().AsTask());
        }
        return result;
    }
    /// <summary>Imports a raw object with transferred lifetime and an optional explicitly preceding manager submission.</summary>
    public PortablePassTexture ImportTexture(string name, GpuTextureHandle handle, GpuTextureDescription description,
        IDisposable lease, GpuSubmissionToken? precedingSubmission = null)
    {
        Check(); build.Name(pass, name);
        if (precedingSubmission is { } token) { Services.Resources.RequireAcceptedSubmission(token); }
        using GpuResourceScope scope = Services.Resources.CreateScope();
        GpuTextureRef reference = scope.ImportTexture(handle, description, lease);
        PortablePassTexture result = build.ImportTexture(reference);
        if (precedingSubmission is { } preceding)
        {
            Services.Resources.RetainUntilSubmissionEnds(preceding, Services.Resources.AcquireUse(reference));
            build.ExternalResults.Add(preceding.WaitAsync().AsTask());
        }
        return result;
    }
    public PortablePassTexture CreateTexture(string name, GpuTextureDescription description)
    { Check(); var resource = new PortablePassTexture(build, build.Name(pass, name), description); build.InternalResources.Add(resource); return resource; }
    public PortablePassBuffer CreateBuffer(string name, GpuBufferDescription description)
    { Check(); var resource = new PortablePassBuffer(build, build.Name(pass, name), description); build.InternalResources.Add(resource); return resource; }
    public PortablePassView CreateView(string name, PortablePassTexture texture, GpuTextureViewDescription description = default)
    {
        Check(); build.Check(texture); build.Name(pass, name, "view");
        var view = new PortablePassView(texture, description); build.Views.Add(view); return view;
    }
    public PortablePassBindings CreateBindings(string name, PortableShaderProgram program, uint group, IPortablePassBindingInputs inputs)
    {
        Check(); build.Name(pass, name, "bindings"); ArgumentNullException.ThrowIfNull(program); ArgumentNullException.ThrowIfNull(inputs);
        var writer = new PortablePassBindingWriter(build);
        try { inputs.Write(writer); }
        finally { writer.Close(); }
        var result = new PortablePassBindings(build, program, group, writer.Finish(), writer.Resources.ToArray()); build.BindingSets.Add(result); return result;
    }
    public PortablePassBuilder AddPass<TState>(string name, TState state, PortablePassRecordAction<TState> record)
    {
        Check(); ArgumentNullException.ThrowIfNull(record);
        var node = new PortablePassBuilder(build, this, build.Name(pass, name, "pass"), context => record(context, state));
        foreach (PortablePassBuilder writer in contentWriters) { node.Dependencies.Add(writer); }
        build.Passes.Add(node); return node;
    }
    public void Retain(IDisposable lease) { Check(); build.Batch.Retain(lease); }
    private readonly HashSet<PortablePassBuilder> contentWriters = [];
    public PortablePassContentGeneration<TContent> RegisterContent<TContent>(TContent content, IDisposable lease,
        ReadOnlySpan<PortablePassBuilder> writers)
    {
        Check(); ArgumentNullException.ThrowIfNull(lease);
        if (writers.IsEmpty) { throw new ArgumentException("At least one content writer is required.", nameof(writers)); }
        PortablePassBuilder[] snapshot = writers.ToArray();
        foreach (PortablePassBuilder writer in snapshot)
        {
            if (writer is null || !ReferenceEquals(writer.Context, this) || !writer.Uses.Values.Any(access => access != GpuRenderGraphAccess.Read))
            { throw new ArgumentException("Every writer must be a writing pass from this feature build.", nameof(writers)); }
        }
        lock (build.Runtime.Gate)
        {
            var state = new PortableContentState(build, lease, snapshot.Distinct().ToArray());
            build.Batch.Retain(state.Hold()); build.Generations.Add(state);
            return new(state, content);
        }
    }
    public PortablePassContentGeneration<TContent> RegisterContent<TContent>(TContent content, IDisposable lease,
        GpuSubmissionToken submission)
    {
        Check(); ArgumentNullException.ThrowIfNull(lease);
        lock (build.Runtime.Gate)
        {
            build.Services.Resources.RequireAcceptedSubmission(submission);
            var state = new PortableContentState(build, lease, []);
            build.Services.Resources.RetainUntilSubmissionEnds(submission, state.Hold());
            state.Accept(submission.WaitAsync().AsTask(), independent: true);
            build.Generations.Add(state); build.Dependencies.Add(state);
            build.Batch.Retain(state.Hold()); return new(state, content);
        }
    }
    public bool TryUseContent<TContent>(PortablePassContentGeneration<TContent>? generation, out TContent content)
    {
        Check(); content = default!;
        if (generation is null) { return false; }
        lock (build.Runtime.Gate)
        {
            PortableContentState state = generation.State;
            if (!ReferenceEquals(state.Runtime, build.Runtime)) { throw new ArgumentException("The content belongs to another runtime.", nameof(generation)); }
            if (!state.CanUse(build)) { if (state.Failed) { state.Invalidate(); } return false; }
            build.Batch.Retain(state.Hold());
            if (state.Result is not null) { build.Dependencies.Add(state); }
            else { foreach (PortablePassBuilder writer in state.Writers) { contentWriters.Add(writer); } }
            content = generation.Content; return true;
        }
    }
    public void Instantiate<TState>(PortablePassTemplate<TState> template, TState state)
    { Check(); ArgumentNullException.ThrowIfNull(template); template.Expand(this, state); }
}
