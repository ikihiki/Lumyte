# ADR-GRAPHICS-0016: 2D・3Dで共有する画像エフェクトDAGとRenderGraphへの展開

- 状態: 採用
- 日付: 2026-10-11
- 更新: 2026-10-12

## 背景

blur、色変換、透明度、画像合成を2Dレイヤーと3Dの描画結果へ適用したい。描画システムごとに画像演算とGPU資源管理を実装すると、色・alpha・必要領域の意味が分かれ、同じ処理を重複して保守することになる。一方、2Dの描画順や3Dのカメラ・深度・履歴まで共通化すると、画像処理の利用側に不要な依存が生じる。

画像演算とGPUパス生成を共通ライブラリに分離し、適用する画像と時点は各描画システムが管理する。本ADRは共通エフェクトを定め、2Dレイヤー・背景取得・clipの適用順は2D側が管理する。公開APIと詳細契約・利用例は末尾にまとめる。

## 決定

### 1. 共通処理と描画システムの境界

`Lumyte.Graphics.ImageEffects` は描画済み画像の演算DAGとGPUパス生成を担当する。Abstractions・RenderGraphへ依存し、2D・3Dの具象型、backend、Window、DIコンテナーには依存しない。任意の登録を `Lumyte.Graphics.ImageEffects.DependencyInjection` に分離する。ライブラリは `src/Graphics/` に配置し、プロジェクト・NuGet・namespaceを揃え、shaderは既存Slang基盤を使う。

```text
2D側のレイヤー・背景取得 ─┐
                         ├→ EffectImage + ImageEffectGraph
3D側の画像・適用位置管理 ─┘     → ImageEffectCompiler → RenderGraph → Graphics
```

2D側はレイヤーの画像化、clip・group opacityの適用位置、描画順、背景snapshotを管理する。3D側はカメラ、画像を取り出す時点、露出・トーンマッピングとの前後関係を管理し、条件を満たすcolor画像を同じcompilerへ渡す。深度・法線・motion vector・frame履歴を必要とする演算と、その入力を用意する3D側の接続は今回の共通APIに含めない。

paint DAGは図形内部の塗り、ImageEffectGraphは画像化後の処理、RenderGraphはGPUパスと資源依存を表す。各graphの意味を混在させず、独自のGPUパス実行器を追加しない。RenderGraphの接続契約はmain `81bba9e` に統合された [GRAPHICS-0015](./GRAPHICS-0015-render-graph.md) に従う。

### 2. 定義とRenderGraphへの変換

ImageEffectGraphはGPU資源を生成・所有しない不変DAGとし、Source、標準4演算、Customのfactoryで構築する。同じ定義を別の入力・frameにも使えるが、準備結果は各回に作る。Customは利用者定義と型付きrendererを構築時に結び付け、標準演算と同じDAGに組み込む。

```text
ImageEffectGraph + EffectImage
  → AppendPasses（入力束縛・子の展開・演算別配送）
  → RenderGraphのpass群 + PreparedImageEffect.Output
  → 呼び出し側の後続pass → Compile・Record・Submit
```

AppendPassesはすべてのSource葉を一つの入力画像へ束縛し、子から順に演算別rendererへ渡す。同じnode参照は一回のAppend内で一度だけ展開する。構造が等しい別nodeの統合や、別入力・graph・frameへのGPU結果の流用は行わない。定義の深さは256、展開する異なるnodeは4096を上限とする。

各rendererは入力画像から出力画像とその範囲を求め、一時texture・pass・全資源アクセスをRenderGraphへ追加する。初期標準実装はColorMatrix・Opacity・Compositeを各一つのfragment pass、GaussianBlurをsigmaが0でない軸ごとのfragment passへ展開する。Sourceは入力をそのまま返す。影・glowは基本演算の組み立てで表す。

PreparedImageEffect.Outputは同じRenderGraph内で後続処理へ渡す論理handleである。物理画像へ変換して再importしない。出力を最終成果物にする側がMarkOutput/Exportを宣言し、依存解析、barrier、不要passの除去、一時allocationの確保・解放をRenderGraphへ任せる。compilerはCompile・Submit・Wait・Presentを行わない。

### 3. 画像・領域・色

