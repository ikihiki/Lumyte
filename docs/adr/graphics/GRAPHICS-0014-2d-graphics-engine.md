# ADR-GRAPHICS-0014: 描画順を保持するコマンド記録型2Dグラフィックエンジン

- 状態: 提案
- 日付: 2026-10-10

## 背景

ゲーム内のスプライト、UI、図形、文字を共通の2D描画APIで扱いたい。利用者が図形ごとにGPU buffer、shader、描画状態を組み立てると、backend間の違い、クリッピング、半透明の描画順、GPU使用中のリソース寿命を各機能で重複して管理することになる。

Lumyteには既に [Graphics device](GRAPHICS-0001-graphics-device.md)、[CommandBufferとSubmission](GRAPHICS-0007-command-buffers-and-submission.md)、[描画状態](GRAPHICS-0008-pipeline-programs-and-render-state.md)、[shader引数](GRAPHICS-0009-shader-argument-binding.md)、[indexed draw](GRAPHICS-0011-indexed-and-indirect-commands.md) の契約がある。2D描画はこれらの上に構築し、独立したGPU backend抽象化は追加しない。

本ADRの主要な判断は、即時型のCanvas APIでCPUコマンド列を生成し、描画順を保持して既存Graphics APIへ変換することである。公開境界と、この判断に必要な状態・所有権の契約を定める。実装済みの機能や性能を表すものではない。

## 決定

### 適用範囲と責務

`src/Graphics/Lumyte.Graphics.Drawing2D` にC#ライブラリを配置する。プロジェクト名、NuGet名、名前空間は `Lumyte.Graphics.Drawing2D` とする。実行時にはBCLと `Lumyte.Graphics.Abstractions` を参照し、backendの具象型、Window、Input、Compositionへ依存しない。shader成果物は既存のSlang・shader生成基盤で用意する。

構成は「アプリケーション → Canvas2D → DrawList2D → Renderer2D → IGraphicDevice」とする。CanvasはCPU側の変換・クリップ・描画コマンドを記録し、Rendererは三角形化、連続コマンドのバッチ化、転送用リソースとGPU描画命令を組み立てる。deviceの生成・破棄、queue送信、完了待機、画面への表示はアプリケーションが所有する。

矩形、角丸矩形、円、線、ベジェ曲線を含むパス、画像、整形済みグリフ、オフスクリーン描画を対象とする。最初に矩形・画像・変換・矩形クリップを実装し、その後にパスとグリフ描画を追加する。シーングラフは必要な利用者がCanvasの上に構築し、必須にしない。レイアウト、ヒットテスト、画像デコード、フォント探索、文字列のシェーピング、行分割は本ライブラリの責務に含めない。

### 公開APIの境界

比較元: main `4b8902f5226c17ce86c337d09434a3ae4043bf2d`。対象APIは比較元に存在せず、以下は追加する主要メンバーの宣言である。Graphicsの型には `Lumyte.Graphics.Abstractions`、行列・ベクトルには `System.Numerics` を使用する。

