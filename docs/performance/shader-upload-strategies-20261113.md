# 参照付きupload: 直接mapped書き込みとCPU一時buffer（2026-11-13）

## 決定

数値のCPU snapshotを作らないreference-only契約へ変更したうえで、直接mapped書き込みを既定実装に採用した。CPU batch scratchはproductionから削除し、比較対象として隔離したsourceだけを保存する。これは失敗atomicityのための判断ではない。

CPU一時bufferには、小さいCPU書き込みだけを測ると利益もあった。1要素は約11ns（6.1%）、64要素は約0.50µs（5.1%）短い。ただしallocationは増加し、実GPU copy完了まででは1.1–2.0%の差にとどまり、改善を断定しない。1024要素では一時bufferがCPU書き込みで約24.1%、完了までで約13.0%遅かった。

したがって、small-batch CPU stageだけの差を隠さず記録しつつ、completed transferとallocationのtradeoffから直接方式を選んだ。すべてのbackend／memory構成で直接方式が最速という結論ではない。

## 変更した契約と削除したもの

- 基準: 693b969f85e3cd489de52050ed91c5df689cf21fに対する今回のcandidate。cab86fd3の旧6.45倍／2.24倍の比較とは別の測定である。
- IShaderDataSource.Readの完全なCPU値契約を廃止し、ReadReferences／SetTransferredReferencesへ変更。ShaderBindingSnapshot.Elementsもreference-only metadataへ移した。
- ShaderReferenceMetadataにはimmutableな参照の型、identity、wire slotだけを保存する。Data、numeric配列、元の値object、診断providerを保持しない。
- 四つのbackendの参照付きCopyFromから、IShaderArguments.Capture呼び出し、要素ごとのShaderValueSnapshot、numeric byte配列、batch scratchを削除した。generic writerが現在のprivate unmanaged backingを同期的にmapped領域へ書き、その後に参照slotを符号化する。
- 参照identityとそのrevisionを示すmetadata、転送先elementとの対応はbindingに必要なので残す。GPU-only bufferから隠れたreadbackや待機は行わない。
- 明示Capture、SetArguments、root引数のvalue snapshotは変更しない。これは呼び出し側が独立した値の取得を明示要求したAPIである。
- CopyFrom失敗時はbyteの部分更新を許し、要求したmetadata範囲を全て無効にする。旧byteを復元せず元の例外を返す。範囲外は保つ。無効なmetadataからのtyped copy／bindingはnative使用前に拒否し、全範囲の再転送で回復する。in-flightの寿命・同期責任は緩めない。

## 同じ義務で比較した境界

生成reference classの112-byte private backing（Matrix4x4、scalar、Vector3、read参照とread/write参照）を使った。両方式は同じimmutable reference-only metadataを作り、同じ値を更新する。手書きのserializer replicaは使用していない。

- CPU行: setupで一度mapしたuploadへのCopyFromと入力更新。device／buffer／table／入力objectは再利用し、Map／UnmapとGPU処理は含まない。
- 完了行: 入力更新、Map／CopyFrom／Unmap、single-use command生成、barrier／copy記録、Finish、Submit、実完了待機と解放を含む。GPU-only destinationへ実際にcopyする。
- 一時buffer候補はCopyFromごとに一つのCPU batch byte配列を作り、そこで参照を符号化してから一括copyする。永続・pooled scratchの設計は未測定であり、それらにまで否定を一般化しない。
- 数値のShaderValueSnapshotを作らないこと、resource identityの保持、失敗時のmetadata無効化の義務は共通。実GPUのcompute／readbackでmatrix全16要素、数値・Vector3、read/write参照をuntimedで確認した。

## 固定環境と実行

BenchmarkDotNet0.15.8、SDK10.0.403、.NET10.0.14、Release、AMD RYZEN AI MAX+395（16 core／32 logical）、Radeon8060S／RADV、Mesa26.0.8-1ubuntu0.3。radeon_icd.jsonを明示し、software rendererへ切り替えていない。

Workstation GC、managed heap hard limit2GiB、TieredCompilation／TieredPGO／ReadyToRunを0で揃えた。BDNのpriority昇格は両者とも権限不足。warmup5–10、actual10–15、目標200ms／iteration、pilot invocation数はBDNに任せ、OutlierMode.DontRemoveで外れ値を残した。

最終順序はdirect1、buffered1、buffered2、direct2。2026-11-13T21:08:23+09:00から21:13:54+09:00まで直列実行した。build／他の計測は重ねていない。24case全てのfull JSON、正のoperation数、allocationとStatisticsを検査した。sourceとvariant内DLL hashは各run前後で一致した。

