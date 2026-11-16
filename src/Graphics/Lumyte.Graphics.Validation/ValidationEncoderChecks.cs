using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;

namespace Lumyte.Graphics.Validation;

internal sealed class ValidationEncoderChecks(ValidationCommandBuffer owner, object encoder)
{
    private bool _setting;
    private bool _executing;
    private object? _program;
    private object? _rootProgram;
    private IArgumentTable? _table;
    private IShaderReferenceMetadata? _root;
    private IArgumentTable? _rootHint;
    private Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? _prepare;
    private Action<IReadOnlyCollection<IShaderReference>>? _admit;

    internal static ShaderTargetData Target(ValidationDevice device, IGraphicsShader shader) => shader.Artifact.GetTarget(device.Caps.ShaderTarget);

    internal static ShaderDataLayout RenderRoot(ValidationDevice device, IGraphicsPipeline pipeline, string root)
    {
        ShaderTargetData vertex = Target(device, pipeline.Desc.VertexShader);
        ShaderTargetData? fragment = pipeline.Desc.FragmentShader is { } shader ? Target(device, shader) : null;
        ShaderDataLayout.ValidateProgram(vertex, fragment);
        return ShaderDataLayout.Root(ShaderDataLayout.RootTarget(vertex, fragment, root), root);
    }

    internal static PipelineStage RenderStages(ValidationDevice device, IGraphicsPipeline pipeline, string root)
    {
        PipelineStage stages = ShaderDataLayout.HasRoot(Target(device, pipeline.Desc.VertexShader), root) ? PipelineStage.VertexShader : PipelineStage.None;
        if (pipeline.Desc.FragmentShader is { } shader && ShaderDataLayout.HasRoot(Target(device, shader), root))
        {
            stages |= PipelineStage.FragmentShader;
        }

        return stages;
    }

    internal static IEnumerable<(IGraphicsShader Shader, PipelineStage Stage)> MeshShaders(MeshPipelineDesc desc)
    {
        yield return (desc.MeshShader, PipelineStage.MeshShader);
        if (desc.TaskShader is { } task)
        {
            yield return (task, PipelineStage.TaskShader);
        }

        if (desc.FragmentShader is { } fragment)
        {
            yield return (fragment, PipelineStage.FragmentShader);
        }
    }

    internal static void ValidateMeshRoots(ValidationDevice device, MeshPipelineDesc desc)
    {
        ShaderTargetData[] targets = MeshShaders(desc).Select(pair => Target(device, pair.Shader)).ToArray();
        for (int i = 0; i < targets.Length; i++)
        {
            for (int j = i + 1; j < targets.Length; j++)
            {
                ShaderDataLayout.ValidateProgram(targets[i], targets[j]);
            }
        }
    }

    internal static ShaderDataLayout MeshRoot(ValidationDevice device, IMeshPipeline pipeline, string root)
    {
        ValidateMeshRoots(device, pipeline.Desc);
        ShaderTargetData? target = MeshShaders(pipeline.Desc).Select(pair => Target(device, pair.Shader)).FirstOrDefault(target => ShaderDataLayout.HasRoot(target, root));
        return ShaderDataLayout.Root(target ?? throw new ArgumentException("The program does not declare this root parameter."), root);
    }

