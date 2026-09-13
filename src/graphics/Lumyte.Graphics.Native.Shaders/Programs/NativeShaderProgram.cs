namespace Lumyte.Graphics.Native.Shaders;

/// <summary>Selected host-side shader code and layout. It owns no pipeline, resource, or backend.</summary>
public sealed class NativeShaderProgram : IDisposable
{
    private State? state;

    internal NativeShaderProgram(NativeShaderArtifact artifact)
    {
        NativeGpuShaderProgram code = new(artifact.Stages.Select(static stage => stage.CopyCode()).ToArray());
        state = new(artifact.Target, artifact.RootLayout, artifact.ParameterLayouts, artifact.AbiHash, code);
    }

    /// <summary>Borrowed until this program is disposed. Pipeline creation consumes or snapshots the required bytes.</summary>
    public NativeGpuShaderProgram Code => Current.Code;
    public NativeShaderTarget Target => Current.Target;
    public NativeShaderInputLayout RootLayout => Current.RootLayout;
    public IReadOnlyList<NativeShaderInputLayout> ParameterLayouts => Current.ParameterLayouts;
    public string AbiHash => Current.AbiHash;

    /// <summary>Releases this program's CPU references. No native calls or GPU waits occur.</summary>
    public void Dispose() => Interlocked.Exchange(ref state, null);

    private State Current => Volatile.Read(ref state) ?? throw new ObjectDisposedException(nameof(NativeShaderProgram));
    private sealed record State(NativeShaderTarget Target, NativeShaderInputLayout RootLayout,
        IReadOnlyList<NativeShaderInputLayout> ParameterLayouts, string AbiHash, NativeGpuShaderProgram Code);
}
