# Bounded local MVT input

M1 supports uncompressed local MVT 2, not OSM XML/PBF, HTTP, gzip, arbitrary vendor schemas, points, labels or styling. Schema `lumyte.roads-buildings-water.v1` permits only roads (LineString), buildings (Polygon) and water (Polygon). Unknown layer names and mismatched geometry types fail. Attributes retain their explicit protobuf scalar types; no implicit semantic conversion occurs. Layers may be absent. Extent defaults to the MVT default only when omitted; each supplied positive uint32 extent is preserved.

## Why a small parser

Existing solution inspection supplied for this implementation:

- bertt/mapbox-vector-tile-cs at ecefc028181e0197aca77b2d7ac2ea278f55cbbf, src/VectorTileParser.cs: public entry invokes Serializer.Deserialize<Tile>(stream) before application limits can be checked.
- NetTopologySuite.IO.VectorTiles develop at 16c930fc14f2ba912111684310d9da25370f759d, MapboxTileReader.Read: whole-tile protobuf deserialization likewise precedes application validation.

Neither inspected public entry accepts an allocator or per-construction allocation limits. Post-deserialization feature counting cannot enforce the ADR's pre-allocation requirement. This is the narrowly authorized gap filled here; it is not a claim that those projects are otherwise unsuitable. No dependency from either is copied or introduced. License/maintenance and Browser/AOT evaluation of those candidates is not complete; this parser's Browser/AOT compatibility is also untested.

## Registration

Construct one LocalMvtRegistration with immutable dataset/content identity, profile and explicit tile/file mapping. Pass that same instance to LocalMvtSource and MvtResourceStage; register the source and stage with an ordinary ResourceManager. GetKey normalizes XYZ and embeds the complete profile SHA256 in both ResourceId and the decoder/schema variant. A stage rejects a mismatched variant. The stage opens through ResourceBuildContext.OpenSourceAsync and carries the snapshot SHA256 revision into MapTileContent.SourceRevision.

## Accounting scope

The decoder scans spans without allocating message trees, counts repeated fields before allocating exact-sized arrays, and checks all charges before allocating arrays, strings, boxes and model objects. UTF8 validation/count precedes string creation. Exact-sized owned arrays become immutable arrays without copying or pools. Geometry command and tag arrays, layer key/value tables, strings and scalar boxes are charged; scratch charges are never refunded during a build, conservatively bounding cumulative allocations rather than relying on GC collection. RetainedBytes excludes scratch arrays but conservatively includes unused dictionary strings/values. PeakBuildBytes adds input bytes to cumulative decoder charges. A 256-byte fixed build allowance covers decoder bookkeeping and local snapshot/stream wrappers; the revision string is retained and charged separately.

Managed storage uses 64-bit CLR eight-byte alignment: 24-byte array header, 26-byte string base including terminator, aligned object sizes. These are an accounting convention for owned storage, not a process RSS, runtime allocator, OS file handle, asynchronous framework, crypto implementation, Resources metadata/cache or renderer transformation memory bound. Those surrounding scopes require independent manager/host budgets. Browser/AOT and alternate runtime layouts are not verified. Source uses a single exact-sized input array after checking file length and reservation; stage borrows that immutable snapshot buffer, not a second input copy. No pool or growable decoder collection exists.

Strict geometry validation checks command counts/order, minimum vertices, explicit ClosePath, nondegenerate winding, first-ring exterior, signed coordinate accumulation overflow, tags, UTF8, protobuf lengths/wire types and varint overflow. Ordered exterior/hole flags preserve multipolygon groups. It is not a general topology repair engine: self-intersection and geometric hole containment are not checked. Fixtures are hand authored in tests with no third-party OSM data.
