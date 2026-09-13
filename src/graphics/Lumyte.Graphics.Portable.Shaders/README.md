# Lumyte.Graphics.Portable.Shaders

準備済みの WGSL package から、Portable backend の shader module と group layout を生成するライブラリです。参照先は `Lumyte.Graphics.Portable` で、Native の package／GPU ABI、Resources、RenderGraph、offline compiler には依存しません。

[独立した offline compiler](../../../tools/Lumyte.Graphics.Portable.Shaders.Offline/README.md) では、公式 Tint を使って WGSL から package factory、host C#、管理入力 XML を生成できます。runtime のロード API と build 時の処理は分離します。

`PortableShaderPackage` は WGSL text、entry point、group 順の layout、root／parameter の配置、binding の意味名と ABI hash を保持します。配列入力をコピーし、入れ子の metadata も不変です。ファイル取得、container の展開、Slang のコンパイル、WGSL reflection、C# 型生成はこの package の外側で行います。現時点の型配置は caller が準備した metadata であり、生成器を実装したものではありません。

## 使用例

`package` は CPU 資産側で展開した `PortableShaderPackage`、`backend` は作成済みの `IPortableGpuBackend` です。次は、直接 root を持つ compute program の初期化・実行例です。binding が必要な program では `program.BindingLayouts[group]` から binding を生成し、明示的に設定します。

```csharp
using Lumyte.Graphics.Portable;
using Lumyte.Graphics.Portable.Shaders;

var loader = new PortableShaderLoader(backend);
using var program = loader.Load(package, expectedAbiHash: "example-root-u32-v1");
var pipeline = backend.CreateComputePipeline(program.Description);
using var completion = backend.MainQueue.CreateSemaphore();
using var commands = backend.MainQueue.StartCommandRecording();
commands.BeginCompute();
commands.SetComputePipeline(pipeline);
uint root = 42; // package の root layout と一致する値。
commands.SetComputeRootData(in root);
commands.Dispatch(1);
commands.EndCompute();
backend.MainQueue.Submit([commands], completion, 1);
await backend.MainQueue.WaitAsync(completion, 1);
backend.DestroyComputePipeline(pipeline);
// program は pipeline と GPU の利用が終了してから破棄する。
```

この例に対応する最小の準備済み package は次のように構築できます。実際の出力を作る shader は明示した buffer／texture binding を使います。

```csharp
var package = new PortableShaderPackage(
    PortableShaderPackage.CurrentVersion,
    """
    requires immediate_address_space;
    struct Root { value: u32, }
    var<immediate> root: Root;
    @compute @workgroup_size(1) fn main() { let value = root.value; }
    """,
    [new(GpuShaderStage.Compute, "main")],
    PortableShaderFeatures.ImmediateAddressSpace,
    [],
    new PortableShaderDataLayout("Root", 4, 4,
        [new("value", "u32", 0, 4, 4)]),
    [],
    new PortableShaderBindingSchema([]),
    "example-root-u32-v1");
```

`Kind` は compute／vertex-only／vertex+pixel の package 構成から決まります。`Description` は作成時に固定した低レベルの構成値で、取得時に GPU 操作を行いません。`RootLayout`、`ParameterLayouts`、`BindingSchema` と `AbiHash` は元の不変 metadata を参照できます。ABI hash は build が与える opaque な識別文字列で、loader は任意の `expectedAbiHash` と ordinal 比較します。hash の生成や shader 内容との照合は行いません。

## 所有と診断

loader と program は backend を借用します。各 `Load` は専用の module と group layout を生成し、program が所有します。途中の同期失敗では、それまでに作成した object を逆順に解放します。解放処理の一つが失敗しても残りを試み、元の作成エラーと解放エラーを保持します。`Dispose` は各 object の解放を一度だけ試み、backend を破棄しません。

caller は binding、pipeline、未提出 recording と GPU 利用を終了してから program を破棄します。description／handle のコピーは所有を増やしません。program の破棄は GPU 待機、使用 object の探索、回収の延期を行いません。Browser では作成と破棄をその backend の JavaScript thread で行い、backend と借用 runtime を program より長く保持します。

`Load` の正常復帰は、WebGPU の非同期 shader 検証の成功を保証しません。module／layout の診断は backend が pipeline と提出へ引き継ぎます。提出後の `WaitAsync` が GPU 使用終了と処理結果を確定します。上の例は正常経路であり、失敗処理では使用終了を確認できた object だけを回収してください。

## 要求 feature と対象範囲

`PortableShaderFeatures` は現行の device 契約で確認できる `ImmediateAddressSpace` と `DualSourceBlend` に限定します。全 WGSL 拡張の capability 一覧ではありません。未知の flag は読み飛ばさず失敗し、新たな機能の事前選択が必要になった時に下位の公開契約と併せて拡張します。WGSL source の feature directive、型、使用資源、binding layout の GPU 合法性は runtime が検証します。

root の byte 数が 0 より大きい場合は、明示的な feature flag の有無にかかわらず直接入力を要求します。必要な capability と device の `MaxImmediateSize` を `Load` で確認します。`RootLayout = null` は root を持たない program です。root の解析、Parameter Data の生成・転送、uniform／storage buffer への fallback は行いません。

package version、stage 構成、任意の ABI 対応、binding schema と package 内 group の対応は、このライブラリ自身の metadata 契約として確認します。layout 内の native validation や WGSL parser を複製しません。group 番号は `GroupLayouts` 内の位置で表し、空の group もその位置を維持します。schema の意味名は package 内で一意とし、同じ group／binding の複数 alias は設けません。
