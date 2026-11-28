using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning.Tests;

/// <summary>Provides deterministic managed test instrumentation.</summary>
internal sealed class ManagedSubmission(ManagedCommands commands, List<string> events) : IGraphicsSubmission
{
    public SubmissionStatus Status { get; private set; }

    public ValueTask WaitAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        events.Add("wait");
        commands.Complete();
        Status = SubmissionStatus.Completed;
        return ValueTask.CompletedTask;
    }

    public void Dispose() => events.Add("dispose-submission");
}
