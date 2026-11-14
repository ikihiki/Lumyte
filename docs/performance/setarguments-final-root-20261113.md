# SetArgumentsの最終uniform直接書き込み（2026-11-13）

> 2026-11-14: docs/performance/dataの圧縮バイナリ生データは削除しました。
> JSON／CSV集計とmanifestは保持していますが、記載されたarchiveは現行treeには存在しません。

## 結論とtradeoff

Set時点の値固定を保ち、numericの中間ShaderValueSnapshotとbyte配列を最終native uniform所有へ置き換えた。これは所有権／copy経路の変更であり、この測定から速度改善は主張しない。

VulkanでSet→Dispatch→GPU完了までを測ると、interface経路の1／64回は有意な方向を断定できなかった。明示snapshot経路の1回は中央値約103.2µsから109.0µsへ、約5.6%遅くなる観測があった。独立pairの増加幅は約6.6%と0.6%であり、すべてのrunが同程度悪化したとは言わない。64回の約1.4%増加も断定しない。managed allocationは全caseで減った。snapshot削除を、そのままframe速度やallocation-freeへ読み替えない。

## sourceと同じ出力境界

- 比較元はabba27197abf33cf84f8d06b0d3eb40a2d09fe3a、candidateは今回の最終root storage実装。cab86fd3の過去のCopyFrom測定とは別である。
- 同一harnessはaf1f94dfで凍結し、baseline／candidateの同じ相対pathに配置した。候補の全変更sourceはmanifestへ保存し、productionと測定candidateのC#／native sourceが一致することを確認した。
- 実Vulkan、Slang compute、20-byteのgenerated reference root（uint＋RW buffer参照、uniform容量32-byte）。1／64回のSetとDispatchを一つの新しいcommandへ記録し、Finish、Submit、実完了待機とcommand／submission解放までを含む。output参照は事前に作った非重複element range。
- interface caseは同じmutable classを更新し、その場でSetする。明示snapshot caseは事前に作ったimmutable入力snapshotを渡すAPIの比較であり、入力Capture生成費用はtimed範囲外。どちらも実際のpublic SetArgumentsを呼び、実装replicaを使わない。
- device／buffer／table／shader／pipelineはsetupで再利用する。GPU readbackは別のuntimed submissionで全出力を確認する。各caseを繰り返し、Set後のclass変更や複数世代を含む別のnative correctness fixtureも実行した。
- この表はCPU wall through completion。GPU timestamp、単独Set時間、個々のframe p95、UI／描画frame全体ではない。

## 固定環境と実行

BenchmarkDotNet0.15.8、SDK10.0.403、.NET10.0.14、Release、AMD RYZEN AI MAX+395、Radeon8060S／RADV、Mesa26.0.8-1ubuntu0.3。radeon_icd.jsonを明示した実GPUである。Workstation GC、heap hard limit2GiB、TieredCompilation／TieredPGO／ReadyToRunを0に揃えた。priority昇格は両者とも権限不足。

baseline1→candidate1→candidate2→baseline2のABBAを22:32:30から22:37:00まで直列実行した。warmup5–10、actual10–15、200ms目標、BDN pilot invocation、Launch1。OutlierMode.DontRemoveで外れ値を残し、全16caseのStatistics、allocation、正のoperation数を確認した。各variant内のsource／DLL fingerprintはrun前後で一致した。都合のよいcaseだけの再計測はしていない。

## 結果

µs／batch、B／batch。batchのSet／Dispatch回数が表の個数。p95はiteration推定値のpercentile。JSON／CSVのmain列はbaseline、pr列はcandidate。

| API | 個数 | baseline中央値µs | candidate中央値µs | baseline p95µs | candidate p95µs | baseline B | candidate B | 判定 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| interface Set→完了 | 1 | 104.302 | 106.669 | 120.448 | 119.510 | 3,712 | 3,624 | 差は断定しない |
| interface Set→完了 | 64 | 2179.018 | 2172.804 | 2223.641 | 2261.806 | 151,936 | 147,352 | 差は断定しない |
| snapshot Set→完了 | 1 | 103.231 | 109.019 | 119.586 | 123.156 | 3,600 | 3,496 | 観測された遅延増加 |
| snapshot Set→完了 | 64 | 2171.446 | 2201.945 | 2235.075 | 2309.136 | 144,768 | 139,160 | 差は断定しない |

