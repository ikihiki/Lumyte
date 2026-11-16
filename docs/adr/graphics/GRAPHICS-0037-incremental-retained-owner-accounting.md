# ADR-GRAPHICS-0037: Retained 所有者の差分会計

- 状態: 提案
- 実装範囲: ローカル実装・検証済み、未公開
- 日付: 2026-11-16
- 基準コミット: `0ac47b4445a04b6960e737b323eab6ed498e34cc`
- 関係: [固定 draw record](GRAPHICS-0034-fixed-draw-records.md) の保有量計算の内部実装を変更する。描画、所有期間、LRU、公開 API は変更しない。

## 背景

同じ retained asset の保有量を繰り返し問い合わせると、その都度すべての batch と共有 owner を列挙する。共有と in-flight の正確な union を保ったまま、変更されない owner の再列挙を避けたい。外部 batch の可変見積もりと再入は従来どおり観測する必要がある。

## 決定

### 利用者の変更前・変更後

公開 Renderer2D、DrawingScene2D、統計、予算、準備・記録・完了・retire の記述は変更しない。以前と同じ conservative byte charge を返し、内部の stable owner だけを差分会計する。custom renderer に通知 API や固定サイズ契約を要求しない。

### 差分会計

毎回全 asset の owner 辞書を組み立てる代わりに、参照同一性辞書に owner ごとの idle/active 参照数と byte charge を保持する。asset は重複を取り除いた owner/charge の配列を一度取得し、取得・返却・退避時だけ差分を反映する。数値 ID の再発行、slot map、generation は導入しない。asset 配列は pooled entry への参照を持たず、返却された entry を後から触れない。

idle union と idle+active union の二つの総量を Int128 で保持し、読み出し時だけ long.MaxValue に飽和させる。飽和後の減算も元の正確な残量へ戻る。active から idle への移動では全体 union は変えず、idle の 0→1 だけ加算する。

### サイズが変わる所有者

内部 IRetainedMemory2D.HasStableOwners は opt-in とし、既定は false。組込み PreparedRecordBatch2D の private charge は Prepare 完了時までに確定する。pool の実 capacity を含む _bytes は、その後の upload、CPU packing-data release、Dispose では変更されない。これは実使用量への新しい精密化ではなく、既存の conservative charge を保存する契約である。

共有 DrawingArgumentPlan2D は owner ごとに一度だけ live estimate を再検証する。収集中または opaque graph plan は変動リストに残し、O(variable plans) とする。直接 plan が closed かつ materialized のとき、ArgumentTablePlan.Statistics の一度だけの代入と再収集禁止を根拠に不変と証明し、変動リストから外す。公開 Registrations の外部 materialization を無視する memoize はしない。通常の retained 組込み asset は SealAsync 後に admission されるため、その後の統計読み出しは O(1)。新たな graph observer や公開通知 API は不要である。

通知できない custom batch は従来の全 traversal fallback に残す。mutable identity/bytes、最初の contribution 優先、再入、例外、読み出し間の変更を保存する。custom batch が混在するスコープについて O(1) は主張しない。idle pool bytes は既存の O(1) getter を毎回読むため、rent/return、trim、失敗隔離の変化を取りこぼさない。

### 所有期間と失敗

会計参照数は GPU lifetime reference ではない。completion と command retirement の既存条件だけが資源解放を許可する。in-flight asset を idle eviction で解放しない。旧 frame の再返却は Released guard に従う。accounting callback が失敗した asset を frame に挿入しない。挿入前の再入 release も検出して会計を rollback する。

退避中 cleanup retry の公開 CachedBytes への扱いは比較元と同じであり、新たに全 renderer 所有メモリの統計へ拡張しない。NumericPool の隔離 idle capacity は既存の pool charge に従う。

### Pool と保有 metadata

既存 Signals と同じ Microsoft.Extensions.ObjectPool 10.0.0 の DefaultObjectPool を使用する。entry は同時使用数ではなく未使用保持数を全体で最大 256 に制限する。return policy は owner 参照、bytes、両参照数を必ず消去する。資源や asset を pool に返さない。owner 辞書が空になったとき、4,096 を超える高水位 capacity を捨てる。配列は asset 専有の exact count とし ArrayPool には返さない。

metadata の managed 保有量は描画資源の CachedBytes と区別する。MaxCachedBytes/MaxCachedAssets は引き続き資源と LRU の予算であり、pool の metadata 保持上限で代用しない。

## 検討した代替案

- 辞書 scratch の再利用だけでは、問い合わせごとの全 owner 列挙が残る。正確な互換 fallback としては維持する。
- サイズの一括 memoize は公開登録 owner の materialization や custom owner の任意の変更を取りこぼすため採用しない。
- slot map、generation、全 graph observer は、この内部不変契約と asset 専有配列では必要ないため導入しない。

## 結果と影響

資源の所有期間や描画結果は変えず、asset と live owner に対応した metadata が増える。stable でない所有者の観測費用は残る。差分会計は GPU 解放の権限を追加しない。

### 検証

既存 mutable/reentrant/failure と 0/1/128/long.MaxValue の予算 oracle を維持する。新たに stable owner の一度だけの列挙、重複 page、両 scope の seeded reference union、飽和からの回復、pool 再利用、wrong-scope detach、shared plan materialization、実組込み packing/pool return、挿入失敗と再入 release を検証する。
