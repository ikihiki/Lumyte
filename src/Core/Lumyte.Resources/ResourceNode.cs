namespace Lumyte.Resources;

internal readonly record struct ResourceNode(ResourceAddress Address, RuntimeTypeHandle TypeHandle)
{
    public override string ToString() => $"{Type.GetTypeFromHandle(TypeHandle)}({Address.Id.Value}, {Address.Subresource}, {Address.Variant})";
}
