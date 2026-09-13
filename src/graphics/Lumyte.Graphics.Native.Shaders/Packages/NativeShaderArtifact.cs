namespace Lumyte.Graphics.Native.Shaders;

/// <summary>A target-specific, immutable program and its independently compiled input ABI.</summary>
public sealed class NativeShaderArtifact
{
    public NativeShaderArtifact(NativeShaderTarget target, GpuShaderCodeFormat codeFormat,
        IEnumerable<NativeShaderStageArtifact> stages, NativeShaderCapabilities requiredCapabilities,
        NativeShaderDescriptorHeapAbi descriptorHeapAbi, NativeShaderInputLayout rootLayout,
        IEnumerable<NativeShaderInputLayout> parameterLayouts, string abiHash)
    {
        ArgumentNullException.ThrowIfNull(stages);
        ArgumentNullException.ThrowIfNull(rootLayout);
        ArgumentNullException.ThrowIfNull(parameterLayouts);
        ArgumentException.ThrowIfNullOrWhiteSpace(abiHash);
        NativeShaderStageArtifact[] stageSnapshot = stages.ToArray();
        if (stageSnapshot.Any(static stage => stage is null))
        {
            throw new ArgumentException("A stage artifact cannot be null.", nameof(stages));
        }
        // Reuse the low-level program's unique stage-family contract, without inspecting code.
        NativeGpuShaderProgram shape;
        try { shape = new(stageSnapshot.Select(static stage => stage.BorrowCode()).ToArray()); }
        catch (ArgumentException error)
        {
            throw new ArgumentException("An artifact must define one compute, vertex raster, or mesh raster program with unique stages.", nameof(stages), error);
        }
        if (shape.Mesh is not null) { requiredCapabilities |= NativeShaderCapabilities.MeshShaders; }
        if (shape.Amplification is not null) { requiredCapabilities |= NativeShaderCapabilities.AmplificationShaders; }
        PrimaryStage = shape.Compute is not null ? GpuShaderStage.Compute
            : shape.Mesh is not null ? GpuShaderStage.Mesh : GpuShaderStage.Vertex;

        NativeShaderInputLayout[] layoutSnapshot = parameterLayouts.ToArray();
        HashSet<string> layoutIds = new(StringComparer.Ordinal);
        if (layoutSnapshot.Any(layout => layout is null || !layoutIds.Add(layout.AbiId)))
        {
            throw new ArgumentException("Parameter layouts must have unique ABI identifiers and cannot be null.", nameof(parameterLayouts));
        }
        Target = target;
        CodeFormat = codeFormat;
        Stages = Array.AsReadOnly(stageSnapshot);
        RequiredCapabilities = requiredCapabilities;
        DescriptorHeapAbi = descriptorHeapAbi;
        RootLayout = rootLayout;
        ParameterLayouts = Array.AsReadOnly(layoutSnapshot);
        AbiHash = abiHash;
    }

    public NativeShaderTarget Target { get; }
    public GpuShaderCodeFormat CodeFormat { get; }
    public IReadOnlyList<NativeShaderStageArtifact> Stages { get; }
    public NativeShaderCapabilities RequiredCapabilities { get; }
    public NativeShaderDescriptorHeapAbi DescriptorHeapAbi { get; }
    public NativeShaderInputLayout RootLayout { get; }
    public IReadOnlyList<NativeShaderInputLayout> ParameterLayouts { get; }
    public string AbiHash { get; }
    internal GpuShaderStage PrimaryStage { get; }
}
