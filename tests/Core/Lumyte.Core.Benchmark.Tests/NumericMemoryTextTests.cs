using Lumyte.Core.Time;
using Lumyte.Mathematics.Colors;
using Lumyte.Mathematics.Geometry;
using Lumyte.TextEditing;
using Xunit;

namespace Lumyte.Core.Benchmark.Tests;

/// <summary>Deterministic acceptance for the numeric, memory and document workloads.</summary>
public class NumericMemoryTextTests
{
    /// <summary>Executes time workload checks and checked overflow boundaries.</summary>
    [Fact]
    public void TimeContracts()
    {
        Lumyte.Core.Benchmark.BenchmarkChecks.Verify();
        Assert.Throws<OverflowException>(() => _ = Duration.FromTicks(long.MaxValue) + Duration.FromTicks(1));
        Assert.Equal(-12, (Duration.FromTicks(25) / -2).Ticks);
    }

    /// <summary>Executes numerical oracles and independently checks edge inclusion and packed red.</summary>
    [Fact]
    public void NumericContracts()
    {
        Lumyte.Mathematics.Benchmark.BenchmarkChecks.Verify();
        var rectangle = new Rect2D<double>(0, 0, 10, 20);
        Assert.True(rectangle.Contains(new Point2D<double>(10, 20)));
        Assert.False(rectangle.ContainsHalfOpen(new Point2D<double>(10, 20)));
        Assert.Equal((ushort)0xf800, RawRgb565.FromSrgb(new SRgba<float>(1, 0, 0, 1)).PackedValue);
    }

    /// <summary>Executes finite chunk, reservation and dependency-injection lifetime checks.</summary>
    [Fact]
    public void MemoryContracts() => Lumyte.Memory.Benchmark.BenchmarkChecks.Verify();

    /// <summary>Executes editing checks and independently verifies unpublished transaction rollback.</summary>
    [Fact]
    public void EditingContracts()
    {
        Lumyte.TextEditing.Benchmark.BenchmarkChecks.Verify();
        var document = new TextDocument("a日e\u0301");
        TextDocumentSnapshot before = document.CaptureSnapshot();
        using (TextDocumentTransaction edit = document.BeginEdit(TextEditOrigin.Keyboard))
        {
            edit.Replace(new TextSpan(0, 1), "b", new TextSelection(1, 1));
            Assert.Equal(before, document.CaptureSnapshot());
        }

        Assert.Equal(before, document.CaptureSnapshot());
        Assert.Empty(document.DrainChanges());
        Assert.Equal(new[] { 0, 1, 2, 4 }, TextDocument.GraphemeBoundaries("a日e\u0301"));
    }
}
