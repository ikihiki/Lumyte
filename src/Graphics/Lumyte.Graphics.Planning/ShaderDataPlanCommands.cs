using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

/// <summary>Declares explicit typed shader-data transfers and complete finite reference bindings.</summary>
public static class ShaderDataPlanCommands
{
    /// <summary>Captures immutable element bytes and symbolic reference recipes before compile, reserving staging and one GPU copy.</summary>
    /// <typeparam name="T">The original shader element ABI.</typeparam>
    /// <param name="pass">The upload pass, containing no other writing operation.</param>
    /// <param name="destination">The predeclared written destination version.</param>
    /// <param name="range">The exact written element range.</param>
    /// <param name="values">The values captured synchronously at declaration.</param>
    public static void UploadShaderData<T>(this PlanPass pass, PlanShaderData<T> destination, PlanRange range, ReadOnlySpan<T> values)
        where T : struct, IShaderArguments
    {
        pass.Owner.Mutable();
        pass.Require(destination, range, write: true);
        if (destination.Memory != MemoryPreference.Automatic || (ulong)values.Length != range.Count || values.IsEmpty || values.Length > pass.Owner.Frame.Session.Budget.MaximumShaderDataElements)
        {
            throw new ArgumentException("A shader-data upload requires a nonempty exact captured range and device storage.");
        }

        if (!pass.Uses.Any(u => u.Resource.SameVersion(destination) && u.Range.Contains(range) && (u.Scope.Access & ResourceAccess.CopyWrite) != 0 && (u.Scope.Stages & PipelineStage.Copy) != 0))
        {
            throw new InvalidOperationException("Shader-data upload requires predeclared copy-write use.");
        }

        pass.Owner.ReserveCommand(checked((ulong)values.Length * (ulong)T.TransferSize), checked(values.Length * T.ReferenceCount));
        var captured = new IReadOnlyList<IArgumentPiece>[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            ArgumentCaptureWriter writer = new(pass, T.TransferSize, T.ReferenceCount, shaderData: true);
            values[i].Write(ref writer);
            writer.Complete<T>();
            captured[i] = writer.Pieces.AsReadOnly();
        }

        PlanShaderData<T> staging = pass.Owner.CreateShaderDataCore<T>(destination.Artifact, range.Count, MemoryPreference.Upload, initialized: true);
        pass.Read(staging, staging.Range, new(PipelineStage.Copy, ResourceAccess.CopyRead));
        destination.InstallPayload(range, captured);
        pass.Commands.Add(new ShaderDataUploadCommand<T>(staging, destination, range, captured));
    }

    /// <summary>Declares a typed GPU shader-data copy, preserving canonical reference-domain coherence.</summary>
    /// <typeparam name="T">The shader element ABI.</typeparam>
    /// <param name="pass">The declaring pass.</param>
    /// <param name="source">The explicit source version.</param>
    /// <param name="sourceRange">The source elements.</param>
    /// <param name="destination">The predeclared written destination version.</param>
    /// <param name="destinationRange">The destination elements.</param>
    public static void CopyShaderData<T>(this PlanPass pass, PlanShaderData<T> source, PlanRange sourceRange, PlanShaderData<T> destination, PlanRange destinationRange)
        where T : struct, IShaderArguments
    {
        pass.Owner.Mutable();
        pass.Require(source, sourceRange, write: false);
        pass.Require(destination, destinationRange, write: true);
        if (source.Identity == destination.Identity || sourceRange.Count != destinationRange.Count || destination.Memory != MemoryPreference.Automatic)
        {
            throw new ArgumentException("Shader-data copies require distinct allocations, equal ranges and a device destination.");
        }

        if (T.ReferenceCount != 0 && source.Payload is null)
        {
            throw new InvalidOperationException("The copied shader-data reference closure is unknown.");
        }

        pass.Owner.ReserveCommand();
        destination.InstallCopiedPayload(destinationRange, source.ReferenceUses);
        pass.Commands.Add(new ShaderDataCopyCommand<T>(source, sourceRange, destination, destinationRange));
    }

    /// <summary>Registers typed device shader-data and checks every transitive resource against predeclared consumer uses.</summary>
    /// <typeparam name="T">The original shader element ABI.</typeparam>
    /// <param name="pass">The consuming shader operation.</param>
    /// <param name="data">The exact shader-data version.</param>
    /// <param name="range">The complete reachable element range.</param>
    /// <returns>A typed symbolic reference patched into the common coherent table domain.</returns>
    public static PlanGpuRef<T> ShaderData<T>(this PlanPass pass, PlanShaderData<T> data, PlanRange range)
        where T : struct, IShaderArguments
    {
        pass.Owner.Mutable();
        pass.Require(data, range, write: false);
        if (data.Memory != MemoryPreference.Automatic || (T.ReferenceCount != 0 && data.Payload is null))
        {
            throw new InvalidOperationException("Shader-data references require canonical device storage and known closure.");
        }

        if (!pass.Uses.Any(u => u.Resource.SameVersion(data) && u.Range.Contains(range) && (u.Scope.Access & ResourceAccess.ShaderRead) != 0))
        {
            throw new InvalidOperationException("Shader-data binding requires a predeclared shader read.");
        }

        foreach (PlanResourceUse use in ShaderDataClosure.Collect(data, pass.Owner.Frame.Session.Budget))
        {
            pass.Owner.Validate(use.Resource, use.Range);
            ResourceAccess required = use.Scope.Access & (ResourceAccess.ShaderRead | ResourceAccess.ShaderWrite);
            if (required != ResourceAccess.None && !pass.Uses.Any(u => u.Resource.SameVersion(use.Resource) && u.Range.Contains(use.Range) && (u.Scope.Access & required) == required && (u.Scope.Stages & (PipelineStage.AllGraphics | PipelineStage.ComputeShader)) != 0))
            {
                throw new InvalidOperationException("A transitive shader-data target lacks its explicit shader stage/access declaration.");
            }

            if (use.Resource is PlanTexture && !pass.Uses.Any(u => u.Resource.SameVersion(use.Resource) && u.Range.Contains(use.Range) && u.TextureState == TextureState.Sampled))
            {
                throw new InvalidOperationException("A transitive sampled image lacks its declared sampled layout.");
            }

            pass.Require(use.Resource, use.Range, use.Access != PlanAccess.Read);
            if (use.Access != PlanAccess.Write)
            {
                pass.Require(use.Resource, use.Range, write: false);
            }
        }

        return new(pass, data, range.Count, (reservation, resources, resolved) =>
        {
            resources.JoinShaderData(data.Identity, reservation);
            reservation.RegisterShaderData(() => resources.ShaderData(data, range), resolved);
        });
    }
}
