# ADR-GRAPHICS-0039: 静的 SVG を既存の二次元描画へ変換する

- 状態: 採用
- 日付: 2026-11-17
- 基準コミット: `97accb2ca833a173b88855822b2cd63b8c973c96`

## 背景

アイコンを描画する利用者は SVG を直接読み込めず、図形を手作業で再構築する必要がある。基準コードの src/Graphics/Lumyte.Graphics.Drawing2D/Model/DrawingCanvas2D.cs の FillPath、StrokePath と Replay は再利用できるが、SVG の構文解釈は提供していない。

## 決定

固定した usvg 0.45.1 で SVG を正規化し、表現可能な静的な単色図形だけを既存の二次元コマンドへ変換する。XML、CSS、パス構文を独自実装せず、描画エンジンの拡張や画像化による代用も行わない。正規化後に未対応の機能が残る場合は文書全体を拒否する。

### 利用者の変更前・変更後

変更前は SVG の読み込みができないため、同じ図形を PathBuilder2D と FillPath で記録する。初期化済みの描画先への送信は両例から省略する。

```csharp
using Lumyte.Graphics.Drawing2D;
using System.Numerics;

var path = new PathBuilder2D();
path.MoveTo(new Vector2(1, 1));
path.LineTo(new Vector2(9, 1));
path.LineTo(new Vector2(9, 9));
path.Close();
var canvas = new Canvas2D();
canvas.Begin();
canvas.FillPath(path.Build(), new Lumyte.Mathematics.Colors.SRgba<float>(1, 0, 0, 1));
DrawList2D drawing = canvas.End();
```

変更後は以下の API を追加する。入力バイト列の取得は利用者が明示的に行い、解析自体には外部リソースへのアクセスを許可しない。

```csharp
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Svg;

SvgDocument2D svg = SvgDocument2D.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='10' height='10'><path fill='red' d='M1 1H9V9Z'/></svg>"u8);
var canvas = new Canvas2D();
canvas.Begin();
svg.Draw(canvas);
DrawList2D drawing = canvas.End();
```

### 対応範囲と誤差

基本図形、相対命令と円弧を含むパス、内部 use、usvg が解決する CSS と継承、viewBox、アフィン変換を扱う。描画対象は単色の塗りと線、NonZero と EvenOdd、塗りと線の順序、塗りと線それぞれの不透明度である。線は実線、butt・round・square の端点、miter・round・bevel の接合に限定する。

ルートの表示領域で切り抜き、既存の DrawingCanvas2D の変換とクリップの内側で再生する。呼び出し元の状態は変更しない。96 DPI でサイズを解決し、サイズ変更は呼び出し側の変換で行う。単精度の幾何と既存の曲線分割・アンチエイリアスを使うため、別の SVG 描画系との画素一致は保証しない。

### 変換側で拒否する機能

正規化された木のグループ不透明度、任意の clipPath、アルファ・輝度マスク、フィルター、非通常ブレンド、明示的な分離、線の破線・オフセット・miter-clip、shape-rendering の crispEdges・optimizeSpeed を拒否する。入力の none 以外の vector-effect も拒否する。既存の RenderGraph のグループ不透明度や一部の画像効果は存在するが、初期 API は純粋な DrawingCanvas2D と共通の記録だけを返す。PaintGraph2D の合成は描画先をサンプルしないため、レイヤーブレンドの代用にはしない。

線形・放射グラデーションとベクトルパターンも拒否する。既存のグラデーションは線形光で評価される一方、SVG の既定は sRGB であり、色空間を黙って置き換えない。パターンのためのタイル描画も追加しない。画像、文字、foreignObject、script、animate、animateMotion、animateTransform、set は入力 XML で検出して拒否する。文字の flattened 表現や画像のデコードは初期範囲に含めず、システムフォントも読み込まない。

### 上流の解析で省略される機能

usvg 自体は完全なブラウザーではない。最小限の CSS だけを解決し、イベント、リンク動作、カーソル、動的更新、未知の要素・属性、無効な図形・参照、再帰参照などを無視または削除する。この段階で消えた機能を正規化された木の検査だけで全て検出することは保証しない。明示的に拒否する入力要素と属性は前節に限定する。上流が受理した全ての SVG に忠実であるという契約ではなく、静的な正規化結果に対する厳格な変換である。

