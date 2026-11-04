namespace Lumyte.Graphics.Gpu.Benchmarks;

internal static class SuiteSmoke
{
    internal static void Verify()
    {
        foreach (bool texture in new[] { false, true })
        {
            foreach (bool dispersed in new[] { false, true })
            {
                byte[] first = TransferSlot.Expected(texture, dispersed, 0), second = TransferSlot.Expected(texture, dispersed, 1);
                if (first.Length != 65536 || first.Count(value => value == 0x39) != 16384 || second.Count(value => value == 0xc7) != 16384 || first.Zip(second).Count(pair => pair.First != pair.Second) != 16384)
                {
                    throw new InvalidOperationException("Fixed transfer byte budget or actual changes are wrong.");
                }

                for (int block = 0; block < 16; block++)
                {
                    byte expected = (dispersed ? block % 4 == 0 : block < 4) ? (byte)0x39 : (byte)0;
                    if (first[block * 4096] != expected || first[((block + 1) * 4096) - 1] != expected)
                    {
                        throw new InvalidOperationException("Transfer locality incorrect.");
                    }
                }
            }
        }

        foreach (bool grouped in new[] { false, true })
        {
            int[] order = ImageSet.Order(grouped);
            if (order.Length != 256 || order.Distinct().Count() != 256 || ImageSet.Reference(grouped, 0).Count != 4096 || ImageSet.Reference(grouped, 1).Count != 4096)
            {
                throw new InvalidOperationException("Independent image reference or logical sprite order is invalid.");
            }
        }

        _ = SuiteOptions.Parse(["--scenario", "D06", "--resource", "distinct", "--updates", "upload", "--depth", "3"]);
        _ = SuiteOptions.Parse(["--scenario", "G03", "--resource", "texture", "--updates", "dispersed"]);
        try
        {
            SuiteOptions.Parse(["--scenario", "G03", "--order", "grouped"]);
            throw new InvalidOperationException("G03 accepted an incompatible image ordering option.");
        }
        catch (ArgumentException)
        {
        }
    }
}
