using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Lumyte.Maps.Mvt;
using Lumyte.Resources;

namespace Lumyte.Maps.Http;

/// <summary>Finite explicit tile endpoints with complete nonsecret transport identity.</summary>
public sealed class HttpMvtRegistration
{
    private readonly Dictionary<ResourceId, Uri> _endpoints = [];

    /// <summary>Initializes a new instance of the <see cref="HttpMvtRegistration"/> class. Endpoints and identity contain no secrets.</summary>
    /// <param name="dataSetId">Dataset ID.</param>
    /// <param name="contentVersion">Application content version.</param>
    /// <param name="profile">Shared MVT decoder profile.</param>
    /// <param name="policy">Immutable transport policy.</param>
    /// <param name="endpoints">Explicit bounded endpoint set.</param>
    public HttpMvtRegistration(string dataSetId, string contentVersion, MapTileLoadProfile profile, HttpMvtPolicy policy, IReadOnlyDictionary<MapTileId, Uri> endpoints)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataSetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentVersion);
        if (dataSetId.Length > 256 || contentVersion.Length > 256 || endpoints.Values.Any(uri => uri.AbsoluteUri.Length > 2048))
        {
            throw new ArgumentException("Registration metadata exceeds capacity.", nameof(endpoints));
        }

        if (endpoints.Count > policy.MaxEntries || policy.MaxExpandedBytes > profile.MaxInputBytes || ((policy.MaxExpandedBytes + 31L) & ~7L) + 512 > profile.MaxReservationBytesPerTile)
        {
            throw new ArgumentException("Endpoint metadata or expanded input exceeds registered capacity.", nameof(endpoints));
        }

        Policy = policy;

        // Endpoints are representation identity too: equal datasets cannot alias different routes.
        string routes = string.Join("|", endpoints.OrderBy(p => p.Key.Normalize().Zoom).ThenBy(p => p.Key.Normalize().X).ThenBy(p => p.Key.Normalize().Y).Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.Key.Normalize().Zoom}/{p.Key.Normalize().X}/{p.Key.Normalize().Y}:{p.Value.AbsoluteUri.Length}:{p.Value.AbsoluteUri}")));
        string identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{contentVersion.Length}:{contentVersion}|{policy.Identity}|{routes}")));
        Mvt = new(dataSetId, identity, profile, endpoints.Keys);
        foreach (KeyValuePair<MapTileId, Uri> pair in endpoints)
        {
            policy.Validate(pair.Value);
            _endpoints.Add(GetKey(pair.Key).Address.Id, pair.Value);
        }
    }

    /// <summary>Gets the exact transport-neutral stage registration.</summary>
    public BoundedMvtRegistration Mvt { get; }

    /// <summary>Gets transport limits.</summary>
    public HttpMvtPolicy Policy { get; }

    /// <summary>Gets a normalized complete CPU key.</summary>
    /// <param name="tile">XYZ tile.</param>
    /// <returns>The resource key.</returns>
    public ResourceKey<MapTileContent> GetKey(MapTileId tile) => Mvt.GetKey(tile);

    internal Uri Resolve(ResourceId id) => _endpoints.TryGetValue(id, out Uri? uri) ? uri : throw new FileNotFoundException("Tile endpoint is not registered.");
}
