using System.Runtime.InteropServices.JavaScript;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

internal sealed class BrowserComputeEncoder(BrowserCommandBuffer owner, JSObject handle) : IComputeEncoder
{
    private BrowserComputePipeline? _pipeline;

    private Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? _argumentValidation;
    private BrowserRootStorage? _arguments;
    private object? _argumentProgram;
    private BrowserArgumentTable? _table;

    public void SetArgumentTable(IArgumentTable table)
    {
        owner.ValidatePass(this);
        ArgumentNullException.ThrowIfNull(table);
        if (table is not BrowserArgumentTable selected || !selected.BelongsTo(owner.Owner))
        {
            throw new ArgumentException("Argument table belongs to another device.", nameof(table));
        }

        selected.ThrowIfDisposed();
        owner.Owner.Validation?.Use(owner, selected);
        _table = selected;
    }

    public void SetArgumentValidation(Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? prepare)
    {
        owner.ValidatePass(this);
        _argumentValidation = prepare;
    }

    public void SetArguments<T>(in T value)
        where T : IShaderArguments
    {
        ValidateRootSelection();
        BrowserComputePipeline program = _pipeline!;
        BrowserRootStorage root = BrowserShaderTransfer.CreateRoot(owner.Owner, in value, diagnostic: owner.Owner.Validation == null ? null : DiagnosticRoot(T.RootParameter), prepare: _argumentValidation);
        SelectRoot(root, program);
    }

    public void SetArguments<T>(in T value, string rootParameter = "arguments", string? shaderTypeName = null)
        where T : unmanaged
    {
        ValidateRootSelection();
        BrowserComputePipeline program = _pipeline!;
        BrowserRootStorage root = BrowserShaderTransfer.CreateUnmanaged(owner.Owner, in value, rootParameter, shaderTypeName, diagnostic: DiagnosticRoot(rootParameter), prepare: _argumentValidation);
        SelectRoot(root, program);
    }

    public void SetPipeline(IGraphicsComputePipeline pipeline)
    {
        owner.ValidatePass(this);
        ArgumentNullException.ThrowIfNull(pipeline);
        if (pipeline is not BrowserComputePipeline program || !ReferenceEquals(program.Owner, owner.Owner))
        {
            throw new ArgumentException("Compute program belongs to another device.");
        }

        program.ValidateAlive();
        owner.Owner.Validation?.Use(owner, program);
        _pipeline = program;
    }

    public void Dispatch(uint groupCountX, uint groupCountY = 1, uint groupCountZ = 1)
    {
        using GraphicsValidation.RecordingTransaction? validationRecording = owner.Owner.Validation?.BeginRecording(owner);
        owner.ValidatePass(this);
        uint limit = owner.Owner.Caps.MaxComputeWorkgroupsPerDimension;
        if (groupCountX == 0 || groupCountY == 0 || groupCountZ == 0 || groupCountX > limit || groupCountY > limit || groupCountZ > limit)
        {
            throw new ArgumentOutOfRangeException(nameof(groupCountX));
        }

        PrepareDispatch();
        BrowserInterop.Dispatch(handle, groupCountX, groupCountY, groupCountZ);
        validationRecording?.Commit();
    }

    public void DispatchIndirect(BufferSlice<DispatchIndirectArguments> arguments)
    {
        using GraphicsValidation.RecordingTransaction? validationRecording = owner.Owner.Validation?.BeginRecording(owner);
        owner.ValidatePass(this);
        if (arguments.Count != 1 || arguments.OffsetInBytes % 4 != 0)
        {
            throw new ArgumentException("Indirect execution requires one four-byte-aligned command record.");
        }

        BrowserBuffer<DispatchIndirectArguments> buffer = owner.Buffer(arguments, BufferUsage.Indirect);
        PrepareDispatch();
        owner.Owner.Validation?.Access(owner, buffer, new(PipelineStage.DrawIndirect, ResourceAccess.IndirectRead), arguments.OffsetInBytes, arguments.SizeInBytes);
        BrowserInterop.DispatchIndirect(handle, buffer.Native, arguments.OffsetInBytes);
        validationRecording?.Commit();
    }

    public void End() => owner.EndCompute(this, handle);

    private void ValidateRootSelection()
    {
        owner.ValidatePass(this);
        if (_pipeline == null)
        {
            throw new InvalidOperationException("Select a program before setting its arguments.");
        }

        _pipeline.ValidateAlive();
    }

    private ShaderDataLayout? DiagnosticRoot(string rootParameter) => owner.Owner.Validation == null ? null : ShaderDataLayout.Root(_pipeline!.Data, rootParameter);

    private void SelectRoot(BrowserRootStorage root, BrowserComputePipeline program)
    {
        try
        {
            owner.ValidatePass(this);
            program.ValidateAlive();
            if (!ReferenceEquals(_pipeline, program))
            {
                throw new InvalidOperationException("The selected program changed while preparing root arguments.");
            }

            owner.KeepBinding(root);
        }
        catch
        {
            try
            {
                root.Dispose();
            }
            catch
            {
                // Preserve the original transfer or selection failure.
            }

            throw;
        }

        _arguments = root;
        _argumentProgram = program;
    }

    private void PrepareDispatch()
    {
        owner.ValidatePass(this);
        if (_pipeline == null)
        {
            throw new InvalidOperationException("Select a compute program before dispatch.");
        }

        if (_pipeline.RequiresArguments && _arguments != null && !ReferenceEquals(_argumentProgram, _pipeline))
        {
            throw new InvalidOperationException("Set arguments again after switching to a different program.");
        }

        ShaderBindingData? bindingData = null;
        if (_pipeline.RequiresArguments && _arguments != null)
        {
            _table?.ThrowIfDisposed();
            var dependencies = ShaderBindingDependencies.Collect(_arguments.Arguments, _table, owner.ReadShaderData);
            if (owner.Owner.Validation is { } validation)
            {
                EncoderValidation.ShaderAccesses(validation, owner, dependencies.References, PipelineStage.ComputeShader);
            }

            bindingData = new(dependencies, _pipeline.Data, owner.Owner.Caps);
        }

        _pipeline.ValidateAlive();

        BrowserInterop.SetComputePipeline(handle, _pipeline.Resolve(bindingData));
        if (bindingData != null)
        {
            var binding = new BrowserShaderBinding(owner.Owner, bindingData, _arguments!, true);
            owner.KeepBinding(binding);
            BrowserInterop.SetShaderBinding(handle, binding.Native);
        }
    }
}
