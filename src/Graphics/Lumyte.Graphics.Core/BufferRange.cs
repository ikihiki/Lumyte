namespace Lumyte.Graphics;

// Byte range shared with command backends, referring directly to the concrete allocation.
internal readonly record struct BufferRange(IBufferBackendContract Buffer, ulong Offset, ulong Length);
