using Lumyte.Graphics.Abstractions;
using V = Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

internal sealed unsafe partial class VulkanCommandBuffer
{
    private readonly Dictionary<object, List<NativePreparation>> _prepared = new(ReferenceEqualityComparer.Instance);
    private bool _recordingStarted;
    private bool _preparingNative;

    public void PrepareRender<T>(RenderPassDesc pass, IGraphicsPipeline pipeline, GraphicsRenderStateDesc state, IArgumentTable? table, in T arguments, IndexFormat? indexFormat = null)
        where T : struct, IShaderArguments
    {
        if (pipeline is not VulkanGraphicsPipeline program || !ReferenceEquals(program.Owner, Owner))
        {
            throw new ArgumentException("Pipeline belongs to another device.", nameof(pipeline));
        }

        PrepareGraphics(pass, program, state, table, in arguments, indexFormat);
    }

    public void PrepareMesh<T>(RenderPassDesc pass, IMeshPipeline pipeline, GraphicsRenderStateDesc state, IArgumentTable? table, in T arguments)
        where T : struct, IShaderArguments
    {
        if (pipeline is not VulkanMeshPipeline mesh || !ReferenceEquals(mesh.Program.Owner, Owner))
        {
            throw new ArgumentException("Pipeline belongs to another device.", nameof(pipeline));
        }

        PrepareGraphics(pass, mesh.Program, state, table, in arguments, null);
    }

    public void PrepareCompute<T>(IGraphicsComputePipeline pipeline, IArgumentTable? table, in T arguments)
        where T : struct, IShaderArguments
    {
        RequirePreparation();
        if (pipeline is not VulkanComputePipeline program || !ReferenceEquals(program.Owner, Owner))
        {
            throw new ArgumentException("Pipeline belongs to another device.", nameof(pipeline));
        }

        program.ValidateAlive();
        PrepareNative(program, table, in arguments, null, [], null, null);
    }

    internal static ShaderTargetData PreparationRootTarget(ShaderTargetData vertex, ShaderTargetData? task, ShaderTargetData? fragment, string name)
    {
        if (task != null && ShaderDataLayout.HasRoot(task, name) && !ShaderDataLayout.HasRoot(vertex, name))
        {
            return task;
        }

        return ShaderDataLayout.RootTarget(vertex, fragment, name);
    }

    internal NativePreparation FindPreparation(object identity, Type argumentType, object program, VulkanArgumentTable? table, RenderStateSnapshot? state, TextureFormat[] formats, TextureFormat? depth, IndexFormat? index)
    {
        if (State != CommandBufferState.Recording)
        {
            throw new InvalidOperationException("Prepared arguments have expired.");
        }

        if (!_prepared.TryGetValue(identity, out List<NativePreparation>? entries))
        {
            throw new InvalidOperationException("Arguments have not been prepared on this command.");
        }

        foreach (NativePreparation entry in entries)
        {
            if (entry.ArgumentType == argumentType && ReferenceEquals(entry.Program, program) && ReferenceEquals(entry.Table, table ?? entry.ImplicitTable) &&
                entry.Index == index && entry.Depth == depth && entry.Formats.AsSpan().SequenceEqual(formats) &&
                (state == null ? entry.State == null : entry.State != null && state.Key.Equals(entry.State.Key)))
            {
                entry.Table?.ThrowIfDisposed();
                if (entry.Table != null && !entry.Table.HasRegistrations(entry.Registrations!))
                {
                    throw new InvalidOperationException("Prepared argument table has changed.");
                }

                if (entry.Registrations != null)
                {
                    VulkanShaderBinding.ValidateResources(entry.Registrations);
                }

                return entry;
            }
        }

        throw new InvalidOperationException("Prepared arguments do not match this program, table, render state, attachment formats or index format.");
    }

    private static void ValidatePreparationAbi<T>(ShaderDataLayout layout)
        where T : struct, IShaderArguments
    {
        if (layout.Name != T.ShaderTypeName || layout.Size != T.TransferSize)
        {
            throw new ArgumentException("Prepared argument wire size does not match the shader root.");
        }
    }

    private void RequirePreparation()
    {
        ObjectDisposedException.ThrowIf(State == CommandBufferState.Disposed, this);
        if (State != CommandBufferState.Recording || _active != null || _recordingStarted || _preparingNative)
        {
            throw new InvalidOperationException("Native preparation must precede all recording.");
        }
    }

