using System.Collections.Immutable;
using System.Numerics;
using Lumyte.Composition;

namespace Lumyte.Input.Actions;

/// <summary>Provides declarative factories for input action profiles.</summary>
public static partial class Compose
{
    /// <summary>Contains mutable construction nodes, converted to immutable runtime definitions.</summary>
    public static partial class Definitions
    {
        /// <summary>Builds an ActionDefinition.</summary>
        [Composable]
        public partial class Action
        {
            /// <summary>Gets or sets the Id value.</summary>
            [ComposeParameter]
            public required string Id { get; set; }

            /// <summary>Gets or sets the Kind value.</summary>
            [ComposeParameter]
            public required ActionValueKind Kind { get; set; }

            /// <summary>Gets or sets the Sensitivity value.</summary>
            [ComposeParameter]
            public float Sensitivity { get; set; } = 1;

            /// <summary>Gets or sets a value indicating whether vectors are normalized.</summary>
            [ComposeParameter]
            public bool Normalize { get; set; } = false;

            /// <summary>Gets or sets the SmoothingSeconds value.</summary>
            [ComposeParameter]
            public float SmoothingSeconds { get; set; } = 0;

            /// <summary>Creates an immutable definition from this node.</summary>
            /// <returns>The immutable definition.</returns>
            public ActionDefinition Build() => new(Id, Kind, Sensitivity, Normalize, SmoothingSeconds);
        }

        /// <summary>Builds an ActionBinding.</summary>
        [Composable]
        public partial class Binding
        {
            /// <summary>Gets or sets the Id value.</summary>
            [ComposeParameter]
            public required string Id { get; set; }

            /// <summary>Gets or sets the ActionId value.</summary>
            [ComposeParameter]
            public required string ActionId { get; set; }

            /// <summary>Gets or sets the ContextId value.</summary>
            [ComposeParameter]
            public required string ContextId { get; set; }

            /// <summary>Gets or sets the Control value.</summary>
            [ComposeParameter]
            public required InputControl Control { get; set; }

            /// <summary>Gets or sets the Scale value.</summary>
            [ComposeParameter]
            public Vector2 Scale { get; set; } = Vector2.One;

            /// <summary>Gets or sets the PressThreshold value.</summary>
            [ComposeParameter]
            public float PressThreshold { get; set; } = 0.5f;

            /// <summary>Gets or sets the ReleaseThreshold value.</summary>
            [ComposeParameter]
            public float ReleaseThreshold { get; set; } = 0.4f;

            /// <summary>Creates an immutable definition from this node.</summary>
            /// <returns>The immutable definition.</returns>
            public ActionBinding Build() => new(Id, ActionId, ContextId, Control, Scale, PressThreshold, ReleaseThreshold);
        }

        /// <summary>Builds an InputContext.</summary>
        [Composable]
        public partial class Context
        {
            /// <summary>Gets or sets the Id value.</summary>
            [ComposeParameter]
            public required string Id { get; set; }

            /// <summary>Gets or sets the Priority value.</summary>
            [ComposeParameter]
            public int Priority { get; set; } = 0;

            /// <summary>Gets or sets a value indicating whether the context claims its controls.</summary>
            [ComposeParameter]
            public bool Exclusive { get; set; } = false;

            /// <summary>Creates an immutable definition from this node.</summary>
            /// <returns>The immutable definition.</returns>
            public InputContext Build() => new(Id, Priority, Exclusive);
        }

        /// <summary>Builds a RecognitionDefinition.</summary>
        [Composable]
        public partial class Recognition
        {
            /// <summary>Gets or sets the Id value.</summary>
            [ComposeParameter]
            public required string Id { get; set; }

            /// <summary>Gets or sets the ContextId value.</summary>
            [ComposeParameter]
            public required string ContextId { get; set; }

            /// <summary>Gets or sets the Kind value.</summary>
            [ComposeParameter]
            public required RecognitionKind Kind { get; set; }

            /// <summary>Gets or sets the Actions value.</summary>
            [ComposeParameter]
            public required ImmutableArray<string> Actions { get; set; }

            /// <summary>Gets or sets the Window value.</summary>
            [ComposeParameter]
            public required TimeSpan Window { get; set; }

            /// <summary>Gets or sets the TapCount value.</summary>
            [ComposeParameter]
            public int TapCount { get; set; } = 2;

            /// <summary>Creates an immutable definition from this node.</summary>
            /// <returns>The immutable definition.</returns>
            public RecognitionDefinition Build() => new(Id, ContextId, Kind, Actions, Window, TapCount);
        }

        /// <summary>Builds and validates an immutable action profile from named slots.</summary>
        [Composable]
        public partial class Profile
        {
            private Action[] _actions = [];
            private Binding[] _bindings = [];
            private Context[] _contexts = [];
            private Recognition[] _recognitions = [];

            /// <summary>Creates and validates an independent runtime snapshot.</summary>
            /// <returns>The validated immutable profile.</returns>
            public ActionProfile Build()
            {
                var profile = new ActionProfile(
                    _actions.Select(node => node.Build()).ToImmutableArray(),
                    _bindings.Select(node => node.Build()).ToImmutableArray(),
                    _contexts.Select(node => node.Build()).ToImmutableArray(),
                    _recognitions.Select(node => node.Build()).ToImmutableArray());
                profile.Validate();
                return profile;
            }

            [ComposeSlot]
            private static void Actions(Profile target, IReadOnlyList<Action> children) => target._actions = children.ToArray();

            [ComposeSlot]
            private static void Bindings(Profile target, IReadOnlyList<Binding> children) => target._bindings = children.ToArray();

            [ComposeSlot]
            private static void Contexts(Profile target, IReadOnlyList<Context> children) => target._contexts = children.ToArray();

            [ComposeSlot]
            private static void Recognitions(Profile target, IReadOnlyList<Recognition> children) => target._recognitions = children.ToArray();
        }
    }
}
