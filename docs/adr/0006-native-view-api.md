# ADR 0006: Native View API

## 状態

採用（目標設計）。現行実装の完了を示さず、旧 API との互換経路は設けない。

## 依存 ADR

[ADR 0005: Native Texture API](0005-native-texture-api.md) の texture identity・format 再解釈と、[ADR 0001: Graphics の層構造と共通契約](0001-graphics-api.md) の format に依存する。

## 決定

texture の範囲を表す view 値と、attachment に必要な native render view の所有を分ける。view 値の構築だけでは native object を作らない。shader descriptor の書込みと attachment view の生成は独立する。

## API

生成・破棄操作は `INativeGpuBackend` の member とする。

| API | 契約 |
| --- | --- |
| `NativeGpuTextureViewDimension` | `OneD`、`TwoD`、`TwoDArray`、`ThreeD`、`Cube`、`CubeArray`。 |
| `NativeGpuTextureAspect` の利用 | [ADR 0005](0005-native-texture-api.md) の aspect 型を使う。view は `DepthStencil` による両 aspect の選択も許す。 |
| `NativeGpuTextureView` | texture、view dimension、format、aspect、base mip/count、base layer/count を持つ非所有の範囲値。 |
| `NativeGpuRenderViewFlags` | `None`、`DepthReadOnly`、`StencilReadOnly` の flags。attachment 用 native view の読み取り専用 aspect を指定する。 |
| `NativeGpuRenderViewHandle` | attachment に使う native view の caller-owned identity。public abstract 基底型と protected constructor により外部 backend が非公開派生型で実装し、生成時の `Flags` を保持する。 |
| `CreateRenderView(view, flags = None)`／`DestroyRenderView(view)` | read-only flags を含む attachment 用の native 表現を生成・解放する。shader descriptor の slot とは独立する。 |

`NativeGpuTextureView` は texture、dimension、format、aspect、base mip/count と base layer/count を指定する非所有値である。view dimension で array/cube の解釈を表す。

3D texture の `TwoD`／`TwoDArray` attachment view では base layer/count は mip 内の depth slice 範囲を表す。`ThreeD` view は mip の volume 全体を表し、base layer 0／count 1 を指定する。transition／discard は3D mipのslice部分だけに作用する native 表現を持たないため、3D texture には `ThreeD` view を渡す。slice の attachment view をそのまま使って対象を暗黙に mip 全体へ拡張しない。

`NativeGpuRenderViewHandle` は native attachment view を所有し、生成時の `Flags` を保持する。`DepthReadOnly` と `StencilReadOnly` はその aspect を読み取り専用として使用する指定である。view は親 texture や backing heap を延命しない。

caller は format の再解釈を texture の MutableFormat 契約に従って指定する。基本の depth/stencil aspect 解釈を、追加の color format 再解釈と混同しない。

## コード配置

パスは repository root 相対の目標配置とする。`Lumyte.Graphics.Native` と隣の `.Tests` は新設予定、DirectX 12／Vulkan と各 `.Tests` は既存 project の改編であり、テストは xUnit を使う。

| 配置先 | 内容 |
| --- | --- |
| `src/graphics/Lumyte.Graphics.Native/Views/` | 非所有 texture view、view dimension、render view flags と handle。aspect 型は同じ project の `Textures/` を参照する。生成・破棄 member は `Device/INativeGpuBackend.cs` に宣言する。 |
| `src/graphics/Lumyte.Graphics.DirectX12/Views/` | view 値から RTV／DSV を作る変換、read-only flags と caller-owned render view の解放。 |
| `src/graphics/Lumyte.Graphics.Vulkan/Views/` | attachment 用の永続 image view の生成・解放と read-only flags の保持。 |
| `src/graphics/Lumyte.Graphics.Native.Tests/Views/`、`src/graphics/Lumyte.Graphics.DirectX12.Tests/Views/`、`src/graphics/Lumyte.Graphics.Vulkan.Tests/Views/` | view 値だけでは native object を作らない境界、範囲・aspect・flags の受渡しを検証する unit test。 |
| `src/graphics/Lumyte.Graphics.DirectX12.Tests/Integration/Views/`、`src/graphics/Lumyte.Graphics.Vulkan.Tests/Integration/Views/` | attachment としての利用、read-only aspect と明示破棄を実 GPU で確認する試験。 |

shader descriptor の storage と書込みは各 project の `Descriptors/` が担当し、view 値の生成に結び付けない。

## 使用例

`view` は作成済み depth texture を指す `NativeGpuTextureView` とする。この例では GPU に使用させない。

```csharp
NativeGpuRenderViewHandle renderView = native.CreateRenderView(
    view, NativeGpuRenderViewFlags.DepthReadOnly);
native.DestroyRenderView(renderView);
```

render view の破棄は texture とその allocation を破棄しない。

## 検証方針

値から native view 作成情報への変換と owned object の解放を確認する。format/aspect/dimension/範囲の native 合法性は native 作成と validation へ委ねる。

## 採用差分と未実装範囲

非所有 view 値と明示的な render view を採用する。read-only flags は Lumyte の表現上の補足である。`NativeGpuTextureView` と view dimension は texture transition／discard の subresource 指定にも使用する。render view handle／flags と両 backend の生成・破棄を実装した。native attachment としての clear／描画、read-only aspect の内容保持を含む conformance 検証は、rendering command の実装段階に残る。
