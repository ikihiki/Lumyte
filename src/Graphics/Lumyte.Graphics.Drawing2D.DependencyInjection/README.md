# Lumyte.Graphics.Drawing2D.DependencyInjection

Optional dependency injection registrations for Drawing2D command renderers.
`services.UseGraphics2D()` registers the eleven default command renderers and
`DrawingCommandRenderers2D` with scoped lifetimes. Repeated calls preserve existing registrations.
Add a replacement before building the provider; the last registration wins for single-service resolution.

```csharp
services.UseGraphics2D();
services.AddScoped<IFillPathRenderer2D, CustomFillPathRenderer>();
```

The application creates `Renderer2D` with the resolved command table and controls GPU submission and completion.
Effects are constructed directly through `ImageEffectCompiler` extension methods with their definitions and renderers.
This package registers no effect services. Dispose the scope after prepared drawing work and the renderer are released.
Registration does not create a provider, resolve services, or allocate GPU resources.
