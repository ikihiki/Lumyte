# M5 HTTP source実装の公開契約

M3基準から独立実装したtransport差分。M4 session APIは追加・変更しない。
MAPS-0001と配信計画のM5へ親差分で反映するためのAPI契約メモ。

## assemblyと公開境界

- Lumyte.Maps.Http → Lumyte.Maps.Mvt → Lumyte.Maps。MvtからHttpを参照しない。
- HttpMvtPolicy: immutable scope/access partition、wire/expanded limit、cache resident/entry limit、
  in-flight admission、freshness/negative TTL、total deadline、retry/Retry-After/redirect limit。
- HttpMvtRegistration: finite explicit XYZ→URI一覧、dataset/version/profile/policyからkeyを作る。
  Mvtプロパティで同じBoundedMvtRegistrationをdecoder stageへ渡す。
- HttpMvtSource: IResourceSource/IAsyncDisposable。owned native handlerまたは明示した
  BorrowTrustedClient contract。TimeProviderとAuthorization headerを別々に注入する。
- BoundedMvtRegistration: transport-neutral finite ID一覧、profile、normalized key、bounded snapshot admission。
  CreateSnapshotはReadOnlyMemoryとrevisionを受理する信頼境界。不変storageの責任はadapterにある。
- MvtResourceStage(BoundedMvtRegistration): exact instance/profile/address/snapshotを検査し既存decoderを呼ぶ。
  既存local/memory constructorとmismatch拒否は変えない。

## ADRへの反映候補（親が編集）

1. strict native transportのみ。Browser fetchではraw wire/decompression/redirectを同じように制御できず、
   Browser HTTPは明示的に拒否する。portable memory decodeのBrowser実証と混同しない。
2. initial native adapterは全HTTP操作を直列化し、受付自体も有限。複数通信のthroughput最適化はしない。
3. revisionは展開後の受理bytesのSHA256。GetRevision/OpenSnapshotの同期cacheを共有し、Resourcesの
   構築前後照会でfreshness中の二重downloadをしない。304はcache、送信validator、最終representation URIを照合。
4. snapshotsと開いたread streamは旧bufferのpinを持ち、replacement/eviction/stop後も不変内容を保持。
   sourceのresident budgetは旧pinと新receive bufferを同時に請求し、満杯なら取得前capacity error。
5. 認証partitionは非秘密の権限集合ID。credentialやそのhashをkey、URL、ログへ使わない。
6. manual redirectは同一scheme/host/port/path scopeだけ。別originは拒否し認証を転送しない。
7. deadlineはqueue、connect、headers、body、retry delay全体へ適用。Retry-Afterはtyped delta/date、
   上限超過・不正値は即時拒否。429/500/502/503/504だけ有限retry。auth/format/cap errorはretryしない。
8. endpoint/negative metadata数と文字列長を制限。wire/expanded capは実読取りで検査し、cap+1を予約。
   展開workspaceに32 MiB + 8 KiBを保守的予約し、ネットワーク/TLS/runtimeや他subsystemを含むprocess上限とは呼ばない。
9. no disk cache、公開サーバー依存なし。利用条件/帰属/CORSはアプリ責務。

## 検証入口

- tests/Maps/Lumyte.Maps.Tests: scripted handlerとreal loopback TCP fixture、既存local/memory/decoder試験。
- tests/Maps/Lumyte.Maps.Http.Smoke: synthetic roads MVTをloopback native HTTPからResourcesへ通す
  trim/NativeAOT consumer。公開外部通信、UI、M4 API、benchmark依存なし。
- SDK 10.0.403 / Slang 2026.19。local成果物とlogsは/tmp/maps-m5-*へ保存し、性能成果物をcommitしない。
