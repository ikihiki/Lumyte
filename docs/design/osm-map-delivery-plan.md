# OSM地図描画のstack PR計画

設計の正本は[ADR-MAPS-0001](../adr/maps/MAPS-0001-ui-map-rendering.md)。この文書は実装順、レビュー単位、検証条件を管理する。設計は採用済み。設計PR自体は文書のみとし、実装は下記の段階ごとのstack PRで進める。

## 共通方針

- 各段は直前の段をbaseとするstack PRにし、差分にはその段の機能と試験だけを含める。下段取り込み後は上段のbaseを更新し、差分とCIを再確認する。
- 文書を土台に一度に全プロジェクト・空の実装を追加しない。利用できる縦切りと検証を一段ずつ追加する。
- 機能を隠した未実装APIや成功扱いのstubを公開しない。対象範囲が未対応なら明示的に拒否し、対応する試験を置く。
- 各段で既存UIのrecord-only検証を維持する。GPU画素、Browser/AOT、実ハードウェア性能は別の検証であり、未実施を成功と数えない。
- fixturesは小さい手作り形状から開始する。実都市データは再配布可能な切出しを固定し、出所、日付、範囲、スキーマ、生成条件、SHA-256、帰属・ライセンスをmanifestへ記録する。

## 依存順

```text
D0 設計（今回）
 └─ M1 座標・タイルモデルとローカル入力
     └─ M2 UIで固定地図を描く
         └─ M3 パン・ズームと表示タイル選択
             └─ M4 有界な空間ストリーミング
                 └─ M5 HTTP配信と失敗制御
                     └─ M6 再現可能な性能試験と環境検証
```

### D0: ADRと分割計画

- 対象: assembly境界、UI接続、型付き取得、座標、所有権、容量制限、検証条件。
- 完了条件: 現行sourceとの照合、ADR専用規則とMarkdown検査、独立レビュー。採用はレビューで決める。

### M1: 座標・タイルモデルとローカル入力

- 対象: Mapsの座標と不変CPUモデル、Mvt adapter、Resources pipeline、ローカルの固定MVTを読む最小fixture。必要なprojectとtestのみ追加する。
- まず一つのスキーマに対応する。tile set manifestとstyleの対応を検証し、未知の必須レイヤーや属性型を黙って別用途へ解釈しない。
- decoder候補のライセンス・管理状況・Browser/AOT・allocation制限を実証して依存版を固定する。適切な既存decoderで実現できない場合だけ、欠ける処理を切り分けて再判断する。
- source/pipelineに不変のMapTileLoadProfileを登録し、ResourceIdとvariantにprofile識別を伝達する。属性文字列・decoder内部scratchを含む確保前byte制限と、出力の保持byte計上をこの段で実装する。
- 完了条件: extentの異なるタイル、穴付き面、複数面、線、壊れたcommand、入力途中終了、feature・頂点・属性・byte上限、日付変更線、緯度限界のCPU試験。異なるprofileの非共有、同一profileの共有とcache hitの計上も確認する。ネットワーク不要。
- 対象外: UI、連続移動、HTTP、文字、独自renderer。

### M2: 既存UIで固定地図を描く

- 対象: MapView/MapElement、スタイルの面・線・背景、明示的な固定タイル集合を表示するsession、IUiApplicationContentを使うsample。
- 固定集合以外の自動需要はまだ受け付けず、初期読込み中は背景、完了後は地図を表示する。同時取得数・完了枠・常駐byte予約をこの段から守り、固定集合も無制限に全件常駐させない。未配送要求の取消、配送済みRetainAsyncの終端回収、停止・lease返却を実装する。
- 完了条件: 道路縁取り、穴、半透明、レイヤー順、隣接タイル境界、親clip、UI overlay、DPI、0サイズ、mount/unmount、Canvas状態保持の試験。
- 直接描画と保持型描画の画素比較と、静止frameでのローカル記録再利用を確認する。GPU環境がない試験ではrecord-onlyと明記する。
- 対象外: 新しい範囲の自動取得、HTTP。

### M3: パン・ズームと表示タイル選択

- 対象: camera更新、pointer capture、ポインター基準のwheel zoom、viewportからの要求集合、world wrap。全入力はローカルの有限タイル集合。
- 範囲外は背景にする。無制限の先読みを仮実装せず、需要計算を取得と分離する。
- 完了条件: capture喪失と非活性化、UI controlsとの競合、サイズ変更、日付変更線、反復worldの取得ID共有、連続zoomと整数取得zoomの対応。固定移動経路で再現できること。
- 対象外: 非同期優先queue、HTTP。

