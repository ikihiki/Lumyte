namespace Lumyte.Graphics.Wgpu;

internal sealed record ShaderDataField(int Offset, int Size, string ScalarType, int ElementCount);
