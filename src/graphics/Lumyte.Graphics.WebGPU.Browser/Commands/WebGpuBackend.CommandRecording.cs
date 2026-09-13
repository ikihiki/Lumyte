using P = Lumyte.Graphics.Portable;

namespace Lumyte.Graphics.WebGPU.Browser;

public sealed partial class WebGpuBackend
{
    private abstract record RecordedCommand;
    private sealed record BeginComputeCommand : RecordedCommand;
    private sealed record EndComputeCommand : RecordedCommand;
    private sealed record ComputePipelineCommand(P.GpuComputePipelineHandle Pipeline) : RecordedCommand;
    private sealed record ComputeBindingsCommand(uint Group, P.GpuBindingsHandle Bindings, uint[] DynamicOffsets) : RecordedCommand;
    private sealed record ComputeRootCommand(byte[] Bytes) : RecordedCommand;
    private sealed record DispatchCommand(uint X, uint Y, uint Z) : RecordedCommand;
    private sealed record DispatchIndirectCommand(P.GpuBufferRange Arguments) : RecordedCommand;
    private sealed record CopyBufferCommand(P.GpuBufferRange Source, P.GpuBufferRange Destination) : RecordedCommand;

    private sealed partial class CommandRecording(PortableQueue queue) : P.GpuCommandBuffer
    {
        internal readonly PortableQueue Queue = queue;
        internal readonly List<RecordedCommand> Commands = [];
        private bool computing;
        private bool rendering;
        private readonly List<TextureViewLease> attachmentViews = [];
        private bool consumed;
        private bool released;
        private bool disposed;

        private void RequireRecording()
        {
            Queue.Owner.RequireAvailable();
            ObjectDisposedException.ThrowIf(disposed, this);
            if (consumed) { throw new InvalidOperationException("A submitted or failed recording cannot be reused."); }
        }

        private void RequireCompute()
        {
            RequireRecording();
            if (!computing) { throw new InvalidOperationException("The command requires an open compute scope."); }
        }

        private void RequireOutsidePass()
        {
            RequireRecording();
            if (computing || rendering) { throw new InvalidOperationException("The command requires all pass scopes to be closed."); }
        }

        public override void BeginCompute()
        {
            lock (Queue.Owner.gate)
            {
                RequireOutsidePass();
                Commands.Add(new BeginComputeCommand());
                computing = true;
            }
        }

        public override void EndCompute()
        {
            lock (Queue.Owner.gate)
            {
                RequireCompute();
                Commands.Add(new EndComputeCommand());
                computing = false;
            }
        }

        public override void SetComputePipeline(P.GpuComputePipelineHandle pipeline)
        {
            lock (Queue.Owner.gate)
            {
                RequireCompute();
                Queue.Owner.RequireComputePipeline(pipeline);
                Commands.Add(new ComputePipelineCommand(pipeline));
            }
        }

        public override void SetComputeBindings(uint group, P.GpuBindingsHandle bindings, ReadOnlySpan<uint> dynamicOffsets = default)
        {
            lock (Queue.Owner.gate)
            {
                RequireCompute();
                Queue.Owner.RequireBindings(bindings);
                Commands.Add(new ComputeBindingsCommand(group, bindings, dynamicOffsets.ToArray()));
            }
        }

        public override void SetComputeRootData(ReadOnlySpan<byte> bytes)
        {
            lock (Queue.Owner.gate)
            {
                RequireCompute();
                Commands.Add(new ComputeRootCommand(bytes.ToArray()));
            }
        }

        public override void Dispatch(uint x, uint y = 1, uint z = 1)
        {
            lock (Queue.Owner.gate)
            {
                RequireCompute();
                Commands.Add(new DispatchCommand(x, y, z));
            }
        }

        public override void DispatchIndirect(P.GpuBufferRange arguments)
        {
            lock (Queue.Owner.gate)
            {
                RequireCompute();
                P.GpuBufferRange range = Queue.Owner.ResolveCommandRange(arguments);
                if (range.Length < 12) { throw new ArgumentOutOfRangeException(nameof(arguments), "The logical argument range must cover three uint32 values."); }
                Commands.Add(new DispatchIndirectCommand(range));
            }
        }

        public override void CopyBuffer(P.GpuBufferRange source, P.GpuBufferRange destination)
        {
            lock (Queue.Owner.gate)
            {
                RequireOutsidePass();
                P.GpuBufferRange resolvedSource = Queue.Owner.ResolveCommandRange(source);
                P.GpuBufferRange resolvedDestination = Queue.Owner.ResolveCommandRange(destination);
                if (resolvedSource.Length != resolvedDestination.Length)
                { throw new ArgumentException("Copy ranges must have the same byte length.", nameof(destination)); }
                Commands.Add(new CopyBufferCommand(resolvedSource, resolvedDestination));
            }
        }

        internal void ValidateSubmission()
        {
            RequireRecording();
            if (computing || rendering) { throw new InvalidOperationException("Close every pass scope before submitting its recording."); }
        }

        internal void Consume() => consumed = true;

        internal void Release()
        {
            if (released) { return; }
            released = true;
            foreach (TextureViewLease view in attachmentViews) { Queue.Owner.ReleaseTextureView(view); }
            attachmentViews.Clear();
            Commands.Clear();
        }

        public override void Dispose()
        {
            Queue.Owner.runtime.RequireThread();
            lock (Queue.Owner.gate)
            {
                if (disposed) { return; }
                disposed = true;
                if (!consumed) { Release(); }
            }
        }
    }

    private P.GpuBufferRange ResolveCommandRange(P.GpuBufferRange range)
    {
        BufferResource buffer = RequireBuffer(range.Buffer);
        // Only infer omitted lengths. Resource bounds, usage, and alignment are native validation.
        ulong length = range.Length ?? checked(buffer.Description.Size - range.Offset);
        return range with { Length = length };
    }
}
