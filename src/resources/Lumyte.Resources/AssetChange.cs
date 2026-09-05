namespace Lumyte.Resources;

public readonly record struct AssetChange
{
    public AssetChange(string scheme, string address)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        Scheme = AssetKey.NormalizeScheme(scheme);
        Address = address;
    }

    public string Scheme { get; }

    public string Address { get; }
}
