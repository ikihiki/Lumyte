using System.Numerics;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D.Pipelines;

internal partial record struct GenericPaintArguments2D : IShaderArguments
{
    private TransferData _data;

    public GenericPaintArguments2D(GenericArguments2D arguments)
    {
        _data = new() { Width = arguments.Width, Height = arguments.Height, Commands = arguments.Commands, CommandIndex = arguments.CommandIndex };
        Scene = arguments.Scene;
        Paint = arguments.Paint;
        Resources = arguments.Resources;
    }

    public IGpuRef<Vector4> Scene { get; set; }

    public IGpuRef<Vector4> Paint { get; set; }

    public IGpuRef<GenericResource2D> Resources { get; set; }

    public partial uint Width { get; set; }

    public partial uint Height { get; set; }

    public partial uint Commands { get; set; }

    public partial uint CommandIndex { get; set; }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TransferData
    {
        public ShaderReferenceSlot Scene;
        public ShaderReferenceSlot Paint;
        public ShaderReferenceSlot Resources;
        public uint Width;
        public uint Height;
        public uint Commands;
        public uint CommandIndex;
    }
}
