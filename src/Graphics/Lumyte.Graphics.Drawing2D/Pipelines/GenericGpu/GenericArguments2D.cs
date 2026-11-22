using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D.Pipelines;

internal record struct GenericArguments2D
{
    public IGpuRef<Vector4> Scene { get; set; }

    public IGpuRef<float> Coverage { get; set; }

    public IGpuRef<Vector4> Paint { get; set; }

    public IGpuRef<float> Clip { get; set; }

    public IGpuRef<GenericResource2D> Resources { get; set; }

    public uint Width { get; set; }

    public uint Height { get; set; }

    public uint Commands { get; set; }

    public uint CommandIndex { get; set; }
}
