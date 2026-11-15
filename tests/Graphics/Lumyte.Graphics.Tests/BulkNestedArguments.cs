using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Tests;

internal partial struct BulkNestedArguments : IShaderArguments
{
    public uint Header;
    public NestedBulkValue Nested;
    public Matrix4x4 Transform;
}
