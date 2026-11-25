using System.Numerics;
using Lumyte.Pipelines;

namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Produces canonical unmanaged scene cells, never command or device references.</summary>
public sealed class CpuNormalize2D : IStage<Host<CommandsV1>, Host<ResourceManifestV1>, Host<FrameV1>, Host<SceneV1>>
{
    internal CpuNormalize2D(SceneLimits2D limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        Factory = new Implementation(limits);
    }

    /// <summary>Gets the immutable executable factory.</summary>
    public IStageFactory<Host<CommandsV1>, Host<ResourceManifestV1>, Host<FrameV1>, Host<SceneV1>> Factory { get; }

    internal static Vector4 UInts(uint x, uint y, uint z, uint w) => new(BitConverter.UInt32BitsToSingle(x), BitConverter.UInt32BitsToSingle(y), BitConverter.UInt32BitsToSingle(z), BitConverter.UInt32BitsToSingle(w));

    internal static Vector4[] Normalize(RenderRequest2D request, SceneLimits2D limits, CancellationToken ct)
    {
        limits.Validate();
        request.Context?.ValidateResources();
        var admission = new CanonicalAdmission2D(limits, request.Target);
        admission.Inspect(request.Commands); // No expansion has occurred before all aggregate ceilings pass.
        var cells = new List<Vector4>
        {
            UInts(0x314E4353, (uint)request.Commands.Count, 16, 1), // SCN1
        };
        for (int ordinal = 0; ordinal < request.Commands.Count; ordinal++)
        {
            ct.ThrowIfCancellationRequested();
            DrawingCommand2D command = request.Commands[ordinal];
            var program = new CanonicalProgram2D(admission, request.Target);
            uint root = program.Compile(command);
            program.ValidateFiniteData();
            var clip = NormalizedClip2D.Create(command.State.Clip.Elements, request.Target);
            int start = cells.Count;
            int data = checked(start + 6 + (program.Instructions.Count * 8));
            int mask = checked(data + program.Data.Count);
            int end = checked(mask + clip.Mask.Length);
            uint shape = uint.MaxValue, paint = root;

            // The final multiply is the independent shape/paint split; outer clip nodes follow it.
            for (int i = program.Instructions.Count - 1; i >= 0; i--)
            {
                if (program.Instructions[i].Kind == 11)
                {
                    shape = program.Instructions[i].First;

                    // Resource-free Coverage treats sampled-mask opcodes as their destination rectangle.
                    // The corresponding paint root keeps the sampled mask factor correlated per sample.
                    paint = command is DrawCoverageMaskCommand2D or DrawDistanceFieldCommand2D ? (uint)i : program.Instructions[i].Count;
                    break;
                }
            }

            cells.Add(UInts((uint)ordinal, (uint)ordinal, (uint)end, root));
            cells.Add(UInts((uint)(start + 6), (uint)program.Instructions.Count, (uint)data, (uint)program.Data.Count));
            cells.Add(UInts((uint)clip.Kind, (uint)mask, (uint)clip.Mask.Length, command is DrawMeshCommand2D ? 1u : 0u));
            cells.Add(new(clip.Bounds.X, clip.Bounds.Y, clip.Bounds.Width, clip.Bounds.Height));
            cells.Add(clip.Samples);
            cells.Add(UInts(shape, paint, CommandKind(command), 0));
            foreach (DrawingInstruction instruction in program.Instructions)
            {
                cells.Add(instruction.T0);
                cells.Add(instruction.T1);
                cells.Add(instruction.A);
                cells.Add(instruction.B);
                cells.Add(instruction.C);
                cells.Add(instruction.D);
                cells.Add(UInts(instruction.Kind, instruction.First, instruction.Count, instruction.Mode));
                uint view = instruction.TextureIndex < 0 ? uint.MaxValue : request.ResourceSnapshot.ViewId(program.TextureInputs[instruction.TextureIndex]);
                uint sampler = instruction.SamplerIndex < 0 ? uint.MaxValue : request.ResourceSnapshot.SamplerId(program.SamplerInputs[instruction.SamplerIndex]);
                cells.Add(UInts(view, sampler, 0, 0));
            }

            cells.AddRange(program.Data);
            cells.AddRange(clip.Mask);
        }

        return [.. cells];
    }

