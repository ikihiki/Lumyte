using System.Runtime.CompilerServices;

namespace Lumyte.Graphics.Abstractions;

internal readonly record struct ArgumentKey(object Resource, Type Type, int Kind, ulong Offset, ulong Count, ulong Stride, ArgumentBufferAccess Access)
{
    internal int Namespace => Kind < 2 ? Kind : 2;

    internal ulong Size => checked(Count * Stride);

    public bool Equals(ArgumentKey other) => ReferenceEquals(Resource, other.Resource) && Type == other.Type && Kind == other.Kind && Offset == other.Offset && Count == other.Count && Stride == other.Stride && Access == other.Access;

    public override int GetHashCode() => HashCode.Combine(RuntimeHelpers.GetHashCode(Resource), Type, Kind, Offset, Count, Stride, Access);
}
