namespace Lumyte.Graphics.Abstractions;

/// <summary>Specifies PipelineStage values.</summary>
[Flags]
public enum PipelineStage
{
    /// <summary>Specifies None.</summary>
    None = 0,

    /// <summary>Specifies Host.</summary>
    Host = 1,

    /// <summary>Specifies Copy.</summary>
    Copy = 2,

    /// <summary>Specifies VertexShader.</summary>
    VertexShader = 16,

    /// <summary>Specifies FragmentShader.</summary>
    FragmentShader = 32,

    /// <summary>Specifies ComputeShader.</summary>
    ComputeShader = 64,

    /// <summary>Specifies ColorOutput.</summary>
    ColorOutput = 128,

    /// <summary>Specifies AllGraphics.</summary>
    AllGraphics = VertexShader | FragmentShader | ColorOutput,

    /// <summary>Specifies AllCommands.</summary>
    AllCommands = Copy | AllGraphics | ComputeShader,
}