    internal static void ValidateIdentity(SchemaExtent a, SchemaExtent b)
    {
        foreach (string key in new[] { "source", "width", "height", "samples", "gridVersion", "commandCount" })
        {
            if (!a.Metadata.TryGetValue(key, out object? av) || !b.Metadata.TryGetValue(key, out object? bv) || !Equals(av, bv))
            {
                throw new InvalidOperationException("Source/extent/sample/grid mismatch: " + key);
            }
        }

        foreach (string key in new[] { "translationX", "translationY" })
        {
            object av = a.Metadata.TryGetValue(key, out object? ax) ? ax : 0f;
            object bv = b.Metadata.TryGetValue(key, out object? bx) ? bx : 0f;
            if (!Equals(av, bv))
            {
                throw new InvalidOperationException("Retained placement property mismatch.");
            }
        }

        if (!a.Metadata.TryGetValue("samples", out object? samples) || !Equals(samples, 16) || !a.Metadata.TryGetValue("gridVersion", out object? grid) || !Equals(grid, 1))
        {
            throw new InvalidOperationException("Noncanonical sample grid.");
        }
    }

    private static uint CommandKind(DrawingCommand2D c) => c switch
    {
        FillRectCommand2D => 0,
        FillRoundedRectCommand2D => 1,
        FillCircleCommand2D => 2,
        DrawLineCommand2D => 3,
        FillPathCommand2D => 4,
        StrokePathCommand2D => 5,
        DrawImageCommand2D => 6,
        DrawCoverageMaskCommand2D => 7,
        DrawDistanceFieldCommand2D => 8,
        DrawMeshCommand2D => 9,
        DrawPaintGraphCommand2D => 10,
        _ => throw new NotSupportedException("Unknown command kind."),
    };

    private sealed class Implementation(SceneLimits2D limits) : IStageFactory<Host<CommandsV1>, Host<ResourceManifestV1>, Host<FrameV1>, Host<SceneV1>>, IStageProcessor<Host<CommandsV1>, Host<ResourceManifestV1>, Host<FrameV1>, Host<SceneV1>>
    {
        public StageRequirements Describe(StageInputDescriptions inputs)
        {
            if (inputs.Extents[0].Metadata.TryGetValue("commandsSnapshot", out object? snapshot) && snapshot is RenderRequest2D request)
            {
                var admission = new CanonicalAdmission2D(limits, request.Target, inputs.MaxHostBytes);
                admission.Inspect(request.Commands);
                return new StageRequirements(admission.PeakHostBytes, []) { OutputMaxElements = admission.CanonicalCellBound };
            }

            long commands = inputs.Extents[0].ElementCount;
            long cells = checked(1 + (commands * (6L + (256L * 8) + 262144L + 262144L)));
            return new StageRequirements(checked(256 + (128 * cells)), []) { OutputMaxElements = cells };
        }

        public IStageProcessor<Host<CommandsV1>, Host<ResourceManifestV1>, Host<FrameV1>, Host<SceneV1>> Create(StageBuildContext context) => new Implementation(limits);

        public ValueTask<ValueLease<Host<SceneV1>>> PrepareAsync(StagePreparationContext context, ValueLease<Host<CommandsV1>> a, ValueLease<Host<ResourceManifestV1>> b, ValueLease<Host<FrameV1>> c, CancellationToken cancellationToken)
        {
            SchemaData<CommandsV1> commands = context.ReadHost(a);
            SchemaData<ResourceManifestV1> manifest = context.ReadHost(b);
            SchemaData<FrameV1> frame = context.ReadHost(c);
            if (!commands.Extent.Metadata.TryGetValue("commandsSnapshot", out object? snapshot) || snapshot is not RenderRequest2D request)
            {
                throw new InvalidOperationException("Missing immutable commands snapshot.");
            }

            ValidateIdentity(commands.Extent, frame.Extent);
            ValidateIdentity(commands.Extent, manifest.Extent);
            if (commands.Extent.ElementCount != request.Commands.Count || !manifest.Extent.Metadata.TryGetValue("resourceSnapshot", out object? resources) || !ReferenceEquals(resources, request.ResourceSnapshot))
            {
                throw new InvalidOperationException("Resource/command snapshot mismatch.");
            }

            if (!frame.ReadElements<Vector4>().SequenceEqual(request.FrameCells()))
            {
                throw new InvalidOperationException("Frame payload differs from captured grid.");
            }

            ReadOnlySpan<uint> ordinals = commands.ReadElements<uint>();
            for (int i = 0; i < ordinals.Length; i++)
            {
                if (ordinals[i] != i)
                {
                    throw new InvalidOperationException("Noncontiguous logical ordinals.");
                }
            }

            Vector4[] cells = Normalize(request, limits, cancellationToken);
            return ValueTask.FromResult(context.PublishHost(context.CopyHostData<SceneV1, Vector4>(cells, new(cells.Length, request.Metadata()))));
        }

        public void Dispose()
        {
        }
    }
}
