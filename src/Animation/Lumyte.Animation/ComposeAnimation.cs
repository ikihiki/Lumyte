using Lumyte.Composition;
using Lumyte.Core.Time;

namespace Lumyte.Animation;

/// <summary>Generated Composition factories and validated animation definition nodes.</summary>
public static partial class ComposeAnimation
{
    /// <summary>Represents definitions.</summary>
    public static partial class Definitions
    {
        /// <summary>Represents timeline item.</summary>
        public abstract class TimelineItem
        {
            internal abstract TimelineNode Compile(HashSet<TimelineItem> ancestors);
        }

        /// <summary>A composable root definition compiled into an immutable animation timeline.</summary>
        [Composable(Factory = "ComposeAnimation")]
        public partial class Timeline : TimelineItem
        {
            /// <summary>Gets or sets the children.</summary>
            [ComposeContent]
            public IReadOnlyList<TimelineItem> Children { get; set; } = [];

            /// <summary>Validates and snapshots the definition without starting playback or applying values.</summary>
            /// <returns>The computed result.</returns>
            public AnimationTimeline Build()
            {
                TimelineNode node = Compile([]);
                if (node.Length == 0)
                {
                    throw new ArgumentException("The timeline needs a positive duration.");
                }

                return new AnimationTimeline(node);
            }

            internal override TimelineNode Compile(HashSet<TimelineItem> ancestors)
            {
                if (!ancestors.Add(this))
                {
                    throw new ArgumentException("A timeline cannot contain a cycle.");
                }

                try
                {
                    ArgumentNullException.ThrowIfNull(Children);
                    var items = new List<PlacedNode>();
                    long length = 0;
                    foreach (TimelineItem child in Children)
                    {
                        ArgumentNullException.ThrowIfNull(child);
                        TimelineNode compiled = child.Compile(ancestors);
                        items.Add(new PlacedNode(0, compiled));
                        length = Math.Max(length, compiled.Length);
                    }

                    return new GroupNode([.. items], length);
                }
                finally
                {
                    ancestors.Remove(this);
                }
            }
        }

        /// <summary>Places child definitions consecutively in registration order.</summary>
        [Composable(Factory = "ComposeAnimation")]
        public partial class Sequence : TimelineItem
        {
            /// <summary>Gets or sets the children.</summary>
            [ComposeContent]
            public IReadOnlyList<TimelineItem> Children { get; set; } = [];

            internal override TimelineNode Compile(HashSet<TimelineItem> ancestors)
            {
                if (!ancestors.Add(this))
                {
                    throw new ArgumentException("A timeline cannot contain a cycle.");
                }

                try
                {
                    ArgumentNullException.ThrowIfNull(Children);
                    var items = new List<PlacedNode>();
                    long length = 0;
                    foreach (TimelineItem child in Children)
                    {
                        ArgumentNullException.ThrowIfNull(child);
                        TimelineNode compiled = child.Compile(ancestors);
                        items.Add(new PlacedNode(length, compiled));
                        length = checked(length + compiled.Length);
                    }

                    return new GroupNode([.. items], length);
                }
                finally
                {
                    ancestors.Remove(this);
                }
            }
        }

        /// <summary>Places child definitions at the same start time.</summary>
        [Composable(Factory = "ComposeAnimation")]
        public partial class Parallel : TimelineItem
        {
            /// <summary>Gets or sets the children.</summary>
            [ComposeContent]
            public IReadOnlyList<TimelineItem> Children { get; set; } = [];

            internal override TimelineNode Compile(HashSet<TimelineItem> ancestors)
            {
                if (!ancestors.Add(this))
                {
                    throw new ArgumentException("A timeline cannot contain a cycle.");
                }

                try
                {
                    ArgumentNullException.ThrowIfNull(Children);
                    var items = new List<PlacedNode>();
                    long length = 0;
                    foreach (TimelineItem child in Children)
                    {
                        ArgumentNullException.ThrowIfNull(child);
                        TimelineNode compiled = child.Compile(ancestors);
                        items.Add(new PlacedNode(0, compiled));
                        length = Math.Max(length, compiled.Length);
                    }

                    return new GroupNode([.. items], length);
                }
                finally
                {
                    ancestors.Remove(this);
                }
            }
        }

        /// <summary>Contributes a nonnegative duration without producing a value.</summary>
        [Composable(Factory = "ComposeAnimation")]
        public partial class Delay : TimelineItem
        {
            /// <summary>Gets the duration.</summary>
            [ComposeParameter]
            public required Duration Duration { get; init; }

            internal override TimelineNode Compile(HashSet<TimelineItem> ancestors)
            {
                if (!ancestors.Add(this))
                {
                    throw new ArgumentException("A timeline cannot contain a cycle.");
                }

                try
                {
                    ArgumentOutOfRangeException.ThrowIfNegative(Duration.Ticks, nameof(Duration));
                    return new GroupNode([], Duration.Ticks);
                }
                finally
                {
                    ancestors.Remove(this);
                }
            }
        }

