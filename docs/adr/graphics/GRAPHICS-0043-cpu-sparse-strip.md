# ADR-GRAPHICS-0043: CPU の相関サンプルを横方向の描画区間へ変換する

- 状態: 採用
- 日付: 2026-11-18
- 基準コミット: `ab480b6480bf0cb233a60bd29f404e826ed1cf0f`

## 背景

描画方式を選ぶ利用者は、図形の内外判定だけを CPU へ移し、塗りと画像参照を GPU に残す方式を選べない。基準コードの `src/Graphics/Lumyte.Graphics.Drawing2D/Pipelines/DrawingCompose.Standard.cs` は既存の GPU 評価を宣言するだけである。

## 決定

図形ごとの 4×4 サンプルの内外判定を CPU で行い、全サンプルが内側の横区間と境界のビット列へ変換する。塗りと clip は同じサンプル位置で評価し、線形の乗算済み色を平均して元の描画順に SrcOver 合成する。

### 利用者の変更前・変更後

変更前は `DrawingCompose.StandardFillRect()` を既存の FillRect スロットに配置する。変更後は次の宣言を同じスロットへ配置できる。他の十種類にも個別の型付き宣言を用意し、既存宣言の意味は変えない。以下は配置以外の renderer 初期化と解放を省略している。

```csharp
using Lumyte.Graphics.Drawing2D.Pipelines;

var before = DrawingCompose.StandardFillRect();
var after = DrawingCompose.SparseFillRect(
    DrawingCompose.CpuSparse(8 * 1024 * 1024, 64, 65536, 262144));
```

### 契約と API 差分

構成は受動的であり、配置ごとの processor は独立する。同一 build 内で意味の等しい上限を持つ CPU キャッシュだけを共有する。キャッシュは図形だけを保存し、塗り、外部画像、現在のフレーム、引数計画を保持しない。

```diff
 namespace Lumyte.Graphics.Drawing2D.Pipelines;
 public static partial class DrawingCompose
 {
+    // 全上限を明示する。負のキャッシュ上限と正でない処理上限は拒否する。
+    public static SparseCpuOptions2D CpuSparse(long maxCachedBytes, int maxCachedAssets, int maxStrips, int maxBoundaryMasks);
+    // 他の命令種にも独立した型付き factory を提供する。
+    public static Definitions.SparseFillRect SparseFillRect(SparseCpuOptions2D options);
 }
```

各 Prepare は現在の型付き命令から横区間を構築する。画像と距離場は保守的な矩形を使い、元の記述を GPU で評価する。mesh は元の幾何と中心位置で補間した coverage を維持する。予算と不正な入力のエラーを別候補への切替に変換しない。

Record は active pass の外で upload と raster を記録する。連続する互換命令の組ごとに実行し、混在する他方式の描画順と背景の読み込み順を維持する。prepared owner が完了解放されるまで共有 service の寿命を保ち、直接描画、read lease、snapshot の既存 Prepare API と graph adapter を維持する。

## 検討した代替案

- 図形全体の coverage を先に平均する方式は小さいが、塗りと clip の相関を失うため採用しない。
- renderer 全体を命令種ごとに入れ子にする方式は資源 owner と保持機構を重複させるため採用しない。既存の型付き callback と共通資源 owner を使う。

## 結果と影響

利用者は命令種ごとに CPU の図形判定を選べる。キャッシュと区間数の明示的な上限が必要になり、複雑な入力は精度を下げずに予算エラーとして拒否される。高速化や全 scene が単一 pass になることは保証しない。

受入条件は、16 サンプルの解析的判定、塗りと clip の相関、全命令種の画素比較、直接描画と保持描画の完了解放、異なる build 間の非共有を確認することである。GPU compute はこの方式では発行しない。
