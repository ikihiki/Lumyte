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
            private string? _builtIdentifier;

            /// <summary>Gets the identifier committed by the latest successful profile build.</summary>
            public string Identifier => _builtIdentifier ?? Id;

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

            internal void CommitIdentifier(string id) => _builtIdentifier = id;
        }

        /// <summary>Builds an ActionBinding.</summary>
        [Composable]
        public partial class Binding
        {
            private string? _builtIdentifier;

            /// <summary>Gets the identifier committed by the latest successful profile build.</summary>
            public string Identifier => _builtIdentifier ?? Id;

            /// <summary>Gets or sets the Id value.</summary>
            [ComposeParameter]
            public required string Id { get; set; }

            /// <summary>Gets or sets the ActionId value.</summary>
            [ComposeParameter]
            public required ActionReference ActionId { get; set; }

            /// <summary>Gets or sets the optional owning context definition.</summary>
            [ComposeParameter]
            public Context? Context { get; set; }

            /// <summary>Gets or sets the ContextId value.</summary>
            [ComposeParameter]
            public string? ContextId { get; set; }

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
            public ActionBinding Build() => Build(Context?.Identifier ?? ContextId ?? throw new InvalidOperationException("A standalone binding requires a context."));

            internal ActionBinding Build(string contextId)
            {
                if (ContextId is not null && ContextId != contextId)
                {
                    throw new ArgumentException("A nested binding must belong to its containing context.");
                }

                return new ActionBinding(Id, ActionId.Id, contextId, Control, Scale, PressThreshold, ReleaseThreshold);
            }

            internal void CommitIdentifier(string id) => _builtIdentifier = id;
        }

        /// <summary>Builds an InputContext.</summary>
        [Composable]
        public partial class Context
        {
            private Action[] _actions = [];
            private Binding[] _bindings = [];
            private Recognition[] _recognitions = [];
            private string? _builtIdentifier;
            private string? _builtParentId;

            /// <summary>Gets or sets child contexts whose parent is inferred from nesting.</summary>
            [ComposeContent]
            public IReadOnlyList<Context> Children { get; set; } = [];

            /// <summary>Gets or sets an optional parent definition for a flat composition.</summary>
            [ComposeParameter]
            public Context? Parent { get; set; }

            /// <summary>Gets the explicit identifier or the identifier assigned by a successful profile build.</summary>
            public string Identifier => _builtIdentifier ?? Id ?? throw new InvalidOperationException("Build the profile before using an anonymous context.");

            /// <summary>Gets or sets the optional parent context identifier.</summary>
            [ComposeParameter]
            public string? ParentId { get; set; }

            /// <summary>Gets or sets the Id value.</summary>
            [ComposeParameter]
            public string? Id { get; set; }

            /// <summary>Gets or sets the Priority value.</summary>
            [ComposeParameter]
            public int Priority { get; set; } = 0;

            /// <summary>Gets or sets a value indicating whether the context claims its controls.</summary>
            [ComposeParameter]
            public bool Exclusive { get; set; } = false;

            internal IReadOnlyList<Action> LocalActions => _actions;

            internal IReadOnlyList<Binding> LocalBindings => _bindings;

            internal IReadOnlyList<Recognition> LocalRecognitions => _recognitions;

            /// <summary>Creates an immutable definition from this node.</summary>
            /// <returns>The immutable definition.</returns>
            public InputContext Build() => Build(Identifier, _builtIdentifier is null ? Parent?.Identifier ?? ParentId : _builtParentId);

            internal InputContext Build(string id, string? parentId) => new(id, Priority, Exclusive, parentId, _actions.Select(node => node.Build()).ToImmutableArray());

            internal void CommitIdentifier(string id, string? parentId)
            {
                _builtIdentifier = id;
                _builtParentId = parentId;
            }

            [ComposeSlot]
            private static void Actions(Context target, IReadOnlyList<Action> children) => target._actions = children.ToArray();

            [ComposeSlot]
            private static void Bindings(Context target, IReadOnlyList<Binding> children) => target._bindings = children.ToArray();

            [ComposeSlot]
            private static void Recognitions(Context target, IReadOnlyList<Recognition> children) => target._recognitions = children.ToArray();
        }

        /// <summary>Builds a RecognitionDefinition.</summary>
        [Composable]
        public partial class Recognition
        {
            private string? _builtIdentifier;

            /// <summary>Gets the identifier committed by the latest successful profile build.</summary>
            public string Identifier => _builtIdentifier ?? Id;

            /// <summary>Gets or sets the Id value.</summary>
            [ComposeParameter]
            public required string Id { get; set; }

            /// <summary>Gets or sets the optional owning context definition.</summary>
            [ComposeParameter]
            public Context? Context { get; set; }

            /// <summary>Gets or sets the ContextId value.</summary>
            [ComposeParameter]
            public string? ContextId { get; set; }

            /// <summary>Gets or sets the Kind value.</summary>
            [ComposeParameter]
            public required RecognitionKind Kind { get; set; }

            /// <summary>Gets or sets the Actions value.</summary>
            [ComposeParameter]
            public required ImmutableArray<ActionReference> Actions { get; set; }

            /// <summary>Gets or sets the Window value.</summary>
            [ComposeParameter]
            public required TimeSpan Window { get; set; }

            /// <summary>Gets or sets the TapCount value.</summary>
            [ComposeParameter]
            public int TapCount { get; set; } = 2;

            /// <summary>Creates an immutable definition from this node.</summary>
            /// <returns>The immutable definition.</returns>
            public RecognitionDefinition Build() => Build(Context?.Identifier ?? ContextId ?? throw new InvalidOperationException("A standalone recognition requires a context."));

            internal RecognitionDefinition Build(string contextId)
            {
                if (ContextId is not null && ContextId != contextId)
                {
                    throw new ArgumentException("A nested recognition must belong to its containing context.");
                }

                return new RecognitionDefinition(Id, contextId, Kind, Actions.Select(action => action.Id).ToImmutableArray(), Window, TapCount);
            }

            internal void CommitIdentifier(string id) => _builtIdentifier = id;
        }

        /// <summary>Builds and validates an immutable action profile from named slots.</summary>
        [Composable]
        public partial class Profile
        {
            private Action[] _actions = [];
            private Binding[] _bindings = [];
            private Context[] _contexts = [];
            private Recognition[] _recognitions = [];

            /// <summary>Gets or sets the root contexts.</summary>
            [ComposeContent]
            public IReadOnlyList<Context> Children { get; set; } = [];

            /// <summary>Creates and validates an independent runtime snapshot.</summary>
            /// <returns>The validated immutable profile.</returns>
            public ActionProfile Build() => CompositionProfileBuilder.Build(_actions, _bindings, _contexts.Concat(Children), _recognitions);

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
