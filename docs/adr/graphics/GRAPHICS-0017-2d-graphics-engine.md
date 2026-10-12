# ADR-GRAPHICS-0017: 描画順を保持するコマンド記録型2Dグラフィックエンジン

- 状態: 採用
- 日付: 2026-10-10
- 更新: 2026-10-12

## 背景

図形、画像、文字の描画形式を既存Graphics APIの上で扱いたい。描画順・クリッピング・色の合成を共通化しつつ、命令ごとのGPU描画方式を外部実装へ差し替えられる必要がある。レイヤーや背景へのエフェクト適用では、画像処理の前後で描画順とclipの意味を保持する。

本ADRは2D描画の構成と実行契約を定め、公開APIと詳細契約・利用例を末尾にまとめる。共通画像エフェクトの設計とAPIは [GRAPHICS-0016](GRAPHICS-0016-image-effects.md) に分離し、本ADRでは2D側の適用方法を定める。実装と検証の範囲はPRに記録し、設計上の受け入れ条件と実測結果を区別する。

## 決定

### 1. 構成と責務

```text
Canvas2D → DrawList2D → Renderer2D ─────────────┐
                            └→ ImageEffects ─┤
                                             ↓
                                 RenderGraph → Graphics
```

| 構成要素 | 責務と依存 |
| --- | --- |
| `Lumyte.Graphics.Drawing2D` | CPU命令記録、描画順、幾何・paint、レイヤー、背景取得。Abstractions・ImageEffects・RenderGraphへ依存 |
| `Lumyte.Graphics.ImageEffects` | レイヤー・背景の画像処理を委譲する接続先。[GRAPHICS-0016](GRAPHICS-0016-image-effects.md) の契約に従う |
| `Lumyte.Graphics.RenderGraph` | パス依存、barrier、不要パスの除去、論理一時資源の確保と解放 |
| 各 `DependencyInjection` パッケージ | 標準実装の任意登録。本体からDIコンテナーへ依存させない |
| アプリケーション | device、外部資源、Submit・完了確認・Present、DI scopeの所有 |

新規ライブラリは `src/Graphics/` に配置し、プロジェクト・NuGet・namespaceを上記名称に揃える。backend具象型、Window、Input、Compositionには依存せず、shaderは既存Slang基盤で用意する。

RenderGraphの接続契約はmain `81bba9e` に統合された [GRAPHICS-0015](./GRAPHICS-0015-render-graph.md) を参照する。独自のパス実行基盤は追加せず、単独2D利用も私有RenderGraphで実行する。

レイアウト、ヒットテスト、画像デコード、ファイル資産管理、フォント探索・shaping・行分割、必須のシーングラフは対象外とする。

### 2. 命令と描画方式

CanvasはCPU上に不変のDrawListを作る。描画順を固定し、連続する同型・同一状態の範囲だけをバッチ化する。レイヤー・clip・隔離合成の境界を越えてまとめず、textureや透明度による全体ソートは行わない。

初期標準実装は、描画先を覆うtriangleを保守的なboundsのscissorで制限し、fragment shaderで形状とpaintを評価する。命令ごとにuploadと描画passを宣言し、同一バッチのpipelineを共有する。表はこの実装の方式を示す。

| 描画命令 | 初期標準実装の方式 |
| --- | --- |
| FillRect | fragmentで矩形内を判定し、共通paintを評価 |
| FillRoundedRect | fragmentで角丸矩形内を判定し、共通paintを評価 |
| FillCircle | 逆変換した座標で円内を判定し、共通paintを評価 |
| DrawLine | CPUでcapを含む輪郭を三角形へ展開し、fragmentで内外判定 |
| FillPath | CPUで曲線を分割し、fragmentでNonZero/EvenOddのwindingを評価 |
| StrokePath | CPUでcap・joinを含む輪郭を三角形へ展開し、fragmentで内外判定 |
| DrawImage | 描画矩形のRGBAをtexelごとにpremultiplyして補間・合成 |
| DrawCoverageMask | coverage textureと共通paintを評価 |
| DrawDistanceField | SDF/MSDF/MTSDFの輪郭を復元し、共通paintを評価 |
| DrawMesh | pixel中心でtriangleの内外・重心座標・頂点coverageを評価。輪郭AAは入力coverageに従う |
| DrawPaintGraph | gradient・画像pattern・path clip・隔離合成をshader内で評価 |

形状・clip・paint clipは同じ4×4 subpixel位置で評価し、相関を保つ。幾何coverageの精度は1/16刻みで、面積の厳密な解析積分ではない。DrawMeshの形状coverageだけはpixel中心の入力値とし、外側clipとpaintは同じsubpixel位置で評価する。曲線分割の画面上誤差は0.125 pixel以下とし、GPU準備はpaint深さ64、幾何・clipを含む展開命令256、データ262144個のVector4を上限とする。pathは65536点・edge、分割深さ24、round strokeの円弧分割4096を上限とする。上限超過とMaxAnisotropy > 1の指定は準備時に拒否し、黙って品質を落とさない。

退化gradientのうち、3点linearの投影分母が0、同心かつ同半径のradial、startAngle = endAngleのsweep、先頭・末尾stopのoffsetが等しいRepeat/Reflectは初期標準実装ではNotSupportedExceptionとする。通常のgradientと全28合成modeは同じpaint評価器で扱い、未対応の入力を別方式へ近似しない。

instancing、GPUでのtile化、三角形rasterizerを使うpath/mesh描画、computeでの動的path処理は今後の最適化とする。固定interfaceを差し替えて導入でき、命令やpaintの契約は維持する。

各命令は指定されたpaintとclipを含む処理全体を担当し、必要なら複数GPUパスを宣言する。FillPathだけを差し替えても、StrokePathの輪郭処理やDrawPaintGraph内部のpath処理には波及しない。

Save/Restore、変換、ClipRect/ClipPathはCPU状態操作である。BeginLayer/EndLayerとDrawBackdropEffectはホストが展開する構造命令とし、上記11命令の専用interfaceを増やさない。Canvas.Endは未完了のSave・レイヤーがあれば失敗し、Cancelは記録全体を破棄する。

### 3. 固定DIと外部実装

11命令それぞれに非genericの専用interfaceを設ける。`DrawingCommandRenderers2D` は全11実装をコンストラクターで受け取り、nullを拒否して型付きreadonly fieldへ保持する。DrawListの型付き領域と描画順の列からバッチを取り出し、専用interfaceを直接呼ぶ。

実装の選択は構築時に固定する。CanHandle、候補探索、自動fallback、描画中のDI解決・Type辞書検索は設けない。入力やGPU機能に対応できない場合は準備時に明示的に失敗する。interface呼び出し自体は残るため、インライン化や性能向上は実測前に保証しない。

DrawingCommandRenderers2D.CreateDefault()は登録表と11標準実装を毎回newし、DI・device・GPU資源を必要としない。UseGraphics2Dは11標準描画実装と描画登録表をTryAddScopedする。エフェクトはcompilerの拡張メソッドへ定義・rendererを直接渡して組み立て、DIへ登録しない。

外部実装は後続AddScopedの単一解決が優先され、先行登録と再Useも保持する。Graphicsへは解決済み登録表を通常のRenderer2Dコンストラクターへ渡す。

DI経路ではCreateDefaultを呼ばず、各interfaceをコンストラクターへ注入する。登録はprovider構築前に完了させ、既存provider・rendererの設定は後から変更しない。device・renderer・providerは描画登録拡張で生成・解決しない。compiler・効果定義・効果rendererは直接構築する。scopeはdevice単位とし、GPU使用、準備結果、rendererの解放後に破棄する。

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

