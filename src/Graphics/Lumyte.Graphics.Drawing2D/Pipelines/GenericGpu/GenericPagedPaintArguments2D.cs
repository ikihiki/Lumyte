using System.Numerics;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D.Pipelines;

internal partial record struct GenericPagedPaintArguments2D : IShaderArguments
{
    private TransferData _data;

    public GenericPagedPaintArguments2D(GenericArguments2D arguments)
    {
        _data = new() { Width = arguments.Width, Height = arguments.Height, Commands = arguments.Commands, CommandIndex = arguments.CommandIndex, TranslationX = arguments.TranslationX, TranslationY = arguments.TranslationY, SampleOffset = arguments.SampleOffset, SampleCount = arguments.SampleCount, PageCells = arguments.PageCells, SceneCells = arguments.SceneCells };
        Scene = arguments.ScenePages;
        Paint = arguments.Paint;
        Resources = arguments.Resources;
    }

    public IGpuRef<GenericScenePage2D> Scene { get; set; }

    public IGpuRef<Vector4> Paint { get; set; }

    public IGpuRef<GenericResource2D> Resources { get; set; }

    public partial uint Width { get; set; }

    public partial uint Height { get; set; }

    public partial uint Commands { get; set; }

    public partial uint CommandIndex { get; set; }

    public partial float TranslationX { get; set; }

    public partial float TranslationY { get; set; }

    public partial uint SampleOffset { get; set; }

    public partial uint SampleCount { get; set; }

    public partial uint SceneCells { get; set; }

    public partial uint PageCells { get; set; }

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
        public float TranslationX;
        public float TranslationY;
        public uint SampleOffset;
        public uint SampleCount;
        public uint SceneCells;
        public uint PageCells;
    }
}
