using System.Diagnostics.CodeAnalysis;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation.Tests;

internal sealed class RecordingMetadataSource : IShaderDataSource
{
    public IShaderReferenceMetadata Value { get; set; } = new ShaderDependencyMetadata([]);

    public int Reads { get; private set; }

    public object ShaderHandle => this;

    public IShaderDataLayout Layout => throw new NotSupportedException();

    public ulong SizeInBytes => 4;

    public MemoryPreference Memory => MemoryPreference.Automatic;

    public void ValidateAlive()
    {
    }

    public bool TryGetDependencies([NotNullWhen(true)] out IShaderReferenceMetadata? dependencies)
    {
        Reads++;
        dependencies = Value;
        return true;
    }
}
