# Native shader offline compiler

Slang 2026.17 から DirectX 12 の DXIL と Vulkan の SPIR-V を生成し、その同じ compile の
reflection から root layout、Native Resources 入力 XML、C# host 型と package factory を生成する。
runtime device を作らず、XML の offset・size を手書きする必要はない。

```slang
import LumyteResources;
struct Arguments
{
    [LumyteResource("View")] uint output;
    uint value;
};
[[vk::push_constant]] ConstantBuffer<Arguments> root;
[shader("compute")][numthreads(1, 1, 1)]
void main()
{
    RWStructuredBuffer<uint> output = ResourceDescriptorHeap[root.output];
    output[0] = root.value;
}
```

`LumyteResources` は compiler が提供する Slang module。`GpuAddress` は `uint64_t`、`View` と
`Sampler` は `uint` field に指定する。属性のない整数は通常の値のまま扱う。文字列、変数名や
shader code の解析から GPU pointer/index の意味を推測しない。属性は `-reflection-json` に
含まれる公式の `userAttribs` を読み取る。

```csharp
var compiler = new NativeShaderCompiler(slangPath, downstreamCompilerDirectory);
var result = await compiler.BuildAsync(new NativeShaderBuildRequest(
    "Example.slang", [new("main", GpuShaderStage.Compute)],
    [new(NativeShaderTarget.DirectX12), new(NativeShaderTarget.Vulkan)],
    "Example.Generated"));
```

`BuildResult.Package` は展開済みの `NativeShaderPackage`。`PackageBytes` は形式識別子
`Lumyte.Native.Shader`、version、compiler version、artifact code と metadata を持つ JSON container。
container のロードとデシリアライズは Resource 層の担当である。
`HostSourceFiles` の `ShaderPackage.g.cs` は同じ package を作る `ShaderPackage.Create()` を持ち、
この factory の利用には runtime I/O や decoder が要らない。出力辞書のキーは平坦なファイル名。

host 型は `<HostNamespace>.DirectX12.Root`／`.Vulkan.Root` とする。同じ source でも
target の alignment により size・offset は変わり得る。`AbiHash` は compiler version、設定と
その target の reflection を識別する。Resources XML の `abiHash`、host 型の定数と artifact は
同じ値を持つ。root と parameter は `ByteSize` を公開する。空 root は `ByteSize = 0` であり、
CLR の空 struct 自体の size は 1 なので GPU へ渡す byte 数は `ByteSize` を使う。

`parameterTypes` に指定した shader type は、同じ source/import/target/define を使った独立した
`StructuredBuffer<T>` probe で反射する。probe は runtime artifact に含めない。これは storage
buffer の element layout の契約であり、constant buffer の packing と共用しない。GPU address
先の探索や自動 upload は行わない。型名の `::`、struct の階層、array index は生成名では `_` に
平坦化し、衝突は build error にする。

float2/3/4 は `System.Numerics.Vector2/3/4`、scalar は対応する C# 型、4 byte bool は uint。
matrix とその他の vector は反射された正確な byte size の `InlineArray<byte>` storage を生成する。
Slang JSON にない matrix row stride を推定しないため、行列の要素 serializer は生成しない。
row-major を compile 設定とする。resource 配列は反射された stride に沿って各要素を生成する。

Vulkan は `-spirv-unified-descriptor-heap-stride` を使用する。固定 stride artifact の生成は
未対応として明示的に拒否する。DirectX 12 root は b0/space0 に一致させる。root を buffer に
置換する経路はない。DXC のディレクトリは子 process の PATH に追加し、compiler installation を
書き換えない。cancel 時は Slang process tree を終了してから一時ファイルを回収する。

テストは隣接する `Lumyte.Graphics.Native.Shaders.Offline.Tests`。`SlangConformance` は実 compiler
を使用し、`LUMYTE_SLANGC` と `LUMYTE_DXC_DIRECTORY` で場所を指定できる。reflection の型・offset、
生成 C# の compile/実行と package factory を検証する。GPU での shader 実行は backend conformance
側の責務とする。

## MSBuild

tool project を `ReferenceOutputAssembly="false"` で参照して `.targets` を import し、
`NativeShaderCompile` に build 設定 JSON を指定する。compiler は `LumyteNativeSlangCompiler`
または `SLANGC_PATH`、DXC の探索先は `LumyteNativeDownstreamCompilerDirectory` で指定する。
compiler の自動取得は行わず、独立配布には Slang と DXC を別途用意する。

```json
{
  "source": "Example.slang",
  "hostNamespace": "Example.Generated",
  "entryPoints": [{"name": "main", "stage": "Compute"}],
  "targets": [{"target": "DirectX12"}, {"target": "Vulkan"}]
}
```

source と includeDirectories は JSON のディレクトリからの相対パス。`rootParameterName`、
`parameterTypes`、`includeDirectories`、`defines` を追加できる。target に `profile`、
`requiredCapabilities`、`descriptorHeapAbi` を指定できる。
`LumyteNativeShaderTool`／`LumyteNativeShaderOutputDirectory` で DLL と出力先を変更できる。
既定は `obj/<Configuration>/<TargetFramework>/Shaders/Native/`。
request パスの ID で同名ファイルを分離し、inventory に列挙した C#／XML だけを consumer に渡す。
全 compile 成功後に所有ファイルを更新・削除し、同値出力は書き直さない。
現在は毎 build で compiler を起動し、import の変更も反映する。compile cache は後続である。

実際の project reference と import の例は [MSBuild consumer](../experiments/shader-build-inputs/README.md) に置く。

参考: [Slang user-defined attributes](https://docs.shader-slang.org/en/stable/external/slang/docs/user-guide/03-convenience-features.html#user-defined-attributes-experimental)、
[Slang reflection](https://docs.shader-slang.org/en/stable/external/slang/docs/user-guide/09-reflection.html#json-reflection-output)。
