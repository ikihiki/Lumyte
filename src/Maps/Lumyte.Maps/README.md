# Lumyte.Maps

CPU上で扱うXYZ識別子、倍精度のWebメルカトル座標、不変のMVT形状モデルを提供します。zoom 0での世界幅は512論理pixelです。Xは世界を繰り返し、Yは繰り返しません。各レイヤーは固有のextent、入力順、型付き属性、タイル外の余白を含む座標、ポリゴン輪郭の向きを保持します。

ローカルMVTのsourceとpipelineの使い方は[入力パッケージのREADME](../Lumyte.Maps.Mvt/README.md)を参照してください。OSM XMLや`.osm.pbf`の解釈、任意のオンライン配信元の選択、外部スタイルとの互換性は提供しません。対応するアプリケーションスキーマを明示して使用します。

## 固定タイル集合を表示するUI session（M2）

`MapSession`は`ResourceManager`を借用し、明示的に指定した正規化済みの固定タイル集合を所有します。既定ではカメラ変更時の新規タイル選択を行いません。HTTP、親タイルへの代替表示、先読みは行いません。親子タイルの半透明描画が重ならないよう、M2では異なるzoomを含む固定集合を明示的に拒否します。構築時に一覧の上限を検査してからコピーします。`SetViewport`は論理的な需要だけを変更し、幅・高さがともに正のとき`Advance`が取得を配送します。配送時は完了枠とprofileの予約量全体を確保し、取消しない`RetainAsync`の終端結果を回収するまで保持します。採用時にはResourcesの正確な内容世代をPinし、revisionとbyte計上を検査してreferenceとleaseを保持します。cache hitでも未cache取得と同じ保持サイズを全額計上します。

常駐予算に収まらない固定集合の残りは待機状態になります。表示中の固定タイルを退避したり、全タイル読込み済みと誤って報告したりしません。失敗は診断情報として保持し、実行器のqueue満杯は通信再試行ではなく待機へ戻します。

`BeginStop`は配送と公開を停止し、`Advance`は終端結果の回収を続けます。`IsStopped`はGPU使用の完了を意味しません。ホストはGPU使用を完了させてから`DisposeAsync`を呼びます。並行する解放呼出しは進行中の試行を共有し、未完了のreference／lease解放状態は再試行用に保持します。借用Managerはsessionとホストframeの退役まで生存させてください。所有者による操作はすべて直列化します。

`Record`は通常のアンチエイリアス付きviewport clipと、内部タイル境界のdevice pixel centerに基づく全pixel所有clipを使い、余白付きパスと輪郭方向を維持します。背景、全タイル共通のスタイルpass、正規化タイルと入力featureの順（同一sort key）で描きます。Canvas状態を復元し、読込みやGPU送信は行いません。座標はdoubleのカメラ原点を引いてからfloatへ変換し、ホストのDPIを二重適用しません。`MaxRecordingCpuBytes`は一回の記録でのパス・命令変換領域を保守的に事前検査します。ホストsnapshotと準備済みGPU資源には別予算が必要であり、process全体のメモリ上限や実測の高速化を示すものではありません。手作り試験だけで、任意の都市データとの互換性や無継ぎ目を保証しません。

`MapView`はsessionを一つ借用してmountし、変更通知の購読・解除と既存UIのローカル記録再利用を行います。unmountしてもsessionを解放しません。同じmount済みElementで借用sessionを交換する操作は公開前に拒否するため、交換時はElementの識別子を変えてください。

### 検証方法

リポジトリの環境とSDKで、`tests/Maps`内のRelease試験を実行します。実GPUでの読戻しは明示的に有効化します。

```sh
LUMYTE_MAPS_GPU_TESTS=1 VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json dotnet test tests/Maps/Lumyte.Maps.Session.Tests -c Release
```

CPU試験は記録までを対象とします。NativeのGPU実行、trim付きpublish、NativeAOT publish、Browser実行は別々の検証です。一つの成功から他の成功を推測せず、[検証手順](../../../tests/Maps/README.md)と実装の検証記録を確認してください。

