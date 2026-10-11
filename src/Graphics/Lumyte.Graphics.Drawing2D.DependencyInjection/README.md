# Lumyte.Graphics.Drawing2D.DependencyInjection

Optional dependency injection registrations for `Lumyte.Graphics.Drawing2D`.

`services.UseGraphics2D()` registers the eleven default command renderers and
`DrawingCommandRenderers2D` with scoped lifetimes. It also calls `UseImageEffects()`
to complete the shared effect registrations. Repeated calls preserve existing
registrations. Add a replacement interface registration before building the
provider; the last registration wins for single-service resolution.

```csharp
services.UseGraphics2D();
services.AddScoped<IFillPathRenderer2D, CustomFillPathRenderer>();
```

The application owns the device, creates `Renderer2D` with the resolved command
and effect registries, and controls GPU submission and completion. Dispose the
scope after all prepared work and the renderer have been released. Registration
does not create a provider, resolve services, or allocate GPU resources.
