# ADR 0009: Native Shader API

## 状態

採用（目標設計）。現行実装の完了を示さず、旧 API との互換経路は設けない。

## 依存 ADR

[ADR 0001: Graphics の層構造と共通契約](0001-graphics-api.md) の shader stage/code format、[ADR 0002: Native Graphics API](0002-native-graphics-api.md) の target と上限、[ADR 0007: Native Bindless API](0007-native-bindless-api.md) と [ADR 0008: Native Descriptor API](0008-native-descriptor-api.md) の参照 ABI に依存する。

## 決定

Native の低レベル shader API は raw DXIL または SPIR-V と entry point を入力にする。target code と native の heap/root ABI を一致させるのは caller と Native 専用 shader compiler の責務である。上位の Native shader loader は展開済みのメモリ上の package からこの値を作るが、この層は package のロード機構へ依存しない。ファイル取得と container 展開は `Lumyte.Resources` の分野とする。

## API

この文書は shader code と program の値を定義する。

| API | 契約 |
| --- | --- |
| `NativeGpuShaderCode` | init-only の `Stage`、`ReadOnlyMemory<byte> Code`、`EntryPoint`（既定値 main）を持つ record。code は device が受け取る raw DXIL または SPIR-V の byte 列。 |
| `NativeGpuShaderProgram(params shaders)` | vertex raster、mesh raster または compute の `NativeGpuShaderCode` をまとめる。構築時に一意な stage 構成を確認し、nullable の `Vertex`／`Pixel`／`Compute`／`Mesh`／`Amplification` property で各値を保持する。package と resource 集合を要求しない。 |

`NativeGpuShaderCode.Stage` は `GpuShaderStage.Vertex/Pixel/Compute/Mesh/Amplification`、`Code` は device が受け取る raw artifact、`EntryPoint` は entry の情報である。program は次のいずれかの stage 構成を表す。

| program の種類 | stage 構成 |
| --- | --- |
| vertex raster | `Vertex` 一つと optional `Pixel`。mesh／amplification は含めない。 |
| mesh raster | `Mesh` 一つ、optional `Amplification` と optional `Pixel`。vertex は含めない。 |
| compute | `Compute` 一つ。raster stage は含めない。 |

pixel を省く経路は depth-only 描画等、native が許可する用途に従う。mesh は `MeshShaders`、amplification はさらに `AmplificationShaders` を要求する。公開名 `Amplification` は DirectX 12 の amplification と Vulkan の task を表す。stage の重複等で program を一意に表せない入力は拒否するが、shader 内の入出力、payload、local size と native linkage の合法性を独自に解析しない。

program は入力配列への後の変更に依存しないが、code の byte 列を構築時に複製しない。caller は pipeline 生成呼出しが戻るまで code の内容を保持する。compute は生成時に native pipeline を完成させるため、その後 caller の byte 列を再利用できる。遅延生成する raster pipeline は backend が必要な code を自身へコピーする。

SPIR-V の `EntryPoint` は module 内の entry を native pipeline 作成時に選択する。DXIL は entry を既にコンパイルした artifact であり、この値は情報として保持する。DXIL 内の別 entry を選択する機能としては扱わず、entry 名を照合する shader reflection も追加しない。

root data は shader の直接引数であり、size は Native device の上限と使用する Native artifact の ABI に従う。root に含めた GPU pointer、descriptor index と値から、shader が Parameter Data と資源参照を算出する。command はその意味を解釈せず、Parameter Data の生成・upload・保持を行わない。

mesh raster でも amplification／mesh／pixel が同じ root を直接参照する。amplification から mesh への payload は shader が生成する stage 間データであり、command の root や CPU が渡す別の Parameter Data にはしない。mesh が出力する vertices／primitive indices は shader の出力であり、native index fetch 用の index buffer を program に要求しない。

