# 同期wire cursorとbackend依存metadata（2026-11-14）

> 2026-11-14: docs/performance/dataの圧縮バイナリ生データは削除しました。
> JSON／CSV集計とmanifestは保持していますが、記載されたarchiveは現行treeには存在しません。

## 結論

C# raw区間（padding込み）→16-byte参照のinline符号化→次区間という生成cursorと、backend固有のidentity-only ownerを実装した。Setの最終uniform所有とCopyFromの最新値・部分失敗契約を維持する。これは表現／所有権の変更であり、frame速度改善やzero allocationは主張しない。

最終ABBAではmapped CopyFromの1要素は約189.5→174.8ns、中央値約7.7%短縮を観測した。ただし64要素やGPU完了込みでは改善を確認していない。Set→完了は約0–3.1%遅い方向で差を断定しない。upload allocationは40B／要素減った一方、interface Setは40B／Set、明示snapshot Setは256B／Set増えた。この増加を隠さない。

## 比較と凍結

baselineは7e89cd7e8cecd2a4a507758253b934c95b612cef。harnessは01581325で凍結した同一sourceであり、二つの既存実fixtureを再利用し、public CopyFrom／SetArgumentsを呼ぶ。production algorithmのreplicaではない。初回pilotの後、ShaderResourcesのunmanaged helperに残っていたCaptureを追加修正したため、pilotをsupersededとして保存し、最終source全体を再凍結してABBAを一度だけ再実行した。pilotの有利なinterface1結果をheadlineに採用していない。

最終順序はbaseline1→candidate1→candidate2→baseline2、2026-11-14 00:40:05–00:46:34 JST。全32caseの正のoperation数・OriginalValues・Statistics・allocation、run前後のsource／DLL hash一致を検査した。OutlierMode.DontRemoveで外れ値を残す。5%未満、pair符号逆転、process spread15%超などでは方向を断定しない。

SDK10.0.403、.NET10.0.14、BenchmarkDotNet0.15.8、Release、AMD RYZEN AI MAX+395／Radeon8060S、Mesa26.0.8-1ubuntu0.3 RADV、radeon_icd.json明示。Workstation GC、heap2GiB、TieredCompilation／TieredPGO／ReadyToRunを0に統一。warmup5–10、actual10–15、iteration200ms目標、Launch1。priority昇格は権限不足で両者同じ。

## 測定境界

- Set caseは20-byte generated root（uint＋RW buffer参照）を1／64回Set・Dispatchし、Finish、Submit、GPU実完了とcommand／submission解放まで含む。interfaceは同じmutable classの現在値を渡す。snapshot入力はsetupで事前Capture済みで、入力Capture費用はtimed範囲外。
- CopyFrom caseは112-byteの参照2個付きgenerated classの1／64要素。mapped caseは値／identity更新と既存mapped uploadへのpublic CopyFrom・metadata公開まで。完了caseは値更新、Map／CopyFrom／Unmap、barrierと実buffer copy、Submitと完了待機・解放を含む。検証用compute dispatch／readbackはuntimedであり、この完了caseの時間には含めない。
- device／shader／pipeline／buffer／tableはsetupで再利用する。GPU oracleは別のuntimed操作で全要素のnumericと参照先を検査する。両variantで全8caseを二度実行するverifyに成功した。
- ns／要素とbatch wall latencyを混同しない。以下はµs／batch、B／batch。p95はBDN iteration推定値のpercentileで、個々のframe p95やGPU timestampではない。

| 境界 | 個数 | baseline中央値µs | candidate中央値µs | baseline p95µs | candidate p95µs | baseline B | candidate B |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| interface Set→GPU完了 | 1 | 106.677 | 109.431 | 116.554 | 129.233 | 3624 | 3664 |
| interface Set→GPU完了 | 64 | 2107.574 | 2172.642 | 2263.302 | 2235.777 | 147352 | 149912 |
| snapshot Set→GPU完了 | 1 | 106.664 | 109.815 | 115.940 | 119.966 | 3496 | 3752 |
| snapshot Set→GPU完了 | 64 | 2177.213 | 2177.643 | 2243.197 | 2265.859 | 139160 | 155544 |
| mapped CopyFrom | 1 | 0.189 | 0.175 | 0.193 | 0.182 | 488 | 448 |
| mapped CopyFrom | 64 | 9.902 | 10.112 | 10.229 | 10.445 | 27200 | 24640 |
| CopyFrom→GPU完了 | 1 | 80.189 | 79.958 | 88.727 | 85.005 | 2352 | 2312 |
| CopyFrom→GPU完了 | 64 | 98.543 | 100.194 | 102.578 | 115.343 | 35176 | 32616 |

