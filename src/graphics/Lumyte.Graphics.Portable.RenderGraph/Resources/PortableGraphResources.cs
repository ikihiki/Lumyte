using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.Portable.Resources;
using System.Runtime.CompilerServices;

namespace Lumyte.Graphics.Portable.RenderGraph;

/// <summary>The common resource facade backed by Portable whole-resource ownership.</summary>
public sealed class PortableGraphResources : IGpuGraphResources
{
    private readonly PortableRenderRuntime runtime;
    private readonly ConditionalWeakTable<GpuResourceRef, GpuGraphResourceRef> wrappers = new();
    private readonly ConditionalWeakTable<GpuGraphPackageRef, GpuResourceRef[]> packages = new();
    private readonly HashSet<IDisposable> owners = [];
    private readonly ConditionalWeakTable<GpuGraphResourceRef, ExternalDependency> externalDependencies = new();
    internal PortableGraphResources(PortableRenderRuntime runtime, GpuResourceManager manager) { this.runtime = runtime; Manager = manager; }
    /// <summary>Borrowed provider extension for integrations that deliberately use the Portable API.</summary>
    public GpuResourceManager Manager { get; }
    public GpuTextureRef ResolveTexture(GpuGraphTextureRef reference) => (GpuTextureRef)Resolve(reference);
    public GpuBufferRef ResolveBuffer(GpuGraphBufferRef reference) => (GpuBufferRef)Resolve(reference);
    /// <summary>Exposes an explicitly owned Portable resource through the common facade without transferring its ownership.</summary>
    public GpuGraphTextureRef ImportTexture(GpuTextureRef reference)
    { lock (runtime.Gate) { runtime.CheckOpen(); Manager.GetTextureHandle(reference); return Wrap(reference); } }
    /// <summary>Exposes an explicitly owned Portable resource through the common facade without transferring its ownership.</summary>
    public GpuGraphBufferRef ImportBuffer(GpuBufferRef reference)
    { lock (runtime.Gate) { runtime.CheckOpen(); Manager.GetBufferRange(reference); return Wrap(reference); } }
    /// <summary>Imports a raw backend texture for host/presentation integration, transferring its lease on success.</summary>
    public PortableGraphResourceImport<GpuGraphTextureRef> ImportTexture(GpuTextureHandle handle, GpuTextureDescription description,
        IDisposable lease, GpuSubmissionToken? precedingSubmission = null)
    {
        lock (runtime.Gate)
        {
            runtime.CheckOpen(); if (precedingSubmission is { } token) { Manager.RequireAcceptedSubmission(token); }
            GpuResourceScope scope = Manager.CreateScope();
            try
            {
                GpuTextureRef resource = scope.ImportTexture(handle, description, lease);
                GpuGraphTextureRef reference = Wrap(resource); TrackPredecessor(reference, resource, precedingSubmission);
                var owner = new PortableGraphResourceImport<GpuGraphTextureRef>(reference, value => ReleaseImport(value, scope));
                owners.Add(owner); return owner;
            }
            catch { scope.Dispose(); throw; }
        }
    }
    /// <summary>Imports a raw backend buffer for host integration, transferring its lease on success.</summary>
    public PortableGraphResourceImport<GpuGraphBufferRef> ImportBuffer(GpuBufferHandle handle, GpuBufferDescription description,
        IDisposable lease, GpuSubmissionToken? precedingSubmission = null)
    {
        lock (runtime.Gate)
        {
            runtime.CheckOpen(); if (precedingSubmission is { } token) { Manager.RequireAcceptedSubmission(token); }
            GpuResourceScope scope = Manager.CreateScope();
            try
            {
                GpuBufferRef resource = scope.ImportBuffer(handle, description, lease);
                GpuGraphBufferRef reference = Wrap(resource); TrackPredecessor(reference, resource, precedingSubmission);
                var owner = new PortableGraphResourceImport<GpuGraphBufferRef>(reference, value => ReleaseImport(value, scope));
                owners.Add(owner); return owner;
            }
            catch { scope.Dispose(); throw; }
        }
    }
    private void TrackPredecessor(GpuGraphResourceRef reference, GpuResourceRef resource, GpuSubmissionToken? preceding)
    {
        if (preceding is not { } token) { return; }
        Manager.RetainUntilSubmissionEnds(token, Manager.AcquireUse(resource));
        externalDependencies.Add(reference, new(token.WaitAsync().AsTask()));
    }
    internal Task? GetExternalDependency(GpuGraphResourceRef reference)
        => externalDependencies.TryGetValue(reference, out ExternalDependency? value) ? value.Result : null;
    private void ReleaseImport(IDisposable owner, GpuResourceScope scope)
    { lock (runtime.Gate) { scope.Dispose(); owners.Remove(owner); } }
    private sealed record ExternalDependency(Task Result);
    internal GpuResourceRef Resolve(GpuGraphResourceRef reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (reference.RuntimeId != runtime.Id) { throw new ArgumentException("The resource belongs to another runtime.", nameof(reference)); }
        return reference switch
        {
            TextureReference texture => texture.Resource,
            BufferReference buffer => buffer.Resource,
            _ => throw new ArgumentException("The resource has no Portable representation.", nameof(reference)),
        };
    }
    internal GpuGraphTextureRef Wrap(GpuTextureRef resource)
    {
        if (!wrappers.TryGetValue(resource, out GpuGraphResourceRef? reference))
        { reference = new TextureReference(runtime.Id, resource); wrappers.Add(resource, reference); }
        return (GpuGraphTextureRef)reference;
    }
    internal GpuGraphBufferRef Wrap(GpuBufferRef resource)
    {
        if (!wrappers.TryGetValue(resource, out GpuGraphResourceRef? reference))
        { reference = new BufferReference(runtime.Id, resource); wrappers.Add(resource, reference); }
        return (GpuGraphBufferRef)reference;
    }
    public GpuGraphResourceScope CreateScope()
    {
        lock (runtime.Gate)
        { runtime.CheckOpen(); var scope = new Scope(this, Manager.CreateScope()); owners.Add(scope); return scope; }
    }
    public GpuGraphResourcePin Pin(GpuGraphResourceRef reference)
    {
        lock (runtime.Gate)
        {
            runtime.CheckOpen(); var pins = new List<IDisposable>();
            try
            {
                foreach (GpuResourceRef resource in ResolveAll(reference)) { pins.Add(Manager.Pin(resource)); }
                var pin = new PinOwner(this, pins); owners.Add(pin); return pin;
            }
            catch { foreach (IDisposable pin in pins) { pin.Dispose(); } throw; }
        }
    }
    public IDisposable AcquireUse(GpuGraphResourceRef reference)
    {
        lock (runtime.Gate)
        {
            runtime.CheckOpen(); var uses = new List<IDisposable>();
            try
            {
                foreach (GpuResourceRef resource in ResolveAll(reference)) { uses.Add(Manager.AcquireUse(resource)); }
                var use = new UseOwner(this, uses); owners.Add(use); return use;
            }
            catch { foreach (IDisposable use in uses) { use.Dispose(); } throw; }
        }
    }
    private IEnumerable<GpuResourceRef> ResolveAll(GpuGraphResourceRef reference)
    {
        if (reference.RuntimeId != runtime.Id) { throw new ArgumentException("The resource belongs to another runtime.", nameof(reference)); }
        if (reference is not GpuGraphPackageRef package) { return [Resolve(reference)]; }
        return packages.TryGetValue(package, out GpuResourceRef[]? resources) ? resources
            : throw new ArgumentException("The package does not belong to this resource facade.", nameof(reference));
    }
    public void Collect() { lock (runtime.Gate) { runtime.CheckOpen(); runtime.Collect(); } }
    public void Trim() { lock (runtime.Gate) { runtime.CheckOpen(); runtime.Collect(); Manager.Trim(); } }
    internal void RequireOwnersReturned()
    {
        if (owners.Count != 0) { throw new InvalidOperationException("Dispose resource scopes, pins and uses before disposing their runtime."); }
    }
    internal void ClearReferences() { wrappers.Clear(); packages.Clear(); }
    internal static GpuTextureDescription Describe(GpuGraphTextureDescription description, GpuTextureUsage usage = GpuTextureUsage.None)
        => new(description.Dimension switch
        {
            GpuGraphTextureDimension.OneD => GpuTextureDimension.Texture1D,
            GpuGraphTextureDimension.TwoD => GpuTextureDimension.Texture2D,
            GpuGraphTextureDimension.ThreeD => GpuTextureDimension.Texture3D,
            _ => throw new ArgumentOutOfRangeException(nameof(description)),
        }, description.Width, description.Height,
        description.Dimension == GpuGraphTextureDimension.ThreeD ? description.DepthOrArrayLayers : 1,
        description.MipLevelCount, description.Dimension == GpuGraphTextureDimension.ThreeD ? 1 : description.DepthOrArrayLayers,
        description.SampleCount, description.Format, usage);
    private static GpuGraphTextureDescription Describe(GpuTextureDescription description)
        => new(description.Width, description.Height, description.Format,
            description.Dimension == GpuTextureDimension.Texture3D ? description.Depth : description.LayerCount,
            description.MipCount, description.SampleCount, description.Dimension switch
            {
                GpuTextureDimension.Texture1D => GpuGraphTextureDimension.OneD,
                GpuTextureDimension.Texture2D => GpuGraphTextureDimension.TwoD,
                GpuTextureDimension.Texture3D => GpuGraphTextureDimension.ThreeD,
                _ => throw new ArgumentOutOfRangeException(nameof(description)),
            });
    private sealed class TextureReference(Guid runtimeId, GpuTextureRef resource)
        : GpuGraphTextureRef(runtimeId, Guid.NewGuid(), Describe(resource.Description))
    { internal GpuTextureRef Resource { get; } = resource; }
    private sealed class BufferReference(Guid runtimeId, GpuBufferRef resource)
        : GpuGraphBufferRef(runtimeId, Guid.NewGuid(), new(resource.Description.Size))
    { internal GpuBufferRef Resource { get; } = resource; }
    private sealed class UseOwner(PortableGraphResources owner, List<IDisposable> leases) : IDisposable
    {
        public void Dispose()
        { lock (owner.runtime.Gate) { foreach (IDisposable lease in leases) { lease.Dispose(); } leases.Clear(); owner.owners.Remove(this); } }
    }
    private sealed class PinOwner(PortableGraphResources owner, List<IDisposable> pins) : GpuGraphResourcePin
    {
        public override void Dispose()
        {
            lock (owner.runtime.Gate)
            { foreach (IDisposable pin in pins) { pin.Dispose(); } pins.Clear(); owner.owners.Remove(this); }
        }
    }
    private sealed class Scope(PortableGraphResources owner, GpuResourceScope scope) : GpuGraphResourceScope
    {
        private bool closed;
        private int imports;
        private readonly List<GpuResourceRef> pendingReleases = [];
        private readonly Dictionary<GpuGraphPackageRef, GpuResourceRef[]> ownedPackages = [];
        public override ValueTask<GpuGraphPackageRef> ImportPackageAsync(GpuPackageUploadData data, CancellationToken cancellationToken = default)
        {
            lock (owner.runtime.Gate)
            {
                ObjectDisposedException.ThrowIf(closed, this);
                IDisposable operation = owner.runtime.BeginOperation(); imports++;
                return ImportCoreAsync(data, operation, cancellationToken);
            }
        }
        private async ValueTask<GpuGraphPackageRef> ImportCoreAsync(GpuPackageUploadData data, IDisposable operation, CancellationToken cancellationToken)
        {
            bool entered = false;
            try
            {
            ArgumentNullException.ThrowIfNull(data); cancellationToken.ThrowIfCancellationRequested();
            await owner.runtime.Work.WaitAsync(cancellationToken).ConfigureAwait(false); entered = true;
            lock (owner.runtime.Gate) { ObjectDisposedException.ThrowIf(closed, this); }
            if (data.Profile != new GpuUploadProfileId("images.sampled", 1) || data.Buffers.Count != 0)
            { throw new NotSupportedException("Portable graph upload currently accepts the images.sampled version 1 profile."); }
            foreach (GpuImageUploadData image in data.Images)
            {
                if (image.Encoding != GpuImageColorEncoding.Linear || image.AlphaMode == GpuImageAlphaMode.Straight
                    || image.Description.Format is not (GpuFormat.Rgba8Unorm or GpuFormat.Bgra8Unorm))
                { throw new NotSupportedException("images.sampled version 1 requires linear opaque or premultiplied RGBA8/BGRA8 image data."); }
            }
            var items = new List<GpuPackageResource>();
            for (int index = 0; index < data.Images.Count; index++)
            {
                GpuImageUploadData image = data.Images[index];
                var uploads = image.Subresources.Select(subresource => PrepareUpload(image.Description, subresource)).ToArray();
                items.Add(new GpuPackageTexture(index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Describe(image.Description, GpuTextureUsage.Sampled | GpuTextureUsage.CopyDestination), uploads));
            }
            var exports = data.Exports.Select(export => export.Kind == GpuPackageUploadExportKind.Image
                ? new GpuPackageExport(export.Name, export.Index.ToString(System.Globalization.CultureInfo.InvariantCulture))
                : throw new NotSupportedException("images.sampled exports images only.")).ToArray();
            GpuPackageRef package = await scope.ImportPackageAsync(new(items, exports), cancellationToken).ConfigureAwait(false);
            lock (owner.runtime.Gate)
            {
                ObjectDisposedException.ThrowIf(closed, this);
                var resultExports = data.Exports.ToDictionary(export => export.Name,
                    export => (GpuGraphResourceRef)owner.Wrap(package.GetExport<GpuTextureRef>(export.Name)), StringComparer.Ordinal);
                var result = new GpuGraphPackageRef(owner.runtime.Id, Guid.NewGuid(), resultExports);
                owner.packages.Add(result, [package]); ownedPackages.Add(result, [package]); return result;
            }
            }
            finally
            {
                try
                {
                    lock (owner.runtime.Gate)
                    {
                        imports--;
                        if (imports == 0)
                        {
                            foreach (GpuResourceRef reference in pendingReleases) { scope.Release(reference); }
                            pendingReleases.Clear();
                            if (closed) { scope.Dispose(); owner.owners.Remove(this); }
                        }
                    }
                }
                finally { if (entered) { owner.runtime.Work.Release(); } operation.Dispose(); }
            }
        }
        private static GpuTextureUpload PrepareUpload(GpuGraphTextureDescription description, GpuImageSubresourceData subresource)
        {
            uint width = Math.Max(1, description.Width >> (int)subresource.MipLevel), height = Math.Max(1, description.Height >> (int)subresource.MipLevel);
            uint depth = description.Dimension == GpuGraphTextureDimension.ThreeD ? Math.Max(1, description.DepthOrArrayLayers >> (int)subresource.MipLevel) : 1;
            int bytesPerPixel = description.Format switch
            {
                GpuFormat.R8Unorm => 1, GpuFormat.Rg8Unorm => 2,
                GpuFormat.Rgba8Unorm or GpuFormat.Bgra8Unorm or GpuFormat.R32Float => 4,
                _ => throw new NotSupportedException("The image profile requires a defined color pixel representation."),
            };
            int rowBytes = checked((int)width * bytesPerPixel), rowPitch = checked((rowBytes + 255) & ~255);
            int slicePitch = checked(rowPitch * (int)height);
            byte[] bytes = new byte[checked(slicePitch * (int)depth)];
            ulong sourceRow = subresource.RowStride == 0 ? (ulong)rowBytes : subresource.RowStride;
            ulong sourceSlice = subresource.SliceStride == 0 ? checked(sourceRow * height) : subresource.SliceStride;
            for (uint slice = 0; slice < depth; slice++)
            {
                for (uint row = 0; row < height; row++)
                {
                    int sourceOffset = checked((int)(slice * sourceSlice + row * sourceRow));
                    subresource.Data.Span.Slice(sourceOffset, rowBytes).CopyTo(bytes.AsSpan(checked((int)slice * slicePitch + (int)row * rowPitch), rowBytes));
                }
            }
            return new(bytes, new(subresource.MipLevel, GpuTextureAspect.All,
                new(0, 0, description.Dimension == GpuGraphTextureDimension.ThreeD ? 0 : subresource.ArrayLayer),
                new(width, height, depth), (ulong)rowPitch, (ulong)slicePitch));
        }
        public override void Release(GpuGraphResourceRef reference)
        {
            lock (owner.runtime.Gate)
            {
                ObjectDisposedException.ThrowIf(closed, this);
                if (reference is not GpuGraphPackageRef package || !ownedPackages.Remove(package, out GpuResourceRef[]? resources))
                { throw new ArgumentException("This scope does not own the package reference.", nameof(reference)); }
                foreach (GpuResourceRef resource in resources)
                { if (imports != 0) { pendingReleases.Add(resource); } else { scope.Release(resource); } }
            }
        }
        public override void Dispose()
        {
            lock (owner.runtime.Gate)
            {
                if (closed) { return; } closed = true; ownedPackages.Clear();
                if (imports == 0) { scope.Dispose(); owner.owners.Remove(this); }
            }
        }
    }
}