EffectImageは原点を含むdevice-pixel範囲と、Rgba16Floatのlinear premultiplied RGBAを組にする。画像は同じ出力pixel grid上の有限範囲を持ち、Bounds左上がtextureの原点に対応する。入力allocationはBoundsと寸法が一致する単一mip/layerで、Sampled用途を持つ。物理allocationの形式・寸法はRecord時に検査する。

DisplayLinear/SceneLinearを明示し、いずれもlinear sRGB primariesとする。色の意味を暗黙に変更・合成せず、露出・トーンマッピング・色域変換は適用側が用意する。演算の途中でHDRのRGBをclampせず、低精度formatや縮小blurへ暗黙に置き換えない。演算式とalphaの扱いは末尾の詳細契約にまとめる。

GetRequiredSourceBoundsは要求する出力からblurの余白やCompositeの逆移動を伝播し、必要入力を保守的に返す。この照会は呼び出し側の画像取得範囲の決定に使い、Append全体を自動で部分描画へ変換するものではない。透明pixelもColorMatrixの定数項で可視になるため、alphaが0の領域を単純に除去しない。

### 4. 固定DIと差し替え

ImageEffectRenderersは4演算の専用interfaceをコンストラクターで受け取り、nullを拒否して型付きreadonlyの参照を保持する。compilerは演算型から対応するfieldへ直接配送する。CanHandle、候補探索、自動fallback、描画中のDI解決は設けず、未対応の入力やGPU機能は明示的に失敗させる。

UseImageEffectsは4標準実装と登録表をTryAddScopedで補完する。先行登録と再Useを保持し、後続AddScopedの単一解決が優先される。DIを使わない場合はCreateDefaultまたは明示的なコンストラクター呼び出しで組み立てる。CreateDefaultは登録表と4標準実装を毎回newし、device・GPU資源・DIを必要としない。DI経路はCreateDefaultを呼ばず、各interfaceをコンストラクターへ注入する。

登録はprovider構築前に完了させ、既存provider・compilerの設定は後から変更しない。登録拡張はdevice・compiler・providerを生成・解決せず、compilerの構築だけでもGPU資源を作らない。scopeはdevice単位とし、GPU使用と準備資源の解放後に破棄する。

標準演算の差し替えでは色・alpha・出力範囲・必要入力範囲の意味を維持する。独自演算では定義にその意味を明記し、rendererと必要範囲計算を一致させる。GetRequiredSourceBoundsは定義から計算するため、rendererだけでfilter半径や座標系を変更しない。複数passを追加する実装も同じinterfaceを使える。画像へ直接書くcompute実装にはGraphicsのstorage texture契約の追加が必要となる。

利用者は不変recordのImageEffectDefinitionを継承し、入力数、出力範囲、出力から各入力への必要範囲を定義する。`IImageEffectRenderer<TDefinition>` がGPUパス生成を担当し、ImageEffectGraph.Customで定義・renderer・子graphを結び付ける。compilerは子の展開と共有、必要領域の伝播、色の意味と宣言した出力範囲の検証を行う。rendererは複数のrender/compute/copy passを宣言でき、固定資源をOwnへ移す。定義はCPU上のsnapshot、rendererは借用する実行依存とし、Appendの間やGPU使用中に変更しない。

Customの入力数は0以上で、子graphの数と一致させる。0入力では定義から有限画像を生成でき、EffectPreparationContext.ColorSemanticで要求された色の意味を取得する。複数の子は同じSource画像から派生した分岐であり、独立した外部画像をSource葉に束縛するAPIは別途決定する。必要範囲が空の子は逆向きの領域計算を省略する。

標準4演算は型別interfaceと固定登録表を維持する。新しい演算はまずCustomを使う独立したライブラリ・利用者実装として追加でき、4引数のImageEffectRenderersやcompilerのswitchを変更する必要はない。DIを使う場合は独自の型付きrendererを通常のサービスとして登録・解決し、Customへ渡す。任意nodeの直接挿入、Type辞書やCanHandleによる実行中の検索は設けない。標準効果の追加もまずCustom経路と専用の定義・renderer・factoryを用いて提供できる。専用の高速経路へ昇格する場合はnode・factory・interface・登録表・標準実装・CreateDefault・DI登録を一緒に拡張し、既存コンストラクターと4演算の契約を維持する。

### 5. 資源宣言・所有権・失敗

