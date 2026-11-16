using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Tests;

// Generated numeric segments and references reach actual native storage unchanged.
internal partial struct NativeAbiValue : IShaderArguments
{
    private Storage _data;

    public partial uint Number { readonly get; set; }

    public IGpuRef<IGraphicsSampler>? Sampler { get; set; }

    public IGpuRef<IGraphicsTextureView>? Texture { get; set; }

    public IGpuRef<uint>? Writable { get; set; }

    public IGpuRef<uint>? First { get; set; }

    public IGpuRef<uint>? Second { get; set; }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct Storage
    {
        public uint Number;
        public uint Padding;
        public ShaderReferenceSlot Sampler;
        public ShaderReferenceSlot Texture;
        public ShaderReferenceSlot Writable;
        public ShaderReferenceSlot First;
        public ShaderReferenceSlot Second;
    }
}