| API | 個数 | baseline1µs | candidate1µs | candidate2µs | baseline2µs |
| --- | ---: | ---: | ---: | ---: | ---: |
| interface Set→完了 | 1 | 103.167 | 107.735 | 105.316 | 108.568 |
| interface Set→完了 | 64 | 2182.174 | 2178.500 | 2172.317 | 2171.557 |
| snapshot Set→完了 | 1 | 103.041 | 109.879 | 107.245 | 106.581 |
| snapshot Set→完了 | 64 | 2183.611 | 2194.885 | 2225.128 | 2162.394 |

5%未満、pair符号逆転、process中央値spread15%超などは改善を断定しない事前方針を継続する。中央値の小さな改善方向だけを採用せず、明示snapshotの遅延増加も残す。

## 実装で無くしたものと残したもの

四つのnative backendのgeneric interface／unmanaged Setは、CaptureやSnapshot.Copyを経由せず、新しいcommand所有の最終uniformへ同期書き込みする。numeric中間配列、pack配列、padding配列を作らず、mutableな元のclass／in参照／診断providerも保持しない。明示snapshot overloadは入力Dataとidentityを同期消費し、numeric cloneなしで独立した最終backingへ書く。明示Capture APIは独立CPU値のままである。

参照の型・identity・slot、RootParameter、wire size、execution admissionとfinal storage ownerは残す。参照encoderの16-byte一時配列やbinding用collectionは残るため、zero allocationではない。final uniformがCPUから書けるメモリを所有することまで削除したとは言わない。

最初のDrawで参照を符号化してsealし、同じSetの後続Drawではrootを書き換えない。各新Setは別世代を作る。transitive metadata／table／Graph group所属とWGSL remapは毎回更新する。失敗したSetは未公開世代を解放し、前回選択と元の例外を保つ。GPU完了前のcommand解放・storage再利用を許可しない。

GraphはIShaderRootEncoderへの追加contractでreference-onlyにgroupを推定し、numeric snapshotを作らない。第三者encoderが新contractを実装しない場合だけ従来のsnapshot fallbackが残る。Sharedの新native-root captureではArguments／RootParameterを使い、legacy Root.Dataを隠れたreadbackで再現しない。既存Capture(snapshot)は互換に残す。

Vulkan uniform、D3D12 CBV、Wgpu／Browser mapped-at-creation uniformであり、push constants／root constants ABIへ変更していない。BrowserのMemoryView→JS slice／mapped setの追加interopコピーは残り、C# snapshot削除と同一視しない。

## 正当性と未検証範囲

Graphics Debug／Release各239成功・86skip、RenderGraph各129成功・skip0、全ホストRelease3,208成功・107skip。Vulkan／Wgpuのnative readback subsetはDebug／Release各12成功・skip0。D3D12管理側Release28成功・native7skip、Browser Release compile成功。新しいroot core24ケースとGraph4ケースはoperation counter／実際のshared helperで直接書き込み、所有世代、失敗、範囲／ABI、reference-only admission、transitive更新を検査する。

実native fixtureではWriter型を検査するcodecでCapture fallbackを拒否し、初回Writeが一度であること、Set後の元値変更、複数Setの遅延GPU読取り、failed Setの前回値、pipeline／table guard、明示snapshot配列の後続変更からの独立性を確認した。一般的なcancel／dispose／submission寿命検査は既存の全ホストsuiteも継続する。

Windows native D3D12、Browser実WebGPU、AOT、presentation、他backendの性能、ordinary unmanagedの単独性能、同一Setから大量のDrawを繰り返す性能、native allocation／VRAMは未測定。

## 保存と再現

- [Harness](../../tools/benchmarks/setarguments-final-root/README.md)、[summary.json](data/setarguments-final-root-20261113/summary.json)、[summary.csv](data/setarguments-final-root-20261113/summary.csv)。
- [manifest.json](data/setarguments-final-root-20261113/manifest.json)は全raw JSON／log／環境／正当性／source patchとhash／candidate sourceをgzipで保存し、展開byte数とSHA-256を検証する。生成BDN wrapperは既定cleanupで残していない。
- baseabba2719と保存したcandidate sourceへ同じharnessを置き、両者の--verify成功後にrun-comparison.shでABBA、analyze.mjsで集計する。通常の再実行も同じ既存LUMYTE_ENV_ROOT／SDK activationを使用する。
