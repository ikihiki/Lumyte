using System.Runtime.InteropServices;

namespace Lumyte.Graphics.Direct3D12;

internal static unsafe partial class NativeMethods
{
    [LibraryImport(Library, EntryPoint = "compute_create")]
    internal static partial int ComputeCreate(nint device, byte* code, ulong size, out nint pipeline);

    [LibraryImport(Library, EntryPoint = "graphics_create")]
    internal static partial int GraphicsCreate(nint device, byte* vs, ulong vsSize, byte* ps, ulong psSize, uint* state, nint partial, out nint pipeline);

    [LibraryImport(Library, EntryPoint = "partial_create", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int PartialCreate(nint device, byte* vs, ulong vsSize, string vsEntry, byte* ps, ulong psSize, string psEntry, uint topology, uint optimization, out nint partial);

    [LibraryImport(Library, EntryPoint = "partial_destroy")]
    internal static partial void PartialDestroy(nint partial);

    [LibraryImport(Library, EntryPoint = "pipeline_destroy")]
    internal static partial void PipelineDestroy(nint pipeline);

    [LibraryImport(Library, EntryPoint = "commands_pipeline")]
    internal static partial int Pipeline(nint commands, nint pipeline, int compute);

    [LibraryImport(Library, EntryPoint = "commands_viewport")]
    internal static partial void Viewport(nint commands, float x, float y, float width, float height, float min, float max);

    [LibraryImport(Library, EntryPoint = "commands_scissor")]
    internal static partial void Scissor(nint commands, int x, int y, int width, int height);

    [LibraryImport(Library, EntryPoint = "commands_blend")]
    internal static partial void Blend(nint commands, float r, float g, float b, float a);

    [LibraryImport(Library, EntryPoint = "commands_stencil")]
    internal static partial void Stencil(nint commands, uint reference);

    [LibraryImport(Library, EntryPoint = "commands_topology")]
    internal static partial void Topology(nint commands, uint topology);

    [LibraryImport(Library, EntryPoint = "commands_indices")]
    internal static partial void Indices(nint commands, nint buffer, ulong offset, uint size, uint format);

    [LibraryImport(Library, EntryPoint = "commands_draw")]
    internal static partial void Draw(nint commands, uint count, uint instances, uint first, int baseVertex, uint firstInstance, int indexed);

    [LibraryImport(Library, EntryPoint = "commands_dispatch")]
    internal static partial void Dispatch(nint commands, uint x, uint y, uint z);

    [LibraryImport(Library, EntryPoint = "commands_indirect")]
    internal static partial int Indirect(nint device, nint commands, nint buffer, ulong offset, uint kind, out nint signature);

    [LibraryImport(Library, EntryPoint = "commands_render_begin")]
    internal static partial int RenderBegin(nint device, nint commands, NativeAttachment* colors, uint count, NativeAttachment* depth);

    [LibraryImport(Library, EntryPoint = "commands_detach_heaps")]
    internal static partial void DetachHeaps(nint commands, out nint rtv, out nint dsv);

    [LibraryImport(Library, EntryPoint = "binding_create")]
    internal static partial int BindingCreate(nint device, byte* bytes, uint size, out nint binding);

    [LibraryImport(Library, EntryPoint = "binding_destroy")]
    internal static partial void BindingDestroy(nint binding);

    [LibraryImport(Library, EntryPoint = "binding_buffer")]
    internal static partial void BindingBuffer(nint device, nint binding, uint slot, nint buffer, ulong size, int writable);

    [LibraryImport(Library, EntryPoint = "binding_texture")]
    internal static partial void BindingTexture(nint device, nint binding, uint slot, nint texture, uint format, uint dimension, uint mip, uint mips, uint layer, uint layers);

    [LibraryImport(Library, EntryPoint = "binding_sampler")]
    internal static partial void BindingSampler(nint device, nint binding, uint slot, uint filter, uint u, uint v, uint w, float min, float max, uint anisotropy, uint compare);

    [LibraryImport(Library, EntryPoint = "commands_binding")]
    internal static partial void Binding(nint commands, nint binding, int compute);
}
