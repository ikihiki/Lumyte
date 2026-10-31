using System.Runtime.InteropServices;

namespace Lumyte.Graphics.Direct3D12;

internal static partial class NativeMethods
{
    [LibraryImport(Library, EntryPoint = "commands_global_dependency")]
    internal static partial void GlobalDependency(nint command, uint beforeStages, uint beforeAccess, uint afterStages, uint afterAccess);

    [LibraryImport(Library, EntryPoint = "commands_buffer_dependency")]
    internal static partial void BufferDependency(nint command, nint buffer, ulong offset, ulong size, uint beforeStages, uint beforeAccess, uint afterStages, uint afterAccess);

    [LibraryImport(Library, EntryPoint = "commands_texture_dependency")]
    internal static partial void TextureDependency(nint command, nint texture, uint mip, uint mips, uint layer, uint layers, uint beforeLayout, uint afterLayout, uint beforeStages, uint beforeAccess, uint afterStages, uint afterAccess);

    [LibraryImport(Library, EntryPoint = "texture_create")]
    internal static partial int TextureCreate(nint device, uint width, uint height, uint mips, uint layers, uint format, uint usage, out nint texture);

    [LibraryImport(Library, EntryPoint = "commands_copy_texture")]
    internal static partial void CopyTexture(nint command, nint source, NativeTextureRegion src, nint destination, NativeTextureRegion dst);

    [LibraryImport(Library, EntryPoint = "commands_copy_buffer_texture")]
    internal static partial void CopyBufferTexture(nint command, nint buffer, ulong offset, uint pitch, nint texture, NativeTextureRegion region, int upload);
}