    internal static IReadOnlyList<(ulong Offset, ulong End, ResourceAccess Access)> Partition(IEnumerable<(ulong Offset, ulong End, ResourceAccess Access)> ranges)
    {
        var events = new SortedDictionary<ulong, List<(ResourceAccess Access, int Delta)>>();
        foreach ((ulong Offset, ulong End, ResourceAccess Access) range in ranges)
        {
            if (range.Offset >= range.End || range.Access == ResourceAccess.None)
            {
                continue;
            }

            Add(range.Offset, range.Access, 1);
            Add(range.End, range.Access, -1);
        }

        var result = new List<(ulong Offset, ulong End, ResourceAccess Access)>();
        var counts = new Dictionary<ResourceAccess, int>();
        ulong previous = 0;
        ResourceAccess active = ResourceAccess.None;
        foreach (KeyValuePair<ulong, List<(ResourceAccess Access, int Delta)>> point in events)
        {
            if (previous < point.Key && active != ResourceAccess.None)
            {
                if (result.Count > 0 && result[^1].End == previous && result[^1].Access == active)
                {
                    result[^1] = (result[^1].Offset, point.Key, active);
                }
                else
                {
                    result.Add((previous, point.Key, active));
                }
            }

            foreach ((ResourceAccess Access, int Delta) change in point.Value)
            {
                counts.TryGetValue(change.Access, out int count);
                counts[change.Access] = count + change.Delta;
            }

            active = ResourceAccess.None;
            foreach (KeyValuePair<ResourceAccess, int> count in counts)
            {
                if (count.Value > 0)
                {
                    active |= count.Key;
                }
            }

            previous = point.Key;
        }

        return result;

        void Add(ulong offset, ResourceAccess access, int delta)
        {
            if (!events.TryGetValue(offset, out List<(ResourceAccess Access, int Delta)>? changes))
            {
                events.Add(offset, changes = []);
            }

            changes.Add((access, delta));
        }
    }

    internal void Check(bool allowSetting = false)
    {
        if (_executing || (_setting && !allowSetting))
        {
            throw new InvalidOperationException("Root argument preparation is already active.");
        }

        owner.ValidatePass(encoder);
    }

    internal void SetValidation(Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? prepare)
    {
        Check();
        _prepare = prepare;
    }

    internal void SelectPipeline(object pipeline)
    {
        Check();
        _program = pipeline;
    }

    internal void SelectTable(IArgumentTable table, Action<IArgumentTable> native)
    {
        Check(true);
        IArgumentTable raw = owner.Device.Unwrap(table);
        using GraphicsValidation.RecordingTransaction tx = owner.Device.Tracker.BeginRecording(owner);
        owner.Device.Tracker.Use(owner, table);
        native(raw);
        _table = table;
        tx.Commit();
    }

    internal void BeginArguments()
    {
        Check();
        if (_program == null)
        {
            throw new InvalidOperationException("Select a program before setting arguments.");
        }

        owner.Device.UnwrapObject(_program);
        _setting = true;
    }

    internal void EndArguments() => _setting = false;

    internal ValidationWriteContext Context(ShaderDataLayout diagnostic, Action<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?> selected)
        => new(owner.Device, diagnostic, metadata =>
        {
            IArgumentTable? hint = RootHint(metadata);
            Action<IReadOnlyCollection<IShaderReference>>? admit = _prepare?.Invoke(metadata);
            Check(true);
            owner.Device.UnwrapObject(_program!);
            foreach (IShaderReference reference in metadata.References)
            {
                ((IValidationReference)reference).ValidateRegistration();
            }

            if (_table != null && hint != null && !ReferenceEquals(_table, hint))
            {
                throw new ArgumentException("Selected table differs from root references.");
            }

            selected(metadata, admit);
        });

    internal void PrepareUnmanaged(ShaderDataLayout diagnostic, int size, IReadOnlyList<ShaderTransferField> fields, string name, Action<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?> selected)
    {
        diagnostic.ValidateTransferLayout(size, fields, name);
        diagnostic.ValidateReferenceCount(0);
        var empty = new ShaderDependencyMetadata([]);
        Action<IReadOnlyCollection<IShaderReference>>? admit = _prepare?.Invoke(empty);
        Check(true);
        selected(empty, admit);
    }

    internal void CommitRoot(IShaderReferenceMetadata metadata, Action<IReadOnlyCollection<IShaderReference>>? admit)
    {
        _root = metadata;
        _rootHint = RootHint(metadata);
        _rootProgram = _program;
        _admit = admit;
    }

