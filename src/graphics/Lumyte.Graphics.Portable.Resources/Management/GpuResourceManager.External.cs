namespace Lumyte.Graphics.Portable.Resources;

public sealed partial class GpuResourceManager
{
    private readonly Dictionary<object, GpuResourceRef> externalResources = new(ReferenceEqualityComparer.Instance);
    internal GpuBufferRef ImportBuffer(GpuBufferHandle handle, GpuBufferDescription description, IDisposable lease)
    {
        CheckOpen(); ArgumentNullException.ThrowIfNull(handle); ArgumentNullException.ThrowIfNull(lease);
        if (externalResources.ContainsKey(handle)) { throw new ArgumentException("The external resource is already imported; share its managed reference.", nameof(handle)); }
        var record = new ResourceRecord(this); var resource = new GpuBufferRef(record, handle, description);
        Register(record, resource, []); externalResources.Add(handle, resource);
        record.Destroy = lease.Dispose; record.RemoveCache = () => externalResources.Remove(handle); return resource;
    }
    internal GpuTextureRef ImportTexture(GpuTextureHandle handle, GpuTextureDescription description, IDisposable lease)
    {
        CheckOpen(); ArgumentNullException.ThrowIfNull(handle); ArgumentNullException.ThrowIfNull(lease);
        if (externalResources.ContainsKey(handle)) { throw new ArgumentException("The external resource is already imported; share its managed reference.", nameof(handle)); }
        var record = new ResourceRecord(this); var resource = new GpuTextureRef(record, handle, description);
        Register(record, resource, []); externalResources.Add(handle, resource);
        record.Destroy = lease.Dispose; record.RemoveCache = () => externalResources.Remove(handle); return resource;
    }
}
