using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph;

/// <summary>Provides internal atomic logical-pass append and imported identity validation.</summary>
public sealed partial class RenderGraph
{
    // Native allocations expose no device/generation. The adapter must additionally
    // validate its captured generation and provenance before claiming the plan lease.
    internal void ValidateAppend(IGraphicDevice device, string name)
    {
        EnsureBuilding();
        ValidateName(name);
        ArgumentNullException.ThrowIfNull(device);
        if (!ReferenceEquals(Device, device) || device.Status != GraphicsDeviceStatus.Ready)
        {
            throw new ArgumentException("The graph must use the same live device.", nameof(device));
        }
    }

    internal IGraphicsTexture GetImportedTexture(RenderGraphTexture resource, IGraphicDevice device)
    {
        ValidateAppend(device, Name);
        CheckImported(resource);
        return (IGraphicsTexture)resource.Allocation!;
    }

    internal TextureViewInfo GetImportedTextureView(RenderGraphTexture resource, IGraphicsTextureView view, IGraphicDevice device)
    {
        ArgumentNullException.ThrowIfNull(view);
        IGraphicsTexture allocation = GetImportedTexture(resource, device);
        if (!ReferenceEquals(view.Texture, allocation))
        {
            throw new ArgumentException("The view does not reference the imported allocation.", nameof(view));
        }

        TextureViewInfo info = view.Info;
        if (info.Format != allocation.Format || info.MipLevelCount == 0 || info.ArrayLayerCount == 0 ||
            info.BaseMipLevel > allocation.MipLevels || info.MipLevelCount > allocation.MipLevels - info.BaseMipLevel ||
            info.BaseArrayLayer > allocation.ArrayLayers || info.ArrayLayerCount > allocation.ArrayLayers - info.BaseArrayLayer)
        {
            throw new ArgumentException("The view is outside the imported allocation.", nameof(view));
        }

        return info;
    }

    internal BufferSlice<T> GetImportedBuffer<T>(RenderGraphBuffer<T> resource, ulong offset, ulong count, IGraphicDevice device)
        where T : unmanaged
    {
        ValidateAppend(device, Name);
        CheckImported(resource);
        return new BufferSlice<T>((IGraphicsBuffer<T>)resource.Allocation!, offset, count);
    }

    internal RenderGraphPass AppendTransactional(
        string name,
        IGraphicDevice device,
        Action<RenderGraphContext> record,
        IReadOnlyList<RenderGraphPassUse> uses,
        IRenderGraphLifetime lifetime,
        Action<RenderGraphAppendStage>? faultInjection = null)
    {
        ValidateAppend(device, name);
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(uses);
        ArgumentNullException.ThrowIfNull(lifetime);
        var pass = new RenderGraphPass(this, name, record);

        // Materialize and validate all declarations on an unreachable detached pass.
        foreach (RenderGraphPassUse use in uses.ToArray())
        {
            ArgumentNullException.ThrowIfNull(use);
            use.Apply(this, device, pass);
        }

        ValidateAppend(device, name);
        _appending = true;
        List<IRenderGraphLifetime>? previousLifetimes = _lifetimes;
        int passCount = _passes.Count;
        int lifetimeCount = previousLifetimes?.Count ?? 0;
        try
        {
            lifetime.Validate();
            faultInjection?.Invoke(RenderGraphAppendStage.Validated);
            _passes.EnsureCapacity(checked(passCount + 1));
            List<IRenderGraphLifetime> lifetimes = previousLifetimes ?? [];
            lifetimes.EnsureCapacity(checked(lifetimeCount + 1));
            _passes.Add(pass);
            faultInjection?.Invoke(RenderGraphAppendStage.PassAdded);
            lifetimes.Add(lifetime);
            _lifetimes = lifetimes;
            faultInjection?.Invoke(RenderGraphAppendStage.LifetimeAdded);
            return pass;
        }
        catch
        {
            if (_passes.Count > passCount)
            {
                _passes.RemoveRange(passCount, _passes.Count - passCount);
            }

            if (_lifetimes is { } lifetimes && lifetimes.Count > lifetimeCount)
            {
                lifetimes.RemoveRange(lifetimeCount, lifetimes.Count - lifetimeCount);
            }

            _lifetimes = previousLifetimes;

            // Failure never owns or disposes the caller's lifetime.
            throw;
        }
        finally
        {
            _appending = false;
        }
    }

    private void CheckImported(RenderGraphResource resource)
    {
        CheckResource(resource);
        if (resource.IsTransient || resource.LocalPassOwner is not null || !_resources.Contains(resource))
        {
            throw new ArgumentException("Only registered external imports can be bound.", nameof(resource));
        }
    }
}
