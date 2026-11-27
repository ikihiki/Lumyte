using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Direct3D12;

internal sealed unsafe partial class Direct3D12CommandBuffer
{
    private readonly List<NativePreparation> _preparations = [];
    private bool _preparing;

    public void PrepareRender<T>(RenderPassDesc pass, IGraphicsPipeline pipeline, GraphicsRenderStateDesc state, IArgumentTable? table, in T arguments, IndexFormat? indexFormat = null)
        where T : struct, IShaderArguments
        => PrepareNative(pass, pipeline, state, table, in arguments, indexFormat);

    public void PrepareMesh<T>(RenderPassDesc pass, IMeshPipeline pipeline, GraphicsRenderStateDesc state, IArgumentTable? table, in T arguments)
        where T : struct, IShaderArguments
        => PrepareNative(pass, pipeline, state, table, in arguments, null);

    public void PrepareCompute<T>(IGraphicsComputePipeline pipeline, IArgumentTable? table, in T arguments)
        where T : struct, IShaderArguments
        => PrepareNative(null, pipeline, null, table, in arguments, null);

    internal NativePreparation FindPreparation(object identity, Type argumentType, object program, RenderStateSnapshot? state, TextureFormat[] formats, TextureFormat? depth, IndexFormat? index, Direct3D12ArgumentTable? table)
    {
        foreach (NativePreparation entry in _preparations)
        {
            if (entry.ArgumentType == argumentType && ReferenceEquals(entry.Identity, identity) && ReferenceEquals(entry.Program, program) && entry.State?.Key == state?.Key && entry.Depth == depth && entry.Index == index && entry.Formats.AsSpan().SequenceEqual(formats) && (table == null || ReferenceEquals(table, entry.Table)))
            {
                entry.Table?.ThrowIfDisposed();
                if (entry.Table != null && entry.Table.Generation != entry.Generation)
                {
                    throw new InvalidOperationException("Argument table changed after native preparation.");
                }

                ValidatePreparationResources(entry.Registrations);
                return entry;
            }
        }

        throw new InvalidOperationException("No compatible command-local native preparation.");
    }

    private static void ValidatePreparationAbi<T>(object pipeline)
        where T : struct, IShaderArguments
    {
        IEnumerable<IGraphicsShader> shaders = pipeline switch
        {
            Direct3D12GraphicsPipeline graphics => new[] { graphics.Desc.VertexShader, graphics.Desc.FragmentShader }.OfType<IGraphicsShader>(),
            Direct3D12MeshPipeline mesh => new[] { mesh.Desc.MeshShader, mesh.Desc.TaskShader, mesh.Desc.FragmentShader }.OfType<IGraphicsShader>(),
            Direct3D12ComputePipeline compute => [compute.Desc.ComputeShader],
            _ => throw new ArgumentException("Foreign pipeline."),
        };
        bool required = false;
        foreach (IGraphicsShader shader in shaders)
        {
            ShaderTargetData target = shader.Artifact.GetTarget(ShaderTarget.Dxil);
            if (!ShaderDataLayout.HasRoot(target))
            {
                continue;
            }

            required = true;
            var layout = ShaderDataLayout.Root(target, T.RootParameter);
            if (layout.Name != T.ShaderTypeName || layout.Size != T.TransferSize)
            {
                throw new ArgumentException("Prepared argument ABI differs from the shader root.");
            }
        }

        if (!required && T.TransferSize != 0)
        {
            throw new ArgumentException("The selected program has no root arguments.");
        }
    }

    private static void ValidatePreparationResources(IShaderReference[] registrations)
    {
        foreach (IShaderReference registration in registrations)
        {
            if (registration.Resource is Direct3D12TextureView view)
            {
                _ = view.Native;
            }
            else if (registration.Resource is Direct3D12Sampler sampler)
            {
                sampler.ValidateAlive();
            }
            else if (registration.Resource is IShaderRawBuffer raw)
            {
                _ = raw.ShaderHandle;
            }
            else if (registration.Resource is IShaderDataSource data)
            {
                _ = data.ShaderHandle;
            }
        }
    }

