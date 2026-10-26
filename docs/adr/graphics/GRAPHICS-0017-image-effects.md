# ADR-GRAPHICS-0017: 2D・3Dで共有する画像エフェクトDAGとRenderGraphへの展開

- 状態: 採用
- 日付: 2026-10-11
- 更新: 2026-10-26

## 背景

blur、色変換、透明度、画像合成を2Dレイヤーと3Dの描画結果へ適用したい。描画システムごとに画像演算とGPU資源管理を実装すると、色・alpha・必要領域の意味が分かれ、同じ処理を重複して保守することになる。一方、2Dの描画順や3Dのカメラ・深度・履歴まで共通化すると、画像処理の利用側に不要な依存が生じる。

画像演算とGPUパス生成を共通ライブラリに分離し、適用する画像と時点は各描画システムが管理する。本ADRは共通エフェクトを定め、2Dレイヤー・背景取得・clipの適用順は2D側が管理する。公開APIと詳細契約・利用例は末尾にまとめる。

## 決定

### 1. 共通処理と描画システムの境界

`Lumyte.Graphics.ImageEffects` は描画済み画像の演算DAGとGPUパス生成を担当する。Abstractions・RenderGraphへ依存し、2D・3Dの具象型、backend、Windowには依存しない。ライブラリは `src/Graphics/` に配置し、プロジェクト・NuGet・namespaceを揃え、shaderは既存Slang基盤を使う。

```text
2D側のレイヤー・背景取得 ─┐
                         ├→ EffectImage + ImageEffectGraph
3D側の画像・適用位置管理 ─┘     → RenderGraphの画像エフェクト拡張 → Graphics
```

2D側はレイヤーの画像化、clip・group opacityの適用位置、描画順、背景snapshotを管理する。3D側はカメラ、画像を取り出す時点、露出・トーンマッピングとの前後関係を管理し、条件を満たすcolor画像を同じRenderGraphへ渡す。深度・法線・motion vector・frame履歴を必要とする演算と、その入力を用意する3D側の接続は今回の共通APIに含めない。

paint DAGは図形内部の塗り、ImageEffectGraphは画像化後の処理、RenderGraphはGPUパスと資源依存を表す。各graphの意味を混在させず、独自のGPUパス実行器を追加しない。RenderGraphの接続契約は[GRAPHICS-0015](./GRAPHICS-0015-render-graph.md)に従う。

### 2. 定義とRenderGraphへの変換

画像エフェクトはRenderGraphへの拡張として提供する。独立したImageEffectCompilerや別の実行器を持たず、graph.AppendGaussianBlurなどを呼び出す。RenderGraph.Deviceの借用deviceを使って固定資源を準備し、同じgraphのCreateTexture・AddPass・UseTextureで一時画像とアクセスを宣言する。deviceが設定されていないgraphへのパス追加は拒否する。必要なpassの選択、依存順序、barrier、一時allocationの確保・解放は既存RenderGraphに任せる。

ImageEffectGraphは2Dなどが再利用する遅延設定としてのみ残し、独立したGPUスケジューラーにはしない。必要入力領域の照会はImageEffectGraph.GetRequiredSourceBoundsへ置き、deviceを必要としない。

ImageEffectGraphはGPU資源を生成・所有しない不変DAGとし、SourceとImageEffectGraph.Createで構築する。利用者がエフェクトを明示的に生成し、子graphと結合する。同じ定義を別の入力・frameにも使えるが、準備結果は各回に作る。RenderGraphのAppend拡張メソッドは、入力EffectImageと効果のパラメーターを受け取り、直接RenderGraphへパスを追加する。戻り値は準備済み出力であり、エフェクトやImageEffectGraphを返さない。

```text
ImageEffectGraph + EffectImage
  → RenderGraph.AppendEffects（入力束縛・子の展開・演算別配送）
  → RenderGraphのpass群 + EffectImage
  → 呼び出し側の後続pass → Compile・Record・Submit
```

