using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;

namespace Lumyte.Graphics.Validation;

internal sealed class ValidationWriteContext(ValidationDevice device, ShaderDataLayout? diagnostic = null, Action<IShaderReferenceMetadata>? prepare = null)
{
    internal ValidationDevice Device { get; } = device;

    internal HashSet<IShaderReference> References { get; } = new(ReferenceEqualityComparer.Instance);

    internal ShaderDataLayout? Diagnostic { get; } = diagnostic;

    internal Action<IShaderReferenceMetadata>? Prepare { get; } = prepare;
}
