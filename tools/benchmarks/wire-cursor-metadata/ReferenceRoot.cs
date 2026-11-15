using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.FinalRoot.Benchmarks;

// Generated codec, not a replica of either backend's capture/packing algorithm.
internal sealed partial class ReferenceRoot : IShaderArguments
{
    private ResourcesArgs _data = default;
    public partial uint Value { get; set; }
    public IGpuRef<uint> Output { get; set; } = null!;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct ResourcesArgs
    {
        public uint Value;
        public ShaderReferenceSlot Output;
    }
}
