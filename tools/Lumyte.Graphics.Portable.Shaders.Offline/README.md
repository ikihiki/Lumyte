# Portable shader offline compiler

完結した WGSL を公式 Tint で処理し、同じ最終 module から Portable package、host C#、管理入力 XML を生成する。
WGSL source の独自 parser、生成 source の書換え、root の buffer fallback は設けない。

```csharp
var result = await PortableShaderCompiler.CompileAsync(
    new PortableShaderSource(wgsl, ["main"]),
    new PortableShaderCompileOptions(tintInfoPath, "Example.Generated", "Draw"));
```

`PortableShaderSource` は `Module`、任意の `EntryPoints` の名前列、`Language` を持つ。
現在対応する Language は Wgsl。Slang は明示的に NotSupported とし、root accessor と
`setLanguagePrelude` の本番経路を追加してから対応する。
options は `TintInfoPath`、`GeneratedNamespace`、`GeneratedName`、任意の `RootTypeName`、
`ParameterTypeNames`、`TintPath` を持つ。TintPath の既定は tint_info と同じディレクトリ。

公式 `tint_info --json` の entry／binding／size／alignment／offset と、標準 reflection 表示の型名を組み合わせる。
`tint` の最初の WGSL IR から `var<immediate>` の型を判別するため、root の指定は不要。
RootTypeName を指定する場合はその判別結果との一致を要求する。
ParameterTypeNames はホスト生成が必要な shader 構造体を選ぶ。offset や stride は手入力しない。
未知の出力形式では拒否し、root なしと推測して進めない。

基準となる公式 Dawn は [v20260911.162847](https://github.com/google/dawn/releases/tag/v20260911.162847)、
revision `80ee0043018a51532ea0fa2e77496cc66634157e`。
この配布の tint／tint_info には version 照会がないため、実行する2本の SHA-256 を ABI identity に含める。
別の revision の適合を認証する仕組みではなく、更新時には conformance を再実行する。
compiler の自動取得やインストール先の変更は行わない。

`PortableShaderBuildResult` の出力は次のとおり。

- `Package`: 元の WGSL と反映された metadata を持つ準備済み `PortableShaderPackage`。
- `GeneratedSources`: `PortableShaderGeneratedFile(FileName, Content)` の列。`<Name>Package.Create()`、host 構造体、vector 補助型を含む。
- `ResourceInputs`: group ごとの `.portable.resources.xml`。binding の名前は公式出力に変数名がないため `Group0Binding2` のように生成する。
- `ReflectionJson`／`ReflectionText`／`ReflectionIr`: 使用した公式 frontend の出力。実行時に再解析しない。
- `Diagnostics`: compiler の診断。

package と生成管理入力の `AbiHash` は同一。管理入力は Resources generator が `GpuBufferRef`／
`GpuViewRef`／`GpuSamplerRef` を受け取る型へ生成する。shader runtime から Resources への依存はない。
host 型は `<Name><ShaderTypeName>` とし、32 bit scalar／vector／入れ子構造体を明示配置する。
matrix／array の typed host 生成は公式出力に stride が不足するため未対応とし、要求時に診断する。

binding layout は entry の使用から作り、空 group を保持する。Tint が filtering 要求を特定しない sampler は
Filtering、filterability を特定しない float texture は Float を既定とする。これらは WebGPU の
layout 選択であり、binding resource の GPU 適合性検証は WebGPU に委ねる。

## MSBuild

offline tool の project を `ReferenceOutputAssembly="false"` で参照し、同梱 `.targets` を import する。
`PortableShaderCompile` に次の JSON ファイルを指定し、`LumytePortableTintInfo` または `TINT_INFO_PATH` を設定する。
source は JSON のディレクトリからの相対パス。

```json
{
  "source": "Draw.wgsl",
  "hostNamespace": "Example.Generated",
  "name": "Draw",
  "entryPoints": ["main"]
}
```

他に `language`、`rootTypeName`、`parameterTypeNames` を指定できる。
`LumytePortableShaderTool` と `LumytePortableShaderOutputDirectory` で tool DLL と出力先を変更できる。
出力は既定で `obj/<Configuration>/<TargetFramework>/Shaders/Portable/`。
各 request の絶対パスから決めた ID で分離し、生成 XML と C# は inventory 経由で CoreCompile に登録する。
毎 build で compiler を実行し、同値の生成ファイルは書き直さない。
失敗時には古い出力を使って build を続けず、全 compile 成功後に所有出力だけを更新する。

実行可能な参照例は [MSBuild consumer](../experiments/shader-build-inputs/README.md)。
隣接 xUnit project の `ShaderToolchainConformance` は `LUMYTE_TINT_INFO` で tool を指定する。
公式 tool がない環境では該当試験の具体的な skip 理由を出す。
生成 C#／Resources 入力のコンパイルと実行までを確認し、生成物による実 GPU 試験は後続とする。
