using System.Runtime.CompilerServices;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;

namespace Lumyte.Graphics.Validation;

internal sealed class ValidationComputeEncoder : IComputeEncoder
{
    private readonly ValidationCommandBuffer _owner;
    private readonly IComputeEncoder _raw;
    private readonly ValidationEncoderChecks _checks;
    private IGraphicsComputePipeline? _pipeline;

    internal ValidationComputeEncoder(ValidationCommandBuffer owner, IComputeEncoder raw)
    {
        _owner = owner;
        _raw = raw;
        _checks = new(owner, this);
    }

    public void SetArgumentTable(IArgumentTable table) => _checks.SelectTable(table, _raw.SetArgumentTable);

    public void SetArgumentValidation(Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? prepare) => _checks.SetValidation(prepare);

    public void SetPipeline(IGraphicsComputePipeline pipeline)
    {
        _checks.Check();
        IGraphicsComputePipeline native = _owner.Device.Unwrap(pipeline);
        using GraphicsValidation.RecordingTransaction tx = _owner.Device.Tracker.BeginRecording(_owner);
        _owner.Device.Tracker.Use(_owner, pipeline);
        _raw.SetPipeline(native);
        _pipeline = pipeline;
        _checks.SelectPipeline(pipeline);
        tx.Commit();
    }

    public void SetArguments<T>(in T arguments)
        where T : struct, IShaderArguments
    {
        _checks.BeginArguments();
        try
        {
            IShaderReferenceMetadata? metadata = null;
            Action<IReadOnlyCollection<IShaderReference>>? admit = null;
            ValidationWriteContext context = _checks.Context(Diagnostic(T.RootParameter), (value, admission) =>
            {
                metadata = value;
                admit = admission;
            });
            var adapter = new ValidationArguments<T>(in arguments, context);
            _raw.SetArguments(in adapter);
            _checks.CommitRoot(metadata ?? throw new InvalidOperationException("Backend did not invoke the root writer."), admit);
        }
        finally
        {
            _checks.EndArguments();
        }
    }

    public void SetArguments<T>(in T arguments, string rootParameter = "arguments", string? shaderTypeName = null)
        where T : unmanaged
    {
        _checks.BeginArguments();
        try
        {
            IShaderReferenceMetadata? metadata = null;
            Action<IReadOnlyCollection<IShaderReference>>? admit = null;
            _checks.PrepareUnmanaged(Diagnostic(rootParameter), Unsafe.SizeOf<T>(), ShaderTransfer.Fields<T>(), shaderTypeName ?? ShaderTransfer.TypeName<T>(), (value, admission) =>
            {
                metadata = value;
                admit = admission;
            });
            _raw.SetArguments(in arguments, rootParameter, shaderTypeName);
            _checks.CommitRoot(metadata!, admit);
        }
        finally
        {
            _checks.EndArguments();
        }
    }

    public void End()
    {
        _checks.Check();
        _raw.End();
        _owner.EndPass(this);
    }

    public void Dispatch(uint groupCountX, uint groupCountY = 1, uint groupCountZ = 1)
    {
        _checks.Check();
        using GraphicsValidation.RecordingTransaction tx = _owner.Device.Tracker.BeginRecording(_owner);
        PrepareExecution();
        _raw.Dispatch(groupCountX, groupCountY, groupCountZ);
        tx.Commit();
    }

    public void DispatchIndirect(BufferSlice<DispatchIndirectArguments> arguments)
    {
        _checks.Check();
        BufferSlice<DispatchIndirectArguments> native = _owner.Unwrap(arguments);
        using GraphicsValidation.RecordingTransaction tx = _owner.Device.Tracker.BeginRecording(_owner);
        PrepareExecution();
        Indirect(arguments);
        _raw.DispatchIndirect(native);
        tx.Commit();
    }

    private ShaderDataLayout Diagnostic(string root) => ShaderDataLayout.Root(ValidationEncoderChecks.Target(_owner.Device, _pipeline!.Desc.ComputeShader), root);

    private void PrepareExecution()
    {
        _checks.Check();
        if (_pipeline == null)
        {
            throw new InvalidOperationException("Select a pipeline before execution.");
        }

        bool required = ShaderDataLayout.HasRoot(ValidationEncoderChecks.Target(_owner.Device, _pipeline.Desc.ComputeShader));
        _checks.Execute(required, PipelineStage.ComputeShader);
    }

    private void Indirect<T>(BufferSlice<T> arguments)
        where T : unmanaged
        => _owner.Device.Tracker.Access(_owner, arguments.Buffer, new(PipelineStage.DrawIndirect, ResourceAccess.IndirectRead), arguments.OffsetInBytes, arguments.SizeInBytes);
}
