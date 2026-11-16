using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Tests;

internal partial struct CopyEightReferences : IShaderArguments
{
    private Storage _data;

    public CopyEightReferences()
    {
        _data = default;
    }

    public IGpuRef<uint>? R0 { get; set; }

    public IGpuRef<uint>? R1 { get; set; }

    public IGpuRef<uint>? R2 { get; set; }

    public IGpuRef<uint>? R3 { get; set; }

    public IGpuRef<uint>? R4 { get; set; }

    public IGpuRef<uint>? R5 { get; set; }

    public IGpuRef<uint>? R6 { get; set; }

    public IGpuRef<uint>? R7 { get; set; }

    private struct Storage
    {
        public ShaderReferenceSlot R0;

        public ShaderReferenceSlot R1;

        public ShaderReferenceSlot R2;

        public ShaderReferenceSlot R3;

        public ShaderReferenceSlot R4;

        public ShaderReferenceSlot R5;

        public ShaderReferenceSlot R6;

        public ShaderReferenceSlot R7;
    }
}
