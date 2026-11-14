using System.Runtime.InteropServices.JavaScript;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

internal sealed class BrowserComputeEncoder(BrowserCommandBuffer owner, JSObject handle) : IComputeEncoder
{
    private BrowserComputePipeline? _pipeline;

    private Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? _argumentValidation;
    private BrowserRootStorage? _arguments;
    private object? _argumentProgram;
    private bool _settingArguments;
    private BrowserArgumentTable? _table;
    private BrowserArgumentTable? _currentRootHint;

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
        ThrowIfSettingArguments();
        owner.ValidatePass(this);
        _argumentValidation = prepare;
    }

    public void SetArguments<T>(in T value)
        where T : struct, IShaderArguments
    {
        ThrowIfSettingArguments();
        ValidateRootSelection();
        BrowserComputePipeline program = _pipeline!;
        _settingArguments = true;
        try
        {
            BrowserRootStorage root = BrowserShaderTransfer.CreateRoot(owner.Owner, in value, diagnostic: owner.Owner.Validation == null ? null : DiagnosticRoot(T.RootParameter), prepare: _argumentValidation);
            SelectRoot(root, program);
        }
        finally
        {
            _settingArguments = false;
        }
    }

    public void SetArguments<T>(in T value, string rootParameter = "arguments", string? shaderTypeName = null)
        where T : unmanaged
    {
        ThrowIfSettingArguments();
        ValidateRootSelection();
        BrowserComputePipeline program = _pipeline!;
        _settingArguments = true;
        try
        {
            BrowserRootStorage root = BrowserShaderTransfer.CreateUnmanaged(owner.Owner, in value, rootParameter, shaderTypeName, diagnostic: DiagnosticRoot(rootParameter), prepare: _argumentValidation);
            SelectRoot(root, program);
        }
        finally
        {
            _settingArguments = false;
        }
    }

    public void SetPipeline(IGraphicsComputePipeline pipeline)
    {
        ThrowIfSettingArguments();
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
        ThrowIfSettingArguments();
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
        ThrowIfSettingArguments();
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

    public void End()
    {
        ThrowIfSettingArguments();
        owner.EndCompute(this, handle);
    }

    private void ThrowIfSettingArguments()
    {
        if (_settingArguments)
        {
            throw new InvalidOperationException("Root argument preparation is already active.");
        }
    }

    private void ValidateRootSelection()
    {
        ThrowIfSettingArguments();
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

            if (_table != null && root.ImplicitTable != null && !ReferenceEquals(_table, root.ImplicitTable))
            {
                throw new ArgumentException("Selected table differs from root references.");
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

        _currentRootHint = root.ImplicitTable;
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

        if (_pipeline.RequiresArguments && _arguments == null)
        {
            throw new InvalidOperationException("Set arguments before drawing or dispatching.");
        }

        if (_table != null && _currentRootHint != null && !ReferenceEquals(_table, _currentRootHint))
        {
            throw new ArgumentException("Selected table differs from root references.");
        }

        BrowserArgumentTable? selectedTable = _table ?? _currentRootHint;
        selectedTable?.ThrowIfDisposed();
        IReadOnlyCollection<IShaderReference> registrations = selectedTable?.SnapshotRegistrations() ?? Array.Empty<IShaderReference>();
        ShaderBindingData? bindingData = null;
        if (_pipeline.RequiresArguments || selectedTable != null)
        {
            if (_pipeline.RequiresArguments && owner.Owner.Validation is { } validation)
            {
                var dependencies = ShaderBindingDependencies.Collect(_arguments!.Arguments, selectedTable, owner.ReadShaderDependencies);
                EncoderValidation.ShaderAccesses(validation, owner, dependencies.References, PipelineStage.ComputeShader);
            }

            bindingData = new(registrations, _pipeline.Data, owner.Owner.Caps);
        }

        _pipeline.ValidateAlive();

        BrowserInterop.SetComputePipeline(handle, _pipeline.Resolve(bindingData));
        if (bindingData != null)
        {
            var binding = new BrowserShaderBinding(owner.Owner, bindingData, _pipeline.RequiresArguments ? _arguments : null, selectedTable, registrations, true);
            owner.KeepBinding(binding);
            BrowserInterop.SetShaderBinding(handle, binding.Native);
        }
    }
}
