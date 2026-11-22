using System.Numerics;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D.Pipelines;

internal partial record struct GenericClipArguments2D : IShaderArguments
{
    private TransferData _data;

    public GenericClipArguments2D(GenericArguments2D arguments)
    {
        _data = new() { Width = arguments.Width, Height = arguments.Height, Commands = arguments.Commands, CommandIndex = arguments.CommandIndex };
        Scene = arguments.Scene;
        Clip = arguments.Clip;
    }

    public IGpuRef<Vector4> Scene { get; set; }

    public IGpuRef<float> Clip { get; set; }

    public partial uint Width { get; set; }

    public partial uint Height { get; set; }

    public partial uint Commands { get; set; }

    public partial uint CommandIndex { get; set; }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TransferData
    {
        public ShaderReferenceSlot Scene;
        public ShaderReferenceSlot Clip;
        public uint Width;
        public uint Height;
        public uint Commands;
        public uint CommandIndex;
    }
}