### タイルの所有境界と固定集合の繰返し表示

内部の所有境界には`ClipOwnershipRect`を使います。targetのDPIと累積した軸平行変換を適用した後、device pixel centerが半開領域に含まれるかで所有を分割し、所有pixelでは形状のアンチエイリアス用サンプルを維持します。カメラ座標やパス頂点を丸めず、隣接する半透明の余白付きタイルで独立した部分clip coverageが乗算されることを防ぎます。平行移動、軸方向の拡縮、反転に対応します。保持型描画やReplayで加わるものを含め、回転・せん断は準備時に明示的に拒否します。外側のUI境界は通常のアンチエイリアス付きclipを維持します。

採用済みの正規化Xタイルは、日付変更線の両側を含む可視の世界周回位置へ繰り返し表示します。固定の取得需要は変更せず、M3のタイル選択を開始しません。各表示コピーを同じ描画予算へ計上して事前検査します。記録と予算検査は投影・可視性・paintと形状型の適合条件を共有し、画面外や不適合の大量featureだけで小さな可視地図が拒否されないようにします。極端な繰返し表示数は、無制限にviewportを走査せず、パス確保前に拒否します。

## 有限タイル集合のパン・ズーム（M3）

`new MapSessionOptions { SelectVisibleTiles = true }` ではコンストラクターの一覧を有限のローカル入力の全候補として扱います。この一覧は `MaxPendingTiles` 以下で、複数 zoom を登録できます。`SetCamera`／`SetViewport` は需要だけを変更し、次の `Advance` が可視の既知 ID を安定順で配送します。`SourceZoom` は連続 zoom の floor で、`VisibleTiles` は正規化済みの可視候補です。X の world wrap は同一取得 ID を共有し、Y は繰り返しません。未知の ID／未登録 zoom／範囲外は背景になります。

この段では採用済みタイルを停止まで保持します。移動後に不要になった未配送候補は配送しませんが、配送済みの retain は取消さず終端まで回収し、予算内で採用します。可視候補・採用総数は別です。全候補分の常駐容量がなければ後続需要は待機し、古い採用データを退避しません。空間優先 queue、LRU、親 fallback、先読み、HTTP は M4 以降であり、この有限集合を無制限の空間ストリーミングとは呼びません。profile、完了枠、常駐／記録予算と停止契約は固定モードと共通です。

`MapView` は既存 UI の入力に接続します。primary pointer のドラッグは capture を取得し、capture 喪失・非活性化・unmount で終了します。ホイールの上向き 1 step は連続 zoom +0.25、位置は論理 pixel で、限界緯度への clamp を除いてポインター下の地理位置を維持します。`Pan(displacement)` と `ZoomAt(position, zoomDelta)` は同じ camera 更新を独自ホストにも公開します。UI overlay／controls の hit-test と bubbling が優先し、地図専用の OS 入力経路はありません。

## 容量制限付き空間ストリーミング（M4）

4 引数の `new MapSession(resources, tileSet, style, options)` は、tile set の対応 zoom にある正規化 XYZ を viewport から選択します。5 引数の明示一覧 constructor は M2／M3 の動作を保ちます。独自の source は `MapTileSet.GetKey` で dataset・内容版・profile を含む安定した key を返してください。未提供のローカル入力は失敗診断と背景になり、この段では通信や自動の時間ベース再試行を追加しません。

`SetCamera`／`SetViewport` は需要を dirty にするだけで、複数回の更新は次の `Advance` で最新値へまとめます。可視→穴を補う同じ内容版の祖先→先読みの順で、画面中心からの距離、正規化 ID を tie-break に使います。X の世界反復は同じ取得 ID を共有し、Y は繰り返しません。要求前に可視列数×行数を double で検査し、`MaxPendingTiles` を超えた場合は部分集合を配送せず全体を背景にして `IsDemandOverflow` を立てます。サイズを縮めれば回復します。候補と scratch も有界で、spatial の pending／resident count は最大 65,536、先読み半径は 0～2、祖先深さは 0～22 です。

