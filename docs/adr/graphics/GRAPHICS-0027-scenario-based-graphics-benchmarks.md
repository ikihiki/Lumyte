# ADR-GRAPHICS-0027: 描画性能をシナリオと処理工程ごとに比較する

- 状態: 提案
- 日付: 2026-11-04
- 基準コミット: `8d2b1a7f30f09cc086da5bb422740289eee7a9d5`

## 背景

描画処理を変更する開発者は、矩形の件数や平均フレーム時間だけでは、ゲームのHUD、文字の多いエディタ、画像や半透明効果を使う画面への影響を判断できない。2Dでは変更の種類と範囲、内容の分割、描画順、画素数、資産の再利用によって処理負荷が変わり、ある用途の高速化が別の用途のdraw数や保持メモリを増やす場合がある。

基準コードの [RenderingWorkloadCatalog](https://github.com/ikihiki/Lumyte/blob/8d2b1a7f30f09cc086da5bb422740289eee7a9d5/tools/benchmarks/drawing2d-incremental/RenderingWorkloadCatalog.cs) は2D・ゲームUI・エディタUIを分けるが、2Dのケースは静止、全体変形、形状更新、カメラ移動の4種類である。[GRAPHICS-0025の内容分割](GRAPHICS-0025-retained-drawing-content.md#木ページ実行順の役割)と[GRAPHICS-0022のレイヤー描画](GRAPHICS-0022-2d-render-graph-layers.md#決定)の採用判断を維持し、それらの費用を用途別に比較する測定契約を追加する。

## 決定

描画負荷をシナリオと計測範囲で分類し、同じ入力・画質・機器の下で処理工程、仕事量、遅延分布、保持メモリを比較する。2Dは更新の局所性と描画内容の違いを独立に変え、部品測定で原因を絞り、実際のUIを通す用途別測定で影響を確認する。用途の異なる結果を一つの総合スコアへ集約しない。

### 利用者の変更前・変更後

利用者は描画実装を比較する開発者である。例えば「一つの図形の色を変えたときの保持描画の費用」を確認する手順を次のように変える。

| 操作 | 変更前の基準コード | 変更後の測定契約 |
| --- | --- | --- |
| 局所更新を選ぶ | [PublicRetainedApiProbe](https://github.com/ikihiki/Lumyte/blob/8d2b1a7f30f09cc086da5bb422740289eee7a9d5/tools/benchmarks/drawing2d-incremental/PublicRetainedApiProbe.cs)の固定命令数・1／16／256内容ノード・静止または末尾一件変更を実行する | 同じ4,096命令を複数の内容粒度に分け、同じ論理要素を1件・1%・10%・全件変更する。連続変更と分散変更を分ける |
| 再利用の効果を比較する | 同probeのCanvas対照は静止時も全体を記録する | 主比較のCanvasも無変更時に完成済みDrawListを再利用し、強制全記録は別の診断とする |
| 負荷を説明する | 主に矩形の分割と更新、工程時間を確認する | 変更件数、再記録命令数、実draw数、cache再利用、保持量を同じ入力IDへ対応付ける |
| 別の内容へ広げる | [Content2DWorkloads](https://github.com/ikihiki/Lumyte/blob/8d2b1a7f30f09cc086da5bb422740289eee7a9d5/tools/benchmarks/drawing2d-incremental/Content2DWorkloads.cs)の矩形・pathを測る | 画像、文字、clip、半透明の重なり、layer効果も別シナリオとして選び、各方式の画質と対応範囲を揃える |

以下のケースと構成は提案する測定契約であり、既存CLIの実行手順を変更済みとは扱わない。製品ライブラリの公開API・所有権・描画契約は変更しないため、公開APIの宣言差分はない。Canvas・Scene・UIの公開経路を使い、内部の試作だけを測った結果は診断として区別する。

### 基準コードの制約

| 基準コード | 測定に必要な条件 |
| --- | --- |
| [Renderer2D.Retained](https://github.com/ikihiki/Lumyte/blob/8d2b1a7f30f09cc086da5bb422740289eee7a9d5/src/Graphics/Lumyte.Graphics.Drawing2D/Execution/Renderer2D.Retained.cs) | ノードをまたいだbatch統合を仮定しない。親transform・clip更新の波及と再準備を測る |
| [PreparedRectangleBatch2D](https://github.com/ikihiki/Lumyte/blob/8d2b1a7f30f09cc086da5bb422740289eee7a9d5/src/Graphics/Lumyte.Graphics.Drawing2D/Rendering/PreparedRectangleBatch2D.cs) | 連続最大1,024矩形・矩形clip深さ8の境界を、1,023／1,024／1,025件と深さ7／8／9で比較する |
| [drawing-fragment.slang](https://github.com/ikihiki/Lumyte/blob/8d2b1a7f30f09cc086da5bb422740289eee7a9d5/src/Graphics/Lumyte.Graphics.Drawing2D/Shaders/drawing-fragment.slang#L302) | 汎用shaderの8／16／256命令の区分と実命令数を記録し、区分を跨ぐケースを用意する |
| [DrawingRetentionOptions2D](https://github.com/ikihiki/Lumyte/blob/8d2b1a7f30f09cc086da5bb422740289eee7a9d5/src/Graphics/Lumyte.Graphics.Drawing2D/Execution/DrawingRetentionOptions2D.cs) | 未使用資産の既定512個・推定64 MiBと、使用中資産を区別する。静止時のcache hitを仮定しない |
| [HardwareGpuTimer](https://github.com/ikihiki/Lumyte/blob/8d2b1a7f30f09cc086da5bb422740289eee7a9d5/tools/benchmarks/drawing2d-incremental/HardwareGpuTimer.cs) | 現行のVulkan・Radeon 8060S固定を他GPU対応と見なさず、backendごとの能力照会とtimestamp取得を分離する |
| [RenderingWorkloadRunner](https://github.com/ikihiki/Lumyte/blob/8d2b1a7f30f09cc086da5bb422740289eee7a9d5/tools/benchmarks/drawing2d-incremental/RenderingWorkloadRunner.cs) | 用途別suiteの非空画素・checksum確認に加え、独立した描画参照との比較を必要とする |

### 測定を四つの層に分ける

| 層 | 測定範囲 | 主な目的 |
| --- | --- | --- |
| CPU部品 | Canvas記録・End、Scene編集、文字組み、RenderGraph構築・Compileなどを個別に測る | アルゴリズムと割当の原因を絞る。GPUを含むPrepareとは分ける |
| 描画フレーム | 準備済みの入力から公開APIで更新・記録・Prepare・Submit・完了まで | 描画方式の比較。UIの再構築・layoutは含めない |
| 用途別フレーム | 実UiSurface／Signal／layout／文字組みから描画完了まで | 利用側の負荷を含むゲームUI・エディタUIの比較 |
| 表示を伴う実行 | 実window／swapchain／Present／ホストのフレーム予約 | フレーム間隔、提示方式、idleからの復帰。描画完了までの測定とは別に集計する |

CPU部品はBenchmarkDotNet、実GPUはフレーム単位のrunnerを使います。CPU測定の1操作、実GPUの1論理フレーム、表示された1フレームを混同しません。機能の正しさと寿命は単体・GPUテストで確認し、経過時間を単体テストのassertに使いません。

### 2Dシナリオ

表の「主に調べる負荷」は仮説です。実際の律速箇所は工程別の時間と仕事量から判断します。各ケースで固定seed、論理時刻、描画順、期待される入力状態を共有します。

| ID | 場面と変更 | 独立に変える条件 | 主に調べる負荷・観測値 |
| --- | --- | --- | --- |
| D01 static | 完成した画面を毎フレーム描画。内容の変更なし | 要素数、内容共有、キャッシュ容量、未完了フレーム数 | 記録の再利用、Prepare、draw、毎フレームの固定費。ホストが描画を省略するidleは別ケース |
| D02 local-content | HUDや図形の一部の色・値を実際に変更 | 1要素／1%／10%／全体、連続／分散、内容ノードの粒度 | 再記録の増幅、再compile・reuse、upload、draw、保持量 |
| D03 placement | 図形の内容を固定して位置・回転を変更 | 一つの親／個別配置、1%／全体、treeの形 | 属性更新と再準備の波及。個々の内容を再記録する費用との違い |
| D04 camera | 大きな静的map・図面をpan／zoom | 可視率、総要素数、移動速度、拡大率 | 可視範囲外の処理、clip、camera更新、coverage。panとzoomは別に測る |
| D05 geometry | チャートや波形の頂点・パス形状を更新 | パス数、segment数、輪郭数、更新率、画面上の面積 | 形状生成・記録・準備とGPUのパス評価。transformだけの更新と比較しない |
| D06 images | テクスチャ付きsprite・タイルを描画 | atlas／複数texture、texture数、並び、透明度、占有面積 | resource切替、batch、sampling、帯域。初期uploadと常駐後を分ける |
| D07 text | 固定label、数値更新、編集、幅変更による折返し | glyph数、文字種、変更箇所、vector／MSDF・MTSDF／COLR、atlasの状態 | shaping・layout、輪郭／atlas生成、転送、描画。表示方式は画質条件と一緒に比較する |
| D08 clip-paint | scroll領域、角丸領域、複雑な塗り | clipなし／矩形／path、深さ、clip共有、solid／gradient／合成 | 専用経路の適用範囲、汎用shader評価、batch分断、画素負荷 |
| D09 overlap | 半透明sprite、重なったpopup、particle風の四角形 | 同じ命令数で重なり1／4／16層、alpha、占有面積 | blendとfill。描画順を保存し、透明な層を勝手に並べ替えない |
| D10 layers-effects | 背景ぼかし、隔離opacity、shadow、複数レイヤー | 対象領域、解像度、blur半径、layer数、静止／更新背景 | RenderGraphの構築・Compile、pass数、一時texture、GPU帯域 |
| D11 structural | 項目の挿入・削除・並べ替え・親移動 | 先頭／中央／末尾、連続／分散、増減率、treeの形 | Scene木の編集、順序の維持、再利用、退役資産。値の置換と区別する |
| D12 working-set | 複数画面・文書・atlasを巡回し戻る | 作業集合とキャッシュ予算の比、再訪間隔、frame並行数 | 追い出し後の再compile・再転送、遅いフレーム、メモリの増減 |

#### シナリオを成立させる条件

- D02は同値代入を避け、変更前後の画素が異なることを確認します。画素の変わらない同値更新は独立したno-opケースにします。変えた論理要素数と、実際に再記録した命令数の両方を残します。
- D03は共有したDrawListの複数配置と、別々のDrawListの配置を分けます。描画結果・描画順・配置数を一致させ、共有率だけを変えます。
- D04はカメラの往復中に見える内容量がほぼ一定の区間を主測定にします。viewport外へ移動して仕事が減るフレームを「カメラ更新が高速」と解釈しません。cullingの実装がない経路では、そのまま可視・不可視の処理量を報告します。
- D05は同じboundsに収まる形状を使い、segment数の増加と描画面積の増加を分けます。命令上限を超える入力はunsupportedとして報告し、簡単な形へ置換しません。
- D06はresident textureの描画と、毎フレームの画像差し替え・uploadを別ケースにします。PNG等のファイルdecodeを描画時間へ混ぜず、必要ならasset読込のsuiteで測ります。
- D07は固定文字、同じglyph集合の数値変更、初出glyph、折返し、双方向文字を分けます。フォント・サイズ・hinting等の条件を固定し、フォントのファイルhashと使用可能な文字範囲を確認します。空白や欠落glyphへの置換で負荷が下がった結果は無効です。
- D08は矩形clip深さ8と9、path clip、複雑なpaintを別variantにします。専用経路から外れた場合の理由を記録します。
- D09は論理命令数を固定して重なりを変える測定と、被覆面積を固定して命令数を変える測定を分けます。bounds面積からの推定重なりはproxyとして表示し、実fragment数とは呼びません。
- D10は[GRAPHICS-0022のRenderGraph経路](GRAPHICS-0022-2d-render-graph-layers.md#決定)を使います。現在のdirect Scene経路はlayer/effect非対応なので比較対象に入れません。backdrop効果は背景全体の取得を伴う現在の契約を測り、出力領域だけのROI処理を仮定しません。
- D11は新規作成だけでなく削除と再利用を含む往復traceを用意し、総数の増加だけで測定時間が増える問題を避けます。大規模な追加burstは独立して測ります。
- D12は未使用cacheの予算と、GPU使用中の資産・共有textureのメモリを分けます。未使用cacheの上限を全VRAMの上限と解釈しません。

#### 保持粒度と更新の局所性

まず単色矩形4,096命令を固定し、1内容ノードあたり1／16／64／256／4,096命令とします。座標・paint・描画順・変える論理要素を揃え、Canvasによる変更ノードの再記録とReplaceContentを測定へ含めます。

| 更新パターン | 生成方法 | 比較で見ること |
| --- | --- | --- |
| 単一 | 固定の可視要素を一つ変更 | ノード全体を再記録する増幅 |
| 連続 | 同じ領域の連続した要素を変更 | 局所性による再利用 |
| 分散 | 固定seedで、可能なら異なるノードへ同数の変更を分散 | 同じ変更率での最悪側の増幅 |
| burst | 事前に固定した時刻に全体更新し、その後は静止 | 単発遅延とcache回復。定常フレームと別集計 |

更新率の分母は総論理要素数です。丸め後の変更件数、可視変更件数、変更ノード数、再記録命令数を出します。変更率0で増幅率を計算しません。ノード数と実draw数は別に観測します。

flatな兄弟、階層化したgrid、深いgroupの三種類は別のtree形状として扱います。CPUをflat、GPUをgridで測った結果を同じスケール曲線へ繋げません。通常測定に過去snapshotの保持を強制せず、CaptureSnapshotの0／1／3世代保持は追加の寿命ケースにします。

### ゲームUI・エディタUIの用途別ケース

部品測定を組み合わせただけで用途の性能を推定せず、実際のUI公開・layout・文字組みを経由します。rendererのみの比較には、同じ描画内容をUIなしで渡す対照ケースを用意します。

| ID | 場面 | 更新trace | 主な観測 |
| --- | --- | --- | --- |
| U01 game-hud | 動く2D背景と静止HUD、score／health | 背景60／120Hz、HUD値10Hz。全体layoutを要求する場合は別variant | 背景とHUDの更新頻度、再記録量、合成費用 |
| U02 game-menu | menuを開閉するanimation | transform／opacityだけの変化と、width／spacingの変化を別実行 | 見た目のanimationとlayout animationの差 |
| U03 editor-idle | panel・文字・previewが静止 | 強制描画の定常caseと、ホストの描画予約が止まったidle case | rendererの固定費と、ホストのframe省略・復帰を区別 |
| U04 editor-scroll | 長いリストをscroll | データ総数10,000、可視行20／100。行境界を跨がない移動と行入替え | clip、可視項目の再利用、文字cache、mount／unmount |
| U05 editor-edit | textを一箇所編集する | 同幅の置換／字数変更／折返しを伴う幅変更 | 再shaping、再layout、glyph miss、波及範囲 |
| U06 editor-resize | panel幅やwindowを変える | 連続resizeと一回のDPI変更を分離 | layout全体、文字折返し、target再確保、初回の遅延 |
| U07 editor-preview | 静止した周辺UIと動くpreview | previewを60Hz、UIを入力時のみ更新 | layerの境界、3D・2D混在、不要な再記録 |

既存editor-scroll-listの可視行制限はbenchmark側の方針で、製品の汎用virtualization機能とは扱いません。実装するtraceがTextBoxのcontrolled値置換だけなら、OS入力・IME・Undoを含む編集性能とは記載しません。3D previewは実装時に明示的な新variantとし、現在の2D previewから外挿しません。

traceは壁時計で進めず、論理frame番号と時刻で固定します。遅い実装が更新イベントを少なく処理して有利になることを防ぎます。用途別測定では、文字列生成・通知配送・layout等の必要な仕事を測定区間へ含めます。純粋な描画測定では、その入力を計測外で準備します。

### 2D以外のグラフィックス

| ID | 対象 | 条件と指標 |
| --- | --- | --- |
| G01 submission | command記録、resource binding、submit | 同じ総draw数でcommand buffer数・submit数を変える。空submitはAPIが許す場合のみ基準にする。CPU費用、queue待ち、throughput |
| G02 pipeline | pipeline初回生成・再利用・状態切替 | 同一状態の連続／交互／多数のvariant。既存pipeline-benchmarkを引き継ぎ、初回とwarmを分離 |
| G03 transfer | buffer／textureの更新・読戻し | 固定byte数、連続／分散、upload ring、1／3 frames in flight。CPU準備、転送byte数、GPU転送、待機 |
| G04 render-graph | graphの構築・Compile・Record | chain／fanout／複数output、pass数・資源数、未使用pass比、resource lifetime。構築とCompileを別々に測る |
| G05 models | 同じmeshの多配置と固有mesh、opaque／transparent | draw数を変える測定と総頂点数を変える測定を分離。material・texture共有率、depth、CPU記録、GPU span |
| G06 resources | 初回asset取得、更新、退役、device復旧 | 定常描画とは別実行。初回表示までの時間、peak memory、復旧後の正しい画素・解放 |

G05は[ModelDrawing](https://github.com/ikihiki/Lumyte/blob/8d2b1a7f30f09cc086da5bb422740289eee7a9d5/src/Graphics/Lumyte.Graphics.Drawing/ModelDrawing.cs)で構成できる範囲から始めます。camera culling、LOD、indirect描画等は、選んだ経路が実際に行っている場合だけ結果に含めます。

### 入力の固定と実験数

基準は1920×1080物理pixel、DPR 1、Rgba8Srgbとします。AA・coverage・blend・filterは現在の製品既定を固定し、manifestへ記録します。Rgba16Floatは同じcaseの別variantです。UIの論理寸法を維持してDPRを変える試験と、DPR固定でviewportを広げる試験は分けます。

全パラメーターの直積は実行しません。最初に標準profileを固定し、その後に一つの要因だけを変える試験と、相互作用を確認する小さな組合せを実行します。

| 要因 | 初期の水準 | 固定・記録するもの |
| --- | --- | --- |
| 要素数 | 単純図形100／1,000／10,000、複雑path32／128／512 | caseごとに数の意味を定義。総数・可視数・命令数を併記 |
| 解像度 | 1280×720／1920×1080／3840×2160 | 同じ正規化した図柄のfill試験。UIの密度変化とは別 |
| 内容の粒度 | 固定4,096命令を1／16／64／256／4,096命令ずつ | partition以外の入力、順序、画素を固定 |
| 更新 | 0／1要素／1%／10%／100% | 論理変更件数と可視変更件数、連続／分散 |
| キャッシュ | 製品既定、無効、十分な容量、予算を超える作業集合 | asset数とbyte数のどちらが制限したか。未観測ならunknown |
| 同時frame | 1／3 | target、command、frame ownerとGPU queryをslotごとに保持 |
| 文字 | 固定・数値・日本語・双方向文字・COLR | 同じフォントassetと文字列。各フォントの対応をpreflightで確認 |

相互作用の優先順は「変更率×内容粒度」「clip／paint×batch」「内容共有×キャッシュ予算×同時frame」「解像度×重なり」です。限界試験で時間・メモリの予算を超えた場合も、そのprofileをfailed／budget-exceededとして残し、小さい規模の結果へ黙って置換しません。

### 比較方式と画質

主比較は、同じ入力traceに対する現在の製品経路同士とします。

1. Canvas経路はCanvasや不変geometryを再利用し、無変更時は完成したDrawListを再利用する。変更時の記録方針を明記する。
2. 保持経路は公開Scene APIで編集し、内容変更時は変更ノードをCanvasで記録してReplaceContentする。borrow、Prepare、GPU完了後のRetireまで含める。
3. 強制全記録、内部ページ編集、独自GPU resident試作は診断として実行できるが、主比較のbaselineと入れ替えない。

描画内容によって利用できる経路が異なるため、測定前にシナリオごとの経路対応表を固定します。layer/effectなど片方で非対応ならN/Aとし、CPU commitだけとGPU描画全体を比較しません。backend比較では製品経路・format・AA・shaderを揃えます。

単色矩形のdrawを減らすために、透明物体の順序変更、clip省略、粗いpath、欠落glyph、低い解像度を使うことは禁止します。文字のvectorとdistance fieldなど画質の異なる方式は、等価実装の速度比とせず、拡大縮小時の画像品質を添えた方式比較にします。

### 時間と仕事量の測り方

| 項目 | 境界・意味 |
| --- | --- |
| input/update | 論理入力の適用、Signal配送、Scene／UIの更新 |
| layout | UI layout。native text shapingの時間は内数として別表示 |
| record | Canvas記録とEnd、または保持adapterによる変更内容の公開 |
| graph-build / graph-compile | RenderGraphの宣言とCompile。該当caseだけ測る |
| prepare | rendererのCPU処理、資産取得・生成等を含むPrepareのhost経過時間 |
| encode | command作成、初期barrier、Record、Finish。内訳を採る場合は親区間を二重加算しない |
| submit | Submit呼出しのhost経過時間 |
| slot-wait | 再利用するslotがGPU完了を待つhost経過時間 |
| retirement | 完了済みcommandの破棄とframeの返却・資産解放 |
| host-frame | update開始からSubmit復帰までの実測経過時間。slot待ちを含む外側区間も別途持つ |
| completion-latency | 対応するframeのSubmit開始から完了確認まで。CPU待ちとqueueの滞留も含む |
| gpu-span | 同じqueue上で対応するtimestamp対を取得した区間。upload・barrier・clear・drawのどこを含むか明記 |

Prepareが内部で行うuploadや待機を、名称だけからCPU専用時間と決め付けません。非同期の区間はhost経過時間として扱います。CPUとGPUは重なるため、host-frameとgpu-spanの和をフレーム時間にしません。

入力件数・可視件数・再記録命令数・layout／shaping回数・発行draw数・pass数・compile／reuse差分・APIで記録した転送byte数を採ります。現在のRetentionStatisticsにないeviction数やnative VRAMを捏造しません。取れない値はnullとし、測定元をmanifestへ記録します。

API decoratorで数えるdraw数は、Graphics APIへ実際にforwardした呼出し数です。GPU内部のprimitive数やnative driverによる統合数とは区別します。詳細counterの収集は両版で同じ条件とし、基本測定と重いprofile採取を別実行にします。決定的なtraceの事前生成、JSON出力、画像比較、sample保存用の配列確保を計測区間へ入れません。用途別ケースの文字列生成や通知配送など、シナリオ自体の処理はupdate区間へ含めます。

#### 直列・並行・表示

- 工程診断は1 frame in flightで実行し、各frameの完了を待つ。CPUとGPUのpipelineを止める診断であり、実アプリのthroughputとは区別する。
- throughputは1／3 frames in flightで、開始前にwarmupをdrainする。固定frame数の実行と最終drain・退役を含む時間からframes/secondを計算する。
- 各slotはtarget・command・準備済みframe・submission・queryを専有し、GPU完了まで上書きしない。生データは退役順でなくframe IDとinput IDで対応付ける。
- 重なり得るGPU timestamp区間の合計をGPU占有時間と呼ばない。timestamp非対応・不正・disjointはnull／invalidとして扱い、完了待ち時間で代用しない。
- 表示試験ではPresent mode、VSync、refresh rate、windowサイズ、可視状態を固定する。60Hzの16.67ms、120Hzの8.33msに対する超過率は実際に選んだ区間名とともに出す。Present復帰だけで光子到達や入力→表示遅延を測れたとは扱わない。

#### cold・warm・burst・保持メモリ

初回はdevice初期化、shader／pipeline生成、初回layout、初出glyph／atlas、最初の可視frameを分けて記録します。fresh processでもdriverのdisk cacheがcoldとは限りません。cacheの範囲と初期状態を記録し、ユーザーの全体cacheを自動消去しません。

warm測定では、JIT、pipeline、glyph、Scene資産の各cache状態を記録します。warmupが予算内に安定しなければunstableとして報告します。キャッシュ超過による定常的なmissはD12の性質なので、hitするまで予算を増やして隠しません。burstの前後はイベント名と相対frame番号で集計し、長いidle区間で平均を薄めません。

managed allocation、full GC後のmanaged live heap、process RSS、native allocation、GPU allocationは別指標です。full GCは保持量を調べる専用実行だけで行います。描画測定中にGCを強制しません。retained資産を使い切る・退役する・同じ画面へ戻る周期を繰り返し、増加が上限へ収束するかも確認します。

### 正しさの入口

性能測定前のpreflightと、計測後の検証を必須にします。単なる非空画素、checksumの保存、例外なしでは合格としません。

- 小規模な各caseで、元の入力から独立にCanvasへ記録した参照と比較する。保持Sceneをflattenしたものだけを正解にしない。
- 参照側も、既知色・描画順・clip境界などの小さな解析可能な画像と既存GPUテストで検証する。両経路が同じ誤りを持つ場合を、相互一致だけで見逃さない。
- 最初、変更後、親更新後、並べ替え後、burst直後、最後の論理frameを選び、同じ入力IDで比較する。fixtureのseedと期待状態を検証する。
- 同一backend・同一format・同じ方式では原則として正確な画素一致を使う。backend間の許容差は、色空間・AA・誤差画素率を事前定義し、測定後に閾値を広げない。
- 文字はglyph列・advance・layout boundsとpixelを確認する。cache missケースでは新しいglyphが実際に表示されることを確認する。
- 1／3 frame in flightで同じ論理frameの画像、描画順、使用中資産の寿命が維持されることを確認する。
- unsupported、device loss、OOM、timeout、正解不一致は結果から除去せず、性能比較を無効にした理由として残す。送信成否が不明な資産を再利用しない。

snapshot不変性、Dispose／Retire、例外後の所有権、cache境界は既存のDrawing2D単体テストを活用します。fixtureの決定性、予定どおりの更新率、正確な計測境界と集計単位には、ハーネス用テストを設けます。

### 反復・集計・回帰判断

実GPUフレームrunnerの初期設定は独立した5組のA/Bプロセスとし、A→BとB→Aを交互にします。warmup数は事前pilotで200〜2,000 framesから決めます。定常caseは50 frameごとの直近4区間についてhost-frameとgpu-spanの中央値の最大／最小がそれぞれ1.05以内になることを初期の安定基準とし、A/Bで必要だった大きい方のwarmup数を両版へ固定します。取得不能なGPU指標は判定対象外と明記します。上限で安定しない場合はunstableを残します。

正式測定は固定warmupに続く2,000 measured framesを使います。非常に短いケースはpilotで総測定時間10秒以上となるframe数を選び、その数をA/Bで共通に固定します。周期的なD12やburstでは、負荷そのものの変化を不安定性と誤認しないよう、同じ位相の周期同士でpilotを比較します。測定を都合のよい値が出るまで続ける停止条件は設けません。

CPU部品のBenchmarkDotNetはRelease、5 launches、最低5 warmup iterations、15 measured iterations、目標250 ms/iteration、outlier除去なしを初期設定とします。JIT・tiered PGO・GCは製品の既定構成を主測定にし、PGO無効などは別の診断jobにします。従来のwrapperがtieringを無効にする結果とは設定を揃えるまで比較しません。

固定したフレームtraceを双方で使います。cold測定はwarmupと別のfresh processで複数回採ります。正式比較では`--quick`相当の短いjobを使いません。

- 生frameを保存し、プロセスごとにmedian、p95、最大値、予算超過数を求める。異なるプロセスの全frameを一つに混ぜて独立標本としない。
- 比較単位はcase＋profile＋scope＋機器。A/Bのプロセス単位の差・比とばらつきを提示し、信頼区間を付ける場合はframe単位でなく独立プロセスの組を再標本化する。5組で不十分なら反復を増やす。
- p99を採用判断に使う場合は、少なくとも10,000 measured frames／プロセスへ増やし、独立run間の再現性も確認する。2,000 frameから得たp99は参考値に留める。
- BenchmarkDotNetのiteration平均から得たp95はframe latencyのp95と呼ばない。CPU部品とフレームrunnerは別の表で報告する。
- 毎回の外れ値は残し、GC、温度・clock、他processの干渉が分かれば注記する。計測中は他のbuild／test／benchmarkを並行実行しない。

回帰判定は、機器ごとに同一版A/Aを測って雑音を把握し、caseごとの実用的な絶対差と相対差を事前に決めます。初回の一測定だけでmergeを止める共通閾値は作りません。ある用途の改善と別用途の悪化は別々に提示し、平均で相殺しません。

### ハーネスと成果物

新しいsuiteは`benchmarks/Graphics/`へ配置する案とします。共有fixture、CPU測定、GPU測定、結果集計の責務を分け、同じ入力を各経路へ渡します。

```text
benchmarks/Graphics/
  Lumyte.Graphics.BenchmarkWorkloads/   入力trace・期待状態・公開APIのfixture
  Lumyte.Graphics.Cpu.Benchmarks/       BenchmarkDotNetによるCPU測定
  Lumyte.Graphics.Gpu.Benchmarks/       工程別・pipeline・backend adapter
  scenarios/                           バージョン付きcase/profile定義
tests/Graphics/
  Lumyte.Graphics.Benchmarks.Tests/     入力・counter・集計・所有権の検証
artifacts/graphics-benchmarks/<run-id>/
  manifest.json
  frames.jsonl
  checks.jsonl
  summary.json
  images/
```

構成は測定責務の分離を示します。CPUの図形測定にnative ParleyやVulkanを必須にせず、Text・UI・backend fixtureの依存を必要な場合だけ読み込める構成にします。既存の古い比較ツールにはその実装・測定条件を残します。

manifestにはsource commit／dirty diff、harness commit、scenario version／入力hash／seed、backend／実device／driver、OS／CPU／.NET／JIT・PGO／GC、shader hash、font・texture hash、target／DPR／AA、cache予算、snapshot世代、frame並行数、計測区間・counterの取得方法を記録します。CLIの指定だけでなく、作成されたdeviceの情報を保存します。

frame行はrun ID、case、profile、mode、論理frame ID、入力ID、cold／warm／burst区分、工程時間、GPU queryの有効性、仕事量を持ちます。別runで異なるschemaやscenario versionをそのまま比較しません。JSON出力は計測外にまとめ、生データと画像は`artifacts/`またはCI artifactに保存します。測定値や生成レポートはGitへ追加しません。

## 検討した代替案

### 矩形数と平均フレーム時間だけを使う

条件を揃えやすく、単純な描画の固定費を確認できる。一方、文字組み、path評価、画像転送、clip、効果の負荷と遅いフレームを説明できないため、静止矩形は基準ケースとして残し、用途を代表する総合指標にはしない。

### 全用途の結果を一つのスコアへまとめる

順位は付けやすいが、更新頻度や用途の重みで結論が変わり、局所更新の改善が全体更新や保持量の悪化を隠す。caseごとの時間・仕事量・メモリを比較し、採用する利用側が必要な用途から判断する。

### 部品測定だけ、または用途別測定だけを行う

部品測定は原因を絞りやすいが、UI更新・layout・資源再利用の組合せを評価できない。用途別測定だけでは、改善が入力生成・CPU記録・GPU処理のどこで起きたか切り分けにくい。同じ入力を使えるfixtureを共有し、両方の層で確認する。

### 全パラメーターの直積を毎回実行する

網羅性は高いが、機器占有時間と結果の確認量が増える。標準profile、単一要因の比較、重要な相互作用、限界試験を分け、各結果に実行した条件を残す。

### 保持方式の内部ページ編集を主比較にする

更新機構そのものの費用を切り分けられるが、公開APIで必要なCanvas再記録、内容置換、資産の寿命管理が抜ける。主比較は公開経路を使い、内部編集は測定範囲を明記した診断に限定する。

## 結果と影響

開発者は、同じ見た目の更新でどの工程の費用が変わったかを、仕事量と画素の一致を伴って説明できる。内容の細分化による更新量削減とdraw数・cache管理費用の増加を同じ条件で比較し、2Dの一用途で得た改善を他用途へ外挿することを防げる。

測定側には、決定的な入力、独立した描画参照、backendごとの計測能力、資産の完了・退役、反復と結果保存を管理する負担が増える。製品の公開APIと既存ケースの意味は維持し、新しい条件には別のscenario versionを付ける。既存の結果は入力・範囲・runtime条件を揃えた場合だけ比較対象にする。

測定契約は性能改善を保証しない。専用GPU環境での結果、software GPUでの正しさ、表示を伴う遅延はそれぞれの範囲で報告する。CPUの小規模fixture検証は通常CI、software GPUは画素・寿命の確認、実GPUの性能測定は機器を固定した実行とし、通常CIの負荷変動で性能順位を判定しない。

### 受け入れ条件

- 2Dの12シナリオ、用途別UIの7シナリオ、共通Graphicsの6領域について、入力・対応経路・測定区間・期待状態を識別できる。非対応経路はN/Aと理由を残す。
- 固定4,096命令の粒度比較で画素と描画順が一致し、変更件数、再記録命令数、実draw数、compile／reuse、保持量を比較できる。無変更Canvasは記録を再利用する。
- CPU・GPU・表示の範囲、初回・定常・burst、1／3 frames in flightを区別し、最終drainと資産退役を含む時間を再計算できる。
- 同じ論理frameを独立した参照と照合でき、遅延やsampleの退役順が違っても入力IDを取り違えない。失敗・非対応・予算超過を正常値へ置換しない。
- 生データ、manifest、scenario versionからプロセスごとの分布とA/Bの差を再計算できる。結果に含めた機器・counter・画質・cache条件が追跡できる。
- 通常の単体テストは経過時間の閾値に依存せず、benchmark fixtureの決定性、入力の公平性、計測境界と所有権を検証する。