ImagePatternは整数texelのSource、座標変換、軸別Clamp/Repeat/Mirror/Decal、Nearest/Linearを指定する。外部samplerを持たず、Linearの各tapへSource相対のtile規則を適用する。atlasの隣接画像を読まない。DrawImageもtexelごとのdecode・premultiply後に補間し、補間後のpremultiplyで代用しない。具体式と距離場のrange・padding契約は末尾の詳細契約で定める。

DrawGlyphRunと文字の描画形式選択は外部文字ライブラリが担当する。パス・mesh・coverage・距離場・RGBA・COLRv1をそれぞれ汎用命令へ渡す。COLRv1の3点linear、2円radial、sweep、重複stopの順序、参照glyph、変換・clip、全28合成modeを保持する。variation・CPAL・foreground・font unitsとY反転・cacheは文字側で解決する。

### 5. エフェクトを考慮したレイヤーと背景描画

画像演算は [GRAPHICS-0016](GRAPHICS-0016-image-effects.md) のImageEffectGraphとcompilerへ委譲する。2D側は画像化する内容と適用位置、clip・group opacity、背景snapshot、親への合成を管理する。PaintGraph2Dは図形内部の塗り、ImageEffectGraphは画像化後の処理として分け、paintの隔離合成をレイヤーの画像エフェクトへ置き換えない。

すべての効果をcompiler.Effect拡張メソッドで構築し、同じEffectsへ指定できる。2Dは効果の種類を固定せず、定義が返す出力範囲と必要入力範囲を使ってレイヤー合成と背景取得を行う。独自効果を追加しても11描画命令やレイヤーAPIの変更は不要であり、必要なrendererを構築時に結び付けたgraphを利用者が渡す。

レイヤーと背景はDisplayLinearのlinear premultiplied Rgba16Float画像として、原点と有限範囲を保って渡す。coverageや距離場は先に色画像へ描画し、SDF値自体をfilterへ渡さない。効果で拡張された出力範囲を親の描画先へ戻し、背景取得にはGetRequiredSourceBoundsで求めた必要入力範囲を使う。エフェクトのpassは2Dと同じRenderGraphへ追加し、単独利用でも共有graphでも描画順と資源依存を保持する。

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

単独利用のPrepareAsyncと共有graphへのAppendPassesAsyncを用意する。公開APIと命令別interfaceは本ADR末尾に示す。

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
| CPU snapshot、pipeline、argument table、upload、私有shader-dataとエフェクト準備owner | PreparedDrawingPasses2D |
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
| paintとレイヤーの画像エフェクトを同じgraphで表す | 塗りのcoverageと画像化前後のclip・描画順の責務が混在する |
| CanHandle・優先順位による方式探索 | 命令ごとに指定した実装が固定されない |
| 単色専用命令とpaint種別ごとのDI登録 | 図形とpaintの組み合わせでAPI・登録が増える |
| GPU処理をRecordだけへ隠す | RenderGraphから依存と一時資源を解析できない |
| 独自のGPU backend・パス実行器 | 既存Graphics・RenderGraphの契約と重複する |
| texture順の全体ソート・描画ごとのSubmit | 半透明の順序とアプリ側のGPU実行管理を崩す |
| 文字を単色atlasや平坦なpath列に限定 | 外部の形式選択とCOLRv1の隔離合成を保持できない |

## 結果と影響

図形・画像・外部文字描画が同じpaintと描画順の契約を共有し、命令別の実装をDIでも手動でも差し替えられる。レイヤーと背景へ共通エフェクトを適用し、GPU処理をRenderGraphへまとめられる。

一方、順序保持によって全体ソートほどバッチを減らせない場合があり、複雑なpaint・clip・エフェクトでは中間画像と複数passが必要になる。準備ownerとcompiled planの寿命、全アクセスの宣言、追加失敗時のgraph再構築を利用側が管理する。描画命令の追加時にはinterface、登録表、標準実装、CreateDefault、DI登録を同時に更新する。

既存GraphicsのAPIは変更しない。GPU実装はRenderGraphの採用・契約に依存し、初期のwhole-allocation同期と単一queueの制約に従う。

## 検証方針

| 領域 | 受け入れ条件 |
| --- | --- |
| 命令・DI | 11描画命令の登録対応とエフェクトの直接構築、後勝ち・再Use・CreateDefault、配送順と独立した差し替え、描画登録表構築時のGPU生成と描画中のDI解決がないこと |
| paint・画像 | 6命令のColor/Solid等価性、gradientの退化・stop・extend、tap別tileと負座標、透明境界の補間、shape/clip相関、group opacity |
| 文字・COLRv1 | 全gradient・全28合成・参照glyph・variation・palette・foreground・clipを固定fontと参照画像で比較 |
| エフェクトへの接続・レイヤー | DisplayLinear画像の受け渡し、DPIとoffset、前後clip、group opacity、ネスト、画面外からの影、必要入力の背景取得、透明背景・重なるパネルの置換 |
| RenderGraph | 単独/共有経路の画像一致、全資源宣言、重複import防止、Loadと部分更新の保持、culling・export・snapshot順序 |
| 失敗・寿命 | Append/Compile/Recordの失敗、待機キャンセル、一回実行、GPU使用終了後の解放、複数frameでの非上書き |

CPU準備時間、GPU時間、draw数、転送量、割り当て量、中間面の使用量を実装後に測定する。画像比較では色空間・誤差許容値を固定し、backend共通のfixtureを使用する。

## 別途決定する事項

- RenderGraphのsubresource/byte範囲、pool/alias、パス統合、複数queue、GPU timestamp、compile cache。
- instancing、pathのtile化・三角形rasterizerの利用、mask/paintのcacheとatlas最適化。
- shaping、font fallback、OpenType outline/COLRv1 adapterのライブラリと公開API。
- HDR・広色域のCanvas出力、追加blend mode、シーングラフ、並列準備、frame pool。

## API・詳細契約・利用例

比較元は先行エフェクトブランチ `feat/image-effects` のrevision `d79a127`。以下の4つの差分は追加する公開メンバーの宣言であり、同じ型の宣言は一つの公開型へ統合する。Graphics型は `Lumyte.Graphics.Abstractions`、行列・ベクトルは `System.Numerics`、DI型は `Microsoft.Extensions.DependencyInjection` を使用する。

RenderGraph型は先行ブランチが基づくmain `81bba9e` の公開APIを使用する。`RenderGraphBuilder` は `using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;` のaliasであり、新規型ではない。相互参照するDrawing2D・ImageEffects・RenderGraphの型はそれぞれの名前空間を使用する。ImageEffectGraph・ImageEffectDefinition・型付きrendererの宣言と契約は [GRAPHICS-0016](GRAPHICS-0016-image-effects.md) を参照する。

### 公開API

