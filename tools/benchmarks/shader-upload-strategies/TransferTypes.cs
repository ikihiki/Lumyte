using System.Numerics;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.UploadStrategies.Benchmarks;

// Numeric and vector schemas are used only by untimed GPU verification.
[StructLayout(LayoutKind.Explicit, Size = 32)]
internal struct NumericValue
{
    [FieldOffset(0)] public uint Header;
    [FieldOffset(4)] public Vector3 Position;
    [FieldOffset(16)] public Vector4 Tail;
}

internal struct Vector3Value
{
    public Vector3 Value;
}

internal sealed partial class ReferenceData : IShaderData
{
    private TransferData _data = default;
    public partial Matrix4x4 Matrix { get; set; }
    public partial uint Factor { get; set; }
    public partial Vector3 Position { get; set; }
    public IGpuRef<uint> Input { get; set; } = null!;
    public IGpuRef<uint> Output { get; set; } = null!;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TransferData
    {
        public Matrix4x4 Matrix;
        public uint Factor;
        public Vector3 Position;
        public ShaderReferenceSlot Input;
        public ShaderReferenceSlot Output;
    }
}

internal sealed partial class VerificationArguments : IShaderArguments
{
    private TransferData _data = default;
    public IGpuRef<NumericValue> Numeric { get; set; } = null!;
    public IGpuRef<Vector3Value> Vectors { get; set; } = null!;
    public IGpuRef<ReferenceData> Mixed { get; set; } = null!;
    public IGpuRef<uint> Results { get; set; } = null!;
    public partial uint Count { get; set; }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TransferData
    {
        public ShaderReferenceSlot Numeric;
        public ShaderReferenceSlot Vectors;
        public ShaderReferenceSlot Mixed;
        public ShaderReferenceSlot Results;
        public uint Count;
    }
}