    internal void Execute(bool requiresArguments, PipelineStage stages, bool executes = true)
    {
        Check();
        if (_program == null)
        {
            throw new InvalidOperationException("Select a program before execution.");
        }

        owner.Device.UnwrapObject(_program);
        if (requiresArguments && (_root == null || !ReferenceEquals(_program, _rootProgram)))
        {
            throw new InvalidOperationException("Set arguments for the selected program before execution.");
        }

        if (_table != null && _rootHint != null && !ReferenceEquals(_table, _rootHint))
        {
            throw new ArgumentException("Selected table differs from root references.");
        }

        IArgumentTable? table = _table ?? _rootHint;
        if (table != null)
        {
            owner.Device.Unwrap(table);
        }

        if (!requiresArguments || _root == null)
        {
            return;
        }

        var references = new HashSet<IShaderReference>(ReferenceEqualityComparer.Instance);
        var buffers = new HashSet<IShaderDataSource>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<IShaderReferenceMetadata>();
        pending.Push(_root);
        while (pending.TryPop(out IShaderReferenceMetadata? metadata))
        {
            foreach (IShaderReference reference in metadata.References)
            {
                if (reference is not IValidationReference validated || !ReferenceEquals(validated.Device, owner.Device))
                {
                    throw new ArgumentException("Foreign shader reference.");
                }

                validated.ValidateRegistration();
                if (table == null || !ReferenceEquals(reference.Table, table))
                {
                    throw new ArgumentException("Shader reference uses a different argument table.");
                }

                if (!references.Add(reference))
                {
                    continue;
                }

                if (reference.Resource is IShaderDataSource data && buffers.Add(data))
                {
                    pending.Push(owner.ReadShaderDependencies(data));
                }
            }
        }

        _executing = true;
        try
        {
            _admit?.Invoke(references);
        }
        finally
        {
            _executing = false;
        }

        Check();
        owner.Device.UnwrapObject(_program!);
        foreach (IShaderReference reference in references)
        {
            ((IValidationReference)reference).ValidateRegistration();
        }

        ShaderAccesses(owner, references, stages, executes);
    }

    private static IArgumentTable? RootHint(IShaderReferenceMetadata metadata)
    {
        IArgumentTable? table = null;
        foreach (IShaderReference reference in metadata.References)
        {
            if (reference.Table is not IArgumentTable current)
            {
                throw new ArgumentException("Invalid reference table.");
            }

            if (table != null && !ReferenceEquals(table, current))
            {
                throw new ArgumentException("Root references use different argument tables.");
            }

            table = current;
        }

        return table;
    }

    private static void ShaderAccesses(ValidationCommandBuffer command, IEnumerable<IShaderReference> references, PipelineStage stages, bool executes)
    {
        GraphicsValidation validation = command.Device.Tracker;
        var buffers = new List<(object Storage, ulong Offset, ulong End, ResourceAccess Access)>();
        foreach (IShaderReference reference in references)
        {
            validation.Reference(command, (IValidatedShaderReference)reference);
            validation.Use(command, reference.Table);
            validation.Use(command, reference.Resource);
            if (!executes)
            {
                continue;
            }

            if (reference.Resource is IGraphicsTextureView view)
            {
                TextureViewInfo info = view.Info;
                validation.TextureAccess(command, view.Texture, new(info.BaseMipLevel, info.MipLevelCount, info.BaseArrayLayer, info.ArrayLayerCount), TextureState.Sampled, new(stages, ResourceAccess.ShaderRead));
            }
            else if (reference.Resource is not IGraphicsSampler)
            {
                ResourceAccess access = reference.BufferAccess == ShaderBufferAccess.ReadWrite ? ResourceAccess.ShaderRead | ResourceAccess.ShaderWrite : ResourceAccess.ShaderRead;
                object storage = command.Device.ResourceIdentity(reference.Resource);
                ulong offset = reference.OffsetInBytes;
                ulong end = checked(offset + reference.SizeInBytes);
                buffers.Add((storage, offset, end, access));
            }
        }

        // Partition each allocation once per execution: overlapping views must not
        // create self WAW hazards or widen writes into read-only prefixes/suffixes.
        foreach (IGrouping<object, (object Storage, ulong Offset, ulong End, ResourceAccess Access)> allocation in buffers.GroupBy(buffer => buffer.Storage, ReferenceEqualityComparer.Instance))
        {
            foreach ((ulong Offset, ulong End, ResourceAccess Access) segment in Partition(allocation.Select(buffer => (buffer.Offset, buffer.End, buffer.Access))))
            {
                validation.Access(command, allocation.Key, new(stages, segment.Access), segment.Offset, checked(segment.End - segment.Offset));
            }
        }
    }
}
