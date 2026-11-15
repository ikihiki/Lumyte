using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;
namespace BackendNativeWriters;
// One generated mutable whole-wire value, used unchanged for SetArguments and typed CopyFrom.
internal sealed partial class Inputs : IShaderArguments
{
    private Wire _data = default;
    public partial uint Value { get; set; }
    public partial uint Padding0 { get; set; }
    public partial uint Padding1 { get; set; }
    public partial uint Padding2 { get; set; }
    public IGpuRef<uint> Source { get; set; } = null!;
    public IGpuRef<uint> Target { get; set; } = null!;
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct Wire
    {
        public uint Value;
        public uint Padding0;
        public uint Padding1;
        public uint Padding2;
        public ShaderReferenceSlot Source;
        public ShaderReferenceSlot Target;
    }
}
