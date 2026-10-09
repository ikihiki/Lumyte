# Lumyte.Graphics.Abstractions

バックエンドが実装するグラフィックデバイスの共通契約です。外部の graphics binding には依存しません。

```csharp
using Lumyte.Graphics.Abstractions;

static DeviceCaps Inspect(IGraphicDevice device) => device.Caps;
```

`IGraphicDevice` の操作は `Caps` の取得のみです。デバイスを生成する factory、バックエンドの選択、解放、resource の生成、描画・送信の API は持ちません。生成と解放はアプリケーションの起動・終了部分で、選んだバックエンドの具象型を使って行います。

`DeviceCaps` は生成済みデバイスの利用可能な機能と上限の非所有 snapshot です。同じデバイスは同じ instance を返し、読み取りでは native query、allocation、GPU work を行いません。`with` で作ったコピーは元の snapshot を変更しません。

- サイズと alignment は byte 単位です。texture の dimension は texel 単位、その他の上限は個数です。
- 上限の `0` はその用途への対応がないことを表します。alignment は正数で、`1` は追加の alignment 制約がないことを表します。
- alignment は利用側が検証する値で、要素数やサイズを暗黙に補正するものではありません。image の format ごとの texel block 制約は別途必要です。
- `GraphicsFeatures` は共通の機能 flags です。対応する resource・command API の提供範囲とは区別します。
- 最大サイズは allocation の成功や空きメモリを保証しません。

設計判断は [GRAPHICS-0001](../../../docs/adr/graphics/GRAPHICS-0001-graphics-device.md)、生成手順は [Wgpu](../Lumyte.Graphics.Wgpu/README.md)、[Browser](../Lumyte.Graphics.Browser/README.md)、[Vulkan](../Lumyte.Graphics.Vulkan/README.md) を参照してください。