        /// <summary>Connects an immutable typed value source to a typed result channel.</summary>
        /// <typeparam name="T">The value type.</typeparam>
        [Composable(Factory = "ComposeAnimation")]
        public partial class Track<T> : TimelineItem
        {
            /// <summary>Gets the channel.</summary>
            [ComposeParameter]
            public required AnimationChannel<T> Channel { get; init; }

            /// <summary>Gets the source.</summary>
            [ComposeParameter]
            public required IAnimationSource<T> Source { get; init; }

            /// <summary>Gets the fill.</summary>
            [ComposeParameter]
            public AnimationFillMode Fill { get; init; } = AnimationFillMode.Hold;

            internal override TimelineNode Compile(HashSet<TimelineItem> ancestors)
            {
                if (!ancestors.Add(this))
                {
                    throw new ArgumentException("A timeline cannot contain a cycle.");
                }

                try
                {
                    ArgumentNullException.ThrowIfNull(Channel);
                    ArgumentNullException.ThrowIfNull(Source);
                    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Source.Duration.Ticks, nameof(Source));
                    if (!Enum.IsDefined(Fill))
                    {
                        throw new ArgumentOutOfRangeException(nameof(Fill));
                    }

                    return new ValueNode<T>(Source, Channel, Fill);
                }
                finally
                {
                    ancestors.Remove(this);
                }
            }
        }

        /// <summary>Contributes a named event at its placement time without consuming time.</summary>
        [Composable(Factory = "ComposeAnimation")]
        public partial class Marker : TimelineItem
        {
            /// <summary>Gets the name.</summary>
            [ComposeParameter]
            public required string Name { get; init; }

            /// <summary>Gets the payload.</summary>
            [ComposeParameter]
            public string? Payload { get; init; }

            internal override TimelineNode Compile(HashSet<TimelineItem> ancestors)
            {
                if (!ancestors.Add(this))
                {
                    throw new ArgumentException("A timeline cannot contain a cycle.");
                }

                try
                {
                    ArgumentException.ThrowIfNullOrWhiteSpace(Name);
                    return new MarkerNode(new AnimationEvent(Duration.Zero, Name, Payload));
                }
                finally
                {
                    ancestors.Remove(this);
                }
            }
        }

        /// <summary>Repeats exactly one child a positive finite number of times without expanding it.</summary>
        [Composable(Factory = "ComposeAnimation")]
        public partial class Repeat : TimelineItem
        {
            /// <summary>Gets or sets the children.</summary>
            [ComposeContent]
            public IReadOnlyList<TimelineItem> Children { get; set; } = [];

            /// <summary>Gets the count.</summary>
            [ComposeParameter]
            public required int Count { get; init; }

            internal override TimelineNode Compile(HashSet<TimelineItem> ancestors)
            {
                if (!ancestors.Add(this))
                {
                    throw new ArgumentException("A timeline cannot contain a cycle.");
                }

                try
                {
                    ArgumentNullException.ThrowIfNull(Children);
                    if (Children.Count != 1)
                    {
                        throw new ArgumentException("This operator needs exactly one child.");
                    }

                    ArgumentNullException.ThrowIfNull(Children[0]);
                    TimelineNode child = Children[0].Compile(ancestors);
                    if (child.Length == 0)
                    {
                        throw new ArgumentException("This operator needs a positive child duration.");
                    }

                    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Count);
                    return new RepeatNode(child, Count);
                }
                finally
                {
                    ancestors.Remove(this);
                }
            }
        }

        /// <summary>Evaluates exactly one child with reversed local time.</summary>
        [Composable(Factory = "ComposeAnimation")]
        public partial class Reverse : TimelineItem
        {
            /// <summary>Gets or sets the children.</summary>
            [ComposeContent]
            public IReadOnlyList<TimelineItem> Children { get; set; } = [];

            internal override TimelineNode Compile(HashSet<TimelineItem> ancestors)
            {
                if (!ancestors.Add(this))
                {
                    throw new ArgumentException("A timeline cannot contain a cycle.");
                }

                try
                {
                    ArgumentNullException.ThrowIfNull(Children);
                    if (Children.Count != 1)
                    {
                        throw new ArgumentException("This operator needs exactly one child.");
                    }

                    ArgumentNullException.ThrowIfNull(Children[0]);
                    TimelineNode child = Children[0].Compile(ancestors);
                    if (child.Length == 0)
                    {
                        throw new ArgumentException("This operator needs a positive child duration.");
                    }

                    return new ReverseNode(child);
                }
                finally
                {
                    ancestors.Remove(this);
                }
            }
        }
    }
}