| 境界 | 個数 | baseline process中央値µs | candidate process中央値µs |
| --- | ---: | ---: | ---: |
| interface Set→GPU完了 | 1 | 106.687 / 105.715 | 114.549 / 108.256 |
| interface Set→GPU完了 | 64 | 2076.198 / 2141.154 | 2162.093 / 2180.524 |
| snapshot Set→GPU完了 | 1 | 106.052 / 107.220 | 112.348 / 105.346 |
| snapshot Set→GPU完了 | 64 | 2178.375 / 2171.739 | 2178.314 / 2176.972 |
| mapped CopyFrom | 1 | 0.191 / 0.187 | 0.175 / 0.175 |
| mapped CopyFrom | 64 | 10.094 / 9.804 | 9.830 / 10.342 |
| CopyFrom→GPU完了 | 1 | 79.952 / 80.971 | 81.042 / 77.808 |
| CopyFrom→GPU完了 | 64 | 95.209 / 99.148 | 99.422 / 100.967 |

## 実装範囲と残した経路

生成型の非virtual auto-property／field参照は同期WriteWireでraw区間と参照を交互に書く。numericのfield別シリアライズ、全wire copy後の上書き、Completeの参照second passを使わない。functionalな型別offset順は診断reflectionから独立する。Spanとin参照は呼び出し内だけ借用する。

custom getter／手書きcodecと第三者writer、明示Captureには宣言順・単一codec呼び出しを保つbulk fallbackを残す。手書き互換経路の一時fixupはあり得るが、通常nativeの完了ownerにはwire offsetを保存しない。参照slotの16-byte区間の交差を、fallbackと明示snapshotでもhost安全性として拒否する。

commonはIShaderReferenceMetadata.Referencesだけを消費する。Vulkan／D3D12／Wgpu／Browserのprivate ownerはresource identityだけを保存する。IShaderReferenceに元からある登録slot／range／accessは実binding・Graph・WGSL remapに必要であり、C# wire byte offsetとは区別する。全backendの通常rootはstableなaddress／slot wireをSet中に符号化する。無効参照のencoding失敗は初回Drawより早いSetで発生し得る。dynamic WGSL remapとtransitive admissionはDrawごとに再構成し、Setへ凍結しない。別Setは別世代、failed Setは前回の選択を保持する。encoder callbackを渡さない公開helperのlegacy deferred fixupは別の互換経路である。

ShaderResourcesのunmanaged Draw／DrawIndexed／Dispatchもgeneric Setへ直接渡す。encoder interface default実装とGraphの非対応分岐は第三者互換用のCapture。ModelVertexの明示layout probe、利用者の明示Capture、numericを作らないShaderBindingSnapshot.Capture(rootArguments, …)とは区別する。

一時ShaderValue、参照encoderの16-byte配列、identity list／immutable配列owner、fallback scratchとdelegateなどのallocationが残る。Browserはraw区間ごとのinterop／JS copyも残り、追加callが速いとは未測定である。

## 正当性と限界

Graphics Debug／Release各275成功・88skip、Graph各129成功・skip0、全ホストRelease3,244成功・109skip。Vulkan／Wgpu native subsetはRelease17成功・skip0、Debugは既存15成功とhelper2成功。helperはvalidation off/onで実1pixel redをreadbackし、snapshot overloadを拒否する厳密wrapperで古いCapture経路を検出する。これは旧revisionでのnegative controlを実行した主張ではない。D3D12管理側28成功・Windows native7skip、Browser Release compile成功。

paddingのbyte一致／raw coverage、slot順／overflow／交差、custom getter評価回数、元値変更、複数Set、failed Set、transitive／table／pipeline／lifetimeは決定的なunit・native fixtureで検査した。Windows native、Browser実行／AOT、presentation、他backend性能、ordinary unmanaged helper単独性能、native allocation／VRAM、大きいbatch、実UI frame効果は未測定。

## 保存と再現

- [同一harnessと実行手順](../../tools/benchmarks/wire-cursor-metadata/README.md)
- [summary.json](data/wire-cursor-metadata-20261113/summary.json)／[summary.csv](data/wire-cursor-metadata-20261113/summary.csv)
- [manifest.json](data/wire-cursor-metadata-20261113/manifest.json)には最終・superseded pilotのraw JSON／log／環境／hash／source patch／全candidate変更sourceを圧縮保存する。展開byte数とSHA-256を検証する。main列はbaseline、pr列はcandidate。
- baseline7e89と保存candidate source・削除listへ同じharnessを配置し、--verify成功後にrun-comparison.sh、analyze.mjsを使用する。過去harnessは当時のrevisionの再現用で、API移行後の比較には今回の互換harnessを使う。
