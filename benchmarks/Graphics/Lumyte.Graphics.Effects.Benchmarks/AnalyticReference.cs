using System.Buffers.Binary;
using System.Numerics;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.Graphics.Effects.Benchmarks;

internal static class AnalyticReference
{
    // Fixed a priori: two half-float blur writes plus compositing and shader exp error.
    internal const float AbsoluteTolerance = 0.004f;

    internal static float Verify(EffectProfile profile, byte[] actual)
    {
        if (actual.Length != checked(profile.Width * profile.Height * 8))
        {
            throw new InvalidOperationException("Readback dimensions mismatch.");
        }

        var xMass = new List<double[]>();
        var yMass = new List<double[]>();
        if (profile.Kind == "blur")
        {
            for (int layer = 0; layer < profile.Layers; layer++)
            {
                Rect2D rect = profile.Content(layer);
                xMass.Add(Mass(profile.Width, (int)rect.X, (int)(rect.X + rect.Width), profile.Sigma));
                yMass.Add(Mass(profile.Height, (int)rect.Y, (int)(rect.Y + rect.Height), profile.Sigma));
            }
        }
        else if (profile.Kind == "backdrop")
        {
            xMass.Add(Mass(profile.Width, 0, profile.Width, profile.Sigma));
            xMass.Add(Mass(profile.Width, 0, profile.Width / 2, profile.Sigma));
            yMass.Add(Mass(profile.Height, 0, profile.Height, profile.Sigma));
        }

        float maximum = 0;
        for (int y = 0; y < profile.Height; y++)
        {
            for (int x = 0; x < profile.Width; x++)
            {
                var expected = new Vector4(0, 0, 1, 1);
                if (profile.Kind == "isolation")
                {
                    for (int layer = 0; layer < profile.Layers; layer++)
                    {
                        Rect2D rect = profile.Content(layer);
                        if (Contains(rect, x, y))
                        {
                            bool red = x >= rect.X + (rect.Width / 2);
                            var source = new Vector4(0.5f, red ? 0 : 0.5f, red ? 0 : 0.5f, 0.5f);
                            expected = source + (expected * 0.5f);
                        }
                    }
                }
                else if (profile.Kind == "blur")
                {
                    for (int layer = 0; layer < profile.Layers; layer++)
                    {
                        float alpha = (float)(xMass[layer][x] * yMass[layer][y]);
                        expected = new Vector4(alpha) + (expected * (1 - alpha));
                    }
                }
                else
                {
                    expected = x < profile.Width / 2 ? Vector4.One : new Vector4(1, 0, 0, 1);
                    if (profile.Kind == "backdrop" && Contains(profile.BackdropBounds, x, y))
                    {
                        float alpha = (float)(xMass[0][x] * yMass[0][y]);
                        float white = (float)(xMass[1][x] * yMass[0][y]);
                        expected = new Vector4(alpha, white, white, alpha);
                    }
                }

                int offset = ((y * profile.Width) + x) * 8;
                for (int channel = 0; channel < 4; channel++)
                {
                    ushort bits = BinaryPrimitives.ReadUInt16LittleEndian(actual.AsSpan(offset + (channel * 2), 2));
                    float value = (float)BitConverter.UInt16BitsToHalf(bits);
                    float difference = MathF.Abs(value - expected[channel]);
                    if (!float.IsFinite(value) || difference > AbsoluteTolerance)
                    {
                        throw new InvalidOperationException($"Independent analytic mismatch {profile.Id} at ({x},{y}) channel {channel}: {value} != {expected[channel]}, tolerance {AbsoluteTolerance}.");
                    }

                    maximum = MathF.Max(maximum, difference);
                }
            }
        }

        return maximum;
    }

    private static bool Contains(Rect2D rect, int x, int y) => x + 0.5 >= rect.X && x + 0.5 < rect.X + rect.Width && y + 0.5 >= rect.Y && y + 0.5 < rect.Y + rect.Height;

    // Independent discrete Gaussian integration of a known axis-aligned input rectangle.
    // No DrawList flattening, renderer geometry, GPU output or product blur helper is reused.
    private static double[] Mass(int length, int begin, int end, int sigma)
    {
        int radius = 3 * sigma;
        double[] weights = new double[(2 * radius) + 1];
        double total = 0;
        for (int delta = -radius; delta <= radius; delta++)
        {
            double weight = Math.Exp(-0.5 * delta * delta / (sigma * sigma));
            weights[delta + radius] = weight;
            total += weight;
        }

        double[] result = new double[length];
        for (int pixel = 0; pixel < length; pixel++)
        {
            double sum = 0;
            for (int delta = -radius; delta <= radius; delta++)
            {
                if (pixel + delta >= begin && pixel + delta < end)
                {
                    sum += weights[delta + radius];
                }
            }

            result[pixel] = sum / total;
        }

        return result;
    }
}