### M4: 容量制限付き空間ストリーミング

- 対象: M2の容量・停止契約の上に、空間的な優先queue、Resources取得の重複抑制、未配送要求の取消、最新需要への完了採用、LRU、先読み、親タイル代替を追加する。ローカルsourceへ遅延を注入する。
- 実行中・完了待ち・表示・退役待ちを含む所有台帳を持つ。Resources側cacheとsession側固定参照の境界も試験する。
- 完了条件: 逆順完了、急速な往復移動、旧要求の再利用、内容版切替、複数session共有、queue満杯、decoder上限超過、実行器の受付容量不足、取消を無視するI/O、Cooperative pump、途中解放失敗。
- 低い予算を使った決定的試験で上限と縮退を確認し、時間しきい値を機能試験の合否にしない。停止時は新規公開せず、実処理・cleanup完了を回収してからIsStoppedになること。
- 対象外: 実ネットワーク、永続cache。

### M5: HTTP配信と失敗制御

- 対象: 明示設定のHTTP source、profileによる読取り・展開制限と通信期限、revisionと再検証、欠損の期限、回数制限付き再試行。sessionの需要取消では配送済み共有取得を中断せず、終端まで枠を保持する。
- URLは信頼したアプリ設定から構成し、外部属性から任意URLを開かない。認証情報はタイルIDやログへ含めず、配信元・redirect先を制限する。
- 完了条件: ローカルHTTP fixtureで200/304/404/401/429/5xx、Retry-After、途中切断、誤ったContent-Length、圧縮展開超過、期限切れ、停止中の応答を検証する。
- 帰属表示、配信条件、CORSとBrowser fetchの制約をsampleで示す。CIは外部公開サーバーへ依存しない。ディスクcacheは必要性を確認して別差分とし、初期は無効。

### M6: 性能試験と環境検証

- 対象: 既存benchmark runnerへ地図workloadを追加する。新しい計測基盤を並立させない。
- workload: 静止した密集市街地、固定パン・連続zoom、タイルの追加・退避。文字なしと文字ありを混同しない。
- 区間: 入力取得、デコード、需要計算、公開、描画記録、準備、submitからGPU実完了までを分ける。既知データを先読みした描画試験と、cold streamingの体感遅延は別指標にする。
- 証拠: commit、harness、fixture hash、CPU/GPU/driver、backend、画面寸法、DPI、スタイル、線品質、反復経路、warmupと計測frame、raw値、中央値・p95、allocation、常駐量の高水位を保存する。
- 完了条件: 画素oracleを通した同一品質での計測。GPU実完了を待つ直列latencyと、有界frames-in-flightのthroughputは区別し、readback・PNG保存はheadline区間から除く。Native/Browser、実GPU/ソフトウェアGPUの結果を区別する。

## このstackの外に残す判断

- ラベルの衝突回避・道路沿い配置と、その文字資源寿命・地図全体での重複排除。
- タイルまたはスタイルレイヤー単位の保持とGPU部分更新。現行の単一Element再記録を比較基準とし、UIまたはDrawing2Dの契約変更が必要なら独立に設計する。
- 完全なMapLibre Style互換、回転・傾斜、ラスタ地図、PMTiles/MBTiles入力。

これらがなくてもM6までの道路・建物・水域の地図と性能試験は成立する。追加時はこのstackへ抱き合わせず、必要なAPI判断と検証を別にレビューする。

## 既存実装の調査メモ

実装の採用判断ではなく、再利用先を選ぶための候補。調査日は2026-11-16。公開READMEの存在だけでは安全性・性能・Browser/AOT対応を実証していない。

- [MapLibre GL JS](https://maplibre.org/maplibre-gl-js/docs/): 完成したベクトル地図renderer。地図表示だけなら有力だが、Lumyteの描画性能試験の経路にはならない。
- [MVT仕様](https://github.com/mapbox/vector-tile-spec): 形式と版の確認元。タイルスキーマとスタイルは別に決める。
- [mapbox-vector-tile-cs](https://github.com/bertt/mapbox-vector-tile-cs): .NETのdecoder候補。対応frameworkの表記だけでBrowser/AOTや容量制限に適合すると見なさない。

既存のdecoderをadapterで利用し、配信はResources sourceへ接続する。追加依存、データ利用料、外部サービス契約は、この文書の候補列挙だけでは導入しない。
