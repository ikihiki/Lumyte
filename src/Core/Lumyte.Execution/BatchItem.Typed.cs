namespace Lumyte.Execution;

internal sealed class BatchItem<TCommand, TResult>(WorkDefinition<TCommand, TResult> work, in TCommand command) : BatchResult<TResult>(work)
    where TCommand : struct
{
    private TCommand _command = command;
    private WorkDefinition<TCommand, TResult>? _work = work;
    private TResult _result = default!;

    internal override void Perform(in WorkContext context)
    {
        try
        {
            _result = _work!.Invoke(in _command, in context);
        }
        finally
        {
            _command = default;
        }
    }

    internal override bool Finish(Exception? error)
    {
        _command = default;
        _work = null;
        return base.Finish(error);
    }

    internal override TResult GetResult()
    {
        Error?.Throw();
        if (!Succeeded)
        {
            throw new InvalidOperationException("Result is not complete.");
        }

        return _result;
    }

    internal override void Clear()
    {
        _command = default;
        _result = default!;
        _work = null;
        Definition = null;
        Run = null;
        Error = null;
    }
}
