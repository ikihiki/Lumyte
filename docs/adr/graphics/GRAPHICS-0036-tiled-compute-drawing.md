# ADR-GRAPHICS-0036: 連続する複雑な2D描画をタイル単位のcomputeで合成する

- 状態: 提案
- 日付: 2026-11-16
- 基準コミット: `b9c93f001f960221dc13eed5f6edc0bf04914c02`
- 変更関係: 一部変更
- 変更元: [2D描画](GRAPHICS-0018-2d-graphics-engine.md#基本図形とpaintの標準gpu経路)の標準GPU実行、[固定長描画](GRAPHICS-0034-fixed-draw-records.md#決定)の連続recordの実行方式
- 変更対象: 明示的に作成したcompute対応Renderer2Dの組込み描画の実行・中間丸め
- 継続する判断: 既存constructorの標準描画、公開描画モデル、命令順、CPU clip正規化、資源所有と完了確認

## 背景

複雑なパスやpaintが連続する画面を描く利用者は、形状の評価に加え、描画ごとのattachment合成を負担する。現在の固定長recordは異種図形を連続格納できるが、画素を担当する処理が複数の描画に分かれ、同じ画素の結果を命令列全体でまとめて計算する経路を持たない。

基準コミットの `src/Graphics/Lumyte.Graphics.Drawing2D/Execution/Renderer2D.cs` の `PrepareAsync` は組込みの連続命令を `PreparedRecordBatch2D` へ渡す。同プロジェクトの `Rendering/PreparedRecordBatch2D.cs` と `Shaders/draw-records-fragment.slang` は一つのrecordの形状・paint・clipを4×4位置で評価し、その平均色をattachmentへ合成する。単純な矩形や保持資産の再利用もあるため、computeの使用だけを目的に既存経路を一律置換する理由はない。

## 決定

Renderer2Dに明示的に選べるタイルcompute経路を追加し、対象となる連続命令の画素評価とSrcOver合成を一つのcompute処理へまとめる。CPUが既存の形状・paint・clipを準備してタイル別の順序付き参照列を作り、GPUは各画素を一つのスレッドだけで計算する。既存の標準constructorと単色矩形の描画を維持し、対応環境・数値上の違いを受け入れた利用者だけが選択する。

### 利用者の変更前・変更後

変更前は基準コミットの公開constructorを使う。変更後もCanvasでの記録、PrepareAsync、Record、submit、完了確認と解放の順は同じで、rendererの作成だけを新しいfactoryへ変える。以下は `device`、同一deviceの `targetView`、記録済みの `list` の生成を省略した直接描画の例である。

変更前:

```csharp
using Lumyte.Graphics.Drawing2D;

using var renderer = new Renderer2D(
    device, DrawingCommandRenderers2D.CreateDefault());
```

変更後の提案例:

```csharp
using Lumyte.Graphics.Drawing2D;

using var renderer = Renderer2D.CreateTiledCompute(
    device, DrawingCommandRenderers2D.CreateDefault(),
    new DrawingComputeOptions2D { MaxWorkingBytes = 256L * 1024 * 1024 });
```

どちらも次の実行手順を使う。targetはColorAttachment、入力画像はSampledへ可視化済みであることを利用側が保証する。例外時、送信の有無が不明な資源や待機取消後の資源をGPU完了扱いで解放しない責務も変わらない。

```csharp
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;

PreparedDraw2D prepared = await renderer.PrepareAsync(list, new RenderTarget2D
{
    View = targetView,
    LoadOp = AttachmentLoadOp.Load,
});
IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
prepared.Record(commands);
commands.Finish();
IGraphicsSubmission submission = device.Queue.Submit([commands]);
await submission.WaitAsync();
commands.Dispose();
prepared.Dispose();
submission.Dispose();
```

保持描画の `PrepareAsync(DrawingSceneReadLease2D, ...)` と `PrepareAsync(DrawingSnapshot2D, ...)` も同じ選択を使う。借用をPrepare完了まで維持し、返されたframeへ送信結果をAttachSubmissionして、完了とcommand破棄後にRetireする既存手順は変更しない。

### 選択境界と既存経路の維持

compute選択はrendererの作成時に固定する。同一renderer内の配送は次の規則で決まり、計測時間、cache hit、メモリ圧力、命令数の閾値で切り替えない。

| 入力 | 実行 |
| --- | --- |
| 既定実装のFillPath、StrokePath、DrawLine、FillCircle、FillRoundedRect、FillRect、DrawPaintGraphで、paintが下記の対象内 | タイルcompute |
| SolidPaintNode2Dをrootに持つFillRect | 既存の矩形・固定長record経路 |
| DrawImage、DrawCoverageMask、DrawDistanceField、DrawMesh | 既存の描画経路 |
| 対象外paintを含む命令 | 既存の描画経路 |
| 登録表で差し替えたcustom rendererの命令 | その実装へ配送 |
| layer/effect命令 | 既存どおり直接描画ではNotSupportedException |

表で条件が重なる場合は、layer/effectの拒否、customへの配送、単色FillRectの既存経路、残りのcompute対象判定の順に優先する。対象paintはSolid、Linear、Radial、Sweep、Opacity、Transform、Clip、Compositeと順序付きLayers、およびそれらの子だけからなるgraphである。Image・DistanceFieldを到達可能な子に持つgraphは全体を既存経路へ渡す。paintの一部だけを別経路で評価しない。これはcompute内のtexture微分・sampling契約を新設しないための入力分類であり、準備失敗後のfallbackではない。

元の命令列を先頭から走査し、compute対象の最大連続区間を一つの合成区間とする。既存描画・custom renderer・保持内容ノードの境界で必ず区切り、区間の前後を並べ替えない。保持内容ノードをまたぐ統合は行わない。対象内の一命令だけでも同じ規則を適用する。空の描画を除去しても、そのために既存経路の両側を融合しない。

custom rendererの既定判定は `Renderer2D.IsBuiltinRenderer` と同じ実装identity判定を共用し、型名や描画内容からcustomを組込みと推定しない。factoryが使う組込みshaderは埋込みの既定実装に固定する。外部DrawingResources2Dのshaderをcomputeへ翻訳する契約は追加しない。

### CPU準備とタイル別参照列

処理は `Lumyte.Graphics.Drawing2D` assembly内へ置き、Graphics.Abstractions以外のGPU実行基盤を追加しない。既存のDrawList2D.Commands、DrawRecordPage2D、保持ページはinternalのまま扱い、別assembly向けの公開命令列やfriendアクセスを追加しない。

CPUは既存の平坦化・stroke展開・DrawRecord2Dの96 B格納・paint directory・NumericPayloadを使う。[固定長描画の格納契約](GRAPHICS-0034-fixed-draw-records.md#96-b-の格納契約)と[C#を正とする転送](GRAPHICS-0035-csharp-authoritative-shader-transfer.md#型とc転送配置)を維持する。shader間で形状・paintの点評価を共通化し、同じ演算順と8/16/256の作業配列選択、既存の深さ・作業量上限を保つ。

タイルはtarget-localの16×16画素である。各recordの保守的なDeviceBoundsとtargetの交差から、触れるタイルの半開範囲を求める。参照先の画素でも元のrecordの整数scissor範囲を先に検査し、タイルへ拡大した範囲を図形の有効範囲と取り違えない。面積0の範囲は参照を出さず、有限でないboundsは準備失敗とする。保守的なboundsが得られないDrawPaintGraphはtarget全体を使う。

二回のCPU走査でタイルごとの件数を数え、checkedなprefix sumで範囲を確保し、元のrecord順に参照を埋める。各タイルの参照は重複のない昇順である。内部の数値格納は次に固定する。uintは32 bit、要素は16 B境界、reservedは0とし、C#とshaderの配置を検査する。

| 格納 | stride | 内容 |
| --- | --- | --- |
| TileHeader | 16 B | uint TileX、TileY、FirstReference、ReferenceCount |
| TileReference | 16 B | uint RecordIndex、0、0、0 |
| TileColor | 16 B | float32 linear premultiplied R、G、B、A |

TileHeaderは参照を持つタイルだけをY優先・X昇順に格納する。TileColorはheader順に256要素を予約し、添字は `headerIndex*256+localY*16+localX` とする。空の合成区間はGPU資源もdispatchも作らない。線分のタイル別再分類は行わず、参照されたパスの形状評価は元の辺列を読む。したがって本方式だけで巨大な一つのパスの辺評価量が減るとは保証しない。

### GPUの実行と数値契約

computeのworkgroupは16×16×1とし、各スレッドが一画素を担当する。headerの配列をdeviceのdispatch各軸上限内の二次元格子に割り当て、余剰groupとtarget外laneは範囲検査する。group間の通信や画素色へのatomicは使用しない。各有効laneはTileColorを必ず上書きし、以前のframeの値を読まない。

画素pに対する描画kの入力色は、既存と同じサンプル `s=(floor(p)+(i+0.5,j+0.5)/4)`、i,j=0..3について、`Ck=sum(shape_k(s)*paint_k(s)*clip_k(s))/16` とする。[CPU clip正規化](GRAPHICS-0033-drawing2d-normalized-clips.md#数値規則と-16-サンプル相関)の完成した16 bit membershipを読み、CPUのbinary64判定をcomputeで再計算しない。clipの予算・captured transform・origin・DPRも維持する。

各画素の区間色を透明黒A0から開始し、命令順に `Ak=Ck+Ak-1*(1-Ck.a)` をfloat32で計算する。shape、paint、clipを独立に平均して掛けたり、全命令をサンプル位置ごとに先に合成して最後に平均したりしない。例えば同じ8サンプルを覆う不透明な赤と青をこの順で描くと、平均後合成の区間色は `(0.25,0,0.5,0.75)` であり、サンプルごとに合成した `(0,0,0.5,0.5)` ではない。

区間内の合成ではattachment形式への中間丸めを行わない。このため従来の描画ごとのRgba8SrgbまたはRgba16Floatへの丸めとは結果が異なり得る。さらに浮動小数点の結合順の違いにより、背景との最終合成もbit単位では一致しない。compute factoryはこの数値契約を選択する新APIであり、従来経路の画素完全互換を名乗らない。既存経路・custom・保持ノードの境界では通常のattachment丸めが入るため、ノード分割をまたぐ画素同一性も保証しない。

色空間、各描画内のサンプル相関、paintの隔離合成は従来どおりである。CPU oracleは同じ描画平均後の順序付きfloat32演算を正とし、出力形式への変換誤差とshape/paintの既存数値誤差を分けて検査する。従来経路との差を許容するために既存GPUテストのtoleranceを広げない。

### Load先を読まない最終合成と同期

TileColorは `IGraphicsBuffer<Vector4>` の `ShaderWrite | ShaderRead` 資源とする。同じ物理範囲を、compute専用の引数表にはShaderBufferAccess.ReadWriteだけ、最終描画専用の引数表にはReadOnlyだけで登録する。各passで対応する表を選び、一つの表へ同一範囲のReadOnlyとReadWriteを同時登録しない。基準コミットの `src/Graphics/Lumyte.Graphics.Shared/ShaderBindingData.cs` は選択表全体をbindingへ投影するため、二つの表の分離はbarrierとは別に必要である。storage textureやsRGB storage書込みは必要としない。

区間のcompute終了後、出力bufferへComputeShader/ShaderWrite→FragmentShader/ShaderReadのbarrierを記録する。最終passは一つのactive tileにつき一つの矩形をinstance描画し、各fragmentが対応するTileColorを一度読み、既存のpremultiplied SrcOverでtargetへ合成する。タイル境界の矩形は重ならず、target外を切り詰め、色を補間・filterしない。

透明から作った区間色を背景へ一度合成できるのは、対象命令の外側合成をSrcOverに限定するためである。paint内のCompositeはその命令内で隔離評価し、targetの背景色を入力にしない。背景を読むblendやfilterをこの経路へ混ぜない。

この構成によりLoad先のtextureにSampled、Storage、CopySourceを追加要求しない。targetは入口・出口ともColorAttachmentで、ClearはPreparedDraw2Dが全体の先頭で一度だけ行い、区間ごとの最終passはLoad/Storeとする。空描画のClearも実行する。描画間のColorOutputの依存を維持し、computeの前後に未終了passを残さない。

CPUで準備したrecord・directory・参照列はCopyWrite→ComputeShader/ShaderReadへ可視化する。最終passのTileHeader読出しにも対応するShaderReadの可視性を与える。出力bufferを別区間で再利用するときはFragmentShader/ShaderRead→ComputeShader/ShaderWriteも記録する。引数登録表のaccess admissionとGPU barrierは別の責務であり、どちらも省略しない。

### 上限、失敗と対応環境

MaxWorkingBytesは一つのPrepareで使用するcompute固有のCPU配列、upload、GPU入力、中間色、登録表の概算所有bytesを合計した上限である。共有する同一allocationは一回だけ課金し、GPU側だけを数えてCPU配列を無制限にしない。frame用の中間色は最大の区間の分を一つだけ確保し、順次再利用する。再利用した入力もそのPrepareが参照するallocationとして課金する。既存共通資産のretained予算は別途維持する。driver内部のpipeline・descriptor実装や物理VRAM全体の使用量を保証する値ではない。

| 対象 | 規則 |
| --- | --- |
| MaxWorkingBytes | 既定256 MiB。正数のみ |
| タイル参照数 | 全区間合計で最大4,194,304。各参照16 Bも予算へ課金 |
| record、geometry、paint、clip | 既存の受理上限を維持 |
| bufferとbinding | MaxBufferSize、MaxStorageBufferBindingSize、引数表の実binding上限以下 |
| workgroup | MaxComputeInvocationsPerWorkgroupが256以上、各軸sizeが16/16/1以上 |
| dispatch | MaxComputeWorkgroupsPerDimensionを各軸で満たす |

既存のDeviceCapsと引数表のadmissionを使い、存在しないCompute feature flagを仮定しない。shader targetに対応するcompute artifactと最終描画artifactを用意し、両方のpipeline作成成功を必要とする。capability不足、予算超過、索引・byte数のchecked overflowはPrepare時のNotSupportedExceptionとし、途中まで成功した結果を返さない。optionsの不正値はArgumentOutOfRangeException、既存の入力不正は既存の例外、取消はOperationCanceledException、確保・backendの失敗は元の例外を保持する。

CPUの一時配列・カウンターの確保前にも、その長さと保守的な所有bytesをcheckedで計算し、上限内であることを確認する。全区間を事前計数して予算とbindingsを確認してからGPU資源を確保する。固定長recordのページ境界では、paint等の参照を区間内の論理IDへ再配置してcompute専用の登録表でmaterializeする。最終描画専用の表とは統合せず、両表を別々にbinding admissionへ通し、両方の所有bytesを作業予算へ課金する。収容できなければ準備を拒否し、binding容量の都合で区間を描画先へ追加flushして中間丸めを変えない。入力分類後の失敗で既存描画へ戻すことも、GPU実行中にbufferを増やすこともない。

### 所有権、保持描画とRenderGraph境界

新しい内部batchは既存のIPreparedCommandBatch2Dを実装する。CPU準備はGPUへsubmitせず、Recordは一回だけ、内部のupload・compute・最終描画を呼出元のcommandへ記録する。rendererのDispose後も準備済みbatchの資源tokenは有効で、最後のGPU使用とcommand破棄が済むまで資源を保持する。[GPU寿命契約](GRAPHICS-0014-caller-managed-resource-validation.md#決定)を変更しない。

保持cacheに入るのは不変のrecord・paint・clip・タイル参照列と、その完了済みupload情報である。書換可能なTileColorとそれを含む登録表はframeが所有し、同時in-flight frame間では共有しない。共有immutable入力だけを既存のretained keyで再利用し、kind・順序・内容・transform・clip・extent・format・DPR・device/resource generationの変更をkeyに反映する。区間順序が変わればタイル参照列を再生成する。

既存の `IRetainedCommandBatch2D` / `IRetainedMemory2D` への参加を実装条件とし、immutable資産とframe scratchの所有者を分ける。既存cacheのassetは一つのframeへ排他的に貸し出されるため、その貸出し後、準備済みbatchへ内部のIComputeFrameBinding2D.BindFrameを一回呼ぶ。frameが持つComputeFrameResources2Dからscratchと区間用登録表を借用させ、戻ったbinding tokenもframeが保持する。RecordRetainedの公開されていない既存シグネチャは変えず、このbindingを介してscratchへ接続する。bindingなしのcompute Recordと二重bindはInvalidOperationExceptionとする。

完了とcommand破棄を確認したRetire、または確実な未送信中断では、tokenの解除を先に済ませてからassetをcacheへ返す。解除後のcached assetはframe・scratch・登録表への参照を保持しない。tokenは貸出し世代を照合し、古いcleanup再試行で次のframeのbindingを解除しない。直接描画もPreparedDraw2Dの内部ownerが同じbindingとscratchを持ち、完了後のDisposeで解除する。解除失敗時はassetを再貸出しせず、既存cleanup ownerが再試行を保持する。Retireするまでscratchを再貸出しせず、取消・device loss・送信結果不明では隔離する。cache上限は使用中frameを追い出さず、shared ownerのbytesは一回だけ数える。未送信の中断、部分準備の失敗、cleanupの再試行、HotReloadとdevice lossは既存の保持描画の状態遷移を使う。DisposeからWaitは呼ばない。

本選択は直接描画と保持描画のRenderer2Dだけに適用する。Drawing2D.RenderGraphはRenderer2D factoryを入力としないため、今回の公開APIから自動的にcompute化されるとは説明しない。既存Graph adapterの出力・外部資源宣言・layer/effect経路は変更せず、Graphのbatch収集へ本compute batchを渡さない。GPUでのflatten/binning、画像samplingを含むcompute、Graph統合、damage領域の差分描画は今回の対象外である。

### 契約とAPI差分

比較元は基準コミットの `src/Graphics/Lumyte.Graphics.Drawing2D/Execution/Renderer2D.cs` である。既存の5引数・6引数constructorを削除・変更せず、新規factoryとoptionsを同じnamespaceへ追加する。既存のDI登録とResources連携は従来constructorを使い続ける。factoryの返す型、Prepareの戻り値、公開Canvas・sceneのAPIは変えない。内部実装型は公開しない。

```diff
 namespace Lumyte.Graphics.Drawing2D
 {
+    public sealed record DrawingComputeOptions2D
+    {
+        // 一つのPrepareのcompute固有所有bytes上限。正数、既定256 MiB。
+        public long MaxWorkingBytes { get; init; } = 256L * 1024 * 1024;
+    }
+
     public sealed partial class Renderer2D : IDisposable
     {
+        // 描画平均後のfloat32区間合成を明示選択。自動fallbackなし。
+        // deviceとrenderersは借用。optionsは作成時に検証・snapshot化する。
+        // GPU資源はPrepareで遅延生成し、rendererと準備ownerが共有する。
+        // custom rendererへの配送は維持。外部DrawingResources2Dは受け取らない。
+        public static Renderer2D CreateTiledCompute(
+            IGraphicDevice device,
+            DrawingCommandRenderers2D renderers,
+            DrawingComputeOptions2D? computeOptions = null,
+            DrawingRetentionOptions2D? retentionOptions = null);
     }
 }
```

保持batchとframeの接続に必要な内部追加は次のとおりである。対象namespaceとassemblyは上と同じで、既存の外部custom batchへ実装を要求しない。ComputeFrameResources2Dはframe専有のscratchと登録表のownerであり、そのDisposeはGPU完了、または確実な未送信を確認した後に行う。

```diff
+internal sealed class ComputeFrameResources2D : IDisposable
+{
+    public void Dispose();
+}
+
+internal interface IComputeFrameBinding2D
+{
+    // 排他的なasset貸出し中に一回。frameは借用、tokenは世代付きbinding解除のみ。
+    IDisposable BindFrame(ComputeFrameResources2D frame);
+}
```

## 検討した代替案

- 全描画をcomputeへ移す: 経路は統一できるが、単色矩形の既存最適化とcustom rendererを失い、画像のsampling契約も広げる。対象を明示した連続区間に限定する。
- 全画素が全命令を走査する: 実装は単純だが、離れた図形まで画素ごとに判定する。CPUで保守的に絞ったタイル別参照列を使う。
- GPUがflatten・binning・可変長確保まで行う: CPU前処理を減らせるが、既存のCPU clipの数値正本と有限予算、overflow処理を同時に変える。今回は前処理の境界を維持する。
- computeで頂点だけを生成する: 固定機能を活用できるが、画素を担当する処理を連続命令間で共有するという今回の判断とは異なる。
- targetを直接storageへ変更する: 最終passを省けるが、sRGB、Load先のusage、swapchainの対応を制限する。書込可能bufferから通常のattachment合成へ接続する。
- パスの16 bit shape maskをCPUで作る: 辺評価をGPUから除けるが、面積比例のCPU作業と転送が増え、shapeの数値正本も変える。既存のshape点評価をcomputeで使う。
- sampleごとにシーン全体を合成する: 別のAAモデルとしては有効だが、描画ごとに平均してから合成する既存の意味を変える。採用しない。
- attachmentの中間丸めまで再現する: 互換性の利点はあるが、backendの変換規則をcomputeで模倣する必要がある。既存経路を残し、compute選択時の丸めの差を公開契約にする。

## 結果と影響

対象の連続描画では画素色を担当スレッド内で保持し、区間のattachment合成を一回にまとめられる。利用者は作成APIだけで選択でき、targetのusageやGPU完了までの所有責任は増えない。一方、タイル参照のCPU生成と中間色buffer・最終passを負担し、孤立した図形や巨大な一パスで速くなる保証はない。

既存constructorの挙動と旧ADR本文を維持する。本書は新しい経路の設計提案であり、性能向上や描画品質の検証済み状態を意味しない。

### 受け入れ条件

- 参照列の順序・範囲・重複なし、16画素の境界、右下の端数、空区間、単一命令、custom前後、単色矩形前後、保持ノード前後を確認する。boundsの過小評価による描き落としを独立oracleで検出する。
- 形状とpaintのpoint evaluatorをfragmentとcomputeで共通化し、NonZero/EvenOdd、strokeのcap/join/dash、退化gradient、paintの隔離合成、clipの全16 bit、origin/DPRを検証する。平均後合成の赤青例と、shape・gradient・clip相関の独立期待値を含める。
- 中間TileColorを読んだテストでは単純な正確表現可能値をbit単位で確認する。最終形式への変換だけのテストはCPUのsRGB変換・premultiplyを分離し、Rgba8Srgbは各channelで1 code以内、Rgba16Floatは1 ULP以内とする。一般shape/paintでは既存の数値検証を維持し、旧attachment中間丸めとの差を別に記録する。
- Clear/Load、透明区間、HDRを保持したRgba16Float背景、画像・MSDF・meshとの交互実行を検証する。RenderAttachmentだけのtargetで成功し、active targetをsamplingしないことを確認する。
- capability不足、予算と参照上限の境界、checked overflow、binding不足、pipeline生成失敗、取消では準備結果を返さず、実行方式を変更しないことを確認する。
- GPU validationでupload→compute→fragmentとscratch再利用のbarrier、passごとに分離した登録表のRead/ReadWrite、wgpuで同一範囲のReadOnly/ReadWriteが同時bindingされないこと、最終tile描画の非重複を確認する。Vulkan、DirectX、wgpuの対象backendごとに実行し、software GPUの結果をhardwareの性能根拠にしない。
- 保持資産の再利用と失効、複数in-flight frameのscratch独立、Retire前の非再利用、未送信中断、待機取消、device loss、部分cleanup失敗の再試行を検証する。
- 複雑パス、深いclip、半透明重なり、単純矩形、画像主体、静的保持、全面更新を分け、同じ入力と完了境界で比較する。CPU準備・GPU区間・最終合成・転送量・中間bytesを分離し、frame全体とbounded frames-in-flightも測る。未測定の高速化やdraw数削減だけを採用根拠にしない。