外部allocationは呼び出し側が一度だけimportし、実際の入口state・scopeと外部producerから最初の利用stageまでの可視性を保証する。callbackは宣言済み資源だけを使い、記録中に依存先や一時資源を追加しない。同resourceのUseはscopeを合併する。初期RenderGraphはallocation全体を追跡するため、同じallocationを同じpassでsampleとattachmentに使わず、出力は別画像へ書く。

MarkOutputは必要出力の保持、Exportはさらにfinal state/scopeの指定、DependsOnは順序、KeepAliveは副作用の保持を表す。exportしない画像はimport状態へ自動復帰しない。opaque callbackも全アクセスを宣言し、内部barrierを管理して宣言した入口stateへ戻す。単一Useで表せない異なるstateはpassへ分割する。

| 対象 | 所有者 |
| --- | --- |
| importしたtexture・view等 | 呼び出し側。viewだけでなく親textureもGPU使用終了まで保持 |
| 一時allocationとcontext生成view | CompiledRenderGraph |
| callbackのCPU snapshot、pipeline、argument table、sampler等の固定資源 | PreparedImageEffect。rendererはEffectPreparationContext.Ownへ所有権を移す |

準備ownerのcallback集合は、一つのcompiled planの一回のRecordにだけ使い、記録失敗も消費済みとする。再実行はAppendから新しく準備し、先行GPUが使うtable等を上書きしない。owner解放後のgraphをCompile/Recordしない。

正常系は「準備・Compile → Record → Finish → Submit → 明示完了確認 → command → plan → 準備owner → submissionの解放」とし、DI scopeは最後に解放する。出力を後続submissionでも使う場合は、その完了まで元planと関連資源を保持する。

| 失敗位置 | 処理 |
| --- | --- |
| Appendの失敗 | 内部の新規準備資源をcleanupし、変更途中のgraph全体を捨てて再構築。既存の成功ownerは呼び出し側が解放 |
| Compileの失敗 | GPU記録前なので準備ownerを解放できる |
| Recordの失敗・未送信の記録を破棄 | commandを先に破棄してからplan・準備ownerを解放。再記録しない |
| Submit後の待機キャンセル・Failed | GPU使用終了とはみなさず、明示完了またはbackendの終了保証に従う |

[GRAPHICS-0014](./GRAPHICS-0014-caller-managed-resource-validation.md) に従い、DisposeはGPU完了照会・waitを行わず、寿命違反の検出を保証しない。同一device、mapping排他、宣言と実アクセスの一致、外部資源の生存は呼び出し側の事前条件である。数値・範囲・未知enum、自身の局所状態、展開上限は検査し、nativeの失敗も伝達する。

数値・範囲違反はArgumentException/ArgumentOutOfRangeException、局所的な呼び出し順違反はInvalidOperationException、破棄後操作はObjectDisposedException、非対応形式・機能はNotSupportedExceptionとする。初期実行は一つの一般queueとcommand bufferを使い、共有graphへの同時操作は保証しない。

## 検討した代替案

| 代替案 | 採用しない理由 |
| --- | --- |
| 2D・3Dで画像エフェクトを別実装 | 同じ画像演算と資源管理が重複する |
| paint・任意描画グループ・カメラを一つのgraphへ統合 | 塗り、描画順、画像処理、3D固有入力の責務が混在する |
| 直列の処理列だけで表す | 分岐・再合流と共有入力を自然に表せず、同じ中間結果を再計算する |
| CanHandle・優先順位による方式探索 | 演算ごとに指定した実装が固定されない |
| GPU処理をRecordだけへ隠す | RenderGraphから依存と一時資源を解析できない |
| 標準演算の固定switchだけを公開する | 利用者の新しい演算ごとにライブラリ本体と登録表の変更が必要になる |
| 独自のGPU backend・パス実行器 | 既存Graphics・RenderGraphの契約と重複する |

## 結果と影響

2Dレイヤーと3D画像に同じ画像演算を適用し、色・alpha・領域の契約とGPU処理を共有できる。適用側の描画順やカメラの責務を維持しながら、演算ごとの実行方式をDIでも手動でも差し替えられる。

