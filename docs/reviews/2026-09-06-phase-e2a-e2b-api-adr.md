# E2a / E2b 描画 scope と標準 frame API の ADR

## 決定

`CommandEncoder` の描画状態は、変換、正確な clip、描画先 layer を含む単一の scope 階層で管理する。`BeginState`、`BeginClip`、`BeginLayer` は値型の `CommandEncoderScope` を返し、`using` の終了時に開始前の状態全体を復元する。scope は作成順と逆順にだけ終了できる。順序違反は状態を変更する前に拒否し、同じ scope の重複破棄は何もしない。

矩形 clip と path clip は、開始時の変換を保持した正確な形状として記録する。外接矩形は command の除外と scissor の最適化だけに使う。layer 開始前の clip は layer の合成境界へ記録し、layer 内で追加した clip だけを子 layer の command に適用する。layer 終了時には開始前の変換、clip、親 layer を同時に復元する。

変換の置換には `SetTransform`、現在値への合成には `Transform` を使う。scope 内の変更は scope の外へ残らない。`Finish` は root で一度だけ成功する。未終了 scope があれば記録状態を維持したまま失敗し、scope を閉じた後に再試行できる。`Dispose` は未完了の記録を破棄し、display list を暗黙に作らない。

旧 API の移行対応は次のとおりとする。

| 旧 API | 標準 API |
| --- | --- |
| `Save` / `Restore` | `using (encoder.BeginState())` |
| `PushClip` / `PopClip` | `using (encoder.BeginClip(...))` |
| `PushLayer` / `PopLayer` | `using (encoder.BeginLayer(...))` |
| `Clip(Rect)` | 後続範囲を `BeginClip(Rect)` の `using` で囲む |

既存の対になる API は移行期間だけ同じ scope 状態機械への互換入口として残す。新しい実装と文書では scope API を使用する。

標準のフレーム実行には `GpuRenderContext` と `GpuFrame` を使用する。context は backend、plan cache、retirement queue を一度だけ構成する。presentation 固有処理は `IGpuPresentationAdapter` が画像の取得、present、未提出 frame の破棄を担当する。frame は presentation target を graph へ一度だけ import し、`Submit` で compile、非同期 submit、present を一続きに行う。

material の標準 binding は `DrawMaterialBindings` の各要素に descriptor index、native resource、description、shader stage、descriptor ID をまとめる。この集合から `GpuResourceTable` と graph の read dependency を生成する。低レベルの `GpuResourceTable` を直接渡す入口は、明示制御向けとして残す。

`ResourceHandle<T>` は `IsValid` を公開し、default 値の `TryGetValue` と `TryAcquireLease` は例外を出さず `false` を返す。`TryAcquireLease` は store の世代切替と同じ lock 内で現在 record の参照を増やすため、Value と Generation を別々に読む競合を避ける。snapshot は複数資源を同時に固定する用途として維持する。

RenderGraph の `Write` は以前の内容を必要としない全上書き、`ReadWrite` は以前の内容を読む更新として公開コメントに定義する。pass 内の未宣言 resource 解決エラーには pass 名、resource 名、必要な access を含める。execution の出力取得は `GetExportedTexture` と `GetExportedBuffer` を明示名とし、従来名は互換入口とする。

## 理由

別々の Save、clip、layer stack は、正常終了でも例外終了でも復元規則を呼出し側へ負わせていた。単一 scope によって状態境界と終了順序を同じ規則で表現できる。frame context、binding 集合、単一資源 lease は、標準 consumer が同じ情報や寿命管理を複数箇所へ記述する必要を減らす。
