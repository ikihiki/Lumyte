using Ahjo.Wgpu.Native;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Wgpu;

internal sealed unsafe partial class WgpuCommandBuffer
{
    private readonly Dictionary<object, List<PreparedEntry>> _prepared = new(ReferenceEqualityComparer.Instance);
    private bool _recordingStarted;
    private bool _preparing;

    public void PrepareRender<T>(RenderPassDesc pass, IGraphicsPipeline pipeline, GraphicsRenderStateDesc state, IArgumentTable? table, in T arguments, IndexFormat? indexFormat = null)
        where T : struct, IShaderArguments
    {
        RequirePreparation();
        object identity = arguments.NativePreparationIdentity ?? throw new ArgumentException("Native preparation requires an immutable identity.", nameof(arguments));
        if (pipeline is not WgpuGraphicsPipeline program || !ReferenceEquals(program.Owner, _owner))
        {
            throw new ArgumentException("Pipeline belongs to another device.", nameof(pipeline));
        }

        program.ValidateAlive();
        if (program.RequiresArguments)
        {
            ValidatePreparedRoot<T>(ShaderDataLayout.RootTarget(program.VertexData, program.FragmentData, T.RootParameter));
        }

        RenderStateSnapshot snapshot = PipelineValidation.State(state, _owner.Caps);
        if (indexFormat is { } format && !Enum.IsDefined(format))
        {
            throw new ArgumentOutOfRangeException(nameof(indexFormat));
        }

        RenderColorAttachmentDesc[] attachments = ValidateRenderPass(pass);
        TextureFormat[] formats = attachments.Select(a => a.View.Info.Format).ToArray();
        TextureFormat? depth = pass.DepthStencilAttachment?.View.Info.Format;
        WgpuArgumentTable? selected = PreparationTable(table);
        _preparing = true;
        try
        {
            WgpuRootStorage root = CreatePreparedRoot(in arguments);
            KeepBinding(root);
            selected = RootTable(selected, root);
            IReadOnlyCollection<IShaderReference> registrations = selected?.SnapshotRegistrations() ?? Array.Empty<IShaderReference>();
            ShaderBindingData? data = program.RequiresArguments || selected != null
                ? new(registrations, program.RequiresArguments ? ShaderDataLayout.RootTarget(program.VertexData, program.FragmentData, root.Arguments.RootParameter) : program.VertexData, _owner.Caps) : null;
            WGPURenderPipelineImpl* resolved = program.Resolve(snapshot, formats, data, depth, indexFormat);
            WgpuShaderBinding? binding = null;
            if (data != null)
            {
                WGPUBindGroupLayoutImpl* layout = WGPU.wgpuRenderPipelineGetBindGroupLayout(resolved, 0);
                try
                {
                    binding = new(_owner, data, program.RequiresArguments ? root : null, layout, selected, registrations);
                }
                finally
                {
                    WGPU.wgpuBindGroupLayoutRelease(layout);
                }

                KeepBinding(binding);
            }

            AddPrepared(identity, new(program, typeof(T), root, selected, registrations, snapshot.Key, formats, depth, indexFormat, resolved, null, binding));
        }
        finally
        {
            _preparing = false;
        }
    }

    public void PrepareCompute<T>(IGraphicsComputePipeline pipeline, IArgumentTable? table, in T arguments)
        where T : struct, IShaderArguments
    {
        RequirePreparation();
        object identity = arguments.NativePreparationIdentity ?? throw new ArgumentException("Native preparation requires an immutable identity.", nameof(arguments));
        if (pipeline is not WgpuComputePipeline program || !ReferenceEquals(program.Owner, _owner))
        {
            throw new ArgumentException("Pipeline belongs to another device.", nameof(pipeline));
        }

        program.ValidateAlive();
        if (program.RequiresArguments)
        {
            ValidatePreparedRoot<T>(program.Data);
        }

        WgpuArgumentTable? selected = PreparationTable(table);
        _preparing = true;
        try
        {
            WgpuRootStorage root = CreatePreparedRoot(in arguments);
            KeepBinding(root);
            selected = RootTable(selected, root);
            IReadOnlyCollection<IShaderReference> registrations = selected?.SnapshotRegistrations() ?? Array.Empty<IShaderReference>();
            ShaderBindingData? data = program.RequiresArguments || selected != null ? new(registrations, program.Data, _owner.Caps) : null;
            WGPUComputePipelineImpl* resolved = program.Resolve(data);
            WgpuShaderBinding? binding = null;
            if (data != null)
            {
                WGPUBindGroupLayoutImpl* layout = WGPU.wgpuComputePipelineGetBindGroupLayout(resolved, 0);
                try
                {
                    binding = new(_owner, data, program.RequiresArguments ? root : null, layout, selected, registrations);
                }
                finally
                {
                    WGPU.wgpuBindGroupLayoutRelease(layout);
                }

                KeepBinding(binding);
            }

            AddPrepared(identity, new(program, typeof(T), root, selected, registrations, null, [], null, null, null, resolved, binding));
        }
        finally
        {
            _preparing = false;
        }
    }

