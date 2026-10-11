# ADR-GRAPHICS-0016: 描画順を保持するコマンド記録型2Dグラフィックエンジン

- 状態: 提案
- 日付: 2026-10-10
- 更新: 2026-10-11

## 背景

図形、画像、文字の描画形式、画像エフェクトを既存Graphics APIの上で扱いたい。描画順・クリッピング・色の合成を共通化しつつ、命令ごとのGPU描画方式を外部実装へ差し替えられる必要がある。2Dと3Dは画像処理とGPU資源管理を共有し、それぞれの描画対象や適用位置を管理する。

本ADRは構成と実行契約を定める。[公開APIと詳細契約・利用例](../../graphics/drawing2d-api.md)を参照文書にまとめる。いずれも実装前の提案であり、画質や性能の実測結果ではない。

## 決定

### 1. 構成と責務

```text
Canvas2D → DrawList2D → Renderer2D ──────────────┐
                            └→ ImageEffects ──┤
3D側の画像・エフェクト適用制御 → ImageEffects ──┤
                                              ↓
                                  RenderGraph → Graphics
```

| 構成要素 | 責務と依存 |
| --- | --- |
| `Lumyte.Graphics.Drawing2D` | CPU命令記録、描画順、幾何・paint、レイヤー、背景取得。Abstractions・ImageEffects・RenderGraphへ依存 |
| `Lumyte.Graphics.ImageEffects` | 描画済み画像の演算DAGとGPUパス生成。Abstractions・RenderGraphへ依存し、2D・3Dの具象型を要求しない |
| `Lumyte.Graphics.RenderGraph` | パス依存、barrier、不要パスの除去、論理一時資源の確保と解放 |
| 各 `DependencyInjection` パッケージ | 標準実装の任意登録。本体からDIコンテナーへ依存させない |
| アプリケーション | device、外部資源、Submit・完了確認・Present、DI scopeの所有 |

新規ライブラリは `src/Graphics/` に配置し、プロジェクト・NuGet・namespaceを上記名称に揃える。backend具象型、Window、Input、Compositionには依存せず、shaderは既存Slang基盤で用意する。

