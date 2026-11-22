using Lumyte.Pipelines;

namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Version one TargetWritesV1 canonical layout; references remain host-side metadata, never wire values.</summary>
public readonly struct TargetWritesV1 : ISchema
{
    /// <summary>Gets the fixed numerical and element ABI.</summary>
    public static SchemaDescriptor Descriptor { get; } = DrawingSchema2D.Make<uint>(nameof(TargetWritesV1), dependencyOnly: true, resourceRole: "target-writes");
}
