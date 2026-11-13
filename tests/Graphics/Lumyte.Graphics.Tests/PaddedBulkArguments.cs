using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Tests;

[StructLayout(LayoutKind.Sequential)]
internal partial struct PaddedBulkArguments : IShaderArguments
{
    public byte Tag;
    public uint Value;
}
