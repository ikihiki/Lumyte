using Lumyte.Pipelines;

namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Validates downloaded coverage without changing correlated samples or source identity.</summary>
public sealed class CpuValidateCoverage2D : IStage<Host<Coverage16V1>, Host<FrameV1>, Host<Coverage16V1>>
{
    internal CpuValidateCoverage2D(SamplePolicy2D samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        samples.Validate();
        Factory = new Implementation();
    }

    /// <summary>Gets the executable factory.</summary>
    public IStageFactory<Host<Coverage16V1>, Host<FrameV1>, Host<Coverage16V1>> Factory { get; }

    private sealed class Implementation : IStageFactory<Host<Coverage16V1>, Host<FrameV1>, Host<Coverage16V1>>, IStageProcessor<Host<Coverage16V1>, Host<FrameV1>, Host<Coverage16V1>>
    {
        public StageRequirements Describe(StageInputDescriptions inputs) => new(128, []);

        public IStageProcessor<Host<Coverage16V1>, Host<FrameV1>, Host<Coverage16V1>> Create(StageBuildContext context) => new Implementation();

        public ValueTask<ValueLease<Host<Coverage16V1>>> PrepareAsync(StagePreparationContext context, ValueLease<Host<Coverage16V1>> a, ValueLease<Host<FrameV1>> b, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SchemaData<Coverage16V1> coverage = context.ReadHost(a);
            SchemaData<FrameV1> frame = context.ReadHost(b);
            CpuNormalize2D.ValidateIdentity(coverage.Extent, frame.Extent);
            long expected = checked(16L * Convert.ToInt64(frame.Extent.Metadata["width"]) * Convert.ToInt64(frame.Extent.Metadata["height"]) * Convert.ToInt64(frame.Extent.Metadata["commandCount"]));
            if (coverage.Extent.ElementCount != expected)
            {
                throw new InvalidOperationException("Coverage must retain sixteen entries per pixel/command.");
            }

            foreach (float value in coverage.ReadElements<float>())
            {
                if (!float.IsFinite(value) || value < 0 || value > 1)
                {
                    throw new InvalidOperationException("Invalid coverage.");
                }
            }

            return ValueTask.FromResult(context.Forward(a));
        }

        public void Dispose()
        {
        }
    }
}