RenderGraphの接続契約は [PR #35](https://github.com/ikihiki/Lumyte/pull/35) のGRAPHICS-0015、revision `b1721647f3fcf0276d3a0b0d165e948e3ece40e5` を参照する。未取り込みのため、GPU実装開始時に採用と公開契約を確認する。独自のパス実行基盤は追加せず、単独2D利用も私有RenderGraphで実行する。

レイアウト、ヒットテスト、画像デコード、ファイル資産管理、フォント探索・shaping・行分割、必須のシーングラフは対象外とする。3Dのカメラ・深度・履歴・トーンマッピングの適用位置も各描画システムが管理する。

### 2. 命令と描画方式

CanvasはCPU上に不変のDrawListを作る。描画順を固定し、連続する同型・同一状態の範囲だけをバッチ化する。レイヤー・clip・隔離合成の境界を越えてまとめず、textureや透明度による全体ソートは行わない。

| 描画命令 | 標準の描画方式 |
| --- | --- |
| FillRect | quad、解析的な境界coverage、共通paint |
| FillRoundedRect | quad、角丸の解析coverage、共通paint |
| FillCircle | quad、円・変換後の楕円の解析coverage、共通paint |
| DrawLine | 線分を覆うquad、線分・capの解析coverage、共通paint |
| FillPath | CPU前処理とGPUタイルcoverage、共通paint。compute実装へ固定登録で差し替え可能 |
| StrokePath | stroke輪郭の展開、汎用coverage、共通paint |
| DrawImage | RGBA texture付きquad、premultiplied補間と合成 |
| DrawCoverageMask | coverage texture付きquad、共通paint |
| DrawDistanceField | SDF/MSDF/MTSDFの輪郭復元、共通paint |
| DrawMesh | 外部で三角形化した頂点・index・coverage、共通paint |
| DrawPaintGraph | path・gradient・画像pattern・隔離合成の評価 |

各命令は指定されたpaintとclipを含む処理全体を担当し、必要なら複数GPUパスを宣言する。FillPathだけを差し替えても、StrokePathの輪郭処理やDrawPaintGraph内部のpath処理には波及しない。

Save/Restore、変換、ClipRect/ClipPathはCPU状態操作である。BeginLayer/EndLayerとDrawBackdropEffectはホストが展開する構造命令とし、上記11命令の専用interfaceを増やさない。Canvas.Endは未完了のSave・レイヤーがあれば失敗し、Cancelは記録全体を破棄する。

### 3. 固定DIと外部実装

11命令それぞれに非genericの専用interfaceを設ける。`DrawingCommandRenderers2D` は全11実装をコンストラクターで受け取り、nullを拒否して型付きreadonly fieldへ保持する。DrawListの型付き領域と描画順の列からバッチを取り出し、専用interfaceを直接呼ぶ。

実装の選択は構築時に固定する。CanHandle、候補探索、自動fallback、描画中のDI解決・Type辞書検索は設けない。入力やGPU機能に対応できない場合は準備時に明示的に失敗する。interface呼び出し自体は残るため、インライン化や性能向上は実測前に保証しない。

DrawingCommandRenderers2D.CreateDefault()は登録表と11標準実装を毎回newし、DI・device・GPU資源を必要としない。UseGraphics2Dは11標準実装と登録表をTryAddScopedし、UseImageEffectsで4エフェクトとその登録表も補完する。

外部実装は後続AddScopedの単一解決が優先され、先行登録と再Useも保持する。Graphicsへは解決済み登録表を通常のRenderer2Dコンストラクターへ渡す。

DI経路ではCreateDefaultを呼ばず、各interfaceをコンストラクターへ注入する。登録はprovider構築前に完了させ、既存provider・rendererの設定は後から変更しない。device・renderer・compiler・providerは登録拡張で生成・解決しない。scopeはdevice単位とし、GPU使用、準備結果、rendererの解放後に破棄する。

### 4. paint・色・外部文字描画

FillRect、FillRoundedRect、FillCircle、DrawLine、FillPath、StrokePathはColor2DとPaintGraph2Dの両方を受け取る。Color2D版はSolidへ正規化し、描画命令の種類は変えない。paintは変換前の論理座標で評価し、形状boundsへ自動伸縮しない。

PaintGraph2DはSolid、linear/radial/sweep gradient、ImagePattern、Opacity、Clip、Transform、Layers、Compositeを持つ不変DAGとする。命令・path・nodeのsnapshotを公開し、外部実装がbackendのprivate型へ依存せず評価できるようにする。

| 項目 | 共通契約 |
| --- | --- |
| 座標 | 左上原点、X右・Y下。`targetPixel = Transform(local, CanvasTransform) * PixelScale - PixelOrigin` |
| 色 | sRGBをdecodeし、Straight入力はalphaを掛けてから線形光で補間・合成する |
| 中間画像 | linear premultipliedのRgba16Float。暗黙の低精度化は行わない |
| 2D出力 | 単一mip/layerのRgba8Srgbまたはdisplay-linearのRgba16Float。通常合成はSrcOver |
| coverage | 同一幾何段階のshape・Canvas clip・paint clipの相関を保ち、独立AAの重複乗算を避ける |
| group opacity | 子を合成したpremultiplied RGBAへ一度適用し、各子へ分配しない |
| 隔離合成 | Compositeの子を透明面へ独立評価する。backdropはgraphの子で、既存Canvas背景ではない |

Clear・Src・DestIn等の透明領域の意味を保持し、評価範囲を輪郭のunionだけへ縮めない。有限のtarget・clip・外側形状を基準に評価する。paint内の外側coverageをLayersの各子へ分配しない。

ImagePatternは整数texelのSource、座標変換、軸別Clamp/Repeat/Mirror/Decal、Nearest/Linearを指定する。外部samplerを持たず、Linearの各tapへSource相対のtile規則を適用する。atlasの隣接画像を読まない。DrawImageもtexelごとのdecode・premultiply後に補間し、補間後のpremultiplyで代用しない。具体式と距離場のrange・padding契約は参照文書で定める。

DrawGlyphRunと文字の描画形式選択は外部文字ライブラリが担当する。パス・mesh・coverage・距離場・RGBA・COLRv1をそれぞれ汎用命令へ渡す。COLRv1の3点linear、2円radial、sweep、重複stopの順序、参照glyph、変換・clip、全28合成modeを保持する。variation・CPAL・foreground・font unitsとY反転・cacheは文字側で解決する。

### 5. 共通画像エフェクトと2Dレイヤー

ImageEffectGraphは描画済み画像を処理する不変DAGとし、paint DAGやCanvasのレイヤー構造とは分ける。初期演算はColorMatrix、GaussianBlur、Opacity、Compositeで、Sourceを入力画像へ束縛する。演算別interfaceとImageEffectRenderersで実装を固定し、同じ演算を2D・3Dから利用する。

| 演算・入力 | 契約 |
| --- | --- |
| EffectImage | 原点を含むdevice-pixel範囲と、linear premultipliedのRgba16Float画像 |
| 色の意味 | DisplayLinear/SceneLinearを明示。異なる意味の画像を暗黙に合成・変換しない |
| ColorMatrix | straight linearへ戻して4×5行列を適用し、alphaを制限して再premultiply。HDRのRGBはclampしない |
| GaussianBlur | device pixel単位のsigma、各軸半径ceil(3σ)、正規化kernel、範囲外透明。必要入力と拡張出力を計算 |
| Opacity | 合成済みRGBAへ一度適用 |
| Composite | SrcOver/Plus、整数pixelのsource移動、両画像を含む外接矩形。隙間も透明pixelとして後続演算へ渡す |

初期blurはfragment shaderによる横・縦の分離filterとし、縮小近似を暗黙に行わない。影・glowは基本演算の組み立てで表す。画像へ直接書くcompute経路には、現Graphicsにないstorage texture契約が必要となる。

BeginLayerは有限のContentBounds、Effects、Opacity、任意のOutputClipをsnapshotする。内容を透明面へ描き、EndLayerで次の順に処理して開始時の状態へ戻す。

```text
内容描画 → Effects → group opacity → 最終clip → 親targetへSrcOver
```

ContentBoundsは効果前の幾何clipであり、単なるculling用boundsではない。親clipとOutputClipは効果後、レイヤー内の追加clipは効果前に適用する。画面外から届く影を保つため入力を最終targetへ先に切らず、拡張後の出力をContentBoundsで再び切らない。画像化の前後のclipは独立した操作で、同一幾何段階のAA相関とは区別する。

Save/Restoreは各レイヤー内で完結させ、外側のSaveを内側から消費しない。空のレイヤーは出力なしとし、0寸法のtextureを作らない。sigmaと画像offsetはCanvas変換・DPIから独立した出力pixel単位である。

DrawBackdropEffectは呼び出し直前までの現在レイヤーだけを別画像へ取得する。出力領域から必要入力余白を逆算し、効果結果を `effect × coverage + original × (1 − coverage)` で置換する。元背景をSrcOverで二重合成せず、取得→効果→置換→後続描画の順序を保持する。

背景取得には初期化済みのSampled | RenderAttachment targetを要求する。sRGB背景はshaderでdecodeしてRgba16Floatへ取得し、alphaを再乗算しない。読出しできない表示先には、対応するオフスクリーン面を使ってから合成する。

### 6. RenderGraphとの接続

Renderer2Dは各命令実装の準備結果からpassと全資源アクセスを宣言する。callbackは一つのrender/compute passまたはcopy群を記録し、encoderを閉じて返す。callback中に新たな依存先を追加せず、pass間barrierはRenderGraphが生成する。複数dispatchが必要な実装もこの境界で表す。

入口となる主要APIを以下に示す。比較元はmain `c1f7c1d`。`RenderGraphBuilder` は `Lumyte.Graphics.RenderGraph.RenderGraph` の型aliasである。全型と命令別interfaceは[API一覧](../../graphics/drawing2d-api.md)を参照する。

```diff
+namespace Lumyte.Graphics.Drawing2D
+{
+    public sealed class Renderer2D : IDisposable
+    {
+        // deviceと登録表を借用。2引数版は標準effect表を使用する。
+        public Renderer2D(IGraphicDevice device, DrawingCommandRenderers2D renderers);
+        public Renderer2D(IGraphicDevice device, DrawingCommandRenderers2D renderers,
+            ImageEffectRenderers effects);
+        // 私有graphへ追加・Compileし、単独実行用のownerを返す。Submitなし。
+        public ValueTask<PreparedDraw2D> PrepareAsync(DrawList2D list,
+            RenderTarget2D target, CancellationToken cancellationToken = default);
+        // 共有graphへ追加し、callbackの固定資源を所有する結果を返す。
+        public ValueTask<PreparedDrawingPasses2D> AppendPassesAsync(RenderGraphBuilder graph,
+            DrawList2D list, RenderTargetBinding2D target, DrawingGraphBindings2D bindings,
+            CancellationToken cancellationToken = default);
+        // 全準備・GPU使用・準備結果の解放後に呼ぶ。暗黙のwaitなし。
+        public void Dispose();
+    }
+    public interface IPreparedCommandBatch2D : IDisposable
+    {
+        // 準備時と同じgraph/targetへ一度だけpassとアクセスを宣言する。
+        void AppendPasses(RenderGraphBuilder graph, RenderTargetBinding2D target);
+        void Dispose();
+    }
+}
```

| 接続事項 | 契約 |
| --- | --- |
| 入力 | 外部allocationを一度だけimportし、DrawingGraphBindings2Dで既存handleを共有。対応はAppend時に固定 |
| 一時画像 | graphの論理handleで接続し、物理画像へ変換して再importしない |
| target | 確定したdescriptor・range・原点を渡し、viewはRecord時に解決。初期targetは単一mip/layer |
| アクセス | shader参照先、copy元/先、index/indirect、typed buffer・shader dataを含める。同resourceのUseはscopeを合併して一回 |
| 内容の保持 | Load・部分更新・SrcOverはColorRead \| ColorWrite。初回全面Clearだけをwrite-onlyにできる |
| 観測と順序 | MarkOutput/Exportは必要出力、DependsOnは順序、KeepAliveは副作用の保持を表す |
| 状態 | exportしない資源はimport状態へ自動復帰しない。入口の実state・scopeと外部同期は利用者が保証 |

初期RenderGraphはallocation全体を追跡する。同じallocationを同じpassでsampleとattachmentに使わず、深いpaint参照や別subresourceでもtargetへのfeedbackを拒否する。背景処理はsnapshotへ分離する。shader-dataの一時生成は準備ownerが行ってimportし、転送metadataの既存契約も維持する。

単独PrepareAsyncは入力をSampled、targetをColorAttachmentとしてimport/exportする。入力はVertexShader | FragmentShader | ComputeShaderのShaderReadまで可視化済みとし、targetはColorOutputのColorRead | ColorWriteを入口scopeとする。共有graphでは利用者が実際のstate・scopeを渡し、read-only importでも最初の利用stageまで外部producerの可視性を保証する。

外部実装を一つのopaque callbackへ包む場合も、全アクセスと入口stateを宣言し、内部barrierを自身で管理して同じstateへ戻す。別のexit stateは現RenderGraphで宣言できない。一つのUseで表せないcopy→sample等の異なるstateは複数passへ分割する。宣言できない方式を推測して実行しない。

### 7. 所有権・完了・失敗

| 対象 | 所有者 |
| --- | --- |
| importしたtexture・view・sampler等 | アプリケーション。viewだけでなく親textureもGPU使用終了まで保持 |
| 一時allocationとcontext生成view | CompiledRenderGraph |
| CPU snapshot、pipeline、argument table、upload、私有shader-data | PreparedDrawingPasses2D / PreparedImageEffect |
| 単独利用のplanと準備owner | PreparedDraw2D |

準備ownerのcallback集合は、一つのcompiled planの一回のRecordにだけ使う。再実行にはAppendから新しく準備し、先行GPUが使うtableやstagingを上書きしない。owner解放後のgraphをCompile/Recordしない。

正常系は「準備・Compile → Record → Finish → Submit → 明示完了確認 → command → plan → 準備owner → submissionの解放」とする。最後にrendererとDI scopeを解放する。単独利用ではPreparedDraw2Dがplanと準備ownerの解放をまとめる。plan出力を後続submissionでも使う場合は、その完了まで保持する。

| 失敗位置 | 処理 |
| --- | --- |
| Appendの失敗・キャンセル | 内部の新規準備資源をcleanupし、変更途中のgraph全体を捨てて再構築。既存の成功ownerは利用者が解放 |
| Compileの失敗 | GPU記録前なので準備ownerを解放できる |
| Recordの失敗・未送信の記録を破棄 | commandを先に破棄してからplan・準備ownerを解放。再記録しない |
| Submit後の待機キャンセル・Failed | GPU使用終了とはみなさず、明示完了またはbackendの終了保証に従う |

[GRAPHICS-0014](GRAPHICS-0014-caller-managed-resource-validation.md)に従い、DisposeはGPU完了照会・waitを行わず、寿命違反の検出を保証しない。同一device、mapping排他、宣言と実アクセスの一致、外部資源の生存は利用者の事前条件である。数値・範囲・未知enum、自身の局所状態、graphの容量・展開上限は準備時に検査し、nativeの失敗も伝達する。単色化・レイヤー省略・自動fallbackで成功扱いにしない。

初期実行は一つの一般queueとcommand bufferを使い、内部Submit・Wait・Presentは行わない。Surfaceの取得・semaphore・提示解放、複数command間の転送metadata公開はアプリ側の既存Graphics契約に従う。Canvas・renderer・共有graphへの同時操作は保証しない。

## 検討した代替案

| 代替案 | 採用しない理由 |
| --- | --- |
| 2D・3Dで画像エフェクトを別実装 | 同じ画像演算と資源管理が重複する |
| paint・任意描画グループ・カメラを一つのgraphへ統合 | 塗り、描画順、画像処理、3D固有入力の責務が混在する |
| CanHandle・優先順位による方式探索 | 命令ごとに指定した実装が固定されない |
| 単色専用命令とpaint種別ごとのDI登録 | 図形とpaintの組み合わせでAPI・登録が増える |
| GPU処理をRecordだけへ隠す | RenderGraphから依存と一時資源を解析できない |
| 独自のGPU backend・パス実行器 | 既存Graphics・RenderGraphの契約と重複する |
| texture順の全体ソート・描画ごとのSubmit | 半透明の順序とアプリ側のGPU実行管理を崩す |
| 文字を単色atlasや平坦なpath列に限定 | 外部の形式選択とCOLRv1の隔離合成を保持できない |

## 結果と影響

図形・画像・外部文字描画が同じpaintと描画順の契約を共有し、命令別の実装をDIでも手動でも差し替えられる。2Dレイヤーと3D画像に同じ画像演算を適用し、GPU処理をRenderGraphへまとめられる。

一方、順序保持によって全体ソートほどバッチを減らせない場合があり、複雑なpaint・clip・エフェクトでは中間画像と複数passが必要になる。準備ownerとcompiled planの寿命、全アクセスの宣言、追加失敗時のgraph再構築を利用側が管理する。命令・演算の追加時にはinterface、登録表、標準実装、CreateDefault、DI登録を同時に更新する。

既存GraphicsのAPIは変更しない。GPU実装はRenderGraphの採用・契約に依存し、初期のwhole-allocation同期と単一queueの制約に従う。

## 検証方針

| 領域 | 受け入れ条件 |
| --- | --- |
| 命令・DI | 11命令と4エフェクトの登録対応、後勝ち・再Use・CreateDefault、配送順と独立した差し替え、登録表構築時のGPU生成と描画中のDI解決がないこと |
| paint・画像 | 6命令のColor/Solid等価性、gradientの退化・stop・extend、tap別tileと負座標、透明境界の補間、shape/clip相関、group opacity |
| 文字・COLRv1 | 全gradient・全28合成・参照glyph・variation・palette・foreground・clipを固定fontと参照画像で比較 |
| エフェクト・レイヤー | kernelと範囲、HDR/alpha、DPIとoffset、前後clip、ネスト、画面外からの影、透明背景・重なるパネルの置換 |
| RenderGraph | 単独/共有経路の画像一致、全資源宣言、重複import防止、Loadと部分更新の保持、culling・export・snapshot順序 |
| 失敗・寿命 | Append/Compile/Recordの失敗、待機キャンセル、一回実行、GPU使用終了後の解放、複数frameでの非上書き |

CPU準備時間、GPU時間、draw数、転送量、割り当て量、中間面の使用量を実装後に測定する。画像比較では色空間・誤差許容値を固定し、backend共通のfixtureを使用する。

## 別途決定する事項

- RenderGraphのsubresource/byte範囲、pool/alias、パス統合、複数queue、GPU timestamp、compile cache。
- storage textureによるcompute画像処理、LUT・畳み込み・変形・morphology・Bloom・履歴効果のAPIと品質。
- パスの三角形化、誤差・複雑度・メモリの具体的上限、mask/paintのcacheとatlas最適化。
- shaping、font fallback、OpenType outline/COLRv1 adapterのライブラリと公開API。
- HDR・広色域のCanvas出力、追加blend mode、シーングラフ、並列準備、frame pool。