### 所有権と配布

新しい Lumyte.Graphics.Svg アセンブリは二次元描画に依存するが、既存アセンブリへ SVG 依存を追加しない。解析中だけ入力を借用し、ネイティブ結果を finally で解放してから完全に管理側の不変スナップショットを返す。保持する GPU リソースや IDisposable の責務はない。解析結果は再利用できるが、描画先 canvas の同時操作は呼び出し側で直列化する。

ネイティブの cdylib と staticlib は専用 crate に分離し、Cargo.lock と完全一致の usvg バージョンで依存を固定する。既存の Parley と同じ Fallout の RustToolchain と MSBuild の呼び出し規約を利用し、現在の SDK ホスト向けにビルドする。Windows の DLL、macOS の dylib、Linux の so と依存ライセンス・Rust 標準ライブラリ通知を出力および publish にコピーする。初期段階はソースからのデスクトップ用で、NuGet パッケージと Browser/WASM は対象外である。クロス配布には利用者が対象用ライブラリと通知を用意し、LumyteBuildUsvgNative=false と対象の LumyteUsvgNativePath と、通知を含む LumyteUsvgNativeOutput を必ず指定する。

### 契約と API 差分

```diff
+namespace Lumyte.Graphics.Svg;
+public sealed class SvgDocument2D
+{
+    // 96 DPI で解決した論理ピクセル単位の表示領域。
+    public System.Numerics.Vector2 Size { get; }
+    // 非圧縮 UTF-8、最大 4 MiB。外部ファイル・ネットワーク・フォントを読まない。
+    // 無効な入力と未対応機能は FormatException、サイズ超過は ArgumentOutOfRangeException。
+    public static SvgDocument2D Parse(System.ReadOnlySpan<byte> utf8);
+    // Begin 済みの canvas に記録。変換とクリップを保持する。
+    public void Draw(Lumyte.Graphics.Drawing2D.DrawingCanvas2D canvas);
+}
```

C ABI は内部境界であり、次の操作に限定する。

```diff
+// 入力は呼び出し中のみ借用。結果は成功・失敗とも所有権を返す。
+void* ls_parse(const unsigned char* data, size_t length);
+// 結果の解放まで有効な読み取り専用領域と長さ。
+const float* ls_words(const void* document, size_t* length);
+const unsigned char* ls_error(const void* document, size_t* length);
+// 同じ非 null 結果を一度だけ解放する。
+void ls_free(void* document);
```

XML と SVG の解析エラー、未対応の正規化結果、深さ 128 または出力の浮動小数点要素数 4 Mi 超過は診断文字列を返す。解析が失敗した場合は部分的な drawing を公開しない。ネイティブライブラリの不足や ABI の不一致は通常の .NET ロード例外として表面化し、空の描画で代用しない。入力サイズ制限は実行時間・メモリ全体の厳密な上限ではなく、敵対的な文書の隔離実行までは保証しない。既存エンジンには変換後の曲線分割の辺数・線の三角形数それぞれ 65,536 の制限があり、解析成功は任意の拡大率で描画可能なことを保証しない。描画時にこの上限を超える場合は既存エンジンの例外を返す。

## 検討した代替案

- XML と CSS の独自解析は依存を減らせるが、継承、単位、use、曲線の解釈を重複実装するため採用しない。
- resvg による画像化は多くの SVG を描けるが、既存のベクトル描画への接続という目的に合わない。
- すべての SVG 機能を先に二次元エンジンへ追加すると範囲と依存が広がるため、表現不能な機能の明示的な拒否を選ぶ。

## 結果と影響

静的な単色アイコンを既存の二次元描画と同じ送信経路で利用できる。既存 API を変更しない一方、利用者は対応範囲を確認し、ネイティブライブラリを配布する必要がある。

受け入れ条件は実際の usvg 解析から既存のパス・塗り・線・変換・クリップに至る検証、未対応機能と無効入力の拒否、ネイティブの所有権とエラーの検証である。描画経路のスモーク検証と、対象環境のビルド・配布確認を行い、未検証のプラットフォームの画素一致を保証しない。
