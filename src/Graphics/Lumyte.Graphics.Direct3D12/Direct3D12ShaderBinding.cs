using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Direct3D12;

internal sealed unsafe class Direct3D12ShaderBinding : IDisposable
{
    private nint _native;

    internal Direct3D12ShaderBinding(Direct3D12Device owner, ShaderBindingSnapshot snapshot, ShaderTargetData target)
    {
        byte[] root = ShaderDataLayout.Root(target, snapshot.Root.RootParameter).Pack(snapshot.Root, (value, kind) => Direct3D12ShaderReferenceEncoding.Pack(owner, value, kind));
        if (root.Length > 65536)
        {
            throw new NotSupportedException("Root data exceeds the D3D12 constant buffer limit.");
        }

        fixed (byte* bytes = root)
        {
            owner.Check(NativeMethods.BindingCreate(owner.Native, bytes, checked((uint)root.Length), out _native));
        }

        try
        {
            foreach (IShaderReference reference in snapshot.References)
            {
                switch (reference.Resource)
                {
                    case Direct3D12TextureView view:
                        if (reference.Slot >= 128 || view.Info.Dimension != TextureViewDimension.D2 || view.Info.BaseArrayLayer != 0 || view.Texture.ArrayLayers != 1)
                        {
                            throw new NotSupportedException("The shader helper supports single-layer Texture2D views in slots 0..127.");
                        }

                        NativeMethods.BindingTexture(owner.Native, _native, reference.Slot, view.Native, Direct3D12Texture.NativeFormat(view.Info.Format), 0, view.Info.BaseMipLevel, view.Info.MipLevelCount, 0, 1);
                        break;
                    case Direct3D12Sampler sampler:
                        sampler.ValidateAlive();
                        if (reference.Slot >= 16)
                        {
                            throw new NotSupportedException("Sampler slot exceeds 15.");
                        }

                        SamplerDesc d = sampler.Desc;
                        uint filter = d.MaxAnisotropy > 1 ? 0x55u : (d.MinFilter == FilterMode.Linear ? 0x10u : 0) | (d.MagFilter == FilterMode.Linear ? 4u : 0) | (d.MipmapFilter == FilterMode.Linear ? 1u : 0);
                        if (d.Compare.HasValue)
                        {
                            filter |= 0x80;
                        }

                        NativeMethods.BindingSampler(owner.Native, _native, reference.Slot, filter, Address(d.AddressU), Address(d.AddressV), Address(d.AddressW), d.LodMinClamp, d.LodMaxClamp, d.MaxAnisotropy, d.Compare.HasValue ? (uint)d.Compare.Value + 1 : 8);
                        break;
                    default:
                        if (reference.Slot >= 64)
                        {
                            throw new NotSupportedException("Buffer slot exceeds 63.");
                        }

                        (nint handle, ulong size, BufferUsage usage) = reference.Resource switch
                        {
                            IShaderRawBuffer raw => ((nint)raw.ShaderHandle, raw.SizeInBytes, raw.Usage),
                            IShaderDataSource data => ((nint)data.ShaderHandle, data.SizeInBytes, BufferUsage.ShaderRead),
                            _ => throw new ArgumentException("Unknown buffer reference."),
                        };
                        if (size % 4 != 0)
                        {
                            throw new NotSupportedException("Raw shader storage size must be divisible by four.");
                        }

                        if ((usage & BufferUsage.ShaderRead) != 0)
                        {
                            NativeMethods.BindingBuffer(owner.Native, _native, reference.Slot, handle, size, 0);
                        }

                        if ((usage & BufferUsage.ShaderWrite) != 0)
                        {
                            NativeMethods.BindingBuffer(owner.Native, _native, reference.Slot, handle, size, 1);
                        }

                        break;
                }
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal nint Native => _native;

    public void Dispose()
    {
        if (_native != 0)
        {
            NativeMethods.BindingDestroy(_native);
        }

        _native = 0;
    }

    private static uint Address(AddressMode mode) => mode switch { AddressMode.Repeat => 1, AddressMode.MirrorRepeat => 2, AddressMode.ClampToEdge => 3, _ => throw new ArgumentException("Unknown address mode.") };
}