初期実装はpass融合やframe間cacheを行わないため、基本演算を増やすほどpass数・中間面・準備資源が増える。準備ownerとcompiled planの寿命、全アクセスの宣言、追加失敗時のgraph再構築を呼び出し側が管理する。独自演算を利用者のライブラリとして追加でき、compilerや標準登録表の変更は不要である。定義の不変性、正しい必要入力範囲、全資源アクセスの宣言は独自実装側の責務となる。既存GraphicsのAPIは変更せず、RenderGraphのwhole-allocation同期と単一queueの制約に従う。

## 検証方針

| 領域 | 受け入れ条件 |
| --- | --- |
| compiler | Sourceの単一入力束縛、標準・独自nodeの一回展開、別Appendの分離、深さとnode数の上限 |
| 独自効果 | 入力数とsnapshot、0入力生成、複数入力の必要範囲の合併、空の必要範囲、出力範囲・色の検証、複数pass、失敗時の固定資源解放、通常DI登録と2D適用 |
| 演算・領域 | kernelと範囲、HDR/alpha、SceneLinearの保持、整数offset、透明な隙間、必要入力範囲とsamplingの一致 |
| 固定DI | 4演算の登録対応、後勝ち・再Use・CreateDefault、独立した差し替え、登録表構築時のGPU生成と描画中のDI解決がないこと |
| RenderGraph | 全資源宣言、重複import防止、culling・export、後続passへの出力接続 |
| 失敗・寿命 | Append/Compile/Recordの失敗、待機キャンセル、一回実行、GPU使用終了後の解放、複数frameでの非上書き |

準備時間、GPU時間、pass数、中間面・固定資源の割り当て量を測定する。画像比較は色空間・誤差許容値を固定し、backend共通のfixtureを使用する。2Dへの適用順と背景置換は2D側で検証する。

## 別途決定する事項

- 複数の独立した入力画像、深度・法線・motion vector・frame履歴と3D側の接続。
- storage textureによるcompute画像処理、LUT・畳み込み・変形・morphology・Bloom・履歴効果のAPIと品質。
- pass融合、frameをまたぐ画像・pipeline cacheと寿命・更新規則。
- RenderGraphのsubresource/byte範囲、pool/alias、複数queue、GPU timestamp、compile cache。

## API・詳細契約・利用例

比較元はmain `81bba9e`。以下の差分は追加する公開メンバーの宣言である。Graphics型は `Lumyte.Graphics.Abstractions`、RenderGraph型は `Lumyte.Graphics.RenderGraph`、DI型は `Microsoft.Extensions.DependencyInjection` を使用する。`RenderGraphBuilder` は `using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;` のaliasであり、新規型ではない。

### 公開API

ImageEffectGraphはSource葉、標準4演算、Customの不変DAGとし、公開nodeは読み取り用snapshotとする。各標準クラスのAppendPassesは対応interfaceの明示実装。UseImageEffectsは4組とImageEffectRenderersをTryAddScopedで補完し、device・compiler・providerを生成しない。

