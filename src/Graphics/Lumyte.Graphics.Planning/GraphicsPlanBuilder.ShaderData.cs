using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

/// <summary>Declares typed shader-data storage within the common planning owner.</summary>
public sealed partial class GraphicsPlanBuilder
{
    /// <summary>Declares typed shader-data storage with the original shader element ABI.</summary>
    /// <typeparam name="T">The shader element structure.</typeparam>
    /// <param name="artifact">The exact compiled layout authority.</param>
    /// <param name="count">The finite element capacity.</param>
    /// <param name="memory">Device storage or explicitly declared upload staging.</param>
    /// <returns>The initial undefined storage version.</returns>
    public PlanShaderData<T> CreateShaderData<T>(ShaderArtifact artifact, ulong count, MemoryPreference memory = MemoryPreference.Automatic)
        where T : struct, IShaderArguments
        => CreateShaderDataCore<T>(artifact, count, memory, initialized: false);

    /// <summary>Imports independently leased shader data; reference-bearing storage requires canonical repatching before shader reads.</summary>
    /// <typeparam name="T">The original shader element ABI.</typeparam>
    /// <param name="buffer">The exact borrowed native allocation.</param>
    /// <param name="artifact">The immutable compiled layout authority.</param>
    /// <param name="useLease">The independent native use lease.</param>
    /// <param name="initialized">Whether reference-free existing elements are initialized.</param>
    /// <returns>The initial immutable import version.</returns>
    public PlanShaderData<T> ImportShaderData<T>(IGraphicsShaderDataBuffer<T> buffer, ShaderArtifact artifact, IDisposable useLease, bool initialized = true)
        where T : struct, IShaderArguments
    {
        Mutable();
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(artifact);
        if (initialized && T.ReferenceCount != 0)
        {
            throw new NotSupportedException("Importing already-patched reference-bearing bytes requires canonical UploadShaderData; foreign table domains cannot be silently reused.");
        }

        Frame.Own(useLease);
        if (!_imports.TryGetValue(buffer, out ResourceIdentity? identity))
        {
            identity = new(_ => buffer, buffer) { Measure = _ => buffer.SizeInBytes };
            _imports.Add(buffer, identity);
        }

        if (identity.Versions.Count != 0)
        {
            if (identity.Versions[0] is not PlanShaderData<T> original || !ReferenceEquals(original.Artifact, artifact) || initialized != (original.Initialized.Count != 0))
            {
                throw new ArgumentException("Conflicting imported shader-data ABI or initialization authority.");
            }

            return original;
        }

        PlanRange range = new(0, buffer.Count);
        return Add(new PlanShaderData<T>(this, identity, 0, range, initialized ? [range] : [], artifact, buffer.Memory));
    }

    internal PlanShaderData<T> CreateShaderDataCore<T>(ShaderArtifact artifact, ulong count, MemoryPreference memory, bool initialized)
        where T : struct, IShaderArguments
    {
        Mutable();
        ArgumentNullException.ThrowIfNull(artifact);
        if (count == 0 || count > (ulong)Frame.Session.Budget.MaximumShaderDataElements || T.TransferSize <= 0 || memory is not (MemoryPreference.Automatic or MemoryPreference.Upload))
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        ulong bytes = checked(count * (ulong)T.TransferSize);
        if (bytes > Frame.Session.Budget.MaximumAllocationBytes)
        {
            throw new InvalidOperationException("Shader-data allocation reservation exceeds the frame byte ceiling.");
        }

        var identity = new ResourceIdentity(device => device.CreateShaderDataBuffer<T>(artifact, count, memory)) { Measure = _ => bytes };
        PlanRange range = new(0, count);
        return Add(new PlanShaderData<T>(this, identity, 0, range, initialized ? [range] : [], artifact, memory));
    }
}
