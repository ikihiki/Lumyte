using Lumyte.Setup.Smoke;

if (Native.Add(20, 22) != 42)
{
    throw new InvalidOperationException("Native P/Invoke result mismatch.");
}

if (Native.VerifyVulkan() != 0)
{
    throw new InvalidOperationException("Native Vulkan/zlib verification failed.");
}

if (OperatingSystem.IsWindows() && Native.VerifyDirectX() != 0)
{
    throw new InvalidOperationException("Direct3D 12 WARP verification failed.");
}

Console.WriteLine("PASS: Native NuGet restored; C# called C++; Vulkan readback and zlib roundtrip succeeded.");