#### 図形・paint・Canvas・Renderer

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
+    public enum PathVerb2D { Move, Line, Quadratic, Cubic, Close }
+    // Move/LineはP0、Quadraticはcontrol=P0/end=P1、Cubicはcontrols=P0/P1/end=P2。
+    public readonly record struct PathSegment2D(PathVerb2D Verb, Vector2 P0, Vector2 P1, Vector2 P2);
+    public sealed class Path2D
+    {
+        // 外部描画実装も読み取れる不変のsnapshot。変更可能な配列を公開しない。
+        public IReadOnlyList<PathSegment2D> Segments { get; }
+    }
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
+    public enum AlphaRepresentation2D { Straight, Premultiplied }
+    // 単一mip/layerのSampled D2 viewとsamplerを借用。同一deviceを利用者が保証。
+    // sRGB formatはsample時にdecode。Premultipliedはdecode後にRGBがalpha乗算済みとなる表現。
+    public sealed class Image2D
+    {
+        public Image2D(IGraphicsTextureView view, IGraphicsSampler sampler,
+            AlphaRepresentation2D alpha = AlphaRepresentation2D.Straight);
+        public AlphaRepresentation2D Alpha { get; }
+        public IGraphicsTextureView View { get; }
+        public IGraphicsSampler Sampler { get; }
+    }
+    // source領域を基準に各軸を独立して延長する。Decalの範囲外は透明。
+    public enum ImageTileMode2D { Clamp, Repeat, Mirror, Decal }
+    public enum ImageFilter2D { Nearest, Linear }
+    // samplerを持たない画像paint記述。factoryが値を検証・snapshot化する。
+    public sealed record ImagePattern2D
+    {
+        // 単一mip/layerのSampled D2 viewを借用。描画deviceとの一致は利用者が保証。
+        public required IGraphicsTextureView View { get; init; }
+        // viewの選択mip内のtexel矩形。全成分整数、幅・高さは正、範囲外は拒否。
+        public required Rect2D Source { get; init; }
+        public AlphaRepresentation2D Alpha { get; init; } = AlphaRepresentation2D.Straight;
+        // source左上を原点とするtexel座標→paintローカル座標。逆行列も含めfiniteかつ逆変換可能。
+        public Matrix3x2 PatternToLocal { get; init; } = Matrix3x2.Identity;
+        public ImageTileMode2D TileX { get; init; } = ImageTileMode2D.Repeat;
+        public ImageTileMode2D TileY { get; init; } = ImageTileMode2D.Repeat;
+        public ImageFilter2D Filter { get; init; } = ImageFilter2D.Linear;
+    }
+    public sealed class CoverageImage2D
+    {
+        // R8UnormのRを[0,1]のcoverageとして読む。view/samplerは同一deviceから借用。
+        public CoverageImage2D(IGraphicsTextureView view, IGraphicsSampler sampler);
+        public IGraphicsTextureView View { get; }
+        public IGraphicsSampler Sampler { get; }
+    }
+    public enum DistanceFieldEncoding2D { Sdf, Msdf, Mtsdf }
+    public sealed class DistanceField2D
+    {
+        // データtexture。sRGB形式は拒否。SDFはR8、MSDF/MTSDFはRGBA8を使用。
+        // texel距離=(sample-0.5)*distanceRange、内側正。rangeはfiniteかつ正数。
+        public DistanceField2D(IGraphicsTextureView view, IGraphicsSampler sampler,
+            DistanceFieldEncoding2D encoding, float distanceRange);
+        public IGraphicsTextureView View { get; }
+        public IGraphicsSampler Sampler { get; }
+        public DistanceFieldEncoding2D Encoding { get; }
+        public float DistanceRange { get; }
+    }
+    // Positionは論理座標、Coverageは[0,1]。境界AA済みなら必要な頂点を利用者が供給する。
+    public readonly record struct MeshVertex2D(Vector2 Position, float Coverage);
+    public sealed class Mesh2D
+    {
+        // 両配列をコピー。index数は3の倍数、各indexはvertices内。
+        public Mesh2D(ReadOnlySpan<MeshVertex2D> vertices, ReadOnlySpan<uint> indices);
+        public IReadOnlyList<MeshVertex2D> Vertices { get; }
+        public IReadOnlyList<uint> Indices { get; }
+    }
+    public enum ExtendMode2D { Pad, Repeat, Reflect }
+    // COLRv1の全CompositeModeを表す。通常のCanvas合成とは独立する。
+    public enum CompositeMode2D
+    {
+        Clear, Src, Dest, SrcOver, DestOver, SrcIn, DestIn, SrcOut, DestOut,
+        SrcAtop, DestAtop, Xor, Plus, Screen, Overlay, Darken, Lighten,
+        ColorDodge, ColorBurn, HardLight, SoftLight, Difference, Exclusion,
+        Multiply, HslHue, HslSaturation, HslColor, HslLuminosity
+    }
+    // offsetはfinite。入力順は非減少、同一offsetのstopも順序を維持する。
+    public readonly record struct ColorStop2D(float Offset, Color2D Color);
+    // パスの集合とpaintのDAG。生成後は不変、子列とstop列はコピーする。
+    // constructorを非公開とし、factoryのみで生成して循環を防ぐ。
+    public sealed class PaintGraph2D
+    {
+        // 外部実装が解析できる不変の公開node tree。factoryが値と子を検証する。
+        public PaintNode2D Root { get; }
+        // 無限平面のpaint。実際の評価範囲はpath、clip、targetで制限する。
+        public static PaintGraph2D Solid(Color2D color);
+        // 一般的な2点linear。finiteかつ異なる2点。既存3点nodeへ正規化する。
+        public static PaintGraph2D LinearGradient(Vector2 start, Vector2 end,
+            ReadOnlySpan<ColorStop2D> stops, ExtendMode2D extend = ExtendMode2D.Pad);
+        // p0/p1/p2はCOLRv1の3点定義。p2を省略して2点へ縮約しない。
+        public static PaintGraph2D LinearGradient(Vector2 p0, Vector2 p1, Vector2 p2,
+            ReadOnlySpan<ColorStop2D> stops, ExtendMode2D extend);
+        // 2円定義。radiusはfiniteかつ0以上。単一中心へ縮約しない。
+        public static PaintGraph2D RadialGradient(Vector2 c0, float r0, Vector2 c1, float r1,
+            ReadOnlySpan<ColorStop2D> stops, ExtendMode2D extend);
+        // 中心から外周へのradial。radiusはfiniteかつ正数。同心の2円nodeへ正規化する。
+        public static PaintGraph2D RadialGradient(Vector2 center, float radius,
+            ReadOnlySpan<ColorStop2D> stops, ExtendMode2D extend = ExtendMode2D.Pad);
+        // angleはradian、+X基準、Y下向きで時計回り。入力adapterで変換する。
+        public static PaintGraph2D SweepGradient(Vector2 center, float startAngle,
+            float endAngle, ReadOnlySpan<ColorStop2D> stops, ExtendMode2D extend);
+        // 設定をsnapshot化しviewを借用。GPU資源を確保しない。
+        public static PaintGraph2D ImagePattern(ImagePattern2D pattern);
+        // 子graphの合成後のpremultiplied RGBA全体を倍率変更。finiteかつ[0,1]。
+        public static PaintGraph2D Opacity(float opacity, PaintGraph2D child);
+        // paintをpath coverageで切り抜く。paintとpathは同じローカル座標系。
+        public static PaintGraph2D Clip(Path2D path, PaintGraph2D paint,
+            FillRule2D rule = FillRule2D.NonZero);
+        // 子graph全体（geometryとpaint）へ変換を適用する。
+        public static PaintGraph2D Transform(Matrix3x2 transform, PaintGraph2D child);
+        // 配列の先頭からSrcOverで合成。空列は透明。
+        public static PaintGraph2D Layers(ReadOnlySpan<PaintGraph2D> children);
+        // sourceとbackdropを独立した透明面へ評価してから、指定modeで合成。
+        public static PaintGraph2D Composite(PaintGraph2D source,
+            PaintGraph2D backdrop, CompositeMode2D mode);
+    }
+    // 公開nodeは読み取り用のsnapshot。直接生成したnodeをgraphへ挿入するAPIは持たない。
+    public abstract record PaintNode2D;
+    public sealed record SolidPaintNode2D(Color2D Color) : PaintNode2D;
+    public sealed record LinearPaintNode2D(Vector2 P0, Vector2 P1, Vector2 P2,
+        IReadOnlyList<ColorStop2D> Stops, ExtendMode2D Extend) : PaintNode2D;
+    public sealed record RadialPaintNode2D(Vector2 C0, float R0, Vector2 C1, float R1,
+        IReadOnlyList<ColorStop2D> Stops, ExtendMode2D Extend) : PaintNode2D;
+    public sealed record SweepPaintNode2D(Vector2 Center, float StartAngle, float EndAngle,
+        IReadOnlyList<ColorStop2D> Stops, ExtendMode2D Extend) : PaintNode2D;
+    public sealed record ImagePaintNode2D(ImagePattern2D Pattern) : PaintNode2D;
+    public sealed record OpacityPaintNode2D(float Opacity, PaintNode2D Child) : PaintNode2D;
+    public sealed record ClipPaintNode2D(Path2D Path, FillRule2D Rule, PaintNode2D Child) : PaintNode2D;
+    public sealed record TransformPaintNode2D(Matrix3x2 Transform, PaintNode2D Child) : PaintNode2D;
+    public sealed record LayersPaintNode2D(IReadOnlyList<PaintNode2D> Children) : PaintNode2D;
+    public sealed record CompositePaintNode2D(PaintNode2D Source, PaintNode2D Backdrop,
+        CompositeMode2D Mode) : PaintNode2D;
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
+        // Color2D版はSolid(color)として同じ命令を記録する。以下の塗り・線も同様。
+        public void FillRect(Rect2D rect, Color2D color);
+        public void FillRect(Rect2D rect, PaintGraph2D paint);
+        // radiusはfiniteかつ0以上。描画時に短辺の半分まで縮める。
+        public void FillRoundedRect(Rect2D rect, float radius, Color2D color);
+        public void FillRoundedRect(Rect2D rect, float radius, PaintGraph2D paint);
+        public void FillCircle(Vector2 center, float radius, Color2D color);
+        public void FillCircle(Vector2 center, float radius, PaintGraph2D paint);
+        public void DrawLine(Vector2 start, Vector2 end, Color2D color, StrokeStyle2D style);
+        public void DrawLine(Vector2 start, Vector2 end, PaintGraph2D paint, StrokeStyle2D style);
+        public void FillPath(Path2D path, Color2D color, FillRule2D rule = FillRule2D.NonZero);
+        public void FillPath(Path2D path, PaintGraph2D paint, FillRule2D rule = FillRule2D.NonZero);
+        public void StrokePath(Path2D path, Color2D color, StrokeStyle2D style);
+        public void StrokePath(Path2D path, PaintGraph2D paint, StrokeStyle2D style);
+        // sourceはview内のtexel座標、destinationは論理座標。
+        public void DrawImage(Image2D image, Rect2D source, Rect2D destination);
+        // 不変graphを記録。補間・合成は線形光RGBで評価する。
+        public void DrawPaintGraph(PaintGraph2D graph);
+        // coverageは色空間変換しない。paintの座標はCanvasの論理座標。
+        public void DrawCoverageMask(CoverageImage2D mask, Rect2D source,
+            Rect2D destination, PaintGraph2D paint);
+        // SDF/MSDF/MTSDFをcoverageへ復元し、paintを適用する。
+        public void DrawDistanceField(DistanceField2D field, Rect2D source,
+            Rect2D destination, PaintGraph2D paint);
+        // 外部で三角形化済みのデータを描く。
+        public void DrawMesh(Mesh2D mesh, PaintGraph2D paint);
+        // Save/Restoreの対応を検証し、不変のCPUコマンド列を返す。GPU実行なし。
+        public DrawList2D End();
+        // 記録中のCPUコマンドを破棄し、再びBeginできる状態へ戻す。
+        public void Cancel();
+    }
+    // 再使用可能なCPU snapshot。参照する画像のGPU所有権は持たない。
+    public sealed class DrawList2D { }
+    public sealed record RenderTarget2D
+    {
+        // 同一device、親textureも単一sample/mip/layerのD2、RenderAttachment view。
+        // 背景エフェクトを含む場合は親textureのSampled用途も必須。
+        // 初期対応はRgba8SrgbとRgba16Float。寸法はviewから取得する。
+        // Rgba16Floatも初期2Dではdisplay-linearのpremultiplied値を保持する。
+        public required IGraphicsTextureView View { get; init; }
+        // 論理座標から物理pixelへの倍率。finiteかつ正数、既定値1。
+        public float PixelScale { get; init; } = 1;
+        public AttachmentLoadOp LoadOp { get; init; } = AttachmentLoadOp.Load;
+        public Color2D ClearColor { get; init; }
+    }
+    public sealed record RenderTargetBinding2D
+    {
+        // 同じgraphのtexture。Descriptionはallocationの実際の宣言と一致させる。
+        public required RenderGraphTexture Texture { get; init; }
+        public required TextureDesc Description { get; init; }
+        // 初期targetはallocation全体の単一mip/layer。このrange以外は未対応。
+        public TextureSubresourceRange Range { get; init; } = new(0, 1, 0, 1);
+        public float PixelScale { get; init; } = 1;
+        // この画像の左上が、ルート描画先の物理pixel座標のどこにあるか。finiteな整数。
+        public Vector2 PixelOrigin { get; init; }
+        public AttachmentLoadOp LoadOp { get; init; } = AttachmentLoadOp.Load;
+        public Color2D ClearColor { get; init; }
+    }
+    // 外部画像は呼び出し側が一度だけimportし、同じallocationのhandleを共有する。
+    public sealed class DrawingGraphBindings2D
+    {
+        public DrawingGraphBindings2D(RenderGraphBuilder graph);
+        // 同じtexture/handleの再指定は許可、既存対応と異なる再指定は拒否。
+        // 正しいallocationとhandleの対応・同一device・寿命は利用者が保証する。
+        public void BindTexture(IGraphicsTexture texture, RenderGraphTexture resource);
+        // 未登録は準備時に拒否。viewは親textureからこの対応を引く。
+        public RenderGraphTexture GetTexture(IGraphicsTexture texture);
+    }
+    public sealed class Renderer2D : IDisposable
+    {
+        // deviceは借用。2引数版は提供するOpacity rendererをグループ透明度に使用する。
+        public Renderer2D(IGraphicDevice device, DrawingCommandRenderers2D renderers);
+        // グループ透明度用rendererは借用。各Effects graphのrendererはgraph構築時に固定。
+        public Renderer2D(IGraphicDevice device, DrawingCommandRenderers2D renderers,
+            IImageEffectRenderer<OpacityEffect> groupOpacity);
+        // 単独実行の便利API。私有graphへimport→AppendPassesAsync→Compileを行う。
+        // submit/waitは行わない。キャンセル時は内部確保を解放する。
+        public ValueTask<PreparedDraw2D> PrepareAsync(DrawList2D list,
+            RenderTarget2D target, CancellationToken cancellationToken = default);
+        // 共通graphへ追加。物理target viewは不要。返却ownerをGPU完了まで保持する。
+        // 失敗時はgraph全体を再構築。同時追加・追加中のCompileは禁止。
+        public ValueTask<PreparedDrawingPasses2D> AppendPassesAsync(RenderGraphBuilder graph,
+            DrawList2D list, RenderTargetBinding2D target, DrawingGraphBindings2D bindings,
+            CancellationToken cancellationToken = default);
+        // 利用者は全準備・GPU使用・準備結果の解放を先に完了させる。暗黙のwaitなし。
+        public void Dispose();
+    }
+    public sealed class PreparedDrawingPasses2D : IDisposable
+    {
+        // callbackのCPU snapshot・pipeline・table・upload等を所有。graphは所有しない。
+        // 一つのcompiled planで一回だけ実行。GPU使用終了と記録先の破棄後に呼ぶ。
+        public void Dispose();
+    }
+    public sealed class PreparedDraw2D : IDisposable
+    {
+        // 一度だけ。Recordingかつactive passなしの同一device commandに記録する。
+        // 内部copy/compute/barrier/render passを記録し、active passを残さない。
+        public void Record(IGraphicsCommandBuffer commands);
+        // GPU完了→commands破棄後に呼ぶ。CompiledRenderGraphと準備ownerを解放する。
+        // Disposeから完了照会・waitを行わず、寿命違反の検出を保証しない。idempotent。
+        public void Dispose();
+    }
+}
```

#### 命令snapshotと固定レンダラー

全11命令は専用interfaceへ配送し、登録表の構築後に方式を探索しない。命令内のpaint評価も、その命令へ指定した実装が担当する。

```diff
+namespace Lumyte.Graphics.Drawing2D
+{
+    // ClipRectも矩形のPath2Dへ正規化して記録。呼び出し時の変換を固定する。
+    public sealed record ClipElement2D(Path2D Path, Matrix3x2 Transform, FillRule2D Rule);
+    public sealed class ClipStack2D
+    {
+        // 論理的な積集合を表す不変列。各要素の変換は独立して保持する。
+        public IReadOnlyList<ClipElement2D> Elements { get; }
+    }
+    public sealed record DrawingState2D(Matrix3x2 Transform, ClipStack2D Clip);
+    public abstract record DrawingCommand2D(DrawingState2D State);
+    // Color2D overloadもSolidのPaintへ正規化。paintの種類で命令型を分けない。
+    public sealed record FillRectCommand2D(Rect2D Rect, PaintGraph2D Paint,
+        DrawingState2D State) : DrawingCommand2D(State);
+    public sealed record FillRoundedRectCommand2D(Rect2D Rect, float Radius, PaintGraph2D Paint,
+        DrawingState2D State) : DrawingCommand2D(State);
+    public sealed record FillCircleCommand2D(Vector2 Center, float Radius, PaintGraph2D Paint,
+        DrawingState2D State) : DrawingCommand2D(State);
+    public sealed record DrawLineCommand2D(Vector2 Start, Vector2 End, PaintGraph2D Paint,
+        StrokeStyle2D Style, DrawingState2D State) : DrawingCommand2D(State);
+    public sealed record FillPathCommand2D(Path2D Path, PaintGraph2D Paint, FillRule2D Rule,
+        DrawingState2D State) : DrawingCommand2D(State);
+    public sealed record StrokePathCommand2D(Path2D Path, PaintGraph2D Paint, StrokeStyle2D Style,
+        DrawingState2D State) : DrawingCommand2D(State);
+    public sealed record DrawImageCommand2D(Image2D Image, Rect2D Source, Rect2D Destination,
+        DrawingState2D State) : DrawingCommand2D(State);
+    public sealed record DrawCoverageMaskCommand2D(CoverageImage2D Mask, Rect2D Source,
+        Rect2D Destination, PaintGraph2D Paint, DrawingState2D State) : DrawingCommand2D(State);
+    public sealed record DrawDistanceFieldCommand2D(DistanceField2D Field, Rect2D Source,
+        Rect2D Destination, PaintGraph2D Paint, DrawingState2D State) : DrawingCommand2D(State);
+    public sealed record DrawMeshCommand2D(Mesh2D Mesh, PaintGraph2D Paint,
+        DrawingState2D State) : DrawingCommand2D(State);
+    public sealed record DrawPaintGraphCommand2D(PaintGraph2D Graph,
+        DrawingState2D State) : DrawingCommand2D(State);
+    public sealed class RenderPreparationContext2D
+    {
+        // いずれも借用。pass追加はbatch.AppendPassesで行う。
+        public IGraphicDevice Device { get; }
+        public RenderGraphBuilder Graph { get; }
+        public DrawingGraphBindings2D Bindings { get; }
+        // Loadへ正規化。ホストが必要な初回clearのpassを先に宣言する。
+        public RenderTargetBinding2D Target { get; }
+    }
+    // 以下の全PrepareAsyncに共通する契約:
+    // 同型・同一clip/合成境界の連続命令。並べ替え不可。snapshotは準備結果の寿命まで有効。
+    // CPU処理とpass追加に必要なsnapshot・固定資源を準備する。submit/waitは禁止。
+    // 一時texture/bufferはAppendPassesで論理宣言し、物理確保は実行器へ任せる。
+    public interface IFillRectRenderer2D
+    {
+        ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context,
+            ReadOnlyMemory<FillRectCommand2D> commands, CancellationToken cancellationToken = default);
+    }
+    public interface IFillRoundedRectRenderer2D
+    {
+        ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context,
+            ReadOnlyMemory<FillRoundedRectCommand2D> commands, CancellationToken cancellationToken = default);
+    }
+    public interface IFillCircleRenderer2D
+    {
+        ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context,
+            ReadOnlyMemory<FillCircleCommand2D> commands, CancellationToken cancellationToken = default);
+    }
+    public interface IDrawLineRenderer2D
+    {
+        ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context,
+            ReadOnlyMemory<DrawLineCommand2D> commands, CancellationToken cancellationToken = default);
+    }
+    public interface IFillPathRenderer2D
+    {
+        ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context,
+            ReadOnlyMemory<FillPathCommand2D> commands, CancellationToken cancellationToken = default);
+    }
+    public interface IStrokePathRenderer2D
+    {
+        ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context,
+            ReadOnlyMemory<StrokePathCommand2D> commands, CancellationToken cancellationToken = default);
+    }
+    public interface IDrawImageRenderer2D
+    {
+        ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context,
+            ReadOnlyMemory<DrawImageCommand2D> commands, CancellationToken cancellationToken = default);
+    }
+    public interface IDrawCoverageMaskRenderer2D
+    {
+        ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context,
+            ReadOnlyMemory<DrawCoverageMaskCommand2D> commands, CancellationToken cancellationToken = default);
+    }
+    public interface IDrawDistanceFieldRenderer2D
+    {
+        ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context,
+            ReadOnlyMemory<DrawDistanceFieldCommand2D> commands, CancellationToken cancellationToken = default);
+    }
+    public interface IDrawMeshRenderer2D
+    {
+        ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context,
+            ReadOnlyMemory<DrawMeshCommand2D> commands, CancellationToken cancellationToken = default);
+    }
+    public interface IDrawPaintGraphRenderer2D
+    {
+        ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context,
+            ReadOnlyMemory<DrawPaintGraphCommand2D> commands, CancellationToken cancellationToken = default);
+    }
+    public interface IPreparedCommandBatch2D : IDisposable
+    {
+        // 準備時と同じgraph/targetへ一度だけpassとアクセスを宣言する。
+        // target更新順序を保つ。barrier・一時資源はgraph、Submitはアプリが担当。
+        void AppendPasses(RenderGraphBuilder graph, RenderTargetBinding2D target);
+        // PreparedDrawingPasses2Dが所有。graphの一時textureを直接Disposeしない。
+        void Dispose();
+    }
+    public sealed class DrawingCommandRenderers2D
+    {
+        // DIなしで全11標準実装を生成し、下記constructorへ渡した新しい登録表を返す。
+        // 呼び出すたびに表と各実装を新規生成。device・GPU資源・DIサービスは生成しない。
+        public static DrawingCommandRenderers2D CreateDefault();
+        // 全実装を借用し、型付きreadonly fieldへ保持。nullは拒否。
+        public DrawingCommandRenderers2D(
+            IFillRectRenderer2D fillRect,
+            IFillRoundedRectRenderer2D fillRoundedRect,
+            IFillCircleRenderer2D fillCircle,
+            IDrawLineRenderer2D drawLine,
+            IFillPathRenderer2D fillPath,
+            IStrokePathRenderer2D strokePath,
+            IDrawImageRenderer2D drawImage,
+            IDrawCoverageMaskRenderer2D drawCoverageMask,
+            IDrawDistanceFieldRenderer2D drawDistanceField,
+            IDrawMeshRenderer2D drawMesh,
+            IDrawPaintGraphRenderer2D drawPaintGraph);
+        // 構築時に指定された実装をそのまま返す。探索・再解決なし。
+        public IFillRectRenderer2D FillRect { get; }
+        public IFillRoundedRectRenderer2D FillRoundedRect { get; }
+        public IFillCircleRenderer2D FillCircle { get; }
+        public IDrawLineRenderer2D DrawLine { get; }
+        public IFillPathRenderer2D FillPath { get; }
+        public IStrokePathRenderer2D StrokePath { get; }
+        public IDrawImageRenderer2D DrawImage { get; }
+        public IDrawCoverageMaskRenderer2D DrawCoverageMask { get; }
+        public IDrawDistanceFieldRenderer2D DrawDistanceField { get; }
+        public IDrawMeshRenderer2D DrawMesh { get; }
+        public IDrawPaintGraphRenderer2D DrawPaintGraph { get; }
+    }
+}
```

#### 標準描画実装とDI登録

標準クラスのPrepareAsyncは対応interfaceの明示実装とする。UseGraphics2Dは全11描画組とDrawingCommandRenderers2DだけをTryAddScopedで登録する。IGraphicDevice・Renderer2D・providerは登録・生成・解決しない。

```diff
+namespace Lumyte.Graphics.Drawing2D
+{
+    public sealed class DefaultFillRectRenderer2D : IFillRectRenderer2D
+    {
+        public DefaultFillRectRenderer2D();
+    }
+    public sealed class DefaultFillRoundedRectRenderer2D : IFillRoundedRectRenderer2D
+    {
+        public DefaultFillRoundedRectRenderer2D();
+    }
+    public sealed class DefaultFillCircleRenderer2D : IFillCircleRenderer2D
+    {
+        public DefaultFillCircleRenderer2D();
+    }
+    public sealed class DefaultDrawLineRenderer2D : IDrawLineRenderer2D
+    {
+        public DefaultDrawLineRenderer2D();
+    }
+    public sealed class DefaultFillPathRenderer2D : IFillPathRenderer2D
+    {
+        public DefaultFillPathRenderer2D();
+    }
+    public sealed class DefaultStrokePathRenderer2D : IStrokePathRenderer2D
+    {
+        public DefaultStrokePathRenderer2D();
+    }
+    public sealed class DefaultDrawImageRenderer2D : IDrawImageRenderer2D
+    {
+        public DefaultDrawImageRenderer2D();
+    }
+    public sealed class DefaultDrawCoverageMaskRenderer2D : IDrawCoverageMaskRenderer2D
+    {
+        public DefaultDrawCoverageMaskRenderer2D();
+    }
+    public sealed class DefaultDrawDistanceFieldRenderer2D : IDrawDistanceFieldRenderer2D
+    {
+        public DefaultDrawDistanceFieldRenderer2D();
+    }
+    public sealed class DefaultDrawMeshRenderer2D : IDrawMeshRenderer2D
+    {
+        public DefaultDrawMeshRenderer2D();
+    }
+    public sealed class DefaultDrawPaintGraphRenderer2D : IDrawPaintGraphRenderer2D
+    {
+        public DefaultDrawPaintGraphRenderer2D();
+    }
+}
+namespace Lumyte.Graphics.Drawing2D.DependencyInjection
+{
+    public static class Graphics2DServiceCollectionExtensions
+    {
+        // 全11描画interfaceと描画登録表だけをTryAddScopedで登録。effectは登録しない。
+        // 未登録分だけを補完し、再呼び出しで既存の上書きを消さない。
+        // servicesのnullは拒否。provider生成やサービス解決は行わず、同じservicesを返す。
+        public static IServiceCollection UseGraphics2D(this IServiceCollection services);
+    }
+}
```

#### 2Dレイヤーと背景エフェクト

Canvas2Dへ追加するメンバーを以下に示す。BeginLayer・EndLayer・DrawBackdropEffectはホストが扱う構造命令であり、11描画命令のinterfaceを増やさない。

```diff
+namespace Lumyte.Graphics.Drawing2D
+{
+    public sealed record LayerOptions2D
+    {
+        // BeginLayer時のローカル座標。効果前の内容をこの有限矩形にclipする。
+        public required Rect2D ContentBounds { get; init; }
+        public ImageEffectGraph Effects { get; init; } = ImageEffectGraph.Source();
+        // エフェクトの後にグループ全体へ一度適用。finiteかつ[0,1]。
+        public float Opacity { get; init; } = 1;
+        // 指定時は効果後の出力だけをclip。BeginLayer時のローカル座標で固定。
+        public Rect2D? OutputClip { get; init; }
+    }
+    public sealed record BackdropEffectOptions2D
+    {
+        // 背景への効果を表示する矩形。必要入力のsnapshot領域とは別。
+        public required Rect2D OutputBounds { get; init; }
+        public required ImageEffectGraph Effects { get; init; }
+    }
+    public sealed class Canvas2D
+    {
+        public void BeginLayer(LayerOptions2D options);
+        public void EndLayer();
+        public void DrawBackdropEffect(BackdropEffectOptions2D options);
+    }
+}
```

### 詳細契約

#### 座標・塗り・coverage

CanvasはBegin中だけ操作でき、入力不備では命令・状態を変更しない。Endの対応関係検証に失敗した場合は記録を維持する。End成功またはCancel後は新たにBeginできる。PathBuilderはMoveTo前の線・曲線・Closeを拒否する。

左上原点で右がX正、下がY正、正の回転は時計回りとする。`targetPixel = Transform(local, CanvasTransform) * PixelScale - PixelOrigin`。中間面もルート描画先のpixel原点を保持し、単独ルートのPixelOriginは0とする。paintは変換前の論理座標で評価し、形状boundsへ自動伸縮しない。Canvas変換は形状とpaintへ、PaintGraph.Transformは子paintのgeometryを含む全体へ作用する。

2点linearは `p0=start, p1=end, p2=start+perp(end-start)`、`perp(x,y)=(-y,x)` へ正規化する。単一中心radialは `c0=c1=center, r0=0, r1=radius` とする。便利overloadは非退化入力を要求し、一般の3点linear・2円radialの退化規則を変えない。

形状はpaintの外側の幾何clipとし、Canvas clipとgraph Clipとの相関を維持する。同じ幾何評価段階では、独立に平均化したAA coverageの乗算を避け、共通sampleまたは幾何的交差を評価する。外側coverageをLayersの各子へ分配しない。PaintGraph.Opacityは子全体の合成後のpremultiplied RGBAへ一度適用し、Opacity(0.5)を掛けた不透明Layersの重なりもalpha=0.5とする。

DrawCoverageMaskはsample後の[0,1]値をcoverageとする。SDFはR、MSDFはRGBの中央値を輪郭距離とし、MTSDFはRGBの輪郭距離とAのtrue signed distanceを保持する。range・符号・paddingを供給側と一致させ、画面上のpixel幅でAAを評価する。mask・距離場のatlas paddingとsource範囲は供給側が確保する。DrawMeshは頂点Coverageを補間し、境界用頂点のないmeshへ自動AAを仮定しない。

非整数座標を保持し、pixelへの丸めはscissor範囲の計算に限定する。ClipRectは変換後に軸平行かつ整数pixel境界の場合だけscissorを使い、小数・回転・せん断を含むclipはcoverageを評価する。scissorをtarget内へ収め、空の形状・clip・変換後の無面積形状は描画しない。未対応clipを境界矩形へ置き換えずNotSupportedExceptionとする。曲線分割はCanvas変換とPixelScale適用後の画面上誤差を基準とし、現GraphicsにないMSAA/resolveを必須にしない。

通常合成はpremultiplied source-overで、RGB・alphaともsource factor=One、destination factor=OneMinusSourceAlphaとする。clear値も線形RGBへ変換し、sRGB targetへのencodeはattachmentに任せる。Loadする既存内容もdisplay-linearのpremultiplied表現を満たす。色画像とcoverage・距離場のデータtextureを混同しない。

#### 画像パターンのsampling

ImagePattern2Dは外部samplerを持たず、FilterとTileX/TileYを唯一の設定元とする。Image2DからはViewとAlphaを渡す。Sourceは選択mip内の整数texel矩形で、正の寸法を持ちview内に収まる。source左上を原点(0,0)、texel中心を(i+0.5,j+0.5)、周期をsource寸法とし、PatternToLocalの逆変換でsampleする。factoryは未知のenum、非finite値、逆変換不能・逆行列が非finiteとなる行列を拒否する。

各軸の寸法をN、整数tap座標をiとする。Repeatは `positiveMod(i,N)`。Mirrorは `r=positiveMod(i,2*N)` として `r<N ? r : 2*N-1-r`。positiveModは0以上の剰余とし負座標にも適用する。Clampは端のtexelへ固定し、Decalは範囲外tapを透明とする。X/Yは独立して指定する。

Nearestは各成分をfloorしたtexelを選ぶ。Linearはtexel中心基準の4tap双線形補間で、各tapへSource相対のtile規則を適用する。中心座標だけをwrapしてatlas全体をhardware linear samplingする方式は使用しない。Source外の隣接画像を読まないため、この画像paintにはatlas paddingを要求しない。初期対応は単一mipのNearest/Linearとする。

DrawImageとImagePatternの補間は、texelごとにsRGBをdecodeし、Straightならalphaを掛けた後の線形premultiplied RGBで行う。補間後のpremultiplyで代用せず、Premultiplied入力へ再乗算しない。hardware sampling・tap処理・事前変換はこの結果と等価な場合に使う。Prepareは色formatとSampled用途を確認し、同一deviceとview・親texture・samplerの生存は利用者がGPU使用終了まで保証する。

#### COLRv1と隔離合成

文字のfallback・shaping・行配置・描画形式の選択・cacheは外部文字ライブラリが担当する。DrawGlyphRunはDrawing2Dへ追加せず、パス・mesh・coverage・距離場・RGBA・paint graphを対応する汎用命令へ渡す。adapterはfont unitsとY上向き座標を変換し、paint・clip box・角度へ同じ変換を適用する。輪郭の穴はNonZeroと元のwindingで保持する。

| COLRv1要素 | 写像 |
| --- | --- |
| PaintSolid / PaintVarSolid | palette・foreground・alphaを解決したSolid |
| PaintLinearGradient / Var版 | 3点LinearGradientとColorLine |
| PaintRadialGradient / Var版 | 2円RadialGradientとColorLine |
| PaintSweepGradient / Var版 | 座標・角度を変換したSweepGradientとColorLine |
| PaintGlyph | outlineをClipとして子paintへ適用 |
| PaintColrGlyph | 参照先paint graphを解決・共有し、仕様のclip適用範囲を保持 |
| PaintColrLayers | 順序を保持したLayers |
| Transform / Translate / Scale / Rotate / Skew、各Var・center版 | centerを含むアフィン行列のTransform |
| PaintComposite | source・backdropと全28種のCompositeModeを保持 |
| ClipList / ClipBox / variable clip box | 仕様の範囲へ矩形pathのClipを適用 |

adapterは正規化軸とvariation store、CPAL palette、foreground（0xFFFF）、stop alphaをgraph生成前に解決する。ColorLineはvariation適用後にoffset順へ安定ソートし、重複stopのfont内順序を保持する。offsetの一律[0,1]制限・重複除去は行わない。異なる軸・palette・foregroundのrunでgraphを誤共有せず、PaintColrGlyphを輪郭参照へ縮約しない。参照循環、不正offset/glyph、深さ・node数超過は拒否し、共有DAGの実展開コストも評価上限へ数える。

3点linearの投影、偏心2円radialの解と有効領域、sweep角度境界、Pad/Repeat/Reflect、退化・特異変換はOpenType COLRの規則を保持する。未対応段階はPrepareでNotSupportedExceptionとし、別gradientへの近似で成功扱いにしない。補間・合成はsRGBを線形化しpremultiplyした値で行い、HSL modeは仕様のunpremultiplied演算後に再乗算する。alpha=0やPlus等の範囲処理も参照規則に従う。

中間面はlinear premultiplied Rgba16Floatとし、必要なattachment/sample用途がなければPrepareで拒否する。sRGBのままの演算やRgba8への暗黙の精度低下を設けない。Compositeのsource/backdropは独立した透明面へ評価し、backdropを既存Canvas背景と混同しない。結果を一つの描画として外へSrcOverし、nested compositeを平坦化せず中間面境界をまたぐbatch化を禁止する。

Clear/Src/DestInは透明領域も結果へ影響するため、子の輪郭unionだけへ評価範囲を狭めない。target・Canvas clip・外側形状の有限範囲で評価し、透明領域の意味を保持できる場合だけ縮小する。clip coverageを指定位置で適用し、再合成時に二重乗算しない。ImagePatternとOpacityは一般描画用の追加nodeであり、新しいOpenType形式を要求しない。

参照: [OpenType COLR](https://learn.microsoft.com/en-us/typography/opentype/spec/colr)、[CPAL](https://learn.microsoft.com/en-us/typography/opentype/spec/cpal)、[Compositing and Blending Level 1](https://www.w3.org/TR/compositing-1/)。

#### レイヤーのclip順序と背景置換

BeginLayerはoptions・変換・親clipをsnapshot化する。ContentBoundsは入力を実際に幾何clipする有限矩形で、cullingヒントではない。変換後のpixel外接矩形を外向きに丸めて中間画像を作り、元矩形の幾何clipも保持する。画面外の入力が画面内へ影響し得るため、最終target範囲へ先に切り詰めない。空のContentBoundsは出力なしとし、0寸法画像を作らない。

親clipは入力へ適用せず、レイヤー内で追加したclipを入力描画へ適用する。EndLayerは内容→Effects→Opacity→保存した親clipとOutputClipと描画先範囲の積集合→親へのSrcOverの順で処理し、BeginLayer時の状態を復元する。拡張された効果領域をContentBoundsで再度切らない。最終clipのcoverageは一度だけ適用する。

coverage相関を保つのは同じ幾何評価段階内であり、画像化を挟む入力clipと出力clipは別演算とする。同じ境界でも入力0.5×出力0.5=alpha 0.25となり得る。group opacityは合成済み画像へ掛け、Opacity=0.5なら不透明形状同士の重なりもalpha=0.5とする。Save/Restoreは各レイヤー内で完結し、外側Saveの消費・未完了Saveを残すEndLayer・対応のないEndLayerを拒否する。Endは未完了構造を拒否し、Cancelは全構造を破棄する。

エフェクトのsigmaとComposite移動はdevice pixel単位で、Canvas変換・PixelScaleに依存しない。論理座標で一定幅にする場合はアプリが明示変換する。非一様拡大や回転でもGaussianは出力pixel gridのX/Y軸に沿い、暗黙に回転楕円kernelへ変換しない。

DrawBackdropEffectはその位置より前の現在レイヤーの描画先だけを取得し、祖先・後続・自分自身を読まない。targetはSampled | RenderAttachment用途と有効内容を要求する。初期Clearまたは有効なLoadが必要で、読出し不能ならPrepareで拒否する。Rgba8Srgbはsample時にdecodeし、既にpremultipliedの値を再乗算せずRgba16Floatへ描く。異形式のraw copyを色変換の代用にしない。

変換後のOutputBoundsと現在clipから出力pixel領域を求め、GetRequiredSourceBoundsで入力領域を逆算する。必要入力と置換用originalの出力領域の和集合を別textureへsnapshotし、取得元の有効範囲外を透明とする。sample領域をOutputBoundsへ先に切らない。元背景が半透明でも二重合成しないよう、coverageをcとして `result = effect*c + original*(1-c)` で置換する。originalもsnapshotから読み、attachmentはblend無効で書き、clip外は保持する。

#### 準備・RenderGraph・解放

準備ownerはcallbackのCPU snapshot・固定GPU資源を、compiled planは一時allocationと生成viewを、アプリはimport資源を所有する。Drawing2D経由のPreparedImageEffectはPreparedDrawingPasses2Dが所有し、同じGPU使用期間を通じて保持する。エフェクト内部の資源管理は [GRAPHICS-0016](GRAPHICS-0016-image-effects.md) に従う。callbackは宣言済み資源だけを使い、内部Submit/Waitや記録中の追加依存・一時資源生成を行わない。

外部textureはparent allocationごとに一度だけimportし、DrawingGraphBindings2Dへ同じhandleを登録する。準備時にbindingを確定し、後の表変更で追加済みcallbackの入力を変えない。target Descriptionは実際の宣言と一致させる。標準passは全アクセスを宣言し、同じ資源のstage/accessを合併する。初期graphはallocation全体で同期し、同一allocationのsampleとattachmentを同時使用しない。shader-dataは準備側で所有してimportし、通常bufferはtyped handleを使う。

Load・部分更新・source-overはColorRead | ColorWrite、全体ClearのみColorWriteとして前内容の依存を保持する。未初期化領域・Discard後の内容を読まない。背景は更新→snapshot→effect→置換→後続描画の依存を宣言する。MarkOutputは出力を保持し、Exportはさらにfinal state/scopeを指定する。DependsOnは順序、KeepAliveは副作用による保持であり代替しない。exportしない画像は自動でimport状態へ戻らない。

準備ownerは一つのcompiled planで一回のRecordだけに使い、記録失敗も消費済みとする。Append失敗・キャンセルでは途中graphを破棄して再構築し、その呼び出しの新規ownerは内部cleanup、先行する成功ownerは利用者が解放する。Compile失敗ならownerを解放でき、Record失敗・送信取消ならcommandを先に破棄する。Submit後は全GPU使用終了を待ち、command→plan→準備owner→submissionの順で解放する。後続submissionが出力を使う間も元planを保持する。

WaitのキャンセルやFailedという状態名だけをGPU終了とみなさない。Disposeは完了照会・waitを行わず、寿命違反の検出も保証しない。利用者は準備結果→renderer→DI scopeの寿命を保証する。単独Prepareは入力をSampledかつVertexShader | FragmentShader | ComputeShaderのShaderReadへ、targetをColorAttachmentへ可視化済みとしてimportする。共有graphは実際の入口state/scopeを渡し、read-only importも最初の利用stageまで外部producerとの依存を満たす。

opaque callbackは全外部アクセスを宣言し、内部barrierと私有資源を実装側で管理する。終了時は宣言した入口stateへ戻す。現RenderGraphには別exit stateの宣言がなく、単一Useで表せない複数stateはpassへ分割する。宣言できない経路を推測で実行しない。

数値・範囲違反はArgumentException/ArgumentOutOfRangeException、自身の局所的な呼び出し順違反はInvalidOperationException、自身の破棄後操作はObjectDisposedException、非対応形式・機能はNotSupportedExceptionとする。外部資源の寿命・実state・同一deviceの違反を必ず検出する保証とは区別し、nativeの失敗は伝達する。

### 利用例

#### 固定DI構成

登録はprovider構築前に完了する。単一サービス解決の後勝ちを使い、UseGraphics2DのTryAddは先行登録・再登録時の上書きを保持する。CreateDefaultはDIを使わず表と全標準実装を毎回newし、GPU資源を作らない。描画登録表と各graphは実装を借用する。ExternalComputeFillPathRendererは利用者側の実装例で、FillPathだけを置換する。

```csharp
var services = new ServiceCollection();
services.UseGraphics2D();
services.AddScoped<IFillPathRenderer2D, ExternalComputeFillPathRenderer>();