AppendEffectsはすべてのSource葉を一つの入力画像へ束縛し、子から順に各エフェクトのAppendPassesへ渡す。同じnode参照は一回のAppend内で一度だけ展開する。構造が等しい別nodeの統合や、別入力・graph・frameへのGPU結果の流用は行わない。定義の深さは256、展開する異なるnodeは4096を上限とする。

各エフェクトは入力画像から出力画像とその範囲を求め、一時texture・pass・全資源アクセスをRenderGraphへ追加する。提供する初期実装はColorMatrix・Opacity・Compositeを各一つのfragment pass、GaussianBlurをsigmaが0でない軸ごとのfragment passへ展開する。Sourceは入力をそのまま返す。影・glowは基本演算の組み立てで表す。

返すEffectImageは同じRenderGraph内で後続処理へ渡す論理handleである。物理画像へ変換して再importしない。出力を最終成果物にする側がMarkOutput/Exportを宣言し、依存解析、barrier、不要passの除去、一時allocationの確保・解放をRenderGraphへ任せる。拡張メソッドはRenderGraphへパスを宣言するだけで、Compile・Record・Submit・Wait・Presentは呼び出し側が行う。

ImageEffectSubgraphはEffectsコールバックへ渡す親RenderGraphの借用ビューとする。独立したgraphやcompiled planを生成せず、Sourceに適用対象の画像を渡す。AppendGaussianBlurなどはEffectImageを返し、親graphと同じImageEffectsGraphContextと寿命管理を使う。ビューのDisposeは後続の呼び出しを閉じるだけで、固定GPU資源を解放しない。2D内部からのAppendと直接のgraph.AppendEffectsは同じshader状態を利用できる。

ImageEffectsGraphContextは本パッケージに置き、Graphと借用ImageEffectShadersを保持する。標準の拡張メソッドはGraph.GetOrAddExtensionでこの型の状態を取得する。未登録なら既定の埋め込み版を使い、RenderGraphExtensions.AddImageEffects(shaders)なら指定済みのshaderを使う。ResourceManagerとの接続パッケージも同じContextを登録するため、AppendにshaderやManagerを渡さない。Contextはgraphごとに一つで、2Dなど他の拡張からも共有する。Manager経由の非同期ロードは拡張構成の初期化で完了させ、Appendで再ロードやDI解決を行わない。単独利用の埋め込みartifactは初回使用時に共有cacheへロードする。

### 3. 画像・領域・色

EffectImageは原点を含むdevice-pixel範囲と、Rgba16Floatのlinear premultiplied RGBAを組にする。画像は同じ出力pixel grid上の有限範囲を持ち、Bounds左上がtextureの原点に対応する。入力allocationはBoundsと寸法が一致する単一mip/layerで、Sampled用途を持つ。物理allocationの形式・寸法はRecord時に検査する。

DisplayLinear/SceneLinearを明示し、いずれもlinear sRGB primariesとする。色の意味を暗黙に変更・合成せず、露出・トーンマッピング・色域変換は適用側が用意する。演算の途中でHDRのRGBをclampせず、低精度formatや縮小blurへ暗黙に置き換えない。演算式とalphaの扱いは末尾の詳細契約にまとめる。

GetRequiredSourceBoundsは要求する出力からblurの余白やCompositeの逆移動を伝播し、必要入力を保守的に返す。この照会は呼び出し側の画像取得範囲の決定に使い、Append全体を自動で部分描画へ変換するものではない。透明pixelもColorMatrixの定数項で可視になるため、alphaが0の領域を単純に除去しない。

### 4. エフェクト自身に固定のパス実装を持たせる

すべての演算を不変のImageEffectDefinitionで表し、入力数・出力範囲・必要入力領域の計算とAppendPassesを実装する。提供するColorMatrix・GaussianBlur・Opacity・Compositeも同じEffectOperationNodeに格納する。AppendEffectsは演算の型を判別せず、エフェクト自身のAppendPassesを呼ぶ。

