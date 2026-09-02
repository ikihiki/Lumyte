# Lumyte Vulkan Samples

This executable demonstrates the regular Vulkan window, surface, swapchain, and present path:

- Clear only
- Vertex-color rainbow triangle
- Generated texture quad
- Render graph lit cube with a moving point light and a soft floor shadow

Press **Enter** to advance to the next sample and **Esc** to close the window. The title bar always shows the active sample and controls. The window can be resized at any time.

Run the interactive samples:

```powershell
dotnet run --project Lumyte.Graphics.Vulkan.Samples
```

Run a specific sample for a finite presentation check:

```powershell
dotnet run --project Lumyte.Graphics.Vulkan.Samples -- --sample lighting --frames 120
```

Accepted sample names are `clear`, `triangle`, `texture`, and `lighting`.

The app uses the Silk platform window only to create a Vulkan surface. Surface, swapchain, acquire/present binary
semaphores, resize, and out-of-date handling remain in `VulkanPresentation`; platform types do not enter
`Lumyte.Graphics`. GLSL is compiled reproducibly to SPIR-V by the pinned Shaderc package, serialized by the separate
`Lumyte.Shaders` writer, parsed as an untrusted MessagePack package by Graphics runtime, and selected as SPIR-V by the
Vulkan backend.