- `MaxResidentTiles` は表示／cache、実行中・未採用完了、退役待ちの slot を含みます。
- `MaxResidentCpuBytes` は取得前の profile 全額予約と、採用後の保持量、未回収の退役量を含みます。完了 slot は配送前に `min(MaxInFlightTiles, MaxCompletedTiles)` の範囲で予約します。
- `MaxPrefetchTiles` と `PrefetchRadius` は先読みの候補数と近傍範囲です。可視・祖先候補の後に残った pending 容量だけを使います。
- `MaxFallbackDepth` は要求／表示できる同じ内容版の祖先の深さです。準備済み子を優先し、親は欠けている子の ownership cell だけへ clip します。親と子を同じ半透明 pixel に重ねません。

未配送で不要になった要求は除去し、配送済み `RetainAsync` は取消 token を渡さず終端まで所有します。戻った時点で内容版・profile・現在の需要を検査し、同じ内容が再び必要なら古い camera からの結果も再利用します。`GetExpectedSourceRevision(id)` は manifest が期待入力 revision を固定する場合だけ override します。既定の null は「未知」で、実際の snapshot 由来 revision の空値は拒否します。`SetTileSet` は schema と単一予約量を検証して immutable な内容版を切り替え、旧内容を新しい表示へ混在させません。

非表示で固定されていない CPU tile は LRU で退役します。単に session の参照を減らして終わりにはせず、session が `ResourceManager.TrimAsync` を配送・回収します。Manager の build view は要求した tile だけでなく published map の snapshot を pin するため、同じ Manager を借りる全 MapSession（fixed／finite を含む）で小さな maintenance barrier を共有します。退役が始まると新しい Maps retain の受付を止め、受け付け済み retain の実終端回収後に trim し、その完了を観測してから退役分を返金します。後から加わった退役は別 batch として計上します。新しい Manager、job pool、thread、`Task.Run` は作りません。

共有した他 session の live reference／pin は trim しても返却しません。その保持量は他 session に全額計上されます。Maps 以外の外部 snapshot／build view、source が所有する入力 cache、host の描画 snapshot／GPU 準備領域は別の予算です。session の上限を process 全体の物理メモリ上限とは呼びません。Manager／memory scope と host の設定も必要です。全 MapSession owner は、非表示や停止中でも既に配送した結果と maintenance を回収するため `Advance` を進め、Cooperative 実行では共有 Resources job pump も継続してください。

容量不足では不要 cache を退役し、必要な可視 tile まで無限に入れ替えません。可視集合が常駐容量を超えれば残りは待機し、`IsCapacityBlocked` を返します。進行中の予約が解消できる間は親 fallback を保ちますが、親だけで新しい可視 tile を受付できない場合は親を退役して背景へ縮退することがあります。

`BeginStop` は未配送要求を取消し、新規公開を止めます。`IsStopped` は retain、退役、maintenance の実回収後だけ true です。`DisposeAsync` 単独でも同じ仕事を await し、解放失敗時は charge と後始末状態を保って再試行できます。同期 wait を UI owner へ押し込みません。失敗は `MaintenanceFailure`、進行は `PendingTiles`／`RetiringTiles`／`IsMaintenanceInFlight`／`ReadyVisibleTiles` で確認できます。`ReleasedGenerationCount` は自身の trim が実際に解放した Manager generation 数であり、性能値や session 専用の物理 tile 数ではありません。

fixed／finite の通常の停止・描画規則は変えませんが、最終 `DisposeAsync` は spatial と同じ終端回収／Manager maintenance を使います。他の spatial session が先に共有 pin を返していても、最後の fixed／finite owner の解放後に未使用 generation を残し続けません。通常の fixed／finite `BeginStop` の `IsStopped` は従来どおり配送済み処理の回収を表し、最終 cleanup は `DisposeAsync` を await して確認します。解放失敗後は `IsStopped` を false にして再試行を要求します。