    private void PrepareNative<T>(RenderPassDesc? pass, object pipeline, GraphicsRenderStateDesc? state, IArgumentTable? table, in T arguments, IndexFormat? index)
        where T : struct, IShaderArguments
    {
        RequireRecording(true);
        if (_recordingStarted || _preparing)
        {
            throw new InvalidOperationException("Native preparation must precede all recording.");
        }

        object identity = arguments.NativePreparationIdentity ?? throw new ArgumentException("Preparation requires an immutable identity.");
        _preparing = true;
        try
        {
            Direct3D12ArgumentTable? selected = null;
            if (table != null)
            {
                selected = table as Direct3D12ArgumentTable;
                if (selected == null || !selected.BelongsTo(_owner))
                {
                    throw new ArgumentException("Foreign argument table.");
                }

                selected.ThrowIfDisposed();
            }

            RenderStateSnapshot? snapshot = state == null ? null : PipelineValidation.State(state, _owner.Caps);
            TextureFormat[] formats = [];
            TextureFormat? depth = null;
            if (pass != null)
            {
                (RenderColorAttachmentDesc[] attachments, RenderDepthStencilAttachmentDesc? depthAttachment) = ValidateRenderPass(pass);
                formats = attachments.Select(a => a.View.Info.Format).ToArray();
                depth = depthAttachment?.View.Info.Format;
            }

            ValidatePreparationAbi<T>(pipeline);
            byte[] payload = new byte[Direct3D12ShaderTransfer.ValidateRootSize(T.TransferSize) * 4];
            ShaderRootArguments root;
            Direct3D12ArgumentTable? hint;
            if (T.TransferSize == 0)
            {
                root = ShaderRootArguments.Create(T.RootParameter, T.ShaderTypeName, 0, null);
                hint = null;
            }
            else
            {
                (root, hint) = Direct3D12ShaderTransfer.CreateRoot(_owner, in arguments, payload);
            }

            if (selected != null && hint != null && !ReferenceEquals(selected, hint))
            {
                throw new ArgumentException("Root references differ from the explicit table.");
            }

            selected ??= hint;
            selected?.ThrowIfDisposed();
            RequireRecording(true);
            IShaderReference[] registrations = selected?.SnapshotRegistrations() ?? [];
            ValidatePreparationResources(registrations);
            nint native;
            if (pipeline is Direct3D12GraphicsPipeline graphics && ReferenceEquals(graphics.Owner, _owner))
            {
                native = graphics.Resolve(snapshot!, formats, depth, index);
            }
            else if (pipeline is Direct3D12MeshPipeline mesh && ReferenceEquals(mesh.Owner, _owner))
            {
                native = mesh.Resolve(snapshot!, formats, depth);
            }
            else if (pipeline is Direct3D12ComputePipeline compute && ReferenceEquals(compute.Owner, _owner))
            {
                compute.ValidateAlive();
                native = compute.Native;
            }
            else
            {
                throw new ArgumentException("Foreign pipeline.");
            }

            RequireRecording(true);
            Direct3D12ShaderBinding? binding = selected == null ? null : new(_owner, selected, registrations);
            if (binding != null)
            {
                KeepBinding(binding);
            }

            _preparations.Add(new(identity, typeof(T), pipeline, snapshot, formats, depth, index, selected, root, payload, native, binding, registrations, selected?.Generation ?? 0));
        }
        finally
        {
            _preparing = false;
        }
    }

    internal sealed record NativePreparation(object Identity, Type ArgumentType, object Program, RenderStateSnapshot? State, TextureFormat[] Formats, TextureFormat? Depth, IndexFormat? Index, Direct3D12ArgumentTable? Table, ShaderRootArguments Root, byte[] Payload, nint Pipeline, Direct3D12ShaderBinding? Binding, IShaderReference[] Registrations, long Generation);
}