```diff
+namespace Lumyte.Graphics.ImageEffects
+{
+    // 同じ出力pixel grid上の半開区間。Width/Heightは0以上。加減算overflowは拒否。
+    public readonly record struct EffectPixelRect
+    {
+        public EffectPixelRect(int x, int y, int width, int height);
+        public int X { get; }
+        public int Y { get; }
+        public int Width { get; }
+        public int Height { get; }
+        public bool IsEmpty { get; }
+        public EffectPixelRect Offset(int x, int y);
+        public EffectPixelRect OffsetInverse(int x, int y);
+        // 非負の余白で拡張。空領域は空のまま。整数overflowは拒否。
+        public EffectPixelRect Outset(int x, int y);
+        public EffectPixelRect Union(EffectPixelRect other);
+    }
+    // いずれもlinear sRGB primaries。HDRと表示側の適用位置を区別する。
+    public enum EffectColorSemantic { DisplayLinear, SceneLinear }
+    // Bounds左上がtextureの(0,0)、寸法はBoundsのWidth/Heightと一致する単一mip/layer。
+    // 初期入力はRgba16Floatのlinear premultiplied RGBA。handleは同じgraphに属する。
+    // Width/Heightは正、allocationは単一mip/layerで寸法が一致する。
+    public readonly record struct EffectImage(RenderGraphTexture Texture,
+        EffectPixelRect Bounds, EffectColorSemantic ColorSemantic);
+    public sealed class ColorMatrix4x5
+    {
+        // 20個のfinite係数を行優先でコピー。各行はR,G,B,A,定数項の係数。
+        public ColorMatrix4x5(ReadOnlySpan<float> coefficients);
+        public IReadOnlyList<float> Coefficients { get; }
+    }
+    public enum EffectCompositeMode { SrcOver, Plus }
+    public sealed class ImageEffectGraph
+    {
+        public ImageEffectNode Root { get; }
+        public static ImageEffectGraph Source();
+        // 入力数の一致とnullを検査し、子列をコピー。定義とrendererを構築時に固定。
+        public static ImageEffectGraph Custom<TDefinition>(TDefinition definition,
+            IImageEffectRenderer<TDefinition> renderer, params ImageEffectGraph[] inputs)
+            where TDefinition : ImageEffectDefinition;
+        public static ImageEffectGraph ColorMatrix(ColorMatrix4x5 matrix, ImageEffectGraph input);
+        // sigmaはfull-resolutionのdevice pixel単位、finiteかつ0以上。
+        public static ImageEffectGraph GaussianBlur(float sigmaX, float sigmaY, ImageEffectGraph input);
+        public static ImageEffectGraph Opacity(float opacity, ImageEffectGraph input);
+        // sourceだけを指定整数pixel分移動してから合成する。backdropはgraph内の子。
+        public static ImageEffectGraph Composite(ImageEffectGraph source, ImageEffectGraph backdrop,
+            EffectCompositeMode mode, int sourceOffsetX = 0, int sourceOffsetY = 0);
+    }
+    // 利用者が継承する不変の定義。領域計算はGPU資源を確保しない。
+    public abstract record ImageEffectDefinition
+    {
+        public abstract int InputCount { get; }
+        // 子画像の範囲から正の出力範囲を求める。
+        public abstract EffectPixelRect GetOutputBounds(ReadOnlySpan<EffectPixelRect> inputs);
+        // 出力要求から指定入力の必要領域を保守的に求める。不要なら空を返す。
+        public abstract EffectPixelRect GetRequiredInputBounds(int inputIndex, EffectPixelRect output);
+    }
+    public interface IImageEffectRenderer<in TDefinition>
+        where TDefinition : ImageEffectDefinition
+    {
+        // 複数passも可。inputsのspanは呼び出し中のみ有効。色と宣言出力範囲を保持。
+        EffectImage AppendPasses(EffectPreparationContext context, RenderGraphBuilder plan,
+            TDefinition effect, ReadOnlySpan<EffectImage> inputs);
+    }
+    public abstract record ImageEffectNode;
+    // Custom factoryのみが構築。Definitionは不変として借用、Inputsはコピー済みreadonly列。
+    public sealed record CustomEffectNode : ImageEffectNode
+    {
+        public ImageEffectDefinition Definition { get; }
+        public IReadOnlyList<ImageEffectNode> Inputs { get; }
+    }
+    public sealed record SourceEffectNode : ImageEffectNode;
+    public sealed record ColorMatrixEffectNode(ColorMatrix4x5 Matrix, ImageEffectNode Input) : ImageEffectNode;
+    public sealed record GaussianBlurEffectNode(float SigmaX, float SigmaY, ImageEffectNode Input) : ImageEffectNode;
+    public sealed record OpacityEffectNode(float Opacity, ImageEffectNode Input) : ImageEffectNode;
+    public sealed record CompositeEffectNode(ImageEffectNode Source, ImageEffectNode Backdrop,
+        EffectCompositeMode Mode, int SourceOffsetX, int SourceOffsetY) : ImageEffectNode;
+    public sealed class EffectPreparationContext
+    {
+        public IGraphicDevice Device { get; }
+        public EffectColorSemantic ColorSemantic { get; }
+        // 固定資源を準備ownerへ移す。一時textureはRenderGraphへ宣言する。
+        public void Own(IDisposable resource);
+    }
+    public interface IColorMatrixEffectRenderer
+    {
+        EffectImage AppendPasses(EffectPreparationContext context, RenderGraphBuilder plan,
+            ColorMatrixEffectNode effect, EffectImage input);
+    }
+    public interface IGaussianBlurEffectRenderer
+    {
+        EffectImage AppendPasses(EffectPreparationContext context, RenderGraphBuilder plan,
+            GaussianBlurEffectNode effect, EffectImage input);
+    }
+    public interface IOpacityEffectRenderer
+    {
+        EffectImage AppendPasses(EffectPreparationContext context, RenderGraphBuilder plan,
+            OpacityEffectNode effect, EffectImage input);
+    }
+    public interface ICompositeEffectRenderer
+    {
+        EffectImage AppendPasses(EffectPreparationContext context, RenderGraphBuilder plan,
+            CompositeEffectNode effect, EffectImage source, EffectImage backdrop);
+    }
+    public sealed class ImageEffectRenderers
+    {
+        // 表と4標準実装を毎回newする。device・GPU資源・DIは不要。
+        public static ImageEffectRenderers CreateDefault();
+        public ImageEffectRenderers(IColorMatrixEffectRenderer colorMatrix,
+            IGaussianBlurEffectRenderer gaussianBlur, IOpacityEffectRenderer opacity,
+            ICompositeEffectRenderer composite);
+        public IColorMatrixEffectRenderer ColorMatrix { get; }
+        public IGaussianBlurEffectRenderer GaussianBlur { get; }
+        public IOpacityEffectRenderer Opacity { get; }
+        public ICompositeEffectRenderer Composite { get; }
+    }
+    public sealed class DefaultColorMatrixEffectRenderer : IColorMatrixEffectRenderer
+    {
+        public DefaultColorMatrixEffectRenderer();
+    }
+    public sealed class DefaultGaussianBlurEffectRenderer : IGaussianBlurEffectRenderer
+    {
+        public DefaultGaussianBlurEffectRenderer();
+    }
+    public sealed class DefaultOpacityEffectRenderer : IOpacityEffectRenderer
+    {
+        public DefaultOpacityEffectRenderer();
+    }
+    public sealed class DefaultCompositeEffectRenderer : ICompositeEffectRenderer
+    {
+        public DefaultCompositeEffectRenderer();
+    }
+    public sealed class PreparedImageEffect : IDisposable
+    {
+        public EffectImage Output { get; }
+        // graph callbackの固定資源を所有。関連GPU処理とplanの解放後に呼ぶ。
+        public void Dispose();
+    }
+    public sealed class ImageEffectCompiler
+    {
+        // deviceとrenderersは借用する。生成だけではGPU資源を確保しない。
+        public ImageEffectCompiler(IGraphicDevice device, ImageEffectRenderers renderers);
+        // Source葉をsourceへ束縛し、同一planへパスを追加して出力画像を返す。
+        public PreparedImageEffect AppendPasses(RenderGraphBuilder plan, ImageEffectGraph graph, EffectImage source);
+        // 所望の出力を得るために必要なSource領域を逆向きに計算する。資源確保なし。
+        public EffectPixelRect GetRequiredSourceBounds(ImageEffectGraph graph, EffectPixelRect output);
+    }
+}
+namespace Lumyte.Graphics.ImageEffects.DependencyInjection
+{
+    public static class ImageEffectsServiceCollectionExtensions
+    {
+        public static IServiceCollection UseImageEffects(this IServiceCollection services);
+    }
+}
```

