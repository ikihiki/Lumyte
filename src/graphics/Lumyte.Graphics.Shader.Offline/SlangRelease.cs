using System.Runtime.InteropServices;

namespace Lumyte.Graphics.Shader.Offline;

public sealed record SlangReleaseAsset(string ArchiveName, string Sha512)
{
    public Uri DownloadUri => new($"https://github.com/shader-slang/slang/releases/download/v{SlangRelease.Version}/{ArchiveName}");
}

public static class SlangRelease
{
    public const string Version = "2026.7.1";

    public static SlangReleaseAsset GetAsset(OSPlatform platform, Architecture architecture)
    {
        string key = $"{GetOperatingSystem(platform)}-{GetArchitecture(architecture)}";
        return key switch
        {
            "windows-x86_64" => Asset(key, "c1c0c016e955f0c67b6bad1b3e3074bebbd9d3395a2a53075cfb6695cce49e468afffeff8c3133dc84083f4504fc284a690fe630256c8d50a9319ae518e1304a"),
            "windows-aarch64" => Asset(key, "d094583d64dd33ea4cc089b530c2f559054ad2ba3727e0b5e7a693bafc918fc249beb3526f3d25ac0241b50e807a167d77d83bf5e2ff979094e08ae0a8463c7a"),
            "macos-x86_64" => Asset(key, "c5b502a8fa2cdb8118906827c3c67fd2f866644478d635a7ae682dda08099f6d9e6834109ef0d954b87a5769245dd4ea26bb692c053bcd860938a8d75223d0cc"),
            "macos-aarch64" => Asset(key, "de583e04e15b15b98afd817914514c93f96915ea30ebc03a7ece5e23b7f4bb266d43e4e878c7e4b33dc1bf0def0186d607334d6246e26f9ca6b0077909aacf88"),
            "linux-x86_64" => Asset(key, "580eb88d0e840d9428c8b84d2f67e9c11e0fe41d76c1b502d80d1c3a607e4b9b0b0c193f52ccb549d13f14aac70d7c4bb91af08de4352ec1a3674cfedf3e1542"),
            "linux-aarch64" => Asset(key, "dcad6cbbfff302d80c244f34d6e84dad60c084749637a21ae01ea4ca57181a524bf9229e4ba8e8d7038655e5fa7bc8aa548e7a0c2b65c4212c2f2e0dd257f641"),
            _ => throw new PlatformNotSupportedException($"Slang is not configured for {key}."),
        };
    }

    public static SlangReleaseAsset GetCurrentAsset() => GetAsset(
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? OSPlatform.Windows :
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? OSPlatform.Linux : OSPlatform.OSX,
        RuntimeInformation.ProcessArchitecture);

    private static SlangReleaseAsset Asset(string key, string hash) =>
        new($"slang-{Version}-{key}.zip", hash);

    private static string GetOperatingSystem(OSPlatform platform) =>
        platform == OSPlatform.Windows ? "windows" : platform == OSPlatform.Linux ? "linux" : platform == OSPlatform.OSX ? "macos" :
        throw new PlatformNotSupportedException($"Unsupported operating system {platform}.");

    private static string GetArchitecture(Architecture architecture) => architecture switch
    {
        Architecture.X64 => "x86_64",
        Architecture.Arm64 => "aarch64",
        _ => throw new PlatformNotSupportedException($"Unsupported architecture {architecture}."),
    };
}
