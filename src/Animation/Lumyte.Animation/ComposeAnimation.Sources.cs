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
            public IAnimationSource<T> Build() => Compile(new HashSet<object>(ReferenceEqualityComparer.Instance));

            internal IAnimationSource<T> Compile(HashSet<object> ancestors)
            {
                if (!ancestors.Add(this))
                {
                    throw new ArgumentException("A source definition cannot contain a cycle.");
                }

                try
                {
                    return CompileCore(ancestors);
                }
                finally
                {
                    ancestors.Remove(this);
                }
            }

            internal abstract IAnimationSource<T> CompileCore(HashSet<object> ancestors);
        }

        /// <summary>Wraps an existing immutable source for use in a source graph.</summary>
        /// <typeparam name="T">The value type.</typeparam>
        [Composable(Factory = "ComposeAnimation")]
        public partial class Sampled<T> : Source<T>
        {
            /// <summary>Gets the immutable source.</summary>
            [ComposeParameter]
            public required IAnimationSource<T> Value { get; init; }

            internal override IAnimationSource<T> CompileCore(HashSet<object> ancestors)
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

            internal override IAnimationSource<T> CompileCore(HashSet<object> ancestors) => new AnimationCurve<T>(Duration, Keys, Interpolator);
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

            internal override IAnimationSource<T> CompileCore(HashSet<object> ancestors) => new AnimationHermiteCurve<T>(Duration, Keys, Interpolator);
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

            internal override IAnimationSource<T> CompileCore(HashSet<object> ancestors)
            {
                ArgumentNullException.ThrowIfNull(Value);
                ArgumentNullException.ThrowIfNull(TimeMap);
                return new AnimationTimeRemap<T>(Value.Compile(ancestors), TimeMap.Compile(ancestors));
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

            internal override IAnimationSource<T> CompileCore(HashSet<object> ancestors)
            {
                ArgumentNullException.ThrowIfNull(From);
                ArgumentNullException.ThrowIfNull(To);
                ArgumentNullException.ThrowIfNull(Weight);
                return new AnimationBlend<T>(From.Compile(ancestors), To.Compile(ancestors), Weight.Compile(ancestors), Interpolator);
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
    }
}
