using Lumyte.Composition;

namespace Lumyte.UI;

/// <summary>Provides generated static factories for passive UI declarations.</summary>
public static partial class Compose
{
    /// <summary>Contains widget definitions and generated construction members.</summary>
    public static partial class Definitions
    {
        /// <summary>A passive, replaceable configuration with construction-time child ownership.</summary>
        public abstract partial class Widget : ICompositionRebuildable
        {
            private Func<object>? _compositionFactory;
            private List<Action<Widget>>? _templateSlotRecipes;

            internal Func<object>? CompositionFactory
            {
                get
                {
                    Func<object>? factory = _compositionFactory;
                    if (factory is null || _templateSlotRecipes is not { Count: > 0 } recipes)
                    {
                        return factory;
                    }

                    Action<Widget>[] replay = recipes.ToArray();
                    return () =>
                    {
                        Widget next = factory() as Widget ?? throw new InvalidOperationException("A widget recipe returned another target type.");
                        foreach (Action<Widget> apply in replay)
                        {
                            apply(next);
                        }

                        (next._templateSlotRecipes ??= []).AddRange(replay);
                        return next;
                    };
                }
            }

            internal bool RebuildsChildTree => RebuildsChildren;

            /// <summary>Gets a value indicating whether rebuilding this declaration constructs its child tree anew.</summary>
            protected virtual bool RebuildsChildren => false;

            /// <inheritdoc/>
            void ICompositionRebuildable.SetCompositionFactory(Func<object> factory)
            {
                ArgumentNullException.ThrowIfNull(factory);
                if (_compositionFactory is not null)
                {
                    throw new InvalidOperationException("A declaration recipe can be assigned once during construction.");
                }

                _compositionFactory = factory;
            }

            /// <summary>Applies non-child Slot state and preserves it across generated declaration reconstruction.</summary>
            /// <typeparam name="TDeclaration">The receiving declaration type.</typeparam>
            /// <typeparam name="TState">The non-null state stored by the Slot.</typeparam>
            /// <param name="value">The state to assign without cloning.</param>
            /// <param name="assign">Assigns state to the supplied declaration without capturing the original owner.</param>
            protected void RegisterSlotState<TDeclaration, TState>(TState value, Action<TDeclaration, TState> assign)
                where TDeclaration : Widget
            {
                ArgumentNullException.ThrowIfNull(value);
                ArgumentNullException.ThrowIfNull(assign);
                assign((TDeclaration)this, value);
                if (_compositionFactory is not null)
                {
                    // Factory-time with actions already replay themselves. Only later Slot assignments need a recipe.
                    (_templateSlotRecipes ??= []).Add(next => assign((TDeclaration)next, value));
                    DeclarationRecipe = null;
                }
            }

            /// <summary>Applies an unevaluated template setting and preserves it across generated declaration reconstruction.</summary>
            /// <typeparam name="TDeclaration">The receiving declaration type.</typeparam>
            /// <typeparam name="TTemplate">The template delegate type.</typeparam>
            /// <param name="template">The non-null template, retained without evaluation.</param>
            /// <param name="assign">Assigns the template to the supplied declaration. It must not capture the original declaration.</param>
            protected void RegisterTemplateSlot<TDeclaration, TTemplate>(TTemplate template, Action<TDeclaration, TTemplate> assign)
                where TDeclaration : Widget
                where TTemplate : Delegate => RegisterSlotState(template, assign);
        }
    }
}
