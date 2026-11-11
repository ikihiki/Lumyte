# Lumyte.Graphics.Generators

IShaderArgumentsと、それを継承するIShaderDataを実装するapplicationのpartial structへシリアライズ実装を生成します。生成するShaderTypeNameとWriteは型自身の明示的interface実装であり、登録やmodule initializerを必要としません。Writeはgenericなref writerを受け取り、snapshot writer本体を値型としてboxingせず使用します。保持するsnapshotと数値payloadのメモリ確保は残ります。

数値、Vector、Matrix4x4、IGpuRef、入れ子structを扱います。custom getter、配列、任意class、genericまたはnestedな生成対象型はLUMG001で診断します。record structの自動propertyを扱います。partialでない生成対象型もLUMG001で診断します。shaderのlayoutはartifact reflectionからbackendが検証します。

IShaderDataのWriteを生成する場合は、値を読まない不変のIShaderData.StorageSchemaとpublicなStorageMembersも生成します。数値はStorageMembers.Color、参照はStorageMembers.Texture、入れ子はStorageMembers.Nested.Colorのような型付きget-only tokenで指定します。schemaは完全なmember pathと数値型またはIGpuRefの型引数、参照の区別を保持し、参照先の型は展開しません。StorageMembers、Storage、CreateStorageは生成対象型の予約名です。既存memberや型名との衝突、およびtoken containerと同名の子memberはLUMG001です。view型名・private field名は衝突時に決定的なunderscore suffixで回避します。Writeを手書きするcodecにはmetadataやtokenを生成せず、既存の論理値uploadを維持しますが、保持経路は既定で未対応です。

生成する `T.CreateStorage(IGraphicsShaderDataBuffer<T>, ReadOnlySpan<T>)` はbackendのschema検証・初期変換を経て、public sealedな `T.Storage` を返します。直接 `new T.Storage(ShaderDataStorage<T>)` でwrapすることもできます。StorageはCount、Data、全体置換用Setと`ShaderDataStorage<T>`への暗黙変換を持ちます。null引数はArgumentNullException、負のstorage countと範囲外indexはArgumentOutOfRangeExceptionです。

`Element[]` はwrap時に一度だけ確保し、indexerはcached readonly struct viewへの `ref readonly` を返します。入れ子inline値もcached readonly child viewで、UnscopedRef付きref readonly propertyから参照します。数値とIGpuRefのget/setは既存のtyped tokenを使って共通storageのGetValue/SetValue/GetReference/SetReferenceへ委譲します。IGpuRefの参照先は展開せず、plain Tやtransfer bytesへのwritable refも公開しません。viewアクセス自体はboxing・unsafe・アクセス毎のheap allocationを必要としません。backend側のleaf更新もallocation-freeであることが前提です。default viewのget/setは、入れ子viewを取得する段階も含めInvalidOperationExceptionです。viewのcopyは同じstorageとindexへのwrite-throughを保ちます。

`BindingMatrixData` にTransform、Child.Value、Nodeがある場合:

```csharp
BindingMatrixData.Storage values = BindingMatrixData.CreateStorage(staging, initial);
values[0].Transform = matrix;
values[0].Child.Value = vec3;
var cached = values[0];
cached.Node = nodeRef;
staging.CopyFrom(values);
```

project参照では次を追加します。

```xml
<ProjectReference Include="path/to/Lumyte.Graphics.Generators.csproj"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

生成したシリアライズ実装はGraphics.Abstractionsの`IShaderArguments.Capture<T>`から直接呼び出します。RootParameterは既定でargumentsとし、型の静的propertyで変更できます。managed reflectionやInternalsVisibleToは使いません。数値snapshotとIGpuRefのidentityを保持し、GPU addressやbinding番号をapplicationへ公開しません。shader sourceは共通helperのlumyte.slangをincludeし、offlineはSlang.targets、onlineはSlangShaderCompilerで同じABI metadataをartifactへ格納します。
