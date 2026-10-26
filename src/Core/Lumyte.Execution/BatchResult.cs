namespace Lumyte.Execution;

internal abstract class BatchResult<TResult>(WorkDefinition definition) : BatchItem(definition)
{
    internal abstract TResult GetResult();
}
