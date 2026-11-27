using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

/// <summary>Declares plan pass.</summary>
public sealed class PlanPass
{
    internal PlanPass(GraphicsPlanBuilder owner, string name)
    {
        (Owner, Name) = (owner, name);
    }

    /// <summary>Gets name.</summary>
    public string Name { get; }

    /// <summary>Gets uses.</summary>
    public IReadOnlyList<PlanResourceUse> Uses => UseList.AsReadOnly();

    internal GraphicsPlanBuilder Owner { get; }

    internal List<PlanResourceUse> UseList { get; } = [];

    internal List<PlanCommand> Commands { get; } = [];

    internal List<(PlanResource Resource, PlanRange Range)> DiscardedContents { get; } = [];

    internal HashSet<PlanPass> Dependencies { get; } = [];

    internal bool IsRoot { get; set; }

    /// <summary>Declares keep alive.</summary>
    public void KeepAlive()
    {
        Owner.Mutable();
        IsRoot = true;
    }

    /// <summary>Declares depends on.</summary>
    /// <param name = "pass">The pass.</param>
    public void DependsOn(PlanPass pass)
    {
        Owner.Mutable();
        if (pass.Owner != Owner)
        {
            throw new ArgumentException("Foreign pass.");
        }

        Dependencies.Add(pass);
    }

    /// <summary>Declares read.</summary>
    /// <param name = "resource">The resource.</param>
    /// <param name = "range">The range.</param>
    /// <param name = "scope">The scope.</param>
    /// <param name = "textureState">The texture state.</param>
    public void Read(PlanResource resource, PlanRange range, BarrierScope scope, TextureState? textureState = null)
    {
        Owner.Mutable();
        Owner.Validate(resource, range);
        var use = new PlanResourceUse(resource, range, PlanAccess.Read, scope) { TextureState = textureState };
        if (!UseList.Contains(use))
        {
            Owner.ReserveResourceUse();
            UseList.Add(use);
        }
    }

    /// <summary>Declares write.</summary>
    /// <param name = "input">The input.</param>
    /// <param name = "range">The range.</param>
    /// <param name = "mode">The mode.</param>
    /// <param name = "scope">The scope.</param>
    /// <typeparam name = "T">The typed shader or resource contract.</typeparam>
    /// <returns>The declared result.</returns>
    public PlanBuffer<T> Write<T>(PlanBuffer<T> input, PlanRange range, PlanWriteMode mode, BarrierScope scope)
        where T : unmanaged => Owner.Write(this, input, range, mode, scope);

    /// <summary>Declares write.</summary>
    /// <param name = "input">The input.</param>
    /// <param name = "range">The range.</param>
    /// <param name = "mode">The mode.</param>
    /// <param name = "scope">The scope.</param>
    /// <param name = "textureState">The texture state.</param>
    /// <returns>The declared result.</returns>
    public PlanTexture Write(PlanTexture input, PlanRange range, PlanWriteMode mode, BarrierScope scope, TextureState textureState)
    {
        int firstNewUse = UseList.Count;
        PlanTexture output = Owner.Write(this, input, range, mode, scope);
        for (int i = firstNewUse; i < UseList.Count; i++)
        {
            if (UseList[i].Resource.Identity == input.Identity)
            {
                UseList[i] = UseList[i] with
                {
                    TextureState = textureState,
                };
            }
        }

        return output;
    }

    /// <summary>Declares an immutable typed shader-data successor version.</summary>
    /// <typeparam name="T">The original shader element ABI.</typeparam>
    /// <param name="input">The consumed explicit version.</param>
    /// <param name="range">The written element range.</param>
    /// <param name="mode">Discard or preserve semantics.</param>
    /// <param name="scope">The explicit copy or shader access.</param>
    /// <returns>The new version.</returns>
    public PlanShaderData<T> Write<T>(PlanShaderData<T> input, PlanRange range, PlanWriteMode mode, BarrierScope scope)
        where T : struct, IShaderArguments
    {
        if (input.Memory != MemoryPreference.Automatic || (scope.Access & ResourceAccess.CopyWrite) == 0 || (scope.Access & ResourceAccess.ShaderWrite) != 0 || (scope.Stages & PipelineStage.Copy) == 0)
        {
            throw new ArgumentException("Typed shader data is read-only shader storage and is written through explicit GPU copies only.");
        }

        return Owner.Write(this, input, range, mode, scope);
    }

    internal void DiscardContents(PlanResource resource, PlanRange range)
    {
        if (!DiscardedContents.Any(d => d.Resource.SameVersion(resource) && d.Range == range))
        {
            DiscardedContents.Add((resource, range));
        }
    }

    internal void ValidateInternalSynchronization()
    {
        if (Commands.Count > 1 && UseList.Any(u => u.Access != PlanAccess.Read))
        {
            throw new NotSupportedException("A pass containing writes is one structured operation; split multiple operations into explicitly dependent passes.");
        }

        for (int first = 0; first < UseList.Count; first++)
        {
            for (int second = first + 1; second < UseList.Count; second++)
            {
                PlanResourceUse a = UseList[first];
                PlanResourceUse b = UseList[second];
                if (a.Resource.Identity != b.Resource.Identity || !a.Range.Overlaps(b.Range))
                {
                    continue;
                }

                if (a.Resource is PlanTexture && a.TextureState != b.TextureState)
                {
                    throw new NotSupportedException("An intra-pass texture layout transition requires separate explicitly dependent passes.");
                }

                if (Commands.Count > 1 && (a.Access != PlanAccess.Read || b.Access != PlanAccess.Read))
                {
                    throw new NotSupportedException("Intra-pass RAW/WAR/WAW synchronization requires separate explicitly dependent passes.");
                }
            }
        }
    }

    internal void Require(PlanResource resource, PlanRange range, bool write)
    {
        if (!UseList.Any(u => u.Resource.SameVersion(resource) && u.Range.Contains(range) && (write ? u.Access != PlanAccess.Read : u.Access != PlanAccess.Write)))
        {
            throw new InvalidOperationException("Command binding must have a matching predeclared use.");
        }
    }
}