エフェクトレンダラーのinterface、差し替え引数、アダプター、登録表、DI設定は設けない。提供する効果型はsealedとし、実装を変更したい場合は新しい効果型を追加する。利用者の効果も同じ基底型を継承し、独自のAppend拡張メソッドをライブラリとして提供できる。RenderGraph本体の変更は不要である。

AppendColorMatrix・AppendGaussianBlur・AppendOpacity・AppendCompositeは入力画像と数値・行列などのパラメーターを受け取り、graph.AppendEffectへ渡すパス追加専用の拡張メソッドとする。戻り値はEffectImageで、そのまま次の入力にできる。固定資源の所有者を利用側へ返さず、準備前にGraphへ登録する。利用者は効果オブジェクトをnewせずに呼び出す。内部で検証用の不変定義を作り、固定実装へ渡すが、効果やDAGを返す組み立てAPIにはしない。単純な設定は直接引数とし、複雑な設定が必要になった場合だけ設定オブジェクトを使う。

DAGの構築はImageEffectGraph.Create(effect, inputs)で明示する。入力数は0以上で子graphの数と一致させ、子列をコピーする。Source葉は一つの画像へ束縛する。DAG経路の0入力生成はSourceの色semanticを保持し、直接AppendEffectする0入力生成はDisplayLinearを使う。必要領域が空の子は逆向きの領域計算を省略する。

直接AppendEffectは入力数・入力画像・色semanticの一致と宣言した出力範囲を検査する。DAG経路も同じ出力範囲・色契約を検査する。エフェクトは複数のrender/compute/copy passを追加でき、固定資源をcontext.Ownへ移す。領域計算と実際のsamplingを一致させ、出力範囲・色・alphaを保持する。画像へ直接書くcompute処理にはGraphicsのstorage texture契約が必要となる。

### 5. 資源宣言・所有権・失敗

外部allocationは呼び出し側が一度だけimportし、実際の入口state・scopeと外部producerから最初の利用stageまでの可視性を保証する。callbackは宣言済み資源だけを使い、記録中に依存先や一時資源を追加しない。同resourceのUseはscopeを合併する。初期RenderGraphはallocation全体を追跡するため、同じallocationを同じpassでsampleとattachmentに使わず、出力は別画像へ書く。

MarkOutputは必要出力の保持、Exportはさらにfinal state/scopeの指定、DependsOnは順序、KeepAliveは副作用の保持を表す。exportしない画像はimport状態へ自動復帰しない。opaque callbackも全アクセスを宣言し、内部barrierを管理して宣言した入口stateへ戻す。単一Useで表せない異なるstateはpassへ分割する。

| 対象 | 所有者 |
| --- | --- |
| importしたtexture・view等 | 呼び出し側。viewだけでなく親textureもGPU使用終了まで保持 |
| 一時allocationとcontext生成view | CompiledRenderGraph |
| callbackのCPU snapshot、pipeline、argument table、sampler等の固定資源 | 準備開始前からRenderGraph、Compile成功後はCompiledRenderGraph。エフェクトはEffectPreparationContext.Ownへ所有権を移す |
| 借用shaderのLease | 接続する拡張が固定資源より先にgraph.Retainへ登録し、逆順で最後に返す |

準備Contextのcallback集合は、一つのcompiled planの一回のRecordにだけ使い、記録失敗も消費済みとする。Effectsを使ったgraphは成功したCompileで構築を閉じ、再Compileやpassの変更を拒否する。次のframeは新しいgraphへAppendし、先行GPUが使うtable等を上書きしない。再利用する拡張構成と各graphの準備状態は寿命を分ける。

