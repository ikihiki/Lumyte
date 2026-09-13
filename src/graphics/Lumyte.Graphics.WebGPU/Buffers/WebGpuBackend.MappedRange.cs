using System.Buffers;
using P = Lumyte.Graphics.Portable;
using F = WebGpuSharp.FFI;

namespace Lumyte.Graphics.WebGPU;

public sealed partial class WebGpuBackend
{
    private sealed unsafe class MappedRange : P.GpuMappedBufferRange
    {
        private readonly WebGpuBackend owner;
        private readonly BufferResource resource;
        private readonly P.GpuMapMode mode;
        private readonly MappedMemory memory;
        private bool disposed;

        internal MappedRange(WebGpuBackend owner, BufferResource resource, P.GpuMapMode mode, void* pointer, int length)
        {
            this.owner = owner;
            this.resource = resource;
            this.mode = mode;
            memory = new(this, pointer, length);
        }

        private void RequireAccessible()
        {
            ObjectDisposedException.ThrowIf(disposed || resource.Destroyed || owner.disposed, this);
            owner.status.ThrowIfFailed();
        }

        public override Memory<byte> Memory
        {
            get
            {
                RequireAccessible();
                if (mode != P.GpuMapMode.Write) { throw new InvalidOperationException("A read mapping has no writable memory."); }
                return memory.Memory;
            }
        }

        public override ReadOnlyMemory<byte> ReadOnlyMemory
        {
            get { RequireAccessible(); return memory.Memory; }
        }

        public override void Dispose()
        {
            lock (owner.gate)
            {
                if (disposed) { return; }
                disposed = true;
                try { F.WebGPU_FFI.BufferUnmap(resource.Handle); }
                finally { F.WebGPU_FFI.BufferRelease(resource.Handle); }
            }
        }

        private sealed class MappedMemory(MappedRange range, void* pointer, int length) : MemoryManager<byte>
        {
            public override Span<byte> GetSpan()
            {
                range.RequireAccessible();
                return new(pointer, length);
            }

            public override MemoryHandle Pin(int elementIndex = 0)
            {
                range.RequireAccessible();
                if ((uint)elementIndex > (uint)length) { throw new ArgumentOutOfRangeException(nameof(elementIndex)); }
                return new((byte*)pointer + elementIndex);
            }

            public override void Unpin() { }
            protected override void Dispose(bool disposing) { }
        }
    }
}
