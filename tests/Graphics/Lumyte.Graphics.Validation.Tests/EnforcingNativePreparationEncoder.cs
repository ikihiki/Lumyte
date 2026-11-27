using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation.Tests;

internal sealed class EnforcingNativePreparationEncoder(EnforcingNativePreparationCommand command, EnforcingNativePreparationBackend backend) : IComputeEncoder
{
    private IGraphicsComputePipeline? _pipeline;
    private IArgumentTable? _table;
    private object? _identity;
    private bool _ended;

    public void SetPipeline(IGraphicsComputePipeline pipeline)
    {
        Check();
        _pipeline = pipeline;
    }

    public void SetArgumentTable(IArgumentTable table)
    {
        Check();
        _table = table;
    }

    public void SetArgumentValidation(Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? prepare) => throw new NotSupportedException();

    public void SetArguments<T>(in T arguments)
        where T : struct, IShaderArguments
    {
        Check();
        object identity = arguments.NativePreparationIdentity ?? throw new InvalidOperationException("This enforcing backend only accepts prepared arguments.");
        EnforcingNativePreparationCommand.Entry entry = command.Find(identity, _pipeline, _table);
        if (entry.Wire.Length != entry.WireSize)
        {
            throw new InvalidOperationException("Missing immutable wire.");
        }

        _identity = identity;
    }

    public void SetArguments<T>(in T arguments, string rootParameter = "arguments", string? shaderTypeName = null)
        where T : unmanaged => throw new NotSupportedException();

    public void Dispatch(uint groupCountX, uint groupCountY = 1, uint groupCountZ = 1)
    {
        Check();
        command.Find(_identity ?? throw new InvalidOperationException("Missing root."), _pipeline, _table);
        backend.Executions++;
    }

    public void DispatchIndirect(BufferSlice<DispatchIndirectArguments> arguments) => throw new NotSupportedException();

    public void End()
    {
        Check();
        _ended = true;
    }

    private void Check()
    {
        command.Check();
        if (_ended)
        {
            throw new InvalidOperationException("Pass ended.");
        }
    }
}