    private void PrepareGraphics<T>(RenderPassDesc pass, VulkanGraphicsPipeline program, GraphicsRenderStateDesc state, IArgumentTable? table, in T arguments, IndexFormat? index)
        where T : struct, IShaderArguments
    {
        RequirePreparation();
        ArgumentNullException.ThrowIfNull(pass);
        ArgumentNullException.ThrowIfNull(pass.ColorAttachments);
        program.ValidateAlive();
        if (index is { } format && !Enum.IsDefined(format))
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        (RenderColorAttachmentDesc[] attachments, RenderDepthStencilAttachmentDesc? depthAttachment) = ValidateRenderPass(pass);
        TextureFormat[] formats = attachments.Select(a => a.View.Info.Format).ToArray();
        TextureFormat? depth = depthAttachment?.View.Info.Format;

        RenderStateSnapshot snapshot = PipelineValidation.State(state, Owner.Caps);
        PrepareNative(program, table, in arguments, snapshot, formats, depth, index);
    }

    private void PrepareNative<T>(object program, IArgumentTable? table, in T arguments, RenderStateSnapshot? state, TextureFormat[] formats, TextureFormat? depth, IndexFormat? index)
        where T : struct, IShaderArguments
    {
        object identity = arguments.NativePreparationIdentity ?? throw new ArgumentException("Preparation requires a stable argument identity.", nameof(arguments));
        VulkanArgumentTable? selected = null;
        if (table != null)
        {
            if (table is not VulkanArgumentTable native || !native.BelongsTo(Owner))
            {
                throw new ArgumentException("Argument table belongs to another device.", nameof(table));
            }

            native.ThrowIfDisposed();
            selected = native;
        }

        _preparingNative = true;
        try
        {
            if (program is VulkanGraphicsPipeline graphicsProgram && graphicsProgram.RequiresArguments)
            {
                ValidatePreparationAbi<T>(ShaderDataLayout.Root(PreparationRootTarget(graphicsProgram.VertexData, graphicsProgram.TaskData, graphicsProgram.FragmentData, T.RootParameter), T.RootParameter));
            }
            else if (program is VulkanComputePipeline computeProgram && computeProgram.RequiresArguments)
            {
                ValidatePreparationAbi<T>(ShaderDataLayout.Root(computeProgram.Data, T.RootParameter));
            }

            byte[] bytes = new byte[VulkanPushConstants.ValidateStackSize(Owner, T.TransferSize)];
            ShaderRootArguments root;
            VulkanArgumentTable? hint;
            if (T.TransferSize == 0)
            {
                root = ShaderRootArguments.Create(T.RootParameter, T.ShaderTypeName, 0, null);
                hint = null;
            }
            else
            {
                (root, hint) = VulkanShaderTransfer.CreateRoot(Owner, in arguments, bytes);
            }

            if (selected != null && hint != null && !ReferenceEquals(selected, hint))
            {
                throw new ArgumentException("Selected table differs from root references.");
            }

            selected ??= hint;
            selected?.ThrowIfDisposed();
            IShaderReference[]? registrations = selected?.SnapshotRegistrations();
            if (registrations != null)
            {
                VulkanShaderBinding.ValidateResources(registrations);
            }

            V.Pipeline pipeline;
            V.DescriptorSetLayout layout;
            V.PipelineLayout pipelineLayout;
            if (program is VulkanGraphicsPipeline graphics)
            {
                graphics.ValidateAlive();
                pipeline = graphics.Resolve(state!, formats, selected?.LayoutKey, depth, index);
                layout = graphics.ArgumentLayout;
                pipelineLayout = graphics.ArgumentPipelineLayout;
            }
            else
            {
                var compute = (VulkanComputePipeline)program;
                compute.ValidateAlive();
                pipeline = compute.Resolve(selected?.LayoutKey);
                layout = compute.ArgumentLayout;
                pipelineLayout = compute.ArgumentPipelineLayout;
            }

            VulkanShaderBinding? binding = registrations == null ? null : selected!.AcquireBinding(registrations, layout);
            if (binding != null)
            {
                KeepBinding(binding, registrations!);
            }

            if (State != CommandBufferState.Recording || _recordingStarted || _active != null)
            {
                throw new InvalidOperationException("Command changed during native preparation.");
            }

            var entry = new NativePreparation(typeof(T), program, selected, hint, root, bytes, pipeline, pipelineLayout, binding, registrations, state, formats, depth, index);
            if (!_prepared.TryGetValue(identity, out List<NativePreparation>? entries))
            {
                entries = [];
                _prepared.Add(identity, entries);
            }

            entries.Add(entry);
        }
        finally
        {
            _preparingNative = false;
        }
    }

    internal sealed record NativePreparation(Type ArgumentType, object Program, VulkanArgumentTable? Table, VulkanArgumentTable? ImplicitTable, ShaderRootArguments Root, byte[] Bytes, V.Pipeline Pipeline, V.PipelineLayout PipelineLayout, VulkanShaderBinding? Binding, IShaderReference[]? Registrations, RenderStateSnapshot? State, TextureFormat[] Formats, TextureFormat? Depth, IndexFormat? Index);
}
