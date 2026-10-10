using Lumyte.Composition;
using Lumyte.Core.Time;

namespace Lumyte.Animation;

/// <summary>Generated Composition factories and validated animation definition nodes.</summary>
public static partial class ComposeAnimation
{
    /// <summary>Represents definitions.</summary>
    public static partial class Definitions
    {
        /// <summary>An editable source definition compiled to immutable value evaluation.</summary>
        /// <typeparam name="T">The computed value type.</typeparam>
        public abstract class Source<T>
        {
            /// <summary>Validates and snapshots this source graph without applying values.</summary>
            /// <returns>The compiled, pure value source.</returns>
            public IAnimationSource<T> Build()
            {
                var context = new SourceCompilationContext();
                IAnimationSource<T> source = Compile(context);
                return context.SharedSources.Count == 0 ? source : new CompiledAnimationSource<T>(source, context.SharedSources);
            }

            internal IAnimationSource<T> Compile(SourceCompilationContext context)
            {
                if (!context.Ancestors.Add(this))
                {
                    throw new ArgumentException("A source definition cannot contain a cycle.");
                }

                try
                {
                    if (context.Compiled.TryGetValue(this, out object? cached))
                    {
                        context.SharedSources.Add(cached);
                        return (IAnimationSource<T>)cached;
                    }

                    IAnimationSource<T> source = CompileCore(context);
                    context.Compiled.Add(this, source);
                    if (!context.Sources.Add(source))
                    {
                        context.SharedSources.Add(source);
                    }

                    return source;
                }
                finally
                {
                    context.Ancestors.Remove(this);
                }
            }

            internal abstract IAnimationSource<T> CompileCore(SourceCompilationContext context);
        }

        /// <summary>Wraps an existing immutable source for use in a source graph.</summary>
        /// <typeparam name="T">The value type.</typeparam>
        [Composable(Factory = "ComposeAnimation")]
        public partial class Sampled<T> : Source<T>
        {
            /// <summary>Gets the immutable source.</summary>
            [ComposeParameter]
            public required IAnimationSource<T> Value { get; init; }

            internal override IAnimationSource<T> CompileCore(SourceCompilationContext context)
            {
                ArgumentNullException.ThrowIfNull(Value);
                AnimationSourceValidation.Duration(Value.Duration);
                return Value;
            }
        }

        /// <summary>Defines a copied keyframe source with per-segment timing and interpolation.</summary>
        /// <typeparam name="T">The value type.</typeparam>
        [Composable(Factory = "ComposeAnimation")]
        public partial class Curve<T> : Source<T>
        {
            /// <summary>Gets the positive source duration.</summary>
            [ComposeParameter]
            public required Duration Duration { get; init; }

            /// <summary>Gets the default interpolation rule.</summary>
            [ComposeParameter]
            public required IAnimationInterpolator<T> Interpolator { get; init; }

            /// <summary>Gets or sets the strictly ordered keys.</summary>
            [ComposeContent]
            public IReadOnlyList<AnimationKey<T>> Keys { get; set; } = [];

            internal override IAnimationSource<T> CompileCore(SourceCompilationContext context) => new AnimationCurve<T>(Duration, Keys, Interpolator);
        }

        /// <summary>Defines a copied Hermite source with per-second key tangents.</summary>
        /// <typeparam name="T">The value and tangent type.</typeparam>
        [Composable(Factory = "ComposeAnimation")]
        public partial class HermiteCurve<T> : Source<T>
        {
            /// <summary>Gets the positive source duration.</summary>
            [ComposeParameter]
            public required Duration Duration { get; init; }

            /// <summary>Gets the tangent-aware interpolation rule.</summary>
            [ComposeParameter]
            public required IAnimationHermiteInterpolator<T> Interpolator { get; init; }

            /// <summary>Gets or sets the strictly ordered tangent keys.</summary>
            [ComposeContent]
            public IReadOnlyList<AnimationHermiteKey<T>> Keys { get; set; } = [];