using var provider = services.BuildServiceProvider(
    new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
using var scope = provider.CreateScope();

var renderers = scope.ServiceProvider
    .GetRequiredService<DrawingCommandRenderers2D>();
using var renderer = new Renderer2D(device, renderers);
// Effectsはコンパイラーの拡張メソッドで直接構築する。
var compiler = new ImageEffectCompiler(device);
var effects = compiler.GaussianBlur(3, 3, ImageEffectGraph.Source());
```

#### DIなしのGraphics接続

正常系の例。device・targetView・入力画像はアプリ所有。targetToAttachmentは初期化済みtarget allocation全体を実際の前状態からColorAttachmentへ移すbarrierとする。入力画像は前述の入口state/scopeへ可視化済みとし、新規targetはClearで初期化する。

```csharp
var renderers = DrawingCommandRenderers2D.CreateDefault();
using var renderer = new Renderer2D(device, renderers);
var prepared = await renderer.PrepareAsync(drawList, new RenderTarget2D
{
    View = targetView,
    LoadOp = AttachmentLoadOp.Load
});

var commands = device.CreateCommandBuffer(new CommandBufferDesc());
commands.Barrier(targetToAttachment);
prepared.Record(commands); // active passの外から呼ぶ。内部のpassも閉じて返す。
commands.Finish();

var submission = device.Queue.Submit([commands]);
await submission.WaitAsync();

commands.Dispose();
prepared.Dispose();
submission.Dispose();
```

#### 共有RenderGraphへの接続

正常系の例。targetは初期化済み単一mip/layer画像で、sampledInputsは重複のない別allocationとする。例のsampleScopeを入口として渡すには、外部producerから必要な読出しstageへの可視性を利用者が満たす。prepared ownerとcompiled planの両方をGPU使用終了まで保持する。

```csharp
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;

var graph = new RenderGraphBuilder(device, "2d-and-effects");
var bindings = new DrawingGraphBindings2D(graph);
var colorScope = new BarrierScope(PipelineStage.ColorOutput,
    ResourceAccess.ColorRead | ResourceAccess.ColorWrite);
var sampleScope = new BarrierScope(PipelineStage.FragmentShader,
    ResourceAccess.ShaderRead);
var targetHandle = graph.ImportTexture(target, TextureState.ColorAttachment,
    colorScope, "canvas");
bindings.BindTexture(target, targetHandle);
foreach (IGraphicsTexture input in sampledInputs)
{
    var handle = graph.ImportTexture(input, TextureState.Sampled, sampleScope);
    bindings.BindTexture(input, handle);
}
var targetBinding = new RenderTargetBinding2D
{
    Texture = targetHandle,
    Description = new TextureDesc
    {
        Width = target.Width,
        Height = target.Height,
        Format = target.Format,
        Usage = target.Usage,
        MipLevels = target.MipLevels,
        ArrayLayers = target.ArrayLayers,
    },
    Range = new TextureSubresourceRange(0, 1, 0, 1),
    PixelScale = 1,
    LoadOp = AttachmentLoadOp.Load,
};
var drawingOwner = await renderer.AppendPassesAsync(graph, drawList,
    targetBinding, bindings);
graph.ExportTexture(targetHandle, TextureState.ColorAttachment, colorScope);
var plan = graph.Compile();
var commands = device.CreateCommandBuffer(new CommandBufferDesc());
plan.Record(commands);
commands.Finish();
var submission = device.Queue.Submit(new[] { commands });
await submission.WaitAsync();
commands.Dispose();
plan.Dispose();
drawingOwner.Dispose();
submission.Dispose();
```
