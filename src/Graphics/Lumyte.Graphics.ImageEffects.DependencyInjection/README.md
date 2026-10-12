# Lumyte.Graphics.ImageEffects.DependencyInjection

Optional registrations for the four provided `IImageEffectRenderer<TDefinition>` implementations.
`services.UseImageEffects()` uses scoped lifetimes and preserves existing registrations on repeated calls.
A later typed renderer registration takes precedence when the renderer is resolved.

Resolve the renderer before constructing an effect graph, then pass it to `ImageEffectGraph.Create`
or one of the convenience factories. The graph retains that renderer without capability selection,
type lookup, or DI resolution during preparation or recording. User-defined effects follow the same API.

No fixed renderer table is registered. Registration does not create or register a graphics device,
effect compiler, or service provider. The application supplies the device to `ImageEffectCompiler`,
retains prepared work until GPU completion, and disposes the scope after that work has been released.

See [GRAPHICS-0016](../../../docs/adr/graphics/GRAPHICS-0016-image-effects.md) for the design and API contracts.
