# Lumyte.Resources.Sample

Run from the repository root:

```sh
dotnet run --project samples/Lumyte.Resources.Sample
```

The console sample creates a temporary JSON compound asset with a mesh and a
material. It registers the file source and stages by type in DI and configures
four pipelines identified by their output types:

```text
scene.json -> SceneDocument -> MeshInfo     (mesh/0, geometry:v1)
                           -> MaterialInfo (material/0, preview:v1)
MeshInfo + MaterialInfo     -> SceneResource (typed child references)
```

Both transforms normalize their input address to the same document root, so
different element selectors and variants share one parse per published input
revision. Each compound type defines its own typed child references. The sample
SceneResource retains MeshInfo and MaterialInfo as concrete references through
Resident dependencies; there is no generic element metadata contract. Keys such as
`new ResourceKey<MeshInfo>("scene.json", "mesh/0", "geometry:v1")` select pipelines
through `typeof(MeshInfo).TypeHandle` and construct their source ID and address internally.
Each projection copies its data and uses a `Build` dependency, so
it does not retain the document's borrowed JSON elements.

Managed references keep the scene and its children resident. Their nonowning,
unmanaged handles can be copied into ECS components; resolution happens through
a managed read scope. The file source provides both stream reads and change notifications;
the manager starts its reload worker automatically. It changes the JSON file and waits
for automatic reload through its manager-owned watcher. Its existing mesh lease and
original read scope retain the old values, including the children of the old
SceneResource; the same handles resolve the new
values in a new read scope. Monitoring is stopped and joined before the resource
manager is closed.

Expected output includes:

```text
Document elements: mesh/0, material/0
Projection builds during discovery: 0
Initial mesh: 3 vertices; shared document builds: 1
Automatic reload: Published; publication 4; document builds: 2
Existing lease: 3 vertices; same handle after reload: 6 vertices
Original view: copper; current view: silver
Document buffer after release: 0 bytes
```

The document stage rents its encoded-byte buffer from a resident memory scope.
`JsonDocument` borrows that buffer and is disposed by the stage's release method;
the manager releases the scope afterward. JSON parser metadata, projection
objects and strings remain ordinary managed allocations and are not included in
this sample's memory charges. The file source additionally uses bounded scratch
memory. The file source reads inputs directly without storing historical copies;
detected changes fail reads and the reload coordinator retries within its configured limits.

The sample closes leases, read scopes and references before trimming and closing
the manager, then disposes its DI scope and deletes the temporary directory. It
demonstrates compound-document projection and hot reload, with no format-specific
model importer, GPU upload or audio codec.

リソースの取得は`Manager.AcquireAsync → ResourceReference`、値の取得は
`Reference.Acquire → ResourceLease`の順に行います。描画用Leaseを保持している間も
HotReloadは新世代を公開でき、既存Leaseは旧世代の値を保持します。
