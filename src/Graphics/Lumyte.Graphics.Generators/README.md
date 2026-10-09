# Lumyte.Graphics.Generators

ShaderArguments属性とIShaderDataを実装するapplication structのmemberを直接読み出すcodecを生成します。生成codecはmodule initializerからGraphics.AbstractionsのShaderCodecへ登録し、managed reflectionやInternalsVisibleToを必要としません。

数値、Vector、Matrix4x4、IGpuRef、入れ子structを扱います。custom getter、配列、任意class、genericまたはnestedなcodec対象型はLUMG001で診断します。record structの自動propertyを扱います。shaderのlayoutはartifact reflectionからbackendが検証します。

project参照では次を追加します。

```xml
<ProjectReference Include="path/to/Lumyte.Graphics.Generators.csproj"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

生成codecの登録と呼び出しはGraphics.Abstractionsの`ShaderCodec<T>`を通します。数値snapshotとIGpuRefのidentityを保持し、GPU addressやbinding番号をapplicationへ公開しません。shader sourceは共通helperのlumyte.slangをincludeし、offlineはSlang.targets、onlineはSlangShaderCompilerで同じABI metadataをartifactへ格納します。
