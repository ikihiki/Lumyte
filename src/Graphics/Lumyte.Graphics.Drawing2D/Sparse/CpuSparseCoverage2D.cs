using System.Numerics;
using Lumyte.Pipelines;

namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>CPU row-mask coverage over the immutable canonical scene. Upload the result explicitly.</summary>
public sealed class CpuSparseCoverage2D : IStage<Host<SceneV1>, Host<FrameV1>, Host<Coverage16V1>>
{
    /// <summary>Initializes a new instance of the <see cref="CpuSparseCoverage2D"/> class.</summary>
    /// <param name="samples">The canonical correlated grid.</param>
    /// <param name="maxWorkingBytes">The explicit peak working byte ceiling.</param>
    public CpuSparseCoverage2D(SamplePolicy2D samples, long maxWorkingBytes)
    {
        ArgumentNullException.ThrowIfNull(samples);
        samples.Validate();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxWorkingBytes);
        Factory = new Implementation(maxWorkingBytes);
    }

    /// <summary>Gets the executable factory.</summary>
    public IStageFactory<Host<SceneV1>, Host<FrameV1>, Host<Coverage16V1>> Factory { get; }

    internal static float[] Evaluate(ReadOnlySpan<Vector4> scene, ReadOnlySpan<Vector4> frame, SchemaExtent extent, long limit, CancellationToken token)
    {
        int width = checked(Convert.ToInt32(extent.Metadata["width"])), height = checked(Convert.ToInt32(extent.Metadata["height"]));
        int commands = checked(Convert.ToInt32(extent.Metadata["commandCount"]));
        int count = checked((int)GenericGpuPreparation2D.Count(extent));
        if (width > 32768 || height > 32768)
        {
            throw new NotSupportedException("CPU sparse row extent exceeds 32768 pixels.");
        }

        if (checked(256L + (count * 8L) + (width * 2L)) > limit)
        {
            throw new NotSupportedException("CPU coverage working byte ceiling exceeded before allocation.");
        }

        if (frame.Length != 2 || frame[0].X != width || frame[0].Y != height || frame[1].Z != 16 || frame[1].W != 1)
        {
            throw new InvalidOperationException("Frame wire grid mismatch.");
        }

        if (scene.Length < 1 || U(scene[0].X) != 0x314e4353 || U(scene[0].Y) != commands || U(scene[0].Z) != 16 || U(scene[0].W) != 1)
        {
            throw new InvalidOperationException("Scene wire header mismatch.");
        }

        float tx = extent.Metadata.TryGetValue("translationX", out object? x) ? Convert.ToSingle(x) : 0;
        float ty = extent.Metadata.TryGetValue("translationY", out object? y) ? Convert.ToSingle(y) : 0;
        if (!float.IsFinite(tx) || !float.IsFinite(ty))
        {
            throw new InvalidOperationException("Nonfinite placement.");
        }

        // Validate every command before allocating the dense compatible output.
        int header = 1;
        long work = 0;
        for (int j = 0; j < commands; j++)
        {
            Validate(scene, header, j, out int instruction, out int data, out int root);
            if (root >= 0)
            {
                int n = instruction + (root * 8);
                long edges = U(scene[n + 6].X) is 4 or 5 ? U(scene[n + 6].Z) : 1;
                work = checked(work + ((long)width * height * 16 * Math.Max(1, edges)));
            }

            if (work > 268435456)
            {
                throw new NotSupportedException("CPU sparse sample-edge ceiling exceeded.");
            }

            header = checked((int)U(scene[header].Z));
        }

        if (header != scene.Length)
        {
            throw new InvalidOperationException("Trailing canonical scene data.");
        }

        token.ThrowIfCancellationRequested();
        float[] output = new float[count];
        ushort[] masks = new ushort[width];
        header = 1;
        for (int j = 0; j < commands; j++)
        {
            Validate(scene, header, j, out int instruction, out int data, out int root);
            for (int py = 0; py < height; py++)
            {
                token.ThrowIfCancellationRequested();
                for (int px = 0; px < width; px++)
                {
                    ushort mask = 0;
                    bool mesh = root >= 0 && U(scene[instruction + (root * 8) + 6].X) == 5 && U(scene[instruction + (root * 8) + 6].W) == 1;
                    float center = mesh ? Shape(scene, instruction + (root * 8), data, new(px + .5f - tx, py + .5f - ty)) : 0;
                    for (int s = 0; s < 16; s++)
                    {
                        float value = mesh ? center : root < 0 ? 0 : Shape(scene, instruction + (root * 8), data, new(px + (((s % 4) + .5f) / 4) - tx, py + (((s / 4) + .5f) / 4) - ty));
                        if (value > 0)
                        {
                            mask |= (ushort)(1 << s);
                        }

                        if (mesh)
                        {
                            output[(((((j * height) + py) * width) + px) * 16) + s] = value;
                        }
                    }

                    masks[px] = mask;
                }

                // Full runs avoid repeatedly decoding a boundary mask; mesh remains its own fractional branch.
                for (int px = 0; px < width; px++)
                {
                    if (root >= 0 && U(scene[instruction + (root * 8) + 6].X) == 5 && U(scene[instruction + (root * 8) + 6].W) == 1)
                    {
                        continue;
                    }

                    int at = ((((j * height) + py) * width) + px) * 16;
                    if (masks[px] == ushort.MaxValue)
                    {
                        output.AsSpan(at, 16).Fill(1);
                    }
                    else
                    {
                        for (int s = 0; s < 16; s++)
                        {
                            output[at + s] = (masks[px] >> s) & 1;
                        }
                    }
                }
            }

            header = checked((int)U(scene[header].Z));
        }

        return output;
    }

    private static void Validate(ReadOnlySpan<Vector4> cells, int h, int ordinal, out int instruction, out int data, out int root)
    {
        if (h < 1 || h > cells.Length - 6 || U(cells[h].X) != ordinal || U(cells[h].Y) != ordinal)
        {
            throw new InvalidOperationException("Invalid canonical command ordinal.");
        }

        int next = checked((int)U(cells[h].Z));
        instruction = checked((int)U(cells[h + 1].X));
        int instructions = checked((int)U(cells[h + 1].Y));
        data = checked((int)U(cells[h + 1].Z));
        int dataCount = checked((int)U(cells[h + 1].W));
        uint r = U(cells[h + 5].X);
        root = r == uint.MaxValue ? -1 : checked((int)r);
        if (instructions < 1 || instructions > 256 || instruction != h + 6 || data != checked(instruction + (instructions * 8)) || dataCount < 0 || next < checked(data + dataCount) || next > cells.Length || root >= instructions)
        {
            throw new InvalidOperationException("Invalid canonical command ranges.");
        }

        if (root < 0)
        {
            return;
        }

        int n = instruction + (root * 8);
        uint kind = U(cells[n + 6].X), first = U(cells[n + 6].Y), count = U(cells[n + 6].Z);
        if (kind is not (0 or 1 or 2 or 3 or 4 or 5 or 13 or 14))
        {
            throw new NotSupportedException("Canonical shape root is not a resource-free coverage primitive.");
        }

        if ((kind is 4 or 5) && checked((long)first + ((long)count * (kind == 5 ? 3 : 1))) > dataCount)
        {
            throw new InvalidOperationException("Shape data range exceeds command.");
        }

        if (kind == 0 && cells[n + 2].X is not (0 or 1))
        {
            throw new NotSupportedException("Binary constant coverage required.");
        }

        if (kind == 5 && U(cells[n + 6].W) > 1)
        {
            throw new InvalidOperationException("Unknown triangle coverage mode.");
        }

        for (int i = 0; i < dataCount; i++)
        {
            Vector4 v = cells[data + i];
            if (!float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Z) || !float.IsFinite(v.W))
            {
                throw new InvalidOperationException("Nonfinite canonical geometry.");
            }
        }

        for (int i = 0; i < 4; i++)
        {
            Vector4 v = cells[n + i];
            if (!float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Z) || !float.IsFinite(v.W))
            {
                throw new InvalidOperationException("Nonfinite shape data.");
            }
        }
    }

    private static uint U(float v) => BitConverter.SingleToUInt32Bits(v);

    private static float Cross(Vector2 a, Vector2 b) => (a.X * b.Y) - (a.Y * b.X);

    private static float Shape(ReadOnlySpan<Vector4> cells, int n, int data, Vector2 pixel)
    {
        Vector4 t0 = cells[n], t1 = cells[n + 1], a = cells[n + 2], b = cells[n + 3], k = cells[n + 6];
        Vector2 p = new((t0.X * pixel.X) + (t0.Y * pixel.Y) + t0.Z, (t1.X * pixel.X) + (t1.Y * pixel.Y) + t1.Z);
        uint kind = U(k.X);
        if (kind == 0)
        {
            return a.X;
        }

        if (kind is 1 or 13 or 14)
        {
            return p.X >= a.X && p.Y >= a.Y && p.X < a.X + a.Z && p.Y < a.Y + a.W ? 1 : 0;
        }

        if (kind == 2)
        {
            Vector2 half = new(a.Z / 2, a.W / 2);
            Vector2 q = Vector2.Abs(p - new Vector2(a.X, a.Y) - half) - half + new Vector2(b.X);
            return Vector2.Max(q, Vector2.Zero).Length() + MathF.Min(MathF.Max(q.X, q.Y), 0) <= b.X && a.Z > 0 && a.W > 0 ? 1 : 0;
        }

        if (kind == 3)
        {
            return Vector2.Distance(p, new(a.X, a.Y)) < a.Z ? 1 : 0;
        }

        int first = checked(data + (int)U(k.Y)), count = checked((int)U(k.Z));
        if (kind == 4)
        {
            int winding = 0;
            for (int i = 0; i < count; i++)
            {
                Vector4 e = cells[first + i];
                float side = Cross(new(e.Z - e.X, e.W - e.Y), p - new Vector2(e.X, e.Y));
                if (e.Y <= p.Y && e.W > p.Y && side > 0)
                {
                    winding++;
                }

                if (e.W <= p.Y && e.Y > p.Y && side < 0)
                {
                    winding--;
                }
            }

            return U(k.W) == 0 ? (winding != 0 ? 1 : 0) : Math.Abs(winding) % 2;
        }

        float result = 0;
        for (int i = 0; i < count; i++)
        {
            Vector4 v0 = cells[first + (i * 3)], v1 = cells[first + (i * 3) + 1], v2 = cells[first + (i * 3) + 2];
            Vector2 p0 = new(v0.X, v0.Y), p1 = new(v1.X, v1.Y), p2 = new(v2.X, v2.Y);
            float area = Cross(p1 - p0, p2 - p0);
            if (MathF.Abs(area) < 1e-20f)
            {
                continue;
            }

            float u = Cross(p1 - p, p2 - p) / area, v = Cross(p2 - p, p0 - p) / area, w = 1 - u - v;
            if (u >= 0 && v >= 0 && w >= 0)
            {
                result = MathF.Max(result, Math.Clamp((u * v0.Z) + (v * v1.Z) + (w * v2.Z), 0, 1));
            }
        }

        return result;
    }

    private sealed class Implementation(long limit) : IStageFactory<Host<SceneV1>, Host<FrameV1>, Host<Coverage16V1>>, IStageProcessor<Host<SceneV1>, Host<FrameV1>, Host<Coverage16V1>>
    {
        public StageRequirements Describe(StageInputDescriptions inputs)
        {
            if (inputs.Extents.All(extent => extent.Metadata.Count == 1 && extent.Metadata.TryGetValue("LowerBound", out object? value) && Equals(value, true)))
            {
                return new(258, []) { OutputMaxElements = 0 };
            }

            if (inputs.Extents.All(extent => extent.Metadata.Count == 1 && extent.Metadata.TryGetValue("UpperBound", out object? value) && Equals(value, true)))
            {
                long upper = Math.Min(limit, checked(256 + (GenericGpuPreparation2D.MaxSampleEntries * 8) + (32768 * 2)));
                if (upper < 258)
                {
                    throw new NotSupportedException("CPU sparse coverage cannot fit its minimum owned storage.");
                }

                return new(upper, []) { OutputMaxElements = Math.Min(GenericGpuPreparation2D.MaxSampleEntries, (upper - 258) / 8) };
            }

            GenericGpuPreparation2D.Match(inputs.Extents.ToArray());
            long width = Convert.ToInt64(inputs.Extents[0].Metadata["width"]);
            if (width > 32768 || Convert.ToInt64(inputs.Extents[0].Metadata["height"]) > 32768)
            {
                throw new NotSupportedException("CPU sparse row extent exceeds 32768 pixels.");
            }

            long count = GenericGpuPreparation2D.Count(inputs.Extents[0]);
            long bytes = checked(256 + (count * 8) + (width * 2));
            if (bytes > limit)
            {
                throw new NotSupportedException("CPU sparse coverage exceeds its explicit working byte ceiling.");
            }

            return new(bytes, []) { OutputMaxElements = count };
        }

        public IStageProcessor<Host<SceneV1>, Host<FrameV1>, Host<Coverage16V1>> Create(StageBuildContext context) => new Implementation(limit);

        public ValueTask<ValueLease<Host<Coverage16V1>>> PrepareAsync(StagePreparationContext context, ValueLease<Host<SceneV1>> a, ValueLease<Host<FrameV1>> b, CancellationToken cancellationToken)
        {
            SchemaData<SceneV1> scene = context.ReadHost(a);
            SchemaData<FrameV1> frame = context.ReadHost(b);
            GenericGpuPreparation2D.Match(scene.Extent, frame.Extent);
            float[] values = Evaluate(scene.ReadElements<Vector4>(), frame.ReadElements<Vector4>(), scene.Extent, limit, cancellationToken);
            return ValueTask.FromResult(context.PublishHost(context.CopyHostData<Coverage16V1, float>(values, new(values.Length, scene.Extent.Metadata))));
        }

        public void Dispose()
        {
        }
    }
}