            internal override IAnimationSource<T> CompileCore(SourceCompilationContext context) => new AnimationHermiteCurve<T>(Duration, Keys, Interpolator);
        }

        /// <summary>Defines value evaluation at time supplied by another source graph.</summary>
        /// <typeparam name="T">The value type.</typeparam>
        [Composable(Factory = "ComposeAnimation")]
        public partial class TimeRemap<T> : Source<T>
        {
            /// <summary>Gets the value source definition.</summary>
            [ComposeParameter]
            public required Source<T> Value { get; init; }

            /// <summary>Gets the child-local time source definition.</summary>
            [ComposeParameter]
            public required Source<Duration> TimeMap { get; init; }

            internal override IAnimationSource<T> CompileCore(SourceCompilationContext context)
            {
                ArgumentNullException.ThrowIfNull(Value);
                ArgumentNullException.ThrowIfNull(TimeMap);
                return new AnimationTimeRemap<T>(Value.Compile(context), TimeMap.Compile(context));
            }
        }

        /// <summary>Defines two value sources combined by a time-dependent weight source.</summary>
        /// <typeparam name="T">The value type.</typeparam>
        [Composable(Factory = "ComposeAnimation")]
        public partial class Blend<T> : Source<T>
        {
            /// <summary>Gets the source selected by weight zero.</summary>
            [ComposeParameter]
            public required Source<T> From { get; init; }

            /// <summary>Gets the source selected by weight one.</summary>
            [ComposeParameter]
            public required Source<T> To { get; init; }

            /// <summary>Gets the normalized weight source.</summary>
            [ComposeParameter]
            public required Source<float> Weight { get; init; }

            /// <summary>Gets the type-specific blend rule.</summary>
            [ComposeParameter]
            public required IAnimationInterpolator<T> Interpolator { get; init; }

            internal override IAnimationSource<T> CompileCore(SourceCompilationContext context)
            {
                ArgumentNullException.ThrowIfNull(From);
                ArgumentNullException.ThrowIfNull(To);
                ArgumentNullException.ThrowIfNull(Weight);
                return new AnimationBlend<T>(From.Compile(context), To.Compile(context), Weight.Compile(context), Interpolator);
            }
        }

        /// <summary>Compiles a nested source graph into an ordinary typed timeline contribution.</summary>
        /// <typeparam name="T">The value type.</typeparam>
        [Composable(Factory = "ComposeAnimation")]
        public partial class SourceTrack<T> : TimelineItem
        {
            /// <summary>Gets the result channel.</summary>
            [ComposeParameter]
            public required AnimationChannel<T> Channel { get; init; }

            /// <summary>Gets the source definition compiled with this timeline.</summary>
            [ComposeParameter]
            public required Source<T> Source { get; init; }

            /// <summary>Gets the contribution fill behavior.</summary>
            [ComposeParameter]
            public AnimationFillMode Fill { get; init; } = AnimationFillMode.Hold;

            internal override TimelineNode Compile(HashSet<TimelineItem> ancestors)
            {
                ArgumentNullException.ThrowIfNull(Channel);
                ArgumentNullException.ThrowIfNull(Source);
                if (!Enum.IsDefined(Fill))
                {
                    throw new ArgumentOutOfRangeException(nameof(Fill));
                }

                return new ValueNode<T>(Source.Build(), Channel, Fill);
            }
        }

        internal sealed class SourceCompilationContext
        {
            internal HashSet<object> Ancestors { get; } = new(ReferenceEqualityComparer.Instance);

            internal Dictionary<object, object> Compiled { get; } = new(ReferenceEqualityComparer.Instance);

            internal HashSet<object> Sources { get; } = new(ReferenceEqualityComparer.Instance);

            internal HashSet<object> SharedSources { get; } = new(ReferenceEqualityComparer.Instance);
        }
    }
}