row-major と共通座標規約に従う。native の root と descriptor heap に対応する shader ABI はこの低層 API の caller が用意し、descriptor index を raw pointer と読み替えない。Native と Portable の shader code、GPU root/input 構造体と loader は独立する。共通 RenderGraph から利用する場合、その caller は Native の機能 pass 本体である。アプリケーションは機能 pass の CPU 入力と論理 I/O を渡し、shader を選択・ロードしない。Native pass の作者が専用 shader と root を構築し、Portable の構造体を Native root の byte 列として再利用しない。この shader API は共通 RenderGraph の型へ依存しない。

## コード配置

パスは repository root 相対とし、未実装機能の目標配置を含む。`Lumyte.Graphics.Native` と隣の `.Tests` は作成済みで、DirectX 12／Vulkan と各 `.Tests` は既存 project 内へ実装を追加する。テストは xUnit を使う。

| 配置先 | 内容 |
| --- | --- |
| `src/graphics/Lumyte.Graphics.Native/Shaders/` | raw `NativeGpuShaderCode` と `NativeGpuShaderProgram` の公開入力型。package、compiler とファイル取得への依存を持たない。 |
| `src/graphics/Lumyte.Graphics.DirectX12/Shaders/` | raw DXIL／entry の native 表現と Native root ABI の内部対応。pipeline 所有の code 保持は `Pipelines/` と接続する。 |
| `src/graphics/Lumyte.Graphics.Vulkan/Shaders/` | raw SPIR-V／entry の native 表現と Native push-data ABI の内部対応。 |
| `src/graphics/Lumyte.Graphics.Native.Tests/Shaders/`、`src/graphics/Lumyte.Graphics.DirectX12.Tests/Shaders/`、`src/graphics/Lumyte.Graphics.Vulkan.Tests/Shaders/` | raw code と stage／entry の受渡しについて、CPU で確認できる契約試験。 |
| `src/graphics/Lumyte.Graphics.DirectX12.Tests/Integration/Shaders/`、`src/graphics/Lumyte.Graphics.Vulkan.Tests/Integration/Shaders/` | 準備済み artifact で pipeline を作成し、直接 root と対象 ABI、mesh-only／amplification 付き program を対応 GPU で確認する試験。 |

上位の package 入力型・artifact 選択は別 project の `Lumyte.Graphics.Native.Shaders` が担当する。この低層 project と backend が上位 shader project を参照する配置にはしない。

## 使用例

`computeCode` は device の `ShaderCodeFormat` に対応する raw byte 列とする。

```csharp
var shader = new NativeGpuShaderCode
{
    Stage = GpuShaderStage.Compute,
    Code = computeCode,
    EntryPoint = "main"
};
var program = new NativeGpuShaderProgram(shader);
```

この値は shader package の読込みや native pipeline の生成を行わない。

mesh entry も同じ raw code 型を使う。`meshCode` は device の mesh stage に対応する artifact とする。

```csharp
var mesh = new NativeGpuShaderCode
{
    Stage = GpuShaderStage.Mesh,
    Code = meshCode,
    EntryPoint = "meshMain"
};
```

## 検証方針

host byte 列と entry 情報を入力契約どおりに渡すことを確認する。shader の合法性、stage linkage と native ABI 条件は compiler、SPIR-V validator または native pipeline 作成・診断へ委ねる。

## 採用差分と未実装範囲

raw shader、直接 root と caller-owned ABI を採用する。Lumyte の program 値と raw DXIL の経路は target 間の差を表す補足である。raw code／program の値、stage 構成、両 backend の compute pipeline 入力と直接 root の実行を実装した。Native の shader code は既存 package の wrapper を経由しない。

vertex／pixel と mesh／optional amplification の raw program を同じ raster pipeline API に接続し、active stage 群への直接 root と描画を実装した。shader 内で amplification が生成する payload は root とは別の GPU 内通信であり、command が管理する parameter buffer にはしない。shader package と GPU が生成・選択する root はこの低レベル契約の範囲外とする。Native offline compiler と MSBuild による artifact・入力型生成を実装し、Output／2D／標準画像処理の機能 pass へ接続した。mesh package の適合などの残件は ADR 0015 に示す。