```diff
+namespace Lumyte.Graphics.Drawing2D
+{
+    // 左上原点の論理座標。全成分finite、幅・高さは0以上。
+    public readonly record struct Rect2D(float X, float Y, float Width, float Height);
+    // API入力はstraight alphaのsRGB、各成分はfiniteかつ[0,1]。
+    public readonly record struct Color2D(float R, float G, float B, float A);
+    public enum FillRule2D { NonZero, EvenOdd }
+    public enum LineCap2D { Butt, Round, Square }
+    public enum LineJoin2D { Miter, Round, Bevel }
+    public sealed record StrokeStyle2D
+    {
+        // 論理座標での幅。finiteかつ正数、既定値1。
+        public float Width { get; init; } = 1;
+        public LineCap2D Cap { get; init; } = LineCap2D.Butt;
+        public LineJoin2D Join { get; init; } = LineJoin2D.Miter;
+        // finiteかつ1以上。超過したmiterはbevelへ切り替える。
+        public float MiterLimit { get; init; } = 4;
+    }
+    // 完了後は不変。入力列はBuild時にコピーする。
+    public sealed class Path2D { }
+    public sealed class PathBuilder2D
+    {
+        public PathBuilder2D();
+        public void MoveTo(Vector2 point);
+        public void LineTo(Vector2 point);
+        public void QuadraticTo(Vector2 control, Vector2 end);
+        public void CubicTo(Vector2 control1, Vector2 control2, Vector2 end);
+        public void Close();
+        public Path2D Build();
+    }
+    // 単一mip/layerのSampled D2 viewとsamplerを借用。生成時に同一deviceを検証。
+    // Rgba8UnormSrgbの画像はRGB=sRGB、alpha=linearのstraight alphaに限定する。
+    public sealed class Image2D
+    {
+        public Image2D(IGraphicsTextureView view, IGraphicsSampler sampler);
+        public IGraphicsTextureView View { get; }
+        public IGraphicsSampler Sampler { get; }
+    }
+    // shaping・配置済みのグリフ。R8Unorm atlasのRをcoverageとして読む。
+    // AtlasRectはtexel単位、Boundsは論理座標。atlasはrendererと同一device。
+    public readonly record struct GlyphQuad2D(Rect2D AtlasRect, Rect2D Bounds);
+    public sealed class GlyphRun2D
+    {
+        // quadsをコピー。atlasとsamplerは借用する。
+        public GlyphRun2D(IGraphicsTextureView atlas, IGraphicsSampler sampler,
+            ReadOnlySpan<GlyphQuad2D> quads);
+    }
+    public sealed class Canvas2D
+    {
+        public Canvas2D();
+        // 記録なし・スタックなしの状態から開始する。ネスト開始は拒否。
+        public void Begin();
+        public void Save();
+        // Saveに対応しないRestoreは拒否。
+        public void Restore();
+        // finiteな行列のみ。点への適用はローカル変換→既存変換の順。
+        public void ConcatTransform(Matrix3x2 transform);
+        public void Translate(Vector2 offset);
+        public void Rotate(float radians);
+        public void Scale(Vector2 scale);
+        // 呼び出し時の変換で固定し、現在のclipとの積集合にする。
+        public void ClipRect(Rect2D rect);
+        public void ClipPath(Path2D path, FillRule2D rule = FillRule2D.NonZero);
+        public void FillRect(Rect2D rect, Color2D color);
+        // radiusはfiniteかつ0以上。描画時に短辺の半分まで縮める。
+        public void FillRoundedRect(Rect2D rect, float radius, Color2D color);
+        public void FillCircle(Vector2 center, float radius, Color2D color);
+        public void DrawLine(Vector2 start, Vector2 end, Color2D color, StrokeStyle2D style);
+        public void FillPath(Path2D path, Color2D color, FillRule2D rule = FillRule2D.NonZero);
+        public void StrokePath(Path2D path, Color2D color, StrokeStyle2D style);
+        // sourceはview内のtexel座標、destinationは論理座標。
+        public void DrawImage(Image2D image, Rect2D source, Rect2D destination);
+        public void DrawGlyphRun(GlyphRun2D run, Vector2 origin, Color2D color);
+        // Save/Restoreの対応を検証し、不変のCPUコマンド列を返す。GPU実行なし。
+        public DrawList2D End();
+        // 記録中のCPUコマンドを破棄し、再びBeginできる状態へ戻す。
+        public void Cancel();
+    }
+    // 再使用可能なCPU snapshot。参照する画像・atlasのGPU所有権は持たない。
+    public sealed class DrawList2D { }
+    public sealed record RenderTarget2D
+    {
+        // 同一device、単一sample/mip/layer、D2、RenderAttachment view。
+        // 初期対応はRgba8UnormSrgb。寸法はviewから取得する。
+        public required IGraphicsTextureView View { get; init; }
+        // 論理座標から物理pixelへの倍率。finiteかつ正数、既定値1。
+        public float PixelScale { get; init; } = 1;
+        public AttachmentLoadOp LoadOp { get; init; } = AttachmentLoadOp.Load;
+        public Color2D ClearColor { get; init; }
+    }
+    public sealed class Renderer2D : IDisposable
+    {
+        // deviceは借用。backend固有APIを要求しない。
+        public Renderer2D(IGraphicDevice device);
+        // CPU三角形化・バッチ化と内部upload bufferへの書き込みのみ。
+        // submit/waitは行わない。キャンセル時は内部確保を解放する。
+        public ValueTask<PreparedDraw2D> PrepareAsync(DrawList2D list,
+            RenderTarget2D target, CancellationToken cancellationToken = default);
+        // 生存するPreparedDraw2Dがあれば拒否。暗黙のGPU完了待機なし。
+        public void Dispose();
+    }
+    public sealed class PreparedDraw2D : IDisposable
+    {
+        // 一度だけ。Recordingかつactive passなしの同一device commandに記録する。
+        // 内部upload/copy/barrierと専用render passを記録し、passをEndする。
+        public void Record(IGraphicsCommandBuffer commands);
+        // Record先を含むSubmitの直後に呼ぶ。submissionは借用、一度だけ。
+        public void TrackSubmission(IGraphicsSubmission submission);
+        // 下記の寿命契約に従い内部GPUリソースを解放。idempotent。
+        public void Dispose();
+    }
+}
```

