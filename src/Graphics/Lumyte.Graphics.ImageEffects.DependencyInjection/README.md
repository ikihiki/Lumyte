# Lumyte.Graphics.ImageEffects.DependencyInjection

Optional dependency injection registrations for `Lumyte.Graphics.ImageEffects`.

`services.UseImageEffects()` registers the default color matrix, Gaussian blur,
opacity, and composite renderers and `ImageEffectRenderers` with scoped lifetimes.
Existing registrations survive repeated calls; a later interface registration
takes precedence when the registry is resolved.

Registration does not create or register a graphics device, an effect compiler,
or a service provider. The application supplies the device to
`ImageEffectCompiler`, retains prepared work until GPU completion, and disposes
the scope after that work has been released.

See [GRAPHICS-0016](../../../docs/adr/graphics/GRAPHICS-0016-image-effects.md) for the effect system design and API contracts.
