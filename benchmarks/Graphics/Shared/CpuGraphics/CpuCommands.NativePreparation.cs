using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Benchmarks.Testing;

internal sealed partial class CpuCommands
{
    private readonly List<NativePreparation> _preparations = [];
    private bool _preparing;
    private bool _recordingStarted;

    internal TextureFormat[] PassColors { get; private set; } = [];

    internal TextureFormat? PassDepth { get; private set; }

    internal long NativeAllocations { get; private set; }

    internal long NativeMaps { get; private set; }

    public void PrepareRender<T>(RenderPassDesc pass, IGraphicsPipeline pipeline, GraphicsRenderStateDesc state, IArgumentTable? table, in T arguments, IndexFormat? indexFormat = null)
        where T : struct, IShaderArguments
    {
        ArgumentNullException.ThrowIfNull(state);
        Prepare(pass, pipeline, state, table, in arguments, indexFormat);
    }

    public void PrepareCompute<T>(IGraphicsComputePipeline pipeline, IArgumentTable? table, in T arguments)
        where T : struct, IShaderArguments => Prepare(null, pipeline, null, table, in arguments, null);

    public void PrepareMesh<T>(RenderPassDesc pass, IMeshPipeline pipeline, GraphicsRenderStateDesc state, IArgumentTable? table, in T arguments)
        where T : struct, IShaderArguments => throw new NotSupportedException("Mesh execution is not modeled by the CPU benchmark device.");

    internal NativePreparation FindPreparation(object identity, Type argumentType, CpuResource? pipeline, CpuTable? table, GraphicsRenderStateDesc? state, IndexFormat? index)
    {
        Recording(true);
        foreach (NativePreparation entry in _preparations)
        {
            if (ReferenceEquals(entry.Identity, identity) && entry.ArgumentType == argumentType && ReferenceEquals(entry.Program, pipeline) && ReferenceEquals(entry.Table, table)
                && entry.Index == index && (IsCompute || (StateEquals(entry.RenderState, state) && entry.Depth == PassDepth && entry.Colors.AsSpan().SequenceEqual(PassColors))))
            {
                entry.Program.Check();
                entry.Table?.Check();
                foreach (Action validate in entry.Registrations)
                {
                    validate();
                }

                return entry;
            }
        }

        throw new InvalidOperationException("No matching command-local native preparation.");
    }

    private static bool StateEquals(GraphicsRenderStateDesc? left, GraphicsRenderStateDesc? right)
        => left != null && right != null && left.Topology == right.Topology && left.StripIndexFormat == right.StripIndexFormat
            && left.Rasterization == right.Rasterization && left.DepthStencil == right.DepthStencil && left.SampleMask == right.SampleMask
            && left.ColorTargets.SequenceEqual(right.ColorTargets);

    private void Prepare<T>(RenderPassDesc? pass, object pipeline, GraphicsRenderStateDesc? state, IArgumentTable? table, in T arguments, IndexFormat? index)
        where T : struct, IShaderArguments
    {
        Check();
        if (_recordingStarted || _preparing || _pass || State != CommandBufferState.Recording)
        {
            throw new InvalidOperationException("Native preparation must precede recording.");
        }

        object identity = arguments.NativePreparationIdentity ?? throw new ArgumentException("Preparation requires an immutable identity.");
        _preparing = true;
        try
        {
            CpuResource program = Owner.Own(pipeline);
            CpuTable? nativeTable = table == null ? null : (CpuTable)Owner.Own(table);
            TextureFormat[] colors = [];
            TextureFormat? depth = null;
            if (pass != null)
            {
                if (pass.ColorAttachments.Count == 0 && pass.DepthStencilAttachment == null)
                {
                    throw new ArgumentException("Render pass requires attachments.");
                }

                PassWidth = PassHeight = 0;
                foreach (RenderColorAttachmentDesc attachment in pass.ColorAttachments)
                {
                    Attachment(attachment.View);
                }

                if (pass.DepthStencilAttachment is { } attachmentDepth)
                {
                    Attachment(attachmentDepth.View);
                    depth = attachmentDepth.View.Texture.Format;
                }

                colors = pass.ColorAttachments.Select(a => a.View.Texture.Format).ToArray();
            }

            byte[] wire = new byte[T.TransferSize];
            NativeAllocations++;
            NativeMaps++;
            var writer = new CpuPreparationWriter(Owner, wire);
            if (wire.Length != 0)
            {
                arguments.Write(ref writer);
                writer.Complete();
            }

            if (writer.Table != null && writer.Table != nativeTable)
            {
                throw new ArgumentException("Select the argument table that owns the root references.");
            }

            GraphicsRenderStateDesc? snapshot = state == null ? null : state with { ColorTargets = Array.AsReadOnly(state.ColorTargets.ToArray()) };
            _preparations.Add(new(identity, typeof(T), program, nativeTable, wire, snapshot, colors, depth, index, writer.Registrations));
        }
        finally
        {
            _preparing = false;
        }
    }

    internal sealed record NativePreparation(object Identity, Type ArgumentType, CpuResource Program, CpuTable? Table, byte[] Wire, GraphicsRenderStateDesc? RenderState, TextureFormat[] Colors, TextureFormat? Depth, IndexFormat? Index, Action[] Registrations);
}
