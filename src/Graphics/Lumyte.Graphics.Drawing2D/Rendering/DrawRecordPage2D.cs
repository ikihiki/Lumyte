using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

// The legacy CPU compiler remains the admission/numerical oracle. No legacy
// instruction array is uploaded: reachable nodes are lowered to the compact ABI.
internal sealed class DrawRecordPage2D
{
    internal const int Capacity = 32768 / 96;
    private readonly Dictionary<PaintNode2D, Dictionary<Matrix3x2, uint>> _paintCache = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Vector4, uint> _solidCache = [];
    private readonly Dictionary<NormalizedClip2D, uint> _clipCache = new(ReferenceEqualityComparer.Instance);

    private readonly List<(DrawingProgram2D Program, uint Instruction, int Descriptor, bool Image)> _references = [];

    internal List<DrawRecord2D> Records { get; } = [];

    internal List<DrawDirectoryEntry2D> Paints { get; } = [default];

    internal List<DrawDirectoryEntry2D> Clips { get; } = [default];

    internal List<Vector4> Numeric { get; } = [];

    internal List<ImagePaintData2D> Images { get; } = [];

    internal List<MaskDescriptor2D> Masks { get; } = [];

    internal static bool TrySimple(DrawingCommand2D command, DrawingTargetInfo2D target, NormalizedClip2D clip, out DrawRecord2D record, out Color2D color)
    {
        PaintGraph2D? paint = command switch
        {
            FillRectCommand2D value => value.Paint,
            FillRoundedRectCommand2D value => value.Paint,
            FillCircleCommand2D value => value.Paint,
            _ => null,
        };
        record = default;
        color = default;
        if (paint?.Root is not SolidPaintNode2D solid)
        {
            return false;
        }

        color = solid.Color;
        Matrix3x2 pixels = Matrix3x2.CreateScale(target.PixelScale) * Matrix3x2.CreateTranslation(-target.PixelOrigin);
        Matrix3x2 transform = command.State.Transform * pixels;
        if (!Matrix3x2.Invert(transform, out Matrix3x2 inverse) || !float.IsFinite(inverse.M11) || !float.IsFinite(inverse.M12)
            || !float.IsFinite(inverse.M21) || !float.IsFinite(inverse.M22) || !float.IsFinite(inverse.M31) || !float.IsFinite(inverse.M32))
        {
            return true;
        }

        // The old work charge is shape + solid + Multiply + at most one outer
        // clip. Its old Data charge is exactly this mask, independent of sharing.
        if (clip.Mask.Length > 262144)
        {
            throw new NotSupportedException("Drawing exceeds the 262144 geometry element budget.");
        }

        ScissorRect bounds = DrawingBounds2D.GetScissor(command, null, target, null, false, false, clip.Bounds);
        if (bounds.Width == 0 || bounds.Height == 0)
        {
            return true;
        }

        uint kind = 0, extra = 0;
        Vector4 shape = default;
        switch (command)
        {
            case FillRectCommand2D value:
                kind = 1;
                shape = new(value.Rect.X, value.Rect.Y, value.Rect.Width, value.Rect.Height);
                break;
            case FillRoundedRectCommand2D value:
                kind = 2;
                shape = new(value.Rect.X, value.Rect.Y, value.Rect.Width, value.Rect.Height);
                extra = BitConverter.SingleToUInt32Bits(MathF.Min(value.Radius, MathF.Min(value.Rect.Width, value.Rect.Height) * 0.5f));
                break;
            case FillCircleCommand2D value:
                kind = 3;
                shape = new(value.Center, value.Radius, 0);
                break;
        }

        record = new(kind, 0, 0, 0, new(bounds.X, bounds.Y, checked(bounds.X + bounds.Width), checked(bounds.Y + bounds.Height)), new(inverse.M11, inverse.M21, inverse.M31, 0), new(inverse.M12, inverse.M22, inverse.M32, 0), shape, 0, 0, 0, extra);
        return true;
    }

