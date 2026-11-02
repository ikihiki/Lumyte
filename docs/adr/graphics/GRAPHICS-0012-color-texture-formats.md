# ADR-GRAPHICS-0012: 非圧縮カラーテクスチャ形式の拡充

- 状態: 採用
- 日付: 2026-10-10
- 基準コミット: `5600df761a42abf0876e0acc7a26f94265ee34c8`

## 背景

描画コードの利用者は、単チャンネルのマスクにも4チャンネルのカラー形式を使う必要があり、使わない成分まで格納・転送している。また、明るさが0〜1を超えるHDR画像の中間描画先には、8bitの正規化整数では値域が足りない。用途に合うチャンネル数と精度を、共通APIで選べるようにする。

## 決定

単チャンネル・2チャンネルの形式と、16bit浮動小数点などの非圧縮カラー形式を追加する。利用者は必要な成分と値域に合わせて形式を選び、既存の生成・転送APIをそのまま使う。既存の列挙値は維持する。

### 利用者の変更前・変更後

同じ大きさのマスク画像を格納する例を示す。変更前は基準コミットの `src/Graphics/Lumyte.Graphics.Abstractions/TextureFormat.cs` にある `Rgba8Unorm` を使い、R成分にマスク値を入れる。生成とコピー条件の取得は、同ディレクトリの `IGraphicDevice.cs` にある `CreateTexture` と `GetTextureCopyLayout` を使う。

```csharp
TextureFormat format = TextureFormat.Rgba8Unorm;
using var mask = device.CreateTexture(new TextureDesc
{
    Width = width,
    Height = height,
    Format = format,
    Usage = TextureUsage.CopyDestination | TextureUsage.Sampled,
});
TextureCopyLayout copyLayout = device.GetTextureCopyLayout(format);
// 転送する画素列はRGBAの4 bytes/texelで用意する。
```

本ADRで追加する `R8Unorm` を使うと、R成分だけを格納できる。生成方法は同じで、形式と転送する画素列を変更する。

```csharp
TextureFormat format = TextureFormat.R8Unorm;
using var mask = device.CreateTexture(new TextureDesc
{
    Width = width,
    Height = height,
    Format = format,
    Usage = TextureUsage.CopyDestination | TextureUsage.Sampled,
});
TextureCopyLayout copyLayout = device.GetTextureCopyLayout(format);
// 転送する画素列はRだけの1 byte/texelで用意する。
```

両例は生成とコピー条件の取得だけを示し、deviceの生成、画素列の準備、転送、描画は省略している。実際の転送では `copyLayout` の行・オフセット制約を満たし、GPU使用が完了してからマスクを解放する。1画素の格納量は4 bytesから1 byteになるが、行のパディングやデバイスの実際の確保量まで4分の1になるとは限らない。

### 形式と転送の契約

コピーのサイズとオフセット制約は `GetTextureCopyLayout` から取得し、利用者が明示的に転送する。形式の追加はあらゆるデバイスでの使用可否を保証せず、使用方法が非対応なら生成時に拒否する。

格納形式の変更による数値変換はコピーで行わない。同じ形式間でバイトを保持する。深度・ステンシルの使用契約は変更しない。整数サンプリング、32bit浮動小数点、圧縮形式は今回の対象外とする。

### 公開API差分

基準コミットの `TextureFormat` に追加する列挙値を示す。既存の列挙値は省略する。

```diff
 namespace Lumyte.Graphics.Abstractions;
 public enum TextureFormat
 {
+    // Rの8bit正規化整数。1 byte/texel。
+    R8Unorm,
+    // RGの8bit正規化整数。2 bytes/texel。
+    Rg8Unorm,
+    // Rの16bit浮動小数点。2 bytes/texel。
+    R16Float,
+    // RGの16bit浮動小数点。4 bytes/texel。
+    Rg16Float,
+    // RGBAの16bit浮動小数点。8 bytes/texel。
+    Rgba16Float,
+    // RGB各10bitとalpha 2bitの正規化整数。4 bytes/texel。
+    Rgb10A2Unorm,
 }
```

## 検討した代替案

すべてRGBA8へ変換すれば格納形式を揃えられるが、HDRの値域を失い、単チャンネル用途でも不要な成分を保持する。
全形式を一度に追加すれば選択肢は増えるが、整数サンプリングや圧縮ブロックの契約まで変更が必要になるため、今回は非圧縮で補間サンプリングが可能なカラー形式を対象とする。

## 結果と影響

マスクやHDRに適した形式を選択できる。コピーの行サイズとネイティブの描画先形式を形式ごとに扱う必要がある。
共通APIで全カラー形式の生成とview、パディング付き複数レイヤー・mipの往復コピーを検証する。各実装で、指定した形式が維持され、転送した範囲のバイト列が一致することを確認する。
