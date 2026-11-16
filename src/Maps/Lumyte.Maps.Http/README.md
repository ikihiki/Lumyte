# Lumyte.Maps.Http

明示登録した有限のHTTP(S)タイルをResourcesへ接続する、BCL HttpClient adapter。
公開配信元への自動接続、料金の発生するサービス、ディスクcacheはない。
MVT decoderはLumyte.Maps.Mvtの既存実装をそのまま使う。

    // scope/endpointsは信頼したアプリ設定から作る。属性からURLを開かない。
    var tile = new MapTileId(0, 0, 0);
    var scope = new Uri("http://127.0.0.1:8080/tiles/");
    var profile = new MapTileLoadProfile("local-http");
    var policy = new HttpMvtPolicy(scope, accessPartition: "public-fixture");
    var registration = new HttpMvtRegistration("fixture", "v1", profile, policy,
        new Dictionary<MapTileId, Uri> { [tile] = new(scope, "0/0/0.pbf") });
    await using var source = new HttpMvtSource(registration);
    var stage = new MvtResourceStage(registration.Mvt);
    // source/stageを同じResourceManagerへ渡し、registration.GetKey(tile)を取得する。
    // managerとsessionを先に停止・解放し、その後sourceを解放する。

## identityと所有

- dataset/version、profile、全endpoint、HTTP policy、非秘密のaccess partitionがkey identityを作る。
  正規化XYZの重複を拒否し、stageは**同じregistration instance**のsnapshotだけを受け入れる。
- 認証が内容を変える場合は、権限集合を表す非秘密partitionを必ず分ける。
  token・token hashをpartitionやURL、dataset、versionへ入れない。credentialはAuthorization headerのみ。
  partitionが権限を正しく分けることはホストの責務で、権限が変わる場合は別registrationを作る。
- sourceがprivate immutable byte配列を保持し、snapshotはコピーせず借りる。revisionは受理した**展開後bytes**のSHA256。
  replacement後も旧snapshotのbytes/revisionは変わらない。snapshot解放まで旧bufferも容量を占める。
- MvtのBoundedMvtRegistration.CreateSnapshotは信頼したadapter境界。渡したmemoryを不変に保つ責任は呼出側にある。
  ReadOnlyMemory自体が任意の呼出側storageを不変にするものではない。decoder改変やMvt→Http依存はない。

## cache・容量・停止

GetRevisionAsyncとOpenSnapshotAsyncは同じ同期cacheを使う。Resourcesの構築前/後revision照会は、
通常freshness中は追加通信をしない。freshness終了後はETag付きconditional GET。304はpositive cacheと
送ったvalidatorがあり、返ったETagが同じ（または省略）で最終representation URIも一致する場合だけ受理する。偽revisionは作らない。
404はnegative TTL中null revision、snapshotはFileNotFound。401/403はauthorization fatalでretryしない。

- explicit endpoint数、positive/negative metadata数はMaxEntries以下。URI長2048、identity文字列長、ETag長1024も制限する。
- MaxResidentBytesはcacheと旧snapshotの**配列capacity全体**、受信buffer cap+1、展開workspace予約を含む。
  gzip/br decoderとstream scratch用に各処理で32 MiB + 8 KiBを保守的に予約する。
  小さいpayloadでも約32 MiB以上のtransport予算が必要。ネットワーク/TLSランタイム全体や
  Resourcesの出力cache、session、rendererを含むprocess全体の上限ではない。
- HTTP source全体を直列化して同一tileの競合・二重downloadを防ぐ。MaxInFlightは実行+待機の受付上限で、
  満杯は即時capacity error（無限queueではない）。parallel throughputの最適化は未実装。
- freshness中のcacheにもsnapshot pinを課す。容量不足は古い非pin cacheを退避し、なお不足なら取得前に拒否する。
- ResponseHeadersReadでcounted wire/expanded読み取りを行う。Content-Lengthの事前検査に加え実読取りを数え、
  不明/偽length、cap+1、gzip/br展開超過を拒否する。多重/未知encodingと未知media typeは受理しない。
  wireはContent-Encoding展開前のHTTP entity bodyであり、TLS/chunk framing/headerを含むTCP byte数ではない。
  owned handlerのresponse header上限は16 KiB。HTTP framingより後の余剰TCP bytesをentity bodyとは数えない。
- injected TimeProviderがtotal deadlineとRetry-After（delta/date）を支配する。retryは429/500/502/503/504だけ、
  有限回。過大/不正Retry-Afterは即時拒否。接続・headers・body・retry待ちを同じdeadlineに含める。
- caller cancellationとsource disposalは待機/読取り/retry待ちへ伝える。source disposalは全admitted処理の終端を回収。
  sessionが共有ResourcesのRetain待機を取り消しても共有構築そのものを中断する保証は追加しない。

## handlerと配信条件

owned native SocketsHttpHandlerはautomatic redirects/decompression/cookies/ambient proxyを無効にする。
manual redirectは有限回で、同じscheme/authority/portとdirectory scope内のみ。別originへのAuthorization転送はない
（別origin自体を拒否する）。URL userinfo/query/fragmentとpercent-encoded path/backslashを拒否する。
BorrowTrustedClientはredirect/decompression/cookies/credential自動挿入無効、default headerなし、取消を順守するhandlerの
**検証不能なtrusted handler contract**を明示承認した場合だけ使える。借用clientの所有権は移らない。

strict transportはBrowser fetchでは保証できないため**Browserでは明示的に未対応として拒否**する。
M1のportable memory経路とBrowser HTTPを混同しない。Browser向け配信には別途CORS、fetchのredirect/encoding、
認証の設計と検証が必要。アプリはデータの帰属表示（OSMならOpenStreetMap contributors等）、配信元の利用条件、
取得頻度と認可を確認する。loopback fixtureはOSMデータを使わず、外部利用条件を代行しない。

## 検証

dotnet test tests/Maps/Lumyte.Maps.Tests。scripted handlerと実loopback TCP fixtureのみを利用し、
外部公開サーバーに依存しない。200/304/404/401/429/selected5xx、conditional ETag、redirect/auth、
body framing/length、gzip/br cap、deadline/cancel/stop、旧snapshot寿命、Resourcesの実decodeを検査する。