先行pilotも全て保存したが、diagnostic provider互換性の修正と不要なbuffered分岐のproduction除去後に、両方式の全ABBAを再実行した。最終表にpilotを混ぜていない。

## 最終結果

時間はµs／batch、allocationはB／batch。p95はBDN iteration推定値のpercentileであり、個々のframe latencyではない。JSON／CSVのmain列はdirect、pr列はCPU一時bufferを意味する。

| 境界 | 個数 | direct中央値µs | CPU buffer中央値µs | direct p95µs | CPU buffer p95µs | direct B | CPU buffer B | 判定 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| CPU mapped書き込み | 1 | 0.182 | 0.171 | 0.193 | 0.175 | 488 | 624 | 独立runで同方向 |
| CPU mapped書き込み | 64 | 9.825 | 9.322 | 10.369 | 9.973 | 27,200 | 34,392 | 独立runで同方向 |
| CPU mapped書き込み | 1024 | 159.123 | 197.518 | 162.014 | 204.066 | 434,240 | 549,045 | 独立runで同方向 |
| 書き込み＋GPU copy完了 | 1 | 81.225 | 79.606 | 93.434 | 92.367 | 2,352 | 2,488 | 差は断定しない |
| 書き込み＋GPU copy完了 | 64 | 98.641 | 97.531 | 111.330 | 105.860 | 35,176 | 42,368 | 差は断定しない |
| 書き込み＋GPU copy完了 | 1024 | 327.522 | 370.155 | 352.766 | 408.498 | 575,312 | 690,114–690,116 | 独立runで同方向 |

| 境界 | 個数 | direct1µs | buffered1µs | buffered2µs | direct2µs |
| --- | ---: | ---: | ---: | ---: | ---: |
| CPU mapped書き込み | 1 | 0.179 | 0.170 | 0.172 | 0.183 |
| CPU mapped書き込み | 64 | 9.882 | 9.210 | 9.436 | 9.727 |
| CPU mapped書き込み | 1024 | 158.280 | 198.708 | 193.598 | 160.896 |
| 書き込み＋GPU copy完了 | 1 | 84.069 | 79.006 | 80.208 | 79.280 |
| 書き込み＋GPU copy完了 | 64 | 104.683 | 97.947 | 95.911 | 96.105 |
| 書き込み＋GPU copy完了 | 1024 | 324.097 | 371.609 | 369.453 | 328.168 |

5%未満、pair符号逆転、variant内process中央値spread15%超などは改善を断定しない方針である。小さいcompleted-transfer行はno-change／inconclusiveとし、CPU stageの小さな利益だけを全体改善に読み替えない。

## 正当性と範囲

Graphics Debug／Release各215成功・84skip、RenderGraph各125成功・skip0。全ホストRelease3,180成功・105skip。Vulkan／Wgpuの転送fixtureはDebug／Release各10成功・skip0。新しいfake failure／diagnostic検査15件は、元の例外、要求範囲の無効化とretry、host範囲・overflow、slotと16-byte ABI、参照identity、metadataの非numeric性を検査する。実backendでも失敗後のmetadata拒否と再転送後のreadbackを確認した。D3D12管理側Release28成功・native7skip、Browser Release compile成功。

GPU行はCPU wall through completionであり、GPU timestampや描画frame全体ではない。Windows native D3D12、Browser実WebGPU、Wgpuでの性能、presentation、AOT、native allocation／VRAM、cold startup、pooled CPU scratchは未測定。managed allocationはBDNのaggregateであり、部品別やnative量ではない。

## 再現と保存した証拠

- [Harnessと境界](../../tools/benchmarks/shader-upload-strategies/README.md)。凍結harness commitはf350c81b（計測本体はその後変更していない）。
- [summary.json](data/shader-upload-strategies-20261113/summary.json)、[summary.csv](data/shader-upload-strategies-20261113/summary.csv)。
- [manifest.json](data/shader-upload-strategies-20261113/manifest.json)に最終／pilot raw JSON、log、環境、正当性、source patchとhash、最終candidate source全文のgzipを列挙した。全entryの展開byte数とSHA-256を検証した。
- base693b969fへ同じharnessを配置し、directは最終production source、bufferedは保存した候補sourceを使う。両者の--verifyを先に成功させ、run-comparison.shで直列ABBA、analyze.mjsでfull JSONを集計する。生成BDN wrapperは既定cleanupで残していないが、計測本体・依存DLLのhashとengine logは保持する。
