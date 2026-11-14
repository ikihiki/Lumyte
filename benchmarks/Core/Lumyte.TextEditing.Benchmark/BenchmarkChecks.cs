namespace Lumyte.TextEditing.Benchmark;

/// <summary>Public-API editing and rejection contracts.</summary>
public static class BenchmarkChecks
{
    /// <summary>Verifies publication, graphemes, composition and defensive copies.</summary>
    public static void Verify()
    {
        var benchmark = new TextBenchmarks { Length = 256 };
        benchmark.Setup();
        if (benchmark.DeleteGrapheme() != "a日" || benchmark.CompositionLifetime().Length != 256 || benchmark.Changes() != 32)
        {
            throw new InvalidOperationException("Editing oracle mismatch.");
        }

        var candidates = new CandidateBenchmarks { Count = 32 };
        candidates.Setup();
        ImeCandidateSnapshot first = candidates.Candidates();
        ImeCandidateSnapshot second = candidates.Candidates();
        if (ReferenceEquals(first.Items, second.Items) || first.Items.Count != 32 || TextDocument.Normalize("a\r\nb\nc\r") != "a b c ")
        {
            throw new InvalidOperationException("Copy or normalization mismatch.");
        }

        var document = new TextDocument("abc");
        TextDocumentSnapshot before = document.CaptureSnapshot();
        if (document.TryReplace(1, new TextSpan(0, 1), "z", new TextSelection(1, 1), TextEditOrigin.Keyboard) || document.CaptureSnapshot() != before || document.DrainChanges().Count != 0)
        {
            throw new InvalidOperationException("CORE-TEXT-06 stale revision mutated published state.");
        }

        foreach (int count in new[] { 1, 32, 256 })
        {
            var drain = new ChangeDrainBenchmarks { Count = count };
            drain.Setup();
            IReadOnlyList<TextDocumentChange> changes = drain.Drain();
            if (changes.Count != count || drain.Drain().Count != 0)
            {
                throw new InvalidOperationException("Drain replenishment oracle mismatch.");
            }
        }

        foreach (int count in new[] { 0, 1024 })
        {
            var candidateBoundary = new CandidateBenchmarks { Count = count };
            candidateBoundary.Setup();
            if (candidateBoundary.Candidates().Items.Count != count)
            {
                throw new InvalidOperationException("Candidate boundary copy mismatch.");
            }
        }

        benchmark.Rollback();
        benchmark.Replace();
        benchmark.Navigate();
        benchmark.Drain();
    }
}