    internal static void ValidateRange(uint first, uint count, uint available)
    {
        if (first > available || count > available - first)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }
    }

    internal static ulong AlignedStart(ulong offset, ulong alignment, ulong stride)
    {
        if (alignment == 0 || stride == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(alignment));
        }

        ulong a = alignment, b = stride;
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }

        ulong multiple = checked(alignment / a * stride);
        return checked((offset + multiple - 1) / multiple * multiple);
    }

    internal bool FitsAddition(DrawingProgram2D? program, uint root, NormalizedClip2D clip, Color2D color, ulong maximum)
    {
        int outerCells = AdditionalClipCells(clip);
        ulong numeric = checked((ulong)Numeric.Count + (ulong)outerCells);
        ulong paints = (ulong)Paints.Count, clips = checked((ulong)Clips.Count + (outerCells == 0 ? 0ul : 1ul));
        ulong images = (ulong)Images.Count, masks = (ulong)Masks.Count;
        var seen = new HashSet<uint>();
        var solids = new HashSet<Vector4>();
        if (program is null)
        {
            Vector4 linear = DrawingProgram2D.Linear(color);
            if (linear != Vector4.Zero && !_solidCache.ContainsKey(linear))
            {
                numeric++;
                paints++;
            }
        }
        else
        {
            DrawingInstruction terminal = program.Instructions[(int)root];
            if (terminal.Kind is 16 or 17)
            {
                terminal = program.Instructions[(int)terminal.First];
            }

            DrawingInstruction shape = program.Instructions[(int)terminal.First];
            numeric = checked(numeric + (shape.Kind == 4 ? shape.Count : shape.Kind == 5 ? (ulong)shape.Count * 3 : 0));
            if (shape.Kind is 13 or 14)
            {
                masks++;
            }

            CountPaint(terminal.Count);
        }

        return maximum >= 80 && checked(((ulong)Records.Count + 1) * 96) <= maximum
            && paints <= maximum / 16 && clips <= maximum / 16 && Math.Max(1ul, numeric) <= maximum / 16
            && Math.Max(1ul, images) <= maximum / 128 && Math.Max(1ul, masks) <= maximum / 80;

        void CountPaint(uint index)
        {
            if (!seen.Add(index))
            {
                return;
            }

            if (program!.TryPaintIdentity(index, out PaintNode2D? node, out Matrix3x2 transform)
                && _paintCache.TryGetValue(node!, out Dictionary<Matrix3x2, uint>? transforms) && transforms.ContainsKey(transform))
            {
                return;
            }

            DrawingInstruction n = program.Instructions[(int)index];
            if (n.Kind == 0)
            {
                if (n.A == Vector4.Zero || _solidCache.ContainsKey(n.A) || !solids.Add(n.A))
                {
                    return;
                }

                numeric++;
            }
            else if (n.Kind is 6 or 7 or 8)
            {
                numeric = checked(numeric + ((ulong)n.Count * 2) + (n.Kind == 8 ? 4ul : 5ul));
            }
            else if (n.Kind is 9 or 15)
            {
                images++;
                if (n.Kind == 15)
                {
                    CountPaint(n.First);
                }
            }
            else
            {
                CountPaint(n.First);
                if (n.Kind == 12)
                {
                    CountPaint(n.Count);
                }

                if (n.Kind is 16 or 17)
                {
                    clips++;
                    numeric = checked(numeric + (n.Kind == 16 ? 1ul : ((((ulong)n.A.Z * (ulong)n.A.W) + 3) / 4) + 2));
                }

                numeric++;
            }

            paints++;
        }
    }

    internal int AdditionalClipCells(NormalizedClip2D clip)
    {
        if (clip.Kind is NormalizedClipKind2D.None or NormalizedClipKind2D.Scissor || _clipCache.ContainsKey(clip))
        {
            return 0;
        }

        return clip.Kind == NormalizedClipKind2D.SampleMask ? checked(clip.Mask.Length + 2) : 1;
    }

    internal void AddSimple(DrawRecord2D record, Color2D color, NormalizedClip2D clip)
    {
        if (Records.Count == Capacity)
        {
            throw new InvalidOperationException("Close a full record page before appending.");
        }

        Vector4 linear = DrawingProgram2D.Linear(color);
        uint paint = 0;
        if (linear != Vector4.Zero && !_solidCache.TryGetValue(linear, out paint))
        {
            paint = checked((uint)Paints.Count);
            uint first = checked((uint)Numeric.Count);
            Numeric.Add(linear);
            Paints.Add(new(1, first, 1, 0));
            _solidCache.Add(linear, paint);
        }

        Records.Add(record with { PaintId = paint, ClipId = Clip(clip) });
    }

    internal void Add(DrawingCommand2D command, DrawingProgram2D program, uint root, ScissorRect bounds, NormalizedClip2D outer)
    {
        if (Records.Count == Capacity)
        {
            throw new InvalidOperationException("Close a full record page before appending.");
        }

        var ids = new Dictionary<uint, uint>();
        DrawingInstruction terminal = program.Instructions[(int)root];
        if (terminal.Kind is 16 or 17)
        {
            terminal = program.Instructions[(int)terminal.First];
        }

        if (terminal.Kind != 11)
        {
            return; // Empty/non-invertible commands have transparent roots.
        }

        DrawingInstruction shape = program.Instructions[(int)terminal.First];
        if (shape.Kind == 0 && command is not DrawPaintGraphCommand2D)
        {
            return;
        }

        uint paint = Paint(program, terminal.Count, ids);
        uint kind = shape.Kind switch
        {
            0 when command is DrawPaintGraphCommand2D => 9,
            1 => 1,
            2 => 2,
            3 => 3,
            4 => 4,
            5 => shape.Mode == 1 ? 6u : 5u,
            13 => 7,
            14 => 8,
            _ => throw new NotSupportedException("Unknown logical draw shape."),
        };
        uint offset = 0, count = 0;
        if (kind is 4 or 5 or 6)
        {
            count = shape.Count;
            offset = Copy(program.Data, shape.First, checked(shape.Count * (kind == 4 ? 1u : 3u)));
        }
        else if (kind is 7 or 8)
        {
            offset = checked((uint)Masks.Count * 5);
            count = 1;
            _references.Add((program, terminal.First, Masks.Count, false));
            Masks.Add(new(shape.B, shape.C, shape.Mode, 0, 0, 0, null!, null!));
        }

        uint clip = Clip(outer);
        Vector4 inverseX = kind == 9 ? new(1, 0, 0, 0) : shape.T0;
        Vector4 inverseY = kind == 9 ? new(0, 1, 0, 0) : shape.T1;
        Records.Add(new(kind, 0, paint, clip, new(bounds.X, bounds.Y, checked(bounds.X + bounds.Width), checked(bounds.Y + bounds.Height)), inverseX, inverseY, kind is 1 or 2 or 3 or 7 or 8 ? shape.A : default, offset, count, kind == 4 ? shape.Mode : kind == 6 ? 1u : 0u, kind == 2 ? BitConverter.SingleToUInt32Bits(shape.B.X) : 0));
    }

    internal void Resolve(IGpuRef<IGraphicsTextureView> fallback, IGpuRef<IGraphicsSampler> sampler)
    {
        foreach (DrawingProgram2D program in _references.Select(item => item.Program).Distinct())
        {
            program.ResolveReferences(fallback, sampler);
        }

        foreach ((DrawingProgram2D Program, uint Instruction, int Descriptor, bool Image) item in _references)
        {
            DrawingInstruction instruction = item.Program.Instructions[(int)item.Instruction];
            if (item.Image)
            {
                Images[item.Descriptor] = Images[item.Descriptor] with { Texture = instruction.Texture, Sampler = instruction.Sampler };
            }
            else
            {
                Masks[item.Descriptor] = Masks[item.Descriptor] with { Texture = instruction.Texture, Sampler = instruction.Sampler };
            }
        }

        _references.Clear();
    }

    internal void ReleasePackingData()
    {
        Records.Clear();
        Records.TrimExcess();
        Paints.Clear();
        Paints.TrimExcess();
        Clips.Clear();
        Clips.TrimExcess();
        Numeric.Clear();
        Numeric.TrimExcess();
        Images.Clear();
        Images.TrimExcess();
        Masks.Clear();
        Masks.TrimExcess();
        _references.Clear();
        _references.TrimExcess();
        _paintCache.Clear();
        _paintCache.TrimExcess();
        _solidCache.Clear();
        _solidCache.TrimExcess();
        _clipCache.Clear();
        _clipCache.TrimExcess();
    }

    private static Vector4 Bits(uint x, uint y, uint z, uint w) => new(
        BitConverter.UInt32BitsToSingle(x), BitConverter.UInt32BitsToSingle(y), BitConverter.UInt32BitsToSingle(z), BitConverter.UInt32BitsToSingle(w));

    private uint Paint(DrawingProgram2D program, uint index, Dictionary<uint, uint> ids)
    {
        if (ids.TryGetValue(index, out uint existing))
        {
            return existing;
        }

        bool identified = program.TryPaintIdentity(index, out PaintNode2D? node, out Matrix3x2 transform);
        if (identified && _paintCache.TryGetValue(node!, out Dictionary<Matrix3x2, uint>? transforms) && transforms.TryGetValue(transform, out uint shared))
        {
            ids.Add(index, shared);
            return shared;
        }

        DrawingInstruction n = program.Instructions[checked((int)index)];
        if (n.Kind == 0 && _solidCache.TryGetValue(n.A, out uint solid))
        {
            ids.Add(index, solid);
            return solid;
        }

        uint kind, first = checked((uint)Numeric.Count), length;
        switch (n.Kind)
        {
            case 0:
                if (n.A == Vector4.Zero)
                {
                    ids.Add(index, 0);
                    return 0;
                }

                kind = 1; length = 1; Numeric.Add(n.A); break;
            case 6:
            case 7:
            case 8:
                uint stops = Copy(program.Data, n.First, checked(n.Count * 2));
                first = checked((uint)Numeric.Count);
                Numeric.Add(n.T0); Numeric.Add(n.T1); Numeric.Add(n.A);
                if (n.Kind != 8)
                {
                    Numeric.Add(n.B);
                }

                Numeric.Add(Bits(stops, n.Count, n.Mode, 0));
                kind = n.Kind - 4; length = n.Kind == 8 ? 4u : 5u; break;
            case 9:
            case 15:
                uint child = n.Kind == 15 ? Paint(program, n.First, ids) : 0;
                first = checked((uint)Images.Count * 8); length = 8; kind = n.Kind == 9 ? 5u : 9u;
                _references.Add((program, index, Images.Count, true));
                Images.Add(new(n.T0, n.T1, n.A, new(n.B.X, n.B.Y, n.Kind == 15 ? n.D.X : 0, 0), child, checked((uint)n.B.Z), checked((uint)n.B.W), checked((uint)n.C.X), checked((uint)n.C.Y), n.Kind == 15 ? checked((uint)n.D.Y) : 0, 0, 0, null!, null!));
                break;
            case 10:
                uint opacityChild = Paint(program, n.First, ids);
                first = checked((uint)Numeric.Count); length = 1; kind = 6;
                Numeric.Add(new(BitConverter.UInt32BitsToSingle(opacityChild), n.A.X, 0, 0)); break;
            case 12:
                uint source = Paint(program, n.First, ids), backdrop = Paint(program, n.Count, ids);
                first = checked((uint)Numeric.Count); length = 1; kind = 7;
                Numeric.Add(Bits(source, backdrop, n.Mode, 0)); break;
            case 16:
            case 17:
                uint clippedChild = Paint(program, n.First, ids);
                uint clip = Clip(program, n);
                first = checked((uint)Numeric.Count); length = 1; kind = 8;
                Numeric.Add(Bits(clippedChild, clip, 0, 0)); break;
            default: throw new NotSupportedException("Unknown compact paint node.");
        }

        uint id = checked((uint)Paints.Count);
        Paints.Add(new(kind, first, length, 0));
        ids.Add(index, id);
        if (identified)
        {
            if (!_paintCache.TryGetValue(node!, out Dictionary<Matrix3x2, uint>? captured))
            {
                captured = [];
                _paintCache.Add(node!, captured);
            }

            captured[transform] = id;
        }

        if (n.Kind == 0)
        {
            _solidCache[n.A] = id;
        }

        return id;
    }

    private uint Clip(DrawingProgram2D program, DrawingInstruction n)
    {
        uint first;
        if (n.Kind == 16)
        {
            first = checked((uint)Numeric.Count);
            Numeric.Add(Bits((uint)n.A.X, (uint)n.A.Y, (uint)n.A.Z, (uint)n.A.W));
        }
        else
        {
            uint cells = checked((((uint)n.A.Z * (uint)n.A.W) + 3) / 4);
            uint mask = Copy(program.Data, n.Count, cells);
            first = checked((uint)Numeric.Count);
            Numeric.Add(Bits((uint)n.A.X, (uint)n.A.Y, (uint)n.A.Z, (uint)n.A.W));
            Numeric.Add(Bits(mask, cells, 0, 0));
        }

        uint id = checked((uint)Clips.Count);
        Clips.Add(new(n.Kind == 16 ? 3u : 4u, first, n.Kind == 16 ? 1u : 2u, 0));
        return id;
    }

    private uint Clip(NormalizedClip2D clip)
    {
        if (_clipCache.TryGetValue(clip, out uint cached))
        {
            return cached;
        }

        if (clip.Kind is NormalizedClipKind2D.None or NormalizedClipKind2D.Scissor)
        {
            return 0;
        }

        uint first;
        if (clip.Kind == NormalizedClipKind2D.SampleMask)
        {
            uint mask = checked((uint)Numeric.Count);
            Numeric.AddRange(clip.Mask);
            first = checked((uint)Numeric.Count);
            Numeric.Add(Bits(clip.Bounds.X, clip.Bounds.Y, clip.Bounds.Width, clip.Bounds.Height));
            Numeric.Add(Bits(mask, (uint)clip.Mask.Length, 0, 0));
        }
        else
        {
            first = checked((uint)Numeric.Count);
            Numeric.Add(Bits((uint)clip.Samples.X, (uint)clip.Samples.Y, (uint)clip.Samples.Z, (uint)clip.Samples.W));
        }

        uint id = checked((uint)Clips.Count);
        Clips.Add(new((uint)clip.Kind, first, clip.Kind == NormalizedClipKind2D.SampleMask ? 2u : 1u, 0));
        _clipCache.Add(clip, id);
        return id;
    }

    private uint Copy(List<Vector4> source, uint first, uint count)
    {
        if (count == 0)
        {
            return 0;
        }

        if ((ulong)first + count > (ulong)source.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(first));
        }

        uint result = checked((uint)Numeric.Count);
        for (uint i = 0; i < count; i++)
        {
            Numeric.Add(source[checked((int)(first + i))]);
        }

        return result;
    }
}