正常系は「準備・Compile → Record → Finish → Submit → 明示完了確認 → command → plan → submissionの解放」とする。Compileで全拡張の所有権をplanへ一括移譲し、planがview、一時allocation、登録の逆順の固定資源とLeaseを解放する。移譲後のgraph.Disposeはplanの資源を返さない。出力を後続submissionでも使う場合は、その完了まで元planと関連資源を保持する。

| 失敗位置 | 処理 |
| --- | --- |
| Appendの失敗 | 失敗したContextは継続利用を拒否し、部分生成資源をgraphに残す。graph全体をDisposeして再構築 |
| Compileの失敗 | 所有権はgraphに残る。GPU記録前なのでgraph.Disposeで解放できる |
| Recordの失敗・未送信の記録を破棄 | commandを先に破棄してからplanを解放。再記録しない |
| Submit後の待機キャンセル・Failed | GPU使用終了とはみなさず、明示完了またはbackendの終了保証に従う |
| 固定資源の解放失敗 | 失敗した資源と依存先を保持し、同じgraphまたはplanのDisposeで再試行。成功済みの解放を繰り返さない |

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
| 提供効果と利用者効果に別のnode・配送・登録方式を設ける | 領域計算・出力検証・拡張方法が二重化する |
| 独自のGPU backend・パス実行器 | 既存Graphics・RenderGraphの契約と重複する |

## 結果と影響

2Dレイヤーと3D画像に同じ画像演算を適用し、色・alpha・領域の契約とGPU処理を共有できる。適用側の描画順やカメラの責務を維持しながら、すべての演算を同じ自己完結した効果の契約で追加できる。実行方式を変える場合は別の効果型を定義する。

初期実装はpass融合やframe間の固定pipeline cacheを行わないため、基本演算を増やすほどpass数・中間面・準備資源が増える。graph内の拡張状態と借用shaderを共有し、各Appendは準備ContextをGraphへ移す。呼び出し側はcompiled planのGPU使用期間、全アクセスの宣言、追加失敗時のgraph再構築を管理する。演算を利用者のライブラリとして追加でき、RenderGraph本体や固定登録表の変更は不要である。定義の不変性、正しい必要入力範囲、全資源アクセスの宣言は独自実装側の責務となる。RenderGraphのwhole-allocation同期と単一queueの制約に従う。

## 検証方針

| 領域 | 受け入れ条件 |
| --- | --- |
| RenderGraphへの展開 | Sourceの単一入力束縛、すべての演算nodeの一回展開、別Appendの分離、深さとnode数の上限 |
| 独自効果 | 入力数とsnapshot、0入力生成、複数入力の必要範囲の合併、空の必要範囲、出力範囲・色の検証、複数pass、失敗時の固定資源解放、直接生成と2D適用 |
| 演算・領域 | kernelと範囲、HDR/alpha、SceneLinearの保持、整数offset、透明な隙間、必要入力範囲とsamplingの一致 |
| パス追加と構築 | 提供4種類のAppend拡張メソッド、null receiver、直接AppendとDAGの出力検証、独立した効果生成、既定shaderと明示登録の共通経路 |
| RenderGraph | 全資源宣言、重複import防止、culling・export、後続passへの出力接続 |
| 失敗・寿命 | Append/Compile/Recordの失敗、待機キャンセル、一回実行、GPU使用終了後の解放、複数frameでの非上書き |
| 複数拡張 | 直接Appendと2Dの部分ビューで同じContext・shaderを共有し、部分ビュー終了でGPU資源を返さない |
| 準備と解放の失敗 | 準備途中の資源保持、解放失敗の再試行、shader Leaseより先に固定資源を解放 |

準備時間、GPU時間、pass数、中間面・固定資源の割り当て量を測定する。画像比較は色空間・誤差許容値を固定し、backend共通のfixtureを使用する。2Dへの適用順と背景置換は2D側で検証する。

## 別途決定する事項

