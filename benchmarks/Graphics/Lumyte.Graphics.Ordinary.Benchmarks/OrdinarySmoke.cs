using Lumyte.Graphics.Drawing2D;

namespace Lumyte.Graphics.Gpu.Benchmarks;

internal static class OrdinarySmoke
{
    internal static void Verify()
    {
        (string Scenario, string Variant)[] cases = [("D04", "pan"), ("D04", "zoom"), ("D05", "16"), ("D05", "64"), ("D08", "7"), ("D08", "8"), ("D08", "9"), ("D08", "path"), ("D08", "gradient"), ("D09", "1"), ("D09", "4"), ("D09", "16"), ("D12", "small-cache"), ("D12", "large-cache")];
        foreach ((string Scenario, string Variant) item in cases)
        {
            using var fixture = new OrdinaryFixture(item.Scenario, item.Variant);
            for (int frame = 1; frame <= 18; frame++)
            {
                fixture.Apply(frame);
                int expected = fixture.ElementCount;
                DrawList2D reference = OrdinaryFixture.Reference(item.Scenario, item.Variant, frame);
                if (reference.Count != expected || OrdinaryFixture.InputId(item.Scenario, item.Variant, frame) != OrdinaryFixture.InputId(item.Scenario, item.Variant, frame + (item.Scenario == "D12" ? 18 : 2)))
                {
                    throw new InvalidOperationException("Ordinary deterministic count/period mismatch.");
                }

                if (item.Scenario == "D05" && OrdinaryFixture.GeometryPath(int.Parse(item.Variant, System.Globalization.CultureInfo.InvariantCulture), frame, 16, 16).Segments.Count != int.Parse(item.Variant, System.Globalization.CultureInfo.InvariantCulture))
                {
                    throw new InvalidOperationException("D05 source Path2D segment count differs from the profile.");
                }

                if (item.Scenario == "D04" && fixture.VisibleElements != (item.Variant == "zoom" && (frame & 1) != 0 ? 448 : 510))
                {
                    throw new InvalidOperationException("D04 source visibility profile drifted.");
                }

                if (fixture.RerecordedCommands != (item.Scenario == "D05" ? 32 : 0) || fixture.PropertyEdits != (item.Scenario == "D04" ? 1 : 0))
                {
                    throw new InvalidOperationException("Ordinary actual edit counter mismatch.");
                }
            }
        }
    }
}