### コマンド、描画順、クリッピング

CanvasはBegin中だけ操作できる。Endは未対応のSaveがある場合に失敗し、記録を維持する。入力不備はコマンドを追加せず、状態を変更しない。End成功またはCancel後は新しい記録を開始できる。描画コマンドは変換、clip、色、stroke設定をsnapshotにし、後続の変更の影響を受けない。PathBuilderはMoveTo前の線・曲線・Closeを拒否する。

描画順は記録順で固定する。バッチ化は連続し、pipeline、texture、sampler、clip、合成設定が一致するコマンドに限る。透明度やtextureを理由に並べ替えない。画像・グリフはquad、図形は三角形へ変換し、既存のtyped bufferとshader引数で渡す。上限を超えるバッチは順序を維持して分割する。

空の図形、空のclip、変換後に面積を持たない図形は描画しない。矩形clipは変換後も軸に平行な場合にscissorへ変換し、回転・せん断を含む場合はパスclipとして扱う。scissorは描画先の範囲内に収め、空範囲にはGPU命令を発行しない。複雑なclipはcoverage maskの積集合で処理する。maskはrenderer内部で所有し、生成と利用を別passとbarrierで区切る。パスclip非対応の実装段階ではNotSupportedExceptionを返し、境界矩形への置き換えは行わない。

曲線の分割は変換とPixelScaleを適用した画面上の誤差に基づく。基本のアンチエイリアスは境界coverageで行い、clip maskのcoverageも乗算する。現行texture契約にないMSAAやresolveを必須にしない。最大誤差、分割数、maskサイズの具体的な上限は実装前の後続判断で定め、超過時に黙って品質を下げない。

### 座標、色、合成

左上原点、右がX正方向、下がY正方向の論理座標とする。Canvasの行列適用後にPixelScaleを掛け、targetの実寸法でclip spaceへ変換する。回転はこの座標系で正の値が時計回りとなる。非整数の座標も保持し、pixelへの丸めはscissor範囲の計算に限定する。

色はAPIのsRGB入力を線形へ変換し、alphaとcoverageを掛けたプリマルチプライド値としてshaderから出力する。画像のRGBはsRGB viewでdecodeした後にalphaを掛ける。通常合成はsource-overで、RGBとalphaともsource factor=One、destination factor=OneMinusSourceAlphaを使用する。sRGB targetへのencodeはattachmentに任せる。clear値も線形RGBへ変換して渡す。

初期出力は透明またはプリマルチプライドの線形合成結果をsRGB格納したtargetとする。Loadを使う場合、既存内容も同じ表現を満たす必要がある。自身のオフスクリーン出力を画像として再利用する際はstraight alpha画像と区別する必要があるため、その入力型・変換契約は後続ADRで追加する。HDR、グラデーション、色管理、他のblend modeも別途設計する。

### GPU実行、所有権、エラー

アプリケーションは入力画像・atlasをupload済みかつSampled stateにし、targetをColorAttachment stateへ遷移させてからRecordを呼ぶ。外部リソースの前後のbarrierとsubmission間の同期はアプリケーションが管理する。rendererは自分のupload・描画buffer・maskに必要なcopyとbarrierだけを記録する。同じtarget subresourceを同時に画像として読む操作は拒否する。

利用順は「Canvas.End → PrepareAsync → 外部barrier → Record → 必要な後続barrier → commands.Finish → Queue.Submit → TrackSubmission → 完了確認 → commands.Dispose → PreparedDraw2D.Dispose」である。RecordはtargetをStoreして終了する。queue送信やPresentを内部で呼ばず、既存のオフスクリーンtargetで使用できる。未採用のSurface設計を前提にしない。

PreparedDraw2Dは内部リソースを所有する。Record前は破棄可能。Record後に送信を取りやめる場合は、記録先command bufferを先に破棄してから解放する。送信した場合はそのsubmissionを必ずTrackSubmissionで関連付け、Pending中のDisposeを拒否する。関連付け前でも記録先が生存する間はDisposeを拒否する。完了後も記録先を先に破棄してから内部リソースを解放する。submissionはこの確認まで生存させる。FailedではGPU使用終了が確認できる既存backendの契約に従い、状態名だけで安全な解放を推測しない。

