using System.Security.Cryptography;

namespace Lumyte.Graphics;

/// <summary>
/// Stable logical binding ABI shared by render-graph shaders and every graphics backend.
/// Native APIs may translate the logical tables to descriptor spaces, sets, or bind groups.
/// </summary>
public static class GpuShaderBindingConvention
{
    private static readonly byte[] s_abiHash = SHA256.HashData(
        "Lumyte.RenderGraph.ShaderBindings.v2;texture-table=0;sampler-table=1;buffer-table=2;storage-texture-table=3;writable-buffer-table=4;descriptor-index=uint32;root-data=128"u8);

    public const int Version = 2;
    public const int TextureTable = 0;
    public const int SamplerTable = 1;
    public const int BufferTable = 2;
    public const int StorageTextureTable = 3;
    public const int WritableBufferTable = 4;
    public const int RootDataSize = 128;

    /// <summary>SHA-256 identity passed to shader packages and pipeline creation.</summary>
    public static ReadOnlyMemory<byte> AbiHash => s_abiHash.ToArray();

    public static void ValidateRootData(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty || data.Length > RootDataSize || (data.Length & 3) != 0)
        {
            throw new ArgumentException(
                $"Root data must contain 4 to {RootDataSize} bytes in 4-byte units.",
                nameof(data));
        }
    }
}
