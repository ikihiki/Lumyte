# Lumyte.Graphics.Portable.RenderGraph.Generators

準備済みの Portable shader binding schema から、内部 graph の logical resource を受け取る `IPortablePassBindingInputs` を生成します。shader のコンパイル、ファイル取得、GPU 確保や使用宣言は行いません。生成型は Portable pass 作者のためのもので、共通機能 pass の利用者へ公開する shader ABI ではありません。

## 導入

```xml
<ProjectReference Include="../Lumyte.Graphics.Portable.RenderGraph.Generators/Lumyte.Graphics.Portable.RenderGraph.Generators.csproj"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
<PortablePassBindingInput Include="Shaders/Draw.portable.resources.xml" />
<Import Project="../Lumyte.Graphics.Portable.RenderGraph.Generators/buildTransitive/Lumyte.Graphics.Portable.RenderGraph.Generators.targets" />
```

NuGet では analyzer と buildTransitive targets を package に含めます。実行時には `Lumyte.Graphics.Portable.RenderGraph` を参照します。

Portable offline compiler の `.portable.resources.xml` を内容の変更なしで使えます。上記 targets と offline targets を同じ project に導入すると、compiler が出力した group schema を自動的に pass 入力へ接続します。明示選択する場合は `LumytePortableGeneratePassBindings=false` とし、`PortablePassBindingInput` に必要な schema を登録します。

targets は AdditionalFiles に用途の metadata を付けます。ResourceManager 用 generator も同時に導入した場合、この metadata が付いた schema は pass generator だけが処理します。手動で AdditionalFiles を使う場合は `LumytePortableRenderGraphInput=true` を指定するか、`.portable.pass.resources.xml` の名前を使います。schema の namespace/name は生成する型の名前で、同じ shader から異なる入力型を作る場合は別の名前を指定します。

## 生成 API

```xml
<input namespace="Example" name="DrawPassInputs" group="0" abiHash="prepared-abi">
  <field name="Vertices" kind="Buffer" binding="0" />
  <field name="Image" kind="Texture" binding="1" />
  <field name="Filtering" kind="Sampler" binding="2" />
</input>
```

`readonly struct DrawPassInputs` は constructor で `PortablePassBuffer`、`PortablePassView`、`GpuSamplerRef` を受け取り、get-only property を公開します。`StorageTexture` も PortablePassView を使います。Buffer の `VerticesOffset`/`VerticesLength` は init property で、既定値は 0/`ulong.MaxValue`（残り全体）です。`Group` は group 番号、任意の `AbiHash` は用意された shader ABI の識別子です。

```csharp
var inputs = new Example.DrawPassInputs(vertices, imageView, filtering)
{
    VerticesOffset = 256,
    VerticesLength = 1024,
};
var bindings = context.CreateBindings("draw", program, Example.DrawPassInputs.Group, inputs);
// AddPass で実際に使う resource の Read/Write を別途宣言する。
// record context の GetBindings が準備済みの binding を返す。
```

入力 struct は GPU の所有を取得しません。生成された `Write(PortablePassBindingWriter)` は logical resource と range を渡し、provider が実体確定後に管理された binding を準備します。Sampler は pass 作者の scope/pin から渡し、その保持は writer と実行 batch へ接続します。root data や scalar 値を buffer へ移す処理はありません。

名前・重複 binding・XML の構造エラーは `LPPG001` です。GPU の format、layout、usage の合法性は native WebGPU 検証へ委ねます。

## 検証

隣接 Tests は analyzer と MSBuild targets から生成した consumer を実行し、実際の provider と in-memory backend で buffer range、texture、sampler の binding と所有を確認します。生成ソース全体の snapshot は使いません。
