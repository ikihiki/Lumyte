namespace Lumyte.Graphics.Portable;

[Flags]
public enum GpuBufferUsage
{
    None = 0,
    CopySource = 1 << 0,
    CopyDestination = 1 << 1,
    Uniform = 1 << 2,
    Storage = 1 << 3,
    Index = 1 << 4,
    IndirectArguments = 1 << 5,
    MapRead = 1 << 6,
    MapWrite = 1 << 7,
}
