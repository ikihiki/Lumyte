# Lumyte Vulkan triangle

Run the interactive sample:

```powershell
dotnet run --project Lumyte.Graphics.Vulkan.Triangle
```

Run a finite presentation check that closes automatically:

```powershell
dotnet run --project Lumyte.Graphics.Vulkan.Triangle -- --frames 120
```

The sample uses the Silk platform window only to create a Vulkan surface. Surface, swapchain, acquire/present binary
semaphores, resize, and out-of-date handling remain in `VulkanPresentation`; platform types do not enter
`Lumyte.Graphics`. GLSL is compiled reproducibly to SPIR-V by the pinned Shaderc package, serialized by the separate
`Lumyte.Shaders` writer, parsed as an untrusted MessagePack package by Graphics runtime, and selected as SPIR-V by the
Vulkan backend.
