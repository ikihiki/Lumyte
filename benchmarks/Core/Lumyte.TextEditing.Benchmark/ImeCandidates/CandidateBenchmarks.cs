using BenchmarkDotNet.Attributes;

namespace Lumyte.TextEditing.Benchmark;

/// <summary>Candidate-list validation and defensive ownership independent of document length.</summary>
[BenchmarkCategory("Core", "TextEditing", "ImeCandidates")]
public class CandidateBenchmarks
{
    private ImeCandidateSnapshot _candidates = null!;

    /// <summary>Gets or sets bounded candidate counts.</summary>
    [Params(0, 32, 1024)]
    public int Count { get; set; }

    /// <summary>Prepares immutable candidate input outside measurement.</summary>
    [GlobalSetup]
    public void Setup() => _candidates = new ImeCandidateSnapshot(new ImeCandidateListId(new Guid("11111111-2222-3333-4444-555555555555"), 1, 1), 1, 0, Enumerable.Range(0, Count).Select(index => new ImeCandidateItem(index, new string('候', 16))).ToArray(), Count == 0 ? null : 0, Count == 0 ? Array.Empty<int>() : new[] { 0 }, Count == 0 ? null : 0, ImeCandidateOperations.Select);

    /// <summary>Copies and validates a paged candidate snapshot.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-TEXT-07")]
    public ImeCandidateSnapshot Candidates() => ImeCandidateValidation.Copy(_candidates);
}