    public void PrepareMesh<T>(RenderPassDesc pass, IMeshPipeline pipeline, GraphicsRenderStateDesc state, IArgumentTable? table, in T arguments)
        where T : struct, IShaderArguments
    {
        RequirePreparation();
        throw new NotSupportedException("Wgpu does not support Mesh/Task pipelines.");
    }

    internal PreparedEntry FindPrepared(object identity, Type argumentType, object program, WgpuArgumentTable? table, string? state, TextureFormat[] formats, TextureFormat? depth, IndexFormat? index)
    {
        if (_prepared.TryGetValue(identity, out List<PreparedEntry>? entries))
        {
            foreach (PreparedEntry entry in entries)
            {
                if (entry.Matches(argumentType, program, table, state, formats, depth, index))
                {
                    entry.Validate();
                    return entry;
                }
            }
        }

        throw new InvalidOperationException("No compatible native preparation exists on this command buffer.");
    }

    private static void ValidatePreparedRoot<T>(ShaderTargetData target)
        where T : struct, IShaderArguments
    {
        var layout = ShaderDataLayout.Root(target, T.RootParameter);
        if (layout.Name != T.ShaderTypeName || layout.Size != T.TransferSize)
        {
            throw new ArgumentException("Prepared argument wire identity differs from the selected program.");
        }
    }

    private static WgpuArgumentTable? RootTable(WgpuArgumentTable? selected, WgpuRootStorage root)
    {
        if (selected != null && root.ImplicitTable != null && !ReferenceEquals(selected, root.ImplicitTable))
        {
            throw new ArgumentException("Selected table differs from root references.");
        }

        return selected ?? root.ImplicitTable;
    }

    private WgpuRootStorage CreatePreparedRoot<T>(in T arguments)
        where T : struct, IShaderArguments
    {
        if (T.TransferSize != 0)
        {
            return WgpuShaderTransfer.CreateRoot(_owner, in arguments);
        }

        var root = new WgpuRootStorage(_owner, 0);
        try
        {
            root.Initialize(ShaderRootArguments.Create(T.RootParameter, T.ShaderTypeName, 0, null), null);
            root.Seal();
            return root;
        }
        catch
        {
            root.Dispose();
            throw;
        }
    }

    private void RequirePreparation()
    {
        ObjectDisposedException.ThrowIf(State == CommandBufferState.Disposed, this);
        if (State != CommandBufferState.Recording || _active != null || _recordingStarted || _preparing)
        {
            throw new InvalidOperationException("Native preparation must precede all command recording.");
        }
    }

    private WgpuArgumentTable? PreparationTable(IArgumentTable? table)
    {
        if (table == null)
        {
            return null;
        }

        if (table is not WgpuArgumentTable selected || !selected.BelongsTo(_owner))
        {
            throw new ArgumentException("Argument table belongs to another device.", nameof(table));
        }

        selected.ThrowIfDisposed();
        return selected;
    }

    private void AddPrepared(object identity, PreparedEntry entry)
    {
        if (!_prepared.TryGetValue(identity, out List<PreparedEntry>? entries))
        {
            entries = [];
            _prepared.Add(identity, entries);
        }

        entries.Add(entry);
    }

    internal sealed class PreparedEntry(object program, Type argumentType, WgpuRootStorage root, WgpuArgumentTable? table, IReadOnlyCollection<IShaderReference> registrations, string? state, TextureFormat[] formats, TextureFormat? depth, IndexFormat? index, WGPURenderPipelineImpl* render, WGPUComputePipelineImpl* compute, WgpuShaderBinding? binding)
    {
        private readonly long _generation = table?.Generation ?? 0;

        internal WgpuRootStorage Root { get; } = root;

        internal WgpuArgumentTable? Table { get; } = table;

        internal WGPURenderPipelineImpl* Render { get; } = render;

        internal WGPUComputePipelineImpl* Compute { get; } = compute;

        internal WgpuShaderBinding? Binding { get; } = binding;

        internal void Validate()
        {
            Table?.ThrowIfDisposed();
            foreach (IShaderReference registration in registrations)
            {
                if (registration.Resource is WgpuTextureView view)
                {
                    _ = view.Native;
                }
                else if (registration.Resource is WgpuSampler sampler)
                {
                    _ = sampler.Native;
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

            if (Table != null && Table.Generation != _generation)
            {
                throw new InvalidOperationException("Prepared argument table has changed.");
            }
        }

        internal bool Matches(Type type, object selectedProgram, WgpuArgumentTable? selectedTable, string? selectedState, TextureFormat[] selectedFormats, TextureFormat? selectedDepth, IndexFormat? selectedIndex)
            => argumentType == type && ReferenceEquals(program, selectedProgram) && ReferenceEquals(Table, selectedTable ?? Root.ImplicitTable)
                && state == selectedState && depth == selectedDepth && index == selectedIndex && formats.AsSpan().SequenceEqual(selectedFormats);
    }
}