### 詳細契約

#### エフェクトの演算・領域

EffectImageは同じ出力pixel grid上の有限領域とRgba16Floatのlinear premultiplied RGBAを持つ。DisplayLinear/SceneLinearを形式から推測せず、異なるsemanticのCompositeを拒否する。SceneLinearのHDR値を保持し、暗黙の露出・トーンマッピング・色域変換を行わない。

ColorMatrixはalpha>0ならunpremultiply、alpha=0ならRGB=0としたstraight linear RGBAへ4×5行列を適用し、結果alphaを[0,1]へ制限してからRGBへ乗算する。RGBのHDR・負値は保持する。領域を拡大せず、定数項も入力の有限矩形内だけへ作用する。Opacityはfiniteな[0,1]をpremultiplied RGBAへ一度掛ける。

CompositeのSrcOverは `Cs + Cb*(1-As)`、`As + Ab*(1-As)`。PlusはRGB加算、alphaは `min(1,As+Ab)` とする。sourceを指定整数pixel移動し、backdropとの和集合を含む最小外接矩形を出力する。範囲外は透明で、各画像の位置を維持する。離れた画像間の透明な隙間も領域に含み、後続ColorMatrixの定数項はその隙間へも作用する。

GaussianBlurは各軸で `radius=ceil(3*sigma)` とし、離散kernel `exp(-i*i/(2*sigma*sigma))` を正規化してpremultiplied RGBAへ適用する。sigma=0の軸はidentity。出力を半径分拡張し、範囲外は透明をsampleする。初期実装は横・縦のfragment passで別画像へ描き、近似縮小blurを使わない。sigmaは常にfull-resolution device pixelで、資源・領域・sigma上限を超える入力は拒否し黙って半径を縮めない。

