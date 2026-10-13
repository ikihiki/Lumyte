namespace Lumyte.Resources;

internal readonly record struct ResourceNode(ResourceAddress Address, string PipelineId, Type OutputType)
{
    public override string ToString() => $"{PipelineId}<{OutputType.Name}>({Address.Id.Value}, {Address.Subresource}, {Address.Variant})";
}
