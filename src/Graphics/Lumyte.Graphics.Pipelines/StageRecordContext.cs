using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Pipelines;

/// <summary>Provides a borrowed record-only command scope.</summary>
public sealed class StageRecordContext
{
    internal StageRecordContext(IGraphicsCommandBuffer commands)
    {
        Commands = commands;
    }

    /// <summary>Gets the borrowed command; Finish, Submit and disposal are forbidden.</summary>
    public IGraphicsCommandBuffer Commands { get; }
}