GetRequiredSourceBoundsは必要な出力から入力ROIを保守的に逆算する。blurは半径分拡張し、Compositeはsource側のoffsetを逆移動して各子へ伝播し、その和集合を取る。ColorMatrix/Opacityは領域を維持する。Source葉は一回のAppend内で同一入力へ束縛し、共有nodeは一度だけ展開するが、別入力・plan・frameへ結果を流用しない。

DropShadowはalphaの着色→blur→移動→元画像の背後へ合成で表す。移動は影をsource、Opacity(0, Source())を透明backdropとするCompositeを挟み、元画像をその前へ合成する。Glowは移動なしとする。幾何coverageや距離場は画像化してからfilterへ渡し、SDF値自体を色画像としてぼかさない。

### 利用例

#### DIなしで共有RenderGraphへ追加する

呼び出し側が用意したdevice、graph、同じgraphに属する入力EffectImageを使う。後続passはowner.Outputのhandleと範囲を受け取る。出力を最終成果物にする場合はMarkOutput/Exportで保持する。

```csharp
using Lumyte.Graphics.ImageEffects;

var renderers = ImageEffectRenderers.CreateDefault();
var compiler = new ImageEffectCompiler(device, renderers);
var effect = ImageEffectGraph.GaussianBlur(4, 4, ImageEffectGraph.Source());
var owner = compiler.AppendPasses(graph, effect, source);
EffectImage output = owner.Output;
// outputを同じgraphの後続passへ接続し、呼び出し側がCompile・Record・Submitする。
// GPU使用終了後、commandとcompiled planを解放してからowner.Dispose()する。
```

DIを使う場合はUseImageEffectsの後に置き換えたい演算のinterfaceを登録し、解決したImageEffectRenderersを同じcompilerコンストラクターへ渡す。Graphicsへの接続方法は変わらない。

#### 独自エフェクトの定義と適用

位置を移す利用者定義の例。画像原点を変えるだけなのでGPUパスを追加しない。ぼかし・色変換などの標準効果と同じgraphで組み合わせられる。shaderを使う効果ではAppendPasses内で一時textureとpassを追加し、すべての入力・出力を宣言する。

```csharp
public sealed record TranslateEffect(int X, int Y) : ImageEffectDefinition
{
    public override int InputCount => 1;
    public override EffectPixelRect GetOutputBounds(ReadOnlySpan<EffectPixelRect> inputs)
        => inputs[0].Offset(X, Y);
    public override EffectPixelRect GetRequiredInputBounds(int inputIndex, EffectPixelRect output)
        => output.OffsetInverse(X, Y);
}

public sealed class TranslateEffectRenderer : IImageEffectRenderer<TranslateEffect>
{
    public EffectImage AppendPasses(EffectPreparationContext context,
        RenderGraphBuilder plan, TranslateEffect effect, ReadOnlySpan<EffectImage> inputs)
        => inputs[0] with { Bounds = inputs[0].Bounds.Offset(effect.X, effect.Y) };
}

var moved = ImageEffectGraph.Custom(new TranslateEffect(8, 4),
    new TranslateEffectRenderer(), ImageEffectGraph.Source());
var effect = ImageEffectGraph.GaussianBlur(3, 3, moved);
var owner = compiler.AppendPasses(graph, effect, source);
```

このeffectは2D側のLayerOptions2D.EffectsまたはBackdropEffectOptions2D.Effectsにも指定できる。DIを使う場合は `services.AddScoped<IImageEffectRenderer<TranslateEffect>, TranslateEffectRenderer>()` で登録し、解決したrendererをCustomへ渡す。独自実装を標準4演算の登録表へ追加する必要はない。