画像・atlas・targetは借用し、Prepareから記録、送信、GPU完了まで呼び出し側が生存させる。DrawListの保持だけではリソースのDisposeを防がない。グローバルなResourceManager、世代管理、ファイル読み込みは追加せず、その仕組みが必要なアプリケーションから供給する。

Canvas、Renderer、PreparedDrawの同時呼び出しは保証しない。CPUコマンド列は不変として共有できるが、GPUリソースのアクセスと解放は利用者が同期する。asyncの内部upload処理が終わる前のrenderer破棄は拒否する。

数値・範囲違反はArgumentExceptionまたはArgumentOutOfRangeException、呼び出し順違反はInvalidOperationException、破棄済みリソースはObjectDisposedException、非対応format・機能はNotSupportedExceptionとする。未知のenum値も拒否する。通常の入力不備はRecord前に全体検証する。native記録中の失敗は例外で報告し、部分記録されたcommand bufferとPreparedDrawは再使用せず破棄する。暗黙のsubmit、wait、失敗した描画のスキップは行わない。

### 文字描画との接続

文字列は上位の文字処理層で「フォントfallback → shaping → 行配置 → atlas確保 → GlyphRun2D」に変換する。日本語、合字、右から左への表記はその層で解決し、Canvasは配置済みquadを記録順に描く。atlasはR8Unorm coverageとし、文字色はColor2Dを使う。カラーグリフ、SDF/MSDF、atlas eviction、フォントライブラリの選定は別途設計する。

## 検討した代替案

- 描画ごとにGPUへ送信する: 小さな描画で送信回数が増え、アプリケーションのcopyや他のrender passとの同期を組み立てにくいため採用しない。
- textureごとにコマンドを並べ替える: 半透明の重なりとUIの前後関係が変わるため採用しない。連続コマンドだけをまとめる。
- シーングラフを必須にする: 単純なデバッグ描画にもノード管理が必要になり、UIやゲーム側の既存階層と重複するため採用しない。
- 2D専用のGPU backendを作る: device、shader、buffer、同期の既存契約を重複させるため採用しない。
- 文字列処理・ファイル資産管理をCanvasへ統合する: 描画記録と非同期I/O、フォントfallback、キャッシュ世代の責務が混在するため、配置済み描画データを受け取る。

## 結果と影響

ゲーム、UI、図形描画はbackendに依存せず同じCanvasを使用できる。描画順を維持するため結果を予測でき、GPU送信は他の描画処理とまとめられる。一方、texture切り替えの多い描画では全体ソートよりバッチ数が増え、複雑なclipではmask生成のpassとメモリが必要になる。

CPUコマンド記録、GPU準備、記録、完了の境界を持つため、即時描画より呼び出し手順が増える。上位アプリケーションはframeごとの所有者にその手順を集約できる。既存Graphicsの契約は変更せず、新規ライブラリとそのshader・サンプル・テストを追加する。

## 検証方針

実装時は共通APIを使うsample/testを用意し、backend固有device生成はbootstrapへ置く。CPUテストでは変換順、Save/Restore、snapshot、不正入力後の状態、fill rule、stroke join、バッチ境界を確認する。

GPU画像比較では半透明図形の前後入れ替え、sRGBとalpha合成、ネストしたclip、回転矩形clip、パスの穴、曲線拡大、高DPI、atlas文字、Load/Clearを確認する。三角形の共有境界に二重合成や隙間がないことを含め、色空間と誤差許容値を固定する。画像転送・readbackは既存の明示的なcopyと完了待機を使用する。

Recordがsubmit/waitを呼ばないこと、入力不備による部分記録がないこと、Pending中の解放拒否、未送信時の破棄、複数frameをGPU完了前に準備した場合のbuffer非上書きを確認する。代表的なスプライト、UI、パス描画でCPU準備時間、GPU時間、draw call数、転送byte数、割り当て量、mask使用量を測定し、backendごとの対応範囲とともに記録する。測定前に特定のframe rateを保証しない。

## 別途決定する事項

- パスの三角形化アルゴリズム、誤差・複雑度上限、mask cache戦略。
- text shaping、フォントfallback、atlas管理のライブラリと公開API。
- プリマルチプライド画像入力、オフスクリーン再利用、HDRと追加合成。
- シーングラフ、描画cache、複数threadでの準備、frame pool。

これらを具体化し、対応する判断を採用してから実装を進める。
