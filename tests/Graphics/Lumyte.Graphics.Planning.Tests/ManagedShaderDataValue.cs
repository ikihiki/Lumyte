using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning.Tests;

internal sealed record ManagedShaderDataValue(byte[] Bytes, IShaderReference[] References);
