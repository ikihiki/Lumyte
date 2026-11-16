# ADR-MAPS-0001: 既存UI上の地図描画と容量制限付きタイル配信

- 状態: 採用
- 日付: 2026-11-16
- 基準コミット: `5d6f61bb6983901ace58aca180fbe44a9f518b38`

## 背景

地図を使うアプリと描画性能の評価では、道路や建物を既存UIの中へ描き、移動した範囲のデータだけを順次読み込みたい。基準コードにはUIの描画・入力・保持型描画とResourcesの非同期構築があるが、地理座標、タイル選択、地図の描画規則を接続する機構がなく、利用者が一式を組み立てる必要がある。

[UIの描画契約](../ui/UI-0004-drawing-and-clipping.md#決定)は、Elementから既存Canvasへ記録し、GPU実行をホストに残す。[親RenderGraphへの接続](../ui/UI-0008-render-graph-and-resource-lifetime.md#決定)の資源寿命も維持する。基準コードの`src/UI/Lumyte.UI/UiElement.RetainedDrawing.cs`には`CacheRetainedDrawing`、`InvalidateDrawing`、`ValidateRetainedDrawing`があり、`UiDrawingScene2D.cs`は既定の自身→子の順序でElementのローカル描画を再利用する。

Resourcesの連続ストリームは、[独立sessionの設計](../resources/RESOURCES-0005-bounded-streaming.md#決定)と基準コードの`src/Core/Lumyte.Resources/ResourceStreamSession.cs`で確認できる。しかし地図の要求は単一cursorではなく、表示範囲で優先順位と必要集合が変わる独立タイルの集合である。連続ストリームのAPIへ地図の空間選択を押し込めない。

## 決定

OpenStreetMap由来のベクトルタイルを、UIから独立した地図sessionで選択・取得し、既存Canvasへ描くUI部品を追加する。Resourcesとその実行器、UIのElement・入力・保持型描画、アプリのframe寿命を再利用し、地図固有の実装を座標変換、描画規則、空間的な読み込み制御へ限定する。

### 利用者の変更前・変更後

変更前は地図部品がない。カスタム`UiElement.OnRecord`へ道路・建物の描画を実装するだけでは、データ取得、穴付き形状、座標変換、パン、取消、キャッシュの管理は利用者に残る。存在しない旧地図APIとの比較は行わない。

変更後の最小例は次のとおり。以下は提案APIであり、`resources`は地図タイルのpipelineとsourceを設定済みの借用`ResourceManager`、`tileSet`はデータ集合とスキーマを固定した記述、`style`は対応する描画規則である。これらの構築とアプリ登録は省略するが、所有と終了順は省略しない。

```csharp
using Lumyte.Maps;
using Lumyte.Maps.UI;
using Lumyte.UI.Application;

// IUiApplicationContent実装のInitializeAsync内。sessionはcontentが所有する。
map = new MapSession(resources, tileSet, style, new MapSessionOptions());
map.SetCamera(new MapCamera(139.7671, 35.6812, 14));
await context.View.SetInitialRootAsync(
    new MapView { Session = map, ClipToBounds = true }, cancellationToken);

// 同じcontentのAdvance内。待機せず、上限内で完了の採用と要求配送を進める。
map.Advance();

// BeginStop内では受付と公開を止める。IsStoppedはmap.IsStoppedを反映する。
map.BeginStop();

// contentのRetireAsync内。ホストがGPUをdrainした後に呼ぶ。
await map.DisposeAsync();
// ReleaseAsyncでは所有するResources等を解放する。借用Managerは解放しない。
```

`MapView`のサイズは通常のUIレイアウトが決める。サイズ、カメラ、タイル到着が変わると地図の表示が更新される。Widget宣言の作成だけではI/Oを開始せず、`Compose.Use`に置いただけで非同期解放されるとも解釈しない。

### 責務と依存方向

- `Lumyte.Maps`は地理座標、タイル集合、描画規則、session、CPU描画記録を持ち、ResourcesとDrawing2Dを参照する。UI、window、OS入力へは依存しない。
- `Lumyte.Maps.UI`は`MapView`宣言と`UiElement<MapView>`を持つ。Mapsと既存UIを参照し、既存の描画・入力hookだけで接続する。
- `Lumyte.Maps.Mvt`はMVTの読み取りと、Resourcesの型付きpipelineへの接続を持つ。外部decoderの型を公開境界へ漏らさない。
- `Lumyte.Maps.Http`は明示設定した配信元をResourcesのsourceへ接続する。HTTPや圧縮の制限はここで適用する。ローカル入力では不要である。
- アプリのcontentはsessionを所有し、既存`IUiApplicationContent`で起動、`Advance`、停止、GPU完了後の解放を接続する。地図専用window loop、内側RenderGraph、GPU submissionを作らない。

基準コードの`src/UI/Lumyte.UI.Application/IUiApplicationContent.cs`に上記の公開hookがある。一方`IUiApplicationParticipant`と`UiApplicationDispatcher`はinternalであり、別assemblyの地図機能の接続先にはしない。汎用UI・Resourcesの既存公開宣言とGraphicsの既存clipの意味は維持する。地図assemblyに加えて、Graphicsへ描画領域の所有を分割する専用clipを追加する。

### 入力形式と描画規則

実行時の入力は、XYZ形式で座標を付けたMVT 2.xのタイルとする。OSM XMLや`.osm.pbf`からの抽出・単純化・タイル生成は事前処理である。MVTの`.pbf`とOSMの`.osm.pbf`は別形式であり、拡張子だけでdecoderを選ばない。

MVTはレイヤー名や属性の意味を統一しない。タイル集合はデータ集合ID、不変の内容版、sourceのrevision方針、対応zoom範囲、座標規約、スキーマ識別子、帰属表示を必須とし、描画規則はそのスキーマへ明示的に対応付ける。TMSのY反転は入力adapterで一度だけ行う。任意の配信元で同じ見た目になる保証は設けない。

描画規則は版を持つ不変値で、背景、面、線のレイヤー順、属性条件、色、論理pixelの線幅、結合・端点を定める。初期の対象は道路、建物、水域の面と線とし、完全な外部スタイル仕様、地球儀、傾斜、経路探索は今回決める範囲から外す。文字は既存の文字描画へ接続するが、ラベル候補の選択・衝突回避と曲線沿い配置は独立した判断として扱う。

decoderは既存のMVT実装を優先してadapter内で利用する。使用する依存の採否には、ライセンス、管理状況、Browser/AOT、穴付きポリゴン、不正入力と容量制限への対応を要求する。これらを未検証のまま互換性を約束せず、採用したdecoderによって公開地図モデルを変更しない。

### 座標と表示範囲

`MapCamera`は経度、緯度、連続zoomをdoubleで持ち、回転は0固定とする。非finite値は拒否し、経度は[-180,180)へ正規化、緯度はWebメルカトルの限界である約±85.05112878度へ制限する。zoomはタイル集合の対応範囲へ制限し、境界での入力も決定的に扱う。

zoom 0の世界幅を512論理pixelとし、世界幅を`512 × 2^zoom`とする。取得zoomは連続zoomのfloorを対応範囲へ制限し、画面上では残りの倍率を変換で適用する。MVT各レイヤーの`extent`を使い、4096を固定値として仮定しない。

世界座標と表示範囲の計算はdoubleで行い、カメラ近傍の原点を差し引いてからCanvasのfloat座標へ変換する。高zoomで大きな世界座標をそのままfloatへ落とさない。DPIはホストのCanvas変換で一度だけ適用し、地図の線幅や入力座標は論理pixelで扱う。

Xは世界を繰り返すが、取得IDは`x mod 2^z`へ正規化し、表示位置の世界周回番号を別に持つ。Yは繰り返さない。日付変更線の両側で同じタイル取得を共有できる。表示範囲が0のとき新規要求を止め、未使用の先読みを取り消す。

### タイルの描画と保持

地図は`MapView`のローカルboundsへ矩形clipして既存Canvasへ記録する。`OnRecord`ではI/O、待機、デコード、session公開、GPU準備を行わず、採用済みの不変CPUデータだけを読む。記録例外時は既存UIと同じくホストがframe全体を破棄し、部分記録を成功frameにしない。

面はMVTの外周・内周を保持し、穴や複数面を既存Pathの塗り規則へ変換する。道路は中心線と属性を保持し、既存strokeで幅・結合・端点を描く。塗り・strokeの準備を事前メッシュ化で一律に置き換えず、性能試験で評価するLumyte側の処理範囲を保つ。

描画順は、背景→スタイルレイヤー順→同一レイヤー内の明示したsort key→正規化タイルとfeatureの安定順とする。タイル単位で全レイヤーを描き切らない。道路の縁取りと内側も別の描画レイヤーとして処理し、非同期の到着順で重なりを変えない。

境界をまたぐ線と面はタイルのbuffer領域を保持してから描き、可視所有範囲のclipで重複を抑える。線を境界で切って新しい端点を作らない。通常のUI外周は従来のantialias付きclipを使い、隣接タイルの所有境界だけは独立した半透明coverageとして合成しない。

所有境界には`DrawingCanvas2D.ClipOwnershipRect`を使う。準備時のtargetのDPIと原点、記録時および再利用時の変換を適用したdevice座標で、pixel中心が矩形の最小側以上・最大側未満に入るpixelだけを所有する。そのpixelでは形状のantialiasを維持し、所有clip自身はcoverageを減らさない。カメラ、頂点、線幅、通常の外周clipをpixel境界へ丸めない。隣接タイルの共有辺は正規化した同じタイル角から計算し、floatへ変換した最小・最大の端点を直接渡す。原点と幅を独立に丸めて最大端点を再構成すると、小数zoomで一つのpixelを二重所有し得るため、タイル境界には端点overloadを使う。

基準コードでは同じ目的に通常の`ClipRect`だけを使用できる。変更後のタイル内部の記録例は次のとおりで、`canvas`はBegin済みの借用Canvas、`tileBounds`は論理座標の所有矩形、`tileMinimum`と`tileMaximum`は共有角から直接計算した最小・最大端点、`bufferedPath`は所有境界の外側も保持した不変Pathである。外周のUI clipとホストの準備・GPU寿命は変えない。

```csharp
using Lumyte.Graphics.Drawing2D;

// 変更前: 所有境界も通常のantialias付きclipとして別々に合成する。
canvas.Save();
canvas.ClipRect(tileBounds);
canvas.FillPath(bufferedPath, color);
canvas.Restore();

// 変更後: 所有するpixelを分割し、形状のcoverageだけを合成する。
canvas.Save();
canvas.ClipOwnershipRect(tileMinimum, tileMaximum);
canvas.FillPath(bufferedPath, color);
canvas.Restore();
```

専用clipは平行移動、軸に沿う拡大縮小・反転に対応し、DPIは準備時に一度だけ適用する。回転・せん断を含む変換は、保持型の再利用やReplayで加わる場合も準備時に`NotSupportedException`として明示的に拒否する。通常clipへ暗黙に戻さず、ホストは既存契約どおり失敗frameを破棄する。軸方向の隣接所有矩形、整数・小数DPI、半pixel位置、反転、保持型と直接描画の出力を検証する。実入力で確認するまでは無継ぎ目を保証しない。

`MapView`は既定の自身→子の記録順を維持し、`CacheRetainedDrawing`を有効化する。sessionの表示revision、カメラ、規則、サイズが変わると`InvalidateDrawing`し、`ValidateRetainedDrawing`でも借用依存の変更を照合する。変更のないframeで同じローカル記録を再利用できるが、一つのElement内のタイル差分だけをGPUへ更新する保証は追加しない。

保持型UIはElement単位の記録を再利用するため、パン・ズーム時は地図Elementの再記録が必要になり得る。これをタイル単位の保持や変換だけの更新と同一視しない。直接記録と保持型描画を同じ表示契約で利用できることを優先し、保持型で速いという結論は測定で確認する。

### 空間的なストリーミング

sessionは可視タイルと制限付き先読み集合を計算する。要求の優先順位は可視→既存表示の穴を埋めるもの→近傍の先読みとし、同順位では画面中心からの距離と正規化IDで決める。カメラが移動しても同じ内容の採用済みタイルは再利用する。

Resourcesの型付き取得で不変の`MapTileContent`を構築し、既存の実行器にI/Oとデコードを配送する。共有可能な構築と内容revisionはResourcesが担当し、sessionは必要集合・優先順位・採用を担当する。sessionごとの無制限な`Task.Run`、専用thread、独自の汎用job schedulerは作らない。

タイルは未要求→待機→取得・構築中→採用候補→表示中、または失敗へ遷移する。不要な待機要求は取り除くが、配送済みの`ResourceManager.RetainAsync`にはsessionの取消tokenを渡さず、成功・失敗の終端までsessionが所有して回収する。不要になった要求は採用しない印を付け、同時数と予約容量を実完了まで保持する。

基準コードの`src/Core/Lumyte.Resources/ResourceManager.cs`の`RetainAsync`は取消で取得待機だけを終え、共有構築を止めない。待機だけ取消して枠を返すと未追跡の構築が累積するため、この経路でI/Oやdecoderの即時取消は保証しない。構築結果を受け取ったら不要な参照を返し、共有構築を別sessionの都合で中断しない。HTTPの接続・読取り期限と構築容量は不変profileで制限し、実処理取消と完了handleを追加する汎用Resources API変更は含めない。

要求にはsession寿命ID、データ集合の内容版、既知の場合の期待入力revision、規則版を付ける。実際の入力revisionはsourceのsnapshotからデコード出力へ引き継ぎ、内容leaseのrevisionとともに完了時に確認する。現在の内容条件と必要集合を再確認し、期待revisionが未確定だった入力を固定した旧版として扱わない。単に古いカメラから発行されたという理由では捨てず、現時点でも必要なら同じ内容の結果を採用できる。停止済みsessionや異なる内容版の結果は公開せず、借用を返す。異なる規則版でもデコード結果は再利用できるが、旧規則の描画記録を新規則として採用しない。

単一タイルの取得・decode制限は、不変の`MapTileLoadProfile`としてsourceとpipelineの登録に固定する。入力・展開・デコードの総byte数、属性文字列の総byte数、feature数・頂点数、構築と描画用変換を合計した予約byte数を持つ。profile識別子はResourceIdへ含め、decoder/profileの版はvariantへも含める。sourceはResourceIdから同じ登録profileを解決し、stageはAddressから同じ制限を解決する。sessionの可変optionを共有構築へ暗黙伝達しない。

各sessionは待機数、実行中数、未採用完了数、公開数と常駐CPU byte数を制限する。`MapTileSet.LoadProfile`はsource/pipelineと同じ登録値であり、生成時に予約量がsession予算へ収まることを検証する。異なるprofileはkeyを共有せず、同一profileのcache hitでもsessionは出力の保持byte数を全額計上する。共有を理由にsession上の請求を割り引かない。物理共有量の重複排除はManager側の別台帳とする。HTTPのContent-Lengthだけを信用せず、読取りと展開の途中でもprofileを適用する。

受付前にprofileの`MaxReservationBytesPerTile`を予約し、実サイズ確定後に余剰を返す。decoderは文字列、属性表、索引、geometry、poolの実確保単位と内部scratchを含む総byte上限を割当て前に検査できるものを選ぶ。feature数・頂点数から無条件にメモリ量を推測せず、結果サイズの事後検査だけではこの契約を満たさない。

実行器の受付容量不足は待機へ戻し、通信再試行と混ぜない。完了領域の空きを予約できない要求は配送せず、`MaxCompletedTiles`が実行中数より小さくても終端結果を未所有にしない。予約できない場合は先読みを抑え、非表示の未固定データを退避し、なお不足する可視タイルは待機または容量エラーとする。表示中データ、未採用完了、退役待ちも枠を占める。ホストが保持するCPU snapshotとrenderer準備領域は別予算に明示計上し、session予算をprocess全体の上限と呼ばない。Manager cacheと他sessionを含むprocess全体の上限にはManagerとホストのmemory設定も必要である。

CPUキャッシュキーはデータ集合・内容版・正規化タイルID・入力revision・decoder/profile版で、描画記録には規則版を加える。非表示の未固定データをLRUで退避し、表示、記録中の参照、実行中frameの所有物を途中で返さない。ディスクキャッシュは既定で無効とし、有効化時はbyte上限、入力版、期限、利用条件、削除方針を設定する。

データがない領域は背景で表示する。同じ内容版の親タイルを保持している場合のみ代替表示でき、準備済み子の領域を親から除外する。親子を重ねて半透明描画しない。旧内容版の表示を新しい内容版へ暗黙混在させず、内容切替時は表示集合を分離する。

### UI所有者への公開と入力

sessionの変更、`Advance`、`Record`は呼び出し側が直列化する。アプリ統合ではUI ownerで実行し、workerは有界な完了領域へ不変データを渡すだけでElementやCanvasを触らない。`Advance`は完了を上限内で採用し、表示revisionを更新して同期通知する。通知から再入したsession操作は拒否し、外部callbackが失敗した場合はホストへ伝播し、通知を無限再試行しない。

既存`IUiApplicationContent.Advance`が定常的に進めるため、タイル一件ごとのdispatcher投稿は不要である。UI queueが停止・満杯でも未所有のデータを捨て置かない。独自ホストは同じ順序を明示的に進め、Cooperative実行ではResourcesのjob pumpも継続する。

一つのsessionに接続する`MapView`は一つとし、二重mountを拒否する。Elementはmount時に変更通知を購読し、unmount時に解除する。Widgetを交換しても同じsessionのCPU状態は維持できる。unmountはsessionを暗黙にDisposeせず、所有contentが停止と非同期解放を担当する。

`LayoutOverride`は有限のサイズを確定し、必要なら`SetViewport`で需要だけを更新する。ここでは取得を開始しない。直後の描画は現時点の採用済み集合を使い、新しい要求は次の`Advance`で配送する。同一サイズの通知はrevisionを増やさない。

パンは既存pointer captureを使い、capture喪失・window非活性化で終了する。ホイールは`UiWheelInput(Position, Delta)`として既存のhit-testとbubblingへ配送し、pointer captureやkeyboard focusへ転送しない。位置とdeltaは同一acquisitionで取得し、DPIを入力へ再適用しない。`UiInputContext.PointerPosition`と`IsInside`はホイールにも適用する。wheelズームではポインター下の地理位置を保つ。通常のUI controlsやoverlayに入力優先順位を任せ、OSイベントの別経路を作らない。内部カメラを変更した後の描画・必要集合は同じカメラrevisionを使う。

### 所有権、失敗、終了

Resourcesは入力とデコード出力を所有する。sessionは取得完了の`ResourceReference<MapTileContent>`から`Pin`で内容世代の`ResourceLease<MapTileContent>`を取得し、観測revisionと容量を確認してから採用する。論理referenceだけを保持して描画中にHotReloadへ追従させない。referenceと世代leaseの返却責任を台帳へ記録する。CPU描画記録へコピーまたは不変共有した値は、その記録の寿命まで有効とする。lease返却後のpool領域をPathやDrawListが参照しない。sessionはManagerを借用し、Managerより先に終了する。

MapViewはCanvasを借用するだけでBegin/End、target clear、Prepare、Submit、Wait、Presentを行わない。frameのCPU snapshotと準備済みGPU資源は既存ホストが所有する。GPU viewを含まない面・線の記録と、外部textureを借用する記録を区別する。文字・画像等の外部資源を導入する場合も、そのownerをframeへ固定しGPU実完了後に返す既存契約を守る。

`BeginStop`は新規受付と完了の公開を止め、未配送要求を取り除く。配送済み取得は採用しない状態で終端まで回収する。`IsStopped`は稼働中にはfalseを返す。`Advance`は停止後も終了結果を回収し、`IsStopped`は停止要求後に処理中要求、未回収完了、sessionが配送した後始末がすべてなくなったときだけtrueになる。GPU完了はこの値に含めず、ホストが独立に確認する。

contentは`BeginStop`→`IsStopped`確認→ホストのGPU drain→`RetireAsync`でsession解放→slot切離し→`ReleaseAsync`で所有サービス解放の順とする。`DisposeAsync`単独でも受付停止と実処理完了を待つが、外部frameのGPU完了は待たないため、呼び出し側が先に保証する。配送済み取得は取消対応I/Oでもこの経路では中断せず、終了はprofileの期限または処理の実完了を待つ。

解放失敗時は未解放のleaseと後始末状態を保持し、次の`DisposeAsync`で再試行する。途中失敗でManagerを先に閉じず、並行Disposeは同じ試行を共有する。device復旧ではCPUタイルとカメラを維持できるが、旧GPU世代のviewを再使用せず、外部GPU資源を持つcontentは既存`IUiApplicationGraphicsContent`で再構築する。

404・空タイル、不正形式、認証失敗、容量超過、通信一時失敗を区別して表示状態へ記録する。404は期限付きの欠損として扱い、不正形式と認証失敗を自動反復しない。通信一時失敗だけ回数上限と待機を設け、429では配信元のRetry-Afterを尊重する。描画は取得失敗で停止せず背景・利用可能な代替表示を続け、構成不正やsessionの契約違反は呼び出し元へ例外を返す。

### 公開APIの差分

基準コードには地図APIがないため、以下は追加宣言の抜粋である。既存汎用APIの改名・削除はない。値の詳細な構築補助やpipeline登録補助は抜粋外だが、下記の公開値型を外部decoder固有型で置換しない。

```diff
+namespace Lumyte.Graphics.Drawing2D
+{
+    public abstract class DrawingCanvas2D
+    {
+        // device-spaceのpixel中心で半開矩形の所有を分割する。
+        // 形状のcoverageは維持。回転・せん断は準備時に拒否する。
+        public void ClipOwnershipRect(Rect2D rect);
+        // 共有する最大端点を幅から再構成せず保持する。両端はfiniteでminimum <= maximum。
+        // 座標系・変換制約・失敗条件は矩形overloadと同じ。
+        public void ClipOwnershipRect(System.Numerics.Vector2 minimum, System.Numerics.Vector2 maximum);
+    }
+}
+namespace Lumyte.UI
+{
+    // Positionはsurface-local論理pixel、Deltaは正規化step（Y正は上向き）。
+    public sealed record UiWheelInput(System.Numerics.Vector2 Position,
+        System.Numerics.Vector2 Delta) : UiInput;
+}
+namespace Lumyte.Maps
+{
+    public readonly record struct MapCamera(double Longitude, double Latitude, double Zoom);
+    public readonly record struct MapTileId(int Zoom, int X, int Y);
+    public sealed class MapTileContent { /* 不変のレイヤー、属性、面・線 */ }
+    public sealed class MapStyle { /* 対応スキーマ、版、描画レイヤー */ }
+    public sealed record MapTileLoadProfile
+    {
+        // 登録時に全値を検証し固定する。Idと版を取得keyへ含める。
+        public required string Id { get; init; }
+        // HTTP adapterの各取得試行の上限。正数。decodeのCPU期限保証ではない。
+        public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);
+        public int MaxInputBytes { get; init; } = 4 * 1024 * 1024;
+        public int MaxExpandedBytes { get; init; } = 16 * 1024 * 1024;
+        public int MaxDecodedBytes { get; init; } = 16 * 1024 * 1024;
+        public int MaxAttributeBytes { get; init; } = 2 * 1024 * 1024;
+        public int MaxFeatures { get; init; } = 100_000;
+        public int MaxVertices { get; init; } = 1_000_000;
+        // 各領域の同時生存量を含む総上限。部分上限より先に到達しても拒否する。
+        public long MaxReservationBytesPerTile { get; init; } = 32 * 1024 * 1024;
+    }
+    public abstract class MapTileSet
+    {
+        // 必須メタデータ。空文字、逆転範囲、Zoom範囲[0,22]外は構築時に拒否する。
+        public string Id { get; }
+        public string ContentVersion { get; }
+        public string SchemaId { get; }
+        public int MinZoom { get; }
+        public int MaxZoom { get; }
+        public string Attribution { get; }
+        public MapTileLoadProfile LoadProfile { get; }
+        // 正規化済みIDに同じ型付きkeyを返す。範囲外はArgumentOutOfRangeException。
+        public abstract Lumyte.Resources.ResourceKey<MapTileContent> GetKey(MapTileId id);
+    }
+    public sealed record MapSessionOptions
+    {
+        // 既定falseは固定集合。trueは明示した有限候補のみを可視選択する。
+        public bool SelectVisibleTiles { get; init; }
+        // 正数。MaxPendingTilesは実行中を除く。新規要求は容量不足時に待機する。
+        public int MaxPendingTiles { get; init; } = 128;
+        public int MaxInFlightTiles { get; init; } = 4;
+        public int MaxCompletedTiles { get; init; } = 4;
+        public int MaxPublicationsPerAdvance { get; init; } = 2;
+        public long MaxResidentCpuBytes { get; init; } = 128 * 1024 * 1024;
+    }
+    public sealed class MapSession : IAsyncDisposable
+    {
+        // resourcesを借用。null、スキーマ不一致、不正容量は生成時に拒否する。
+        public MapSession(Lumyte.Resources.ResourceManager resources, MapTileSet tiles,
+            MapStyle style, MapSessionOptions options);
+        public MapCamera Camera { get; }
+        public int SourceZoom { get; }
+        public System.Collections.Generic.IReadOnlyList<MapTileId> VisibleTiles { get; }
+        public void Pan(System.Numerics.Vector2 displacement);
+        public void ZoomAt(System.Numerics.Vector2 position, double zoomDelta);
+        public ulong DisplayRevision { get; }
+        // BeginStop/Advanceと同じownerから読む。GPU drain済みの意味ではない。
+        public bool IsStopped { get; }
+        // owner上で同期通知。callbackからsessionへ再入しない。
+        public event Action? Changed;
+        // 座標の正規化とzoom制限は「座標と表示範囲」に従う。
+        public void SetCamera(MapCamera camera);
+        // finiteかつ非負の論理サイズ。需要だけ変更し、I/Oを開始しない。
+        public void SetViewport(System.Numerics.Vector2 logicalSize);
+        // 待機せず採用・配送・停止結果回収を進める。
+        public void Advance();
+        // Begin済みcanvasへ採用済みデータを記録。状態を復元し、寿命は借用。
+        public void Record(Lumyte.Graphics.Drawing2D.DrawingCanvas2D canvas);
+        // 冪等。停止後のSetCamera/SetViewportはInvalidOperationException。
+        public void BeginStop();
+        // GPU完了は呼出側の責任。解放失敗は再試行可能。
+        public ValueTask DisposeAsync();
+    }
+}
+namespace Lumyte.Maps.UI
+{
+    public sealed class MapView : Lumyte.UI.Compose.Definitions.Widget
+    {
+        // 必須の借用session。mount中の二重接続はInvalidOperationException。
+        public required Lumyte.Maps.MapSession Session { get; init; }
+        // MapElementを生成。基底のWidget/Element分離を維持する。
+        protected override Lumyte.UI.UiElement CreateElement();
+    }
+}
```

`SelectVisibleTiles`を使う有限候補モードは、候補一覧を`MaxPendingTiles`以下へ制限し、採用済みデータを停止まで保持する段階的な接続である。未知のIDは背景にし、未配送の不可視候補は配送しない。配送済みretainは終端まで所有して予算内で採用する。常駐容量不足では後続需要を待機させる。この段階を空間優先queueやLRUの実装と同一視しない。

`MapSessionOptions`の既定値は測定目標ではなく、無制限な受付を避ける初期設定である。実行器の受付容量とManagerのmemory設定を検証し、単一タイルの予約が常駐予算を超える構成は生成時に拒否する。解放済みsessionの操作は`ObjectDisposedException`とし、未設定のviewportは0として扱う。

### 検証方針

- 固定のローカルタイルと決定的なfake sourceを使い、GPUなしで座標、日付変更線、緯度限界、extent、0サイズ、親子代替領域を検証する。
- 通信を意図的に遅延・逆順完了させ、重複取得、最新需要の優先、内容版切替、取消後の結果回収、二重mount、unmount後の通知停止を検証する。
- 入力、展開、feature・頂点、完了queue、常駐量それぞれの上限到達を注入し、先読み停止と可視領域の縮退、取消非対応I/O中の予約維持を確認する。Manager cacheも含めた計上範囲を記録する。
- 小さな既知形状で、穴、縁取り、描画順、半透明の親子切替、タイル境界、DPI、UI clipとoverlayを画素比較する。直接記録と保持型記録の出力を同じ条件で比較する。
- 成功・失敗・取消・GPU待機取消・device世代切替・解放再試行をfixtureで確認し、参照が早期返却されないことを検証する。実GPUとfixture、NativeとBrowserの検証結果を区別する。
- 性能試験は静止、パン・ズーム、タイル追加を分離し、固定データのhash・経路・seed・規則・DPI・画像寸法・描画品質を揃える。取得・デコードと、記録・準備・GPU実完了を別区間で計測し、未検証の改善を保証しない。

## 検討した代替案

- 既存の完成した地図rendererをそのまま埋め込む: 地図としての完成度を早く得られるが、描画が別rendererへ流れ、Lumyteの線・面・文字の評価にならない。decoderや入力adapterは既存実装を利用し、描画は既存Canvasへ載せる。
- 画像タイルだけを表示する: 配信・cacheの試験には適するが、主な負荷が画像合成になり、道路や建物のパス描画を評価できない。
- 通常clipのcoverageを所有境界でも別々に合成する: 汎用APIの追加は不要だが、隣接する半透明面の境界で同じpixelが分割されるとcoverageを二重に減らす。形状を丸めずに、所有clipとUI外周clipを区別する。
- 生のOSMをアプリ起動時に解釈する: タグと形状の自由度は高いが、広域データの関連解決、単純化、索引構築まで実行時へ持ち込む。入力は事前生成したベクトルタイルへ限定する。
- Resourcesの連続streamへ全タイルを流す: 固定bufferの仕組みは使えるが、独立IDの優先順位変更とcache共有がcursorの意味に合わない。型付き取得と空間的な需要制御を組み合わせる。
- UI本体へ地図専用描画・job機能を追加する: 接続は短くなるが、汎用UIの責務と資源寿命が地図へ結合する。独立assemblyと既存content hookを利用する。

## 結果と影響

利用者は、通常のUIへ地図部品を配置し、表示範囲に応じた段階的な表示と入力を利用できる。描画がLumyteの既存経路を通るため、実用的なパス負荷の性能試験にも使える。

地図sessionの所有、予算と実行器容量、配信元とスキーマ、帰属表示は利用者の追加責務となる。配信元は明示設定とし、公開OSMタイルサーバーを既定の大量取得先・性能試験先にしない。表示時はデータと配信元に必要な帰属を常時読めるUIに出し、fixtureの再配布・保存には元データのライセンスと配信条件を確認する。

汎用UIやrendererの既存利用者に移行は要求しない。一方、任意のスタイル互換性、全都市のラベル品質、タイル単位のGPU差分更新、一定FPSはこの判断だけでは成立せず、対応する検証なしに保証しない。
