# Lumyte.Graphics.Generators

IShaderArgumentsと、それを継承するIShaderDataを実装するapplicationのpartial structへシリアライズ実装を生成します。生成するShaderTypeNameとWriteは型自身の明示的interface実装であり、登録やmodule initializerを必要としません。Writeはgenericなref writerを受け取り、snapshot writer本体を値型としてboxingせず使用します。保持するsnapshotと数値payloadのメモリ確保は残ります。

数値、Vector、Matrix4x4、IGpuRef、入れ子structを扱います。custom getter、配列、任意class、genericまたはnestedな生成対象型はLUMG001で診断します。record structの自動propertyを扱います。partialでない生成対象型もLUMG001で診断します。shaderのlayoutはartifact reflectionからbackendが検証します。

project参照では次を追加します。

```xml
<ProjectReference Include="path/to/Lumyte.Graphics.Generators.csproj"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

生成したシリアライズ実装はGraphics.Abstractionsの`IShaderArguments.Capture<T>`から直接呼び出します。RootParameterは既定でargumentsとし、型の静的propertyで変更できます。managed reflectionやInternalsVisibleToは使いません。数値snapshotとIGpuRefのidentityを保持し、GPU addressやbinding番号をapplicationへ公開しません。shader sourceは共通helperのlumyte.slangをincludeし、offlineはSlang.targets、onlineはSlangShaderCompilerで同じABI metadataをartifactへ格納します。
