# ADR 0002: Native Graphics API

## 状態

採用（目標設計）。現行実装の完了を示さず、旧 API との互換経路は設けない。

## 依存 ADR

[ADR 0001: Graphics の層構造と共通契約](0001-graphics-api.md) の座標規約、共通値、所有権と検証の境界に依存する。

## 決定

[NoGraphicsAPI の public API](https://github.com/sebbbi/NoGraphicsAPI/blob/main/include/NoGraphicsAPI/NoGraphicsAPI.hpp) を基礎に、DirectX 12 と Vulkan を直接使う Native 層を設ける。caller が allocation、descriptor slot、root data、barrier と completion を管理する。メモリ確保は線形 data と texture に共通の heap とし、その上に線形 region と texture を配置する。線形 data の使用範囲は region 内の非所有 range で渡し、用途別 buffer object は作らない。

`INativeGpuBackend` は一つの native device を表し、Native 専用の shader、Resources と機能 pass 実装がこの層を使う。Portable は独立した API 系統であり、この interface を実装・変換する adapter は設けない。共通 RenderGraph の利用者は Model、Blur、2D 等の機能 pass を追加し、Native の device や handle を参照しない。選択された provider の Native pass 本体が shader の準備、内部 graph と低層 command を構築する。Native の shader package、resource allocator と RenderGraph も、この低レベル層の依存にはしない。型別の割当、使用状態の追跡、application resource の自動延命は Native の上位ライブラリが担当する。

Native API は責務ごとの文書で定義する。各文書は同じ interface と型群の担当 member を説明し、別の backend interface を追加しない。

backend は外部 assembly から `INativeGpuBackend` を実装する。resource／compatibility の公開基底型を実装内の非公開型で継承し、native handle、所属 backend と局所的な破棄状態を保持する。引数が自分の実装型・device に属することは backend 内で確認する。共通契約の `Owner`／`BackendData`、登録済み backend 名の一覧、内部公開先の追加を必要としない。

## API

この文書は device と診断の member を定義する。

| API | 契約 |
| --- | --- |
| `INativeGpuBackend` | native device、主 queue と生成 API の入口。同じ device の object を組み合わせる。 |
| `NativeGpuBackendOptions.EnableValidation` | native debug/validation 機能の有効化要求。Native 独自の resource state 検証層は作らない。 |
| `Capabilities`／`NativeGpuCapabilities` | `RawShaderPointers`、`BufferDescriptors`、`ExplicitTextureTransitions`、`MeshShaders`、`AmplificationShaders` の対応を返す。 |
| `Limits`／`NativeGpuLimits` | `MaxRootDataSize`、heap の容量・整列条件、descriptor の native size/整列条件、texture と dispatch の有効上限を返す。異なる descriptor 型の byte size が同一とは仮定しない。 |
| `NativeGpuLimits.Descriptors`／`NativeGpuDescriptorLimits` | byte-based な descriptor ABI の場合だけ値を持つ。`ResourceSlotStride`／`SamplerSlotStride`、`ImageDescriptorSize`／`BufferDescriptorSize`／`SamplerDescriptorSize` とそれぞれの `Alignment` を ulong で表す。DirectX 12 の opaque handle increment を byte 数として偽装せず、同 backend は null を返す。 |
| `NativeGpuLimits.MeshShader`／`NativeGpuMeshShaderLimits` | mesh 未対応なら null。`NativeGpuDispatchLimits MeshDispatch`、nullable の `AmplificationDispatch`、uint の `MaxOutputVertices`、`MaxOutputPrimitives`、`MaxPayloadSize` を持つ。頂点・primitive の上限は単一 mesh workgroup 当たり。payload は amplification から mesh へ渡す byte 数の上限であり、amplification 未対応なら 0。 |
| `NativeGpuLimits.Dispatch`／`NativeGpuDispatchLimits` | `MaxGroupCountX/Y/Z` と `ulong MaxTotalGroupCount`。前者は各軸、後者は三軸の積の上限。shader 内の local thread 数とは区別する。 |
| `ShaderCodeFormat` | `Dxil` または `SpirV`。 |
| `Dispose()` | caller が未提出記録、提出済み利用と所有 object を解消した後に device を終了する。 |
| `NativeGpuException.NativeErrorCode` | native 呼出し失敗のコードを保持する。device loss は共通の `GpuDeviceLostException` として区別する。 |

`RawShaderPointers` は shader が実 GPU address を typed pointer として dereference できる能力であり、command が GPU address を受け取れることとは別である。未対応 target で descriptor index を address として返したり、address を内部 table index にすり替えたりしない。

mesh は Native の optional 機能とする。`MeshShaders` は mesh stage と直接／一件の間接 mesh dispatch が利用できること、`AmplificationShaders` はさらに amplification stage が利用できることを表す。後者が true なら前者も true とする。mesh のみの program は `MeshDispatch`、amplification を含む program の API dispatch は `AmplificationDispatch` の範囲を使い、その shader が生成する mesh workgroup は `MeshDispatch` の範囲に従う。local thread 数、shared memory、stage 間 payload と出力の組合せなど追加の native 条件は shader compiler と native 診断に委ね、全条件を再実装する limits validator は設けない。

未対応 device でも通常の vertex/indexed draw は使える。機能 pass が自身の vertex 経路を選ぶことと、低レベル mesh command を compute や indexed draw へ暗黙変換することは区別し、後者は行わない。Portable の要件に mesh／amplification を追加しない。

表現できない native 機能は対応済みと報告しない。capability は false とし、表現できない入力は明示的に失敗させる。入力値を捨てたり、別の値へ置き換えて成功扱いにしたりしない。

## コード配置

以下は repository root 相対の配置であり、未実装機能の目標配置を含む。公開契約とその xUnit テストは作成済みで、既存の backend とテスト project 内へ Native 実装を追加する。

| 配置先 | 内容 |
| --- | --- |
| `src/graphics/Lumyte.Graphics.Native/Device/` | `INativeGpuBackend`、options、capabilities、limits と Native 固有例外を置く。責務別 ADR が定義する同 interface の member 宣言もここに集約し、引数・返却値の型は各担当フォルダに置く。 |
| `src/graphics/Lumyte.Graphics.DirectX12/Device/`、`src/graphics/Lumyte.Graphics.Vulkan/Device/` | 既存 project を改編。device の生成・終了、機能と上限の取得、native 診断との接続を実装する。 |
| `src/graphics/Lumyte.Graphics.Native.Tests/Device/` | 共通の Native 契約について、外部 assembly からの実装と、CPU だけで確認できる所有権・エラーの振る舞いを検証する。 |
| `src/graphics/Lumyte.Graphics.DirectX12.Tests/Device/`、`src/graphics/Lumyte.Graphics.Vulkan.Tests/Device/` | 既存テスト project を改編。機能値と native error の写像など、GPU を使わない試験を置く。 |
| `src/graphics/Lumyte.Graphics.DirectX12.Tests/Integration/Device/`、`src/graphics/Lumyte.Graphics.Vulkan.Tests/Integration/Device/` | 実 device の初期化、validation 有効化と終了の試験を置き、通常の unit test と分離する。 |

## 使用例

`native` は初期化済みの `INativeGpuBackend` とする。device が提供する値を読む。

```csharp
var codeFormat = native.ShaderCodeFormat;
var maxRootBytes = native.Limits.MaxRootDataSize;
var rawShaderPointers = native.Capabilities.RawShaderPointers;
var canUseMesh = native.Capabilities.MeshShaders;
var canUseAmplification = native.Capabilities.AmplificationShaders;
```

これらの値は native の機能境界を表す。Native の shader artifact は、この device が実行できる code format と ABI を対象に用意する。

## 検証方針

host memory の安全、整数演算、backend が管理する identity と局所状態だけを確認する。usage、format、barrier、descriptor、shader の適合性は native validation/debug layer と native error に委ねる。必要 feature の選択は初期化時に行うが、native の条件を網羅する独自 validator は作らない。

同じ heap／region への host 操作は、配置・破棄も含めて caller が直列化する。backend の Dispose と他の操作も競合させない。GPU 同期に加えて native が要求する host の外部同期を caller が担い、backend 全体に暗黙の排他や lifetime tracker を置かない。

## 採用差分と未実装範囲

caller-owned resource と明示同期を採用する。参照実装の線形／texture heap の分割は採用せず、共通の純粋 allocation と配置 resource に分離する。任意 shader pointer など target 間で同じ意味を提供できない機能は部分採用とし、capability で区別する。

Native 専用 interface、options、capabilities、native error と両 backend の初期化・終了を実装した。共通 heap と線形 region／texture の配置・独立破棄、render view、descriptor storage と書込み、転送・提出・completion に加え、raw shader、compute pipeline、直接 root と直接／間接 dispatch を提供する。両 backend の `BufferDescriptors`、Vulkan の `RawShaderPointers`、DirectX 12 の `ExplicitTextureTransitions` を true とする。DirectX 12 の raw shader pointer は未対応。mesh／amplification の capability は device が提供する有効機能に応じて返す。

limits は部分実装で、現在は `MaxRootDataSize`、`Dispatch`、optional `Descriptors` と `MeshShader` を提供する。vertex／mesh raster pipeline、rendering と直接／一件の間接 draw・indexed draw・mesh dispatch を実装した。一般の heap／texture 上限、ray tracing、presentation は未実装である。実装と実機検証の範囲は [進捗記録](../designs/graphics-implementation-progress.md) に分けて記載する。