- 複数の独立した入力画像、深度・法線・motion vector・frame履歴と3D側の接続。
- storage textureによるcompute画像処理、LUT・畳み込み・変形・morphology・Bloom・履歴効果のAPIと品質。
- pass融合、frameをまたぐ画像・pipeline cacheと寿命・更新規則。
- RenderGraphのsubresource/byte範囲、pool/alias、複数queue、GPU timestamp、compile cache。

## API・詳細契約・利用例

拡張はRenderGraph.Deviceと型付き拡張状態、共通の所有権管理を利用する。以下は公開メンバーの宣言である。Graphics型は`Lumyte.Graphics.Abstractions`、RenderGraph型は`Lumyte.Graphics.RenderGraph`を使用する。`RenderGraphBuilder`は`using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;`のaliasであり、新規型ではない。

### 公開API

ImageEffectGraphはSource葉とEffectOperationNodeの不変DAGとする。すべての演算はImageEffectDefinition自身に固定のAppendPassesを持つ。公開nodeは読み取り用snapshotとし、RenderGraphの拡張メソッドは入力画像とパラメーターからパスだけを追加する。

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
+        // GPU・RenderGraphなしで必要Source範囲を計算する。
+        public EffectPixelRect GetRequiredSourceBounds(EffectPixelRect output);
+        public static ImageEffectGraph Source();
+        // 入力数の一致とnullを検査し、子列をコピーする。
+        public static ImageEffectGraph Create(ImageEffectDefinition definition, params ImageEffectGraph[] inputs);
+    }
+    public static class ImageEffectRenderGraphExtensions
+    {
+        public static RenderGraphExtensions AddImageEffects(this RenderGraphExtensions extensions,
+            ImageEffectShaders? shaders = null);
+        public static EffectImage AppendEffect(this RenderGraphBuilder plan,
+            ImageEffectDefinition effect, params EffectImage[] inputs);
+        public static EffectImage AppendEffects(this RenderGraphBuilder plan,
+            ImageEffectGraph graph, EffectImage source);
+        // 入力画像とパラメーターからパスを追加する。効果やDAGを返さない。
+        public static EffectImage AppendColorMatrix(this RenderGraphBuilder plan, EffectImage input, ColorMatrix4x5 matrix);
+        public static EffectImage AppendGaussianBlur(this RenderGraphBuilder plan, EffectImage input, float sigmaX, float sigmaY);
+        public static EffectImage AppendOpacity(this RenderGraphBuilder plan, EffectImage input, float opacity);
+        public static EffectImage AppendComposite(this RenderGraphBuilder plan, EffectImage source, EffectImage backdrop,
+            EffectCompositeMode mode, int sourceOffsetX = 0, int sourceOffsetY = 0);
+        // 低水準の入口。呼び出す前にcontextの所有権をgraph等へ確立する。
+        public static EffectImage AppendEffectsWithContext(this RenderGraphBuilder plan,
+            ImageEffectGraph graph, EffectImage source, EffectPreparationContext context);
+        public static EffectImage AppendEffectWithContext(this RenderGraphBuilder plan,
+            ImageEffectDefinition effect, EffectPreparationContext context, params EffectImage[] inputs);
+    }
+    public sealed class ImageEffectsGraphContext
+    {
+        public ImageEffectsGraphContext(RenderGraphBuilder graph, ImageEffectShaders? shaders = null);
+        public EffectImage AppendEffects(ImageEffectGraph effects, EffectImage source);
+        public EffectImage AppendEffect(ImageEffectDefinition effect, params EffectImage[] inputs);
+    }
+    public sealed class ImageEffectShaders
+    {
+        public ImageEffectShaders(ShaderArtifact fullscreen, ShaderArtifact effects);
+        public ImageEffectShaders(IGraphicsShader fullscreen, IGraphicsShader effects);
+        public static ImageEffectShaders Embedded { get; }
+        public ShaderArtifact Fullscreen { get; }
+        public ShaderArtifact Effects { get; }
+    }
+    public sealed class ImageEffectSubgraph : IDisposable
+    {
+        public ImageEffectSubgraph(RenderGraphBuilder graph, EffectImage source);
+        public RenderGraphBuilder Graph { get; }
+        public EffectImage Source { get; }
+        public EffectImage AppendGaussianBlur(EffectImage input, float sigmaX, float sigmaY);
+        public EffectImage AppendOpacity(EffectImage input, float opacity);
+        public EffectImage AppendColorMatrix(EffectImage input, ColorMatrix4x5 matrix);
+        public EffectImage AppendComposite(EffectImage source, EffectImage backdrop,
+            EffectCompositeMode mode, int sourceOffsetX = 0, int sourceOffsetY = 0);
+        public EffectImage AppendEffect(ImageEffectDefinition effect, params EffectImage[] inputs);
+        // 部分ビューを閉じるだけで、固定GPU資源を解放しない。
+        public void Dispose();
+    }
+    // すべての効果が継承する不変の定義。領域計算はGPU資源を確保しない。
+    public abstract record ImageEffectDefinition
+    {
+        public abstract int InputCount { get; }
+        // 子画像の範囲から正の出力範囲を求める。
+        public abstract EffectPixelRect GetOutputBounds(ReadOnlySpan<EffectPixelRect> inputs);
+        // 出力要求から指定入力の必要領域を保守的に求める。不要なら空を返す。
+        public abstract EffectPixelRect GetRequiredInputBounds(int inputIndex, EffectPixelRect output);
+        // 複数pass可。実装は効果自身が固定して保持し、入力spanを保存しない。
+        public abstract EffectImage AppendPasses(EffectPreparationContext context, RenderGraphBuilder plan,
+            ReadOnlySpan<EffectImage> inputs);
+    }
+    public sealed record ColorMatrixEffect : ImageEffectDefinition
+    {
+        public ColorMatrixEffect(ColorMatrix4x5 matrix);
+        public ColorMatrix4x5 Matrix { get; }
+        public override int InputCount { get; }
+        public override EffectPixelRect GetOutputBounds(ReadOnlySpan<EffectPixelRect> inputs);
+        public override EffectPixelRect GetRequiredInputBounds(int inputIndex, EffectPixelRect output);
+        public override EffectImage AppendPasses(EffectPreparationContext context, RenderGraphBuilder plan,
+            ReadOnlySpan<EffectImage> inputs);
+    }
+    public sealed record GaussianBlurEffect : ImageEffectDefinition
+    {
+        public GaussianBlurEffect(float sigmaX, float sigmaY);
+        public float SigmaX { get; }
+        public float SigmaY { get; }
+        public override int InputCount { get; }
+        public override EffectPixelRect GetOutputBounds(ReadOnlySpan<EffectPixelRect> inputs);
+        public override EffectPixelRect GetRequiredInputBounds(int inputIndex, EffectPixelRect output);
+        public override EffectImage AppendPasses(EffectPreparationContext context, RenderGraphBuilder plan,
+            ReadOnlySpan<EffectImage> inputs);
+    }
+    public sealed record OpacityEffect : ImageEffectDefinition
+    {
+        public OpacityEffect(float opacity);
+        public float Opacity { get; }
+        public override int InputCount { get; }
+        public override EffectPixelRect GetOutputBounds(ReadOnlySpan<EffectPixelRect> inputs);
+        public override EffectPixelRect GetRequiredInputBounds(int inputIndex, EffectPixelRect output);
+        public override EffectImage AppendPasses(EffectPreparationContext context, RenderGraphBuilder plan,
+            ReadOnlySpan<EffectImage> inputs);
+    }
+    public sealed record CompositeEffect : ImageEffectDefinition
+    {
+        public CompositeEffect(EffectCompositeMode mode, int sourceOffsetX = 0, int sourceOffsetY = 0);
+        public EffectCompositeMode Mode { get; }
+        public int SourceOffsetX { get; }
+        public int SourceOffsetY { get; }
+        public override int InputCount { get; }
+        public override EffectPixelRect GetOutputBounds(ReadOnlySpan<EffectPixelRect> inputs);
+        public override EffectPixelRect GetRequiredInputBounds(int inputIndex, EffectPixelRect output);
+        public override EffectImage AppendPasses(EffectPreparationContext context, RenderGraphBuilder plan,
+            ReadOnlySpan<EffectImage> inputs);
+    }
+    public abstract record ImageEffectNode;
+    // Create factoryのみが構築。Definitionは不変として借用、Inputsはコピー済みreadonly列。
+    public sealed record EffectOperationNode : ImageEffectNode
+    {
+        public ImageEffectDefinition Definition { get; }
+        public IReadOnlyList<ImageEffectNode> Inputs { get; }
+    }
+    public sealed record SourceEffectNode : ImageEffectNode;
+    public sealed class EffectPreparationContext : IRenderGraphLifetime
+    {
+        public EffectPreparationContext(IGraphicDevice device, EffectColorSemantic colorSemantic,
+            ImageEffectShaders? shaders = null);
+        public IGraphicDevice Device { get; }
+        public EffectColorSemantic ColorSemantic { get; }
+        public ImageEffectShaders? Shaders { get; }
+        // 固定資源をContextへ移す。一時textureはRenderGraphへ宣言する。
+        public void Own(IDisposable resource);
+        // 標準AppendではGraphが所有する。移譲後は利用側からDisposeしない。
+        public void Dispose();
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

#### パラメーターを渡してパスを直接追加する

呼び出し側が用意したdevice、graph、同じgraphに属する入力EffectImageを使う。AppendGaussianBlurへ入力画像とsigmaを直接渡す。利用者がGaussianBlurEffectを生成する必要はない。

```csharp
using Lumyte.Graphics.ImageEffects;

var blurred = graph.AppendGaussianBlur(source, 4, 4);
EffectImage output = graph.AppendOpacity(blurred, 0.5f);
// outputを同じgraphの後続passへ接続し、Compile・Record・Submitする。
// GPU使用終了後、commandとcompiled planを解放する。
```

#### 独自エフェクトとパス追加拡張メソッド

位置を移す利用者定義の例。画像原点を変えるだけなのでGPUパスを追加しない。shaderを使う効果ではAppendPasses内で一時textureとpassを追加し、全アクセスを宣言する。実装を変える場合はTranslateEffectのレンダラーを置き換えず、新しいエフェクト型を追加する。

```csharp
using Lumyte.Graphics.ImageEffects;
using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;

public sealed record TranslateEffect(int X, int Y) : ImageEffectDefinition
{
    public override int InputCount => 1;
    public override EffectPixelRect GetOutputBounds(ReadOnlySpan<EffectPixelRect> inputs)
        => inputs[0].Offset(X, Y);
    public override EffectPixelRect GetRequiredInputBounds(int inputIndex, EffectPixelRect output)
        => output.OffsetInverse(X, Y);
    public override EffectImage AppendPasses(EffectPreparationContext context,
        RenderGraphBuilder plan, ReadOnlySpan<EffectImage> inputs)
        => inputs[0] with { Bounds = inputs[0].Bounds.Offset(X, Y) };
}

public static class TranslateEffectRenderGraphExtensions
{
    public static EffectImage AppendTranslate(this RenderGraphBuilder graph, EffectImage input, int x, int y)
        => graph.AppendEffect(new TranslateEffect(x, y), input);
}
```

```csharp
var moved = graph.AppendTranslate(source, 8, 4);
var blurred = graph.AppendGaussianBlur(moved, 3, 3);
// 出力handleは同じgraphへ接続し、compiled planをGPU使用終了後まで保持する。
```
