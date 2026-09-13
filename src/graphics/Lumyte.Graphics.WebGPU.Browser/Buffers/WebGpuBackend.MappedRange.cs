using System.Buffers;
using System.Runtime.InteropServices.JavaScript;
using P = Lumyte.Graphics.Portable;

namespace Lumyte.Graphics.WebGPU.Browser;

public sealed partial class WebGpuBackend
{
    private sealed class MappedRange : P.GpuMappedBufferRange
    {
        private readonly WebGpuBackend owner;
        private readonly BufferResource resource;
        private readonly P.GpuMapMode mode;
        private readonly JSObject mapped;
        private readonly byte[] bytes;
        private readonly MappedMemory memory;
        private bool disposed;

        internal MappedRange(WebGpuBackend owner, BufferResource resource, P.GpuMapMode mode, JSObject mapped, byte[] bytes)
        {
            this.owner = owner;
            this.resource = resource;
            this.mode = mode;
            this.mapped = mapped;
            this.bytes = bytes;
            memory = new(this, bytes);
        }

        private void RequireAccessible()
        {
            owner.RequireAvailable();
            ObjectDisposedException.ThrowIf(disposed || resource.Destroyed, this);
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
            owner.runtime.RequireThread();
            if (disposed) { return; }
            disposed = true;
            try
            {
                if (!resource.Destroyed)
                {
                    try
                    {
                        if (mode == P.GpuMapMode.Write && !owner.disposed && !owner.status.Failure.IsCompleted)
                        { BrowserInterop.WriteMapped(mapped, bytes); }
                    }
                    finally { BrowserInterop.Unmap(resource.Handle); }
                }
            }
            finally { mapped.Dispose(); }
        }

        private sealed class MappedMemory(MappedRange range, byte[] bytes) : MemoryManager<byte>
        {
            public override Span<byte> GetSpan()
            {
                range.RequireAccessible();
                return bytes;
            }

            public override MemoryHandle Pin(int elementIndex = 0)
            {
                range.RequireAccessible();
                if ((uint)elementIndex > (uint)bytes.Length) { throw new ArgumentOutOfRangeException(nameof(elementIndex)); }
                return bytes.AsMemory(elementIndex).Pin();
            }

            public override void Unpin() { }
            protected override void Dispose(bool disposing) { }
        }
    }
}
