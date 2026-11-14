# Backend-native shader writers: bounded allocation comparison

2026-11-14。Aは旧Shared callback writerのf27e4b29d02a7e5d78c37b91dc94d33881264e2b、Bはbackend-native writerのcheckpoint 3582af8ada8c00ff38553eebc216bc64527171c1（uniform-root実装）である。
Bは計測時にはf27上の未commit sourceとして凍結し、四processをA1→B1→B2→A2で直列実行した。
凍結した計測対象sourceを途中変更せず、各process後にsource／harness／launcher-output DLL hash一致を検査した。

最新HEADのVulkan push constants／D3D12 root constantsへの変更はこのcampaignでは未測定である。
以下の全timing・allocation値は上記checkpoint間だけの結果で、最新HEADには適用しない。
harness source・script・既存hash・summary CSVと数値は変更していない。

## 結論

- 64要素・各2参照のmapped CopyFromは24,640→11,264 B／呼び出し（−54.286%）。
  paired medianは−24.086%／−27.047%で、一貫したCPU転送改善だった。
- 1回のSetを含むrecordingは1,072→800 B、64回は44,464→27,056 B。
  latency差は順序で逆転したため、Setの安定した高速化とは結論しない。
- completed routeもallocationは低下したが、Count=1のmedianは−0.740%／+2.418%、
  Count=64は−0.447%／−0.152%。completed workの実質的高速化は示していない。
- Vulkanだけの短い診断campaignであり、Wgpu／D3D12／Browserの性能へ一般化しない。
  backendがwriterを直接実装したという設計変更と、観測したallocation低下を分けて扱う。

## Workloadと境界

新しい[維持対象harness](../../tools/benchmarks/backend-native-writers/README.md)は両variantでbyte-identical。
通常のpublic API、生成IShaderArguments class、48-byte private wireと二つの実GPU参照を使う。

- CopyFromCpu: count固定64、既にmappedのtyped uploadへCopyFrom。入力更新とmetadata生成を含む。
  CSVのCount欄の?はBDN parameter不在を意味し、要素数不明ではない。
- SetRecordCpu: 入力更新、fresh command buffer作成、Count回のSet、pass終了、未submit recordingの破棄を含む。
  Finish／Submit／GPU完了は含まない。
- FullCompletion: 入力更新、記録／dispatch／barrier／readback copy、Finish／Submit／実WaitAsync、所有者破棄。
  CPU wall completion時間でありGPU timestampではない。出力値oracleのMap／照合はsetupの別操作。
- Count=64の時間・allocationは一つの64回batch全体であり、Set一回の値ではない。

各variantの独立した--verifyでCount 1／64の実GPU出力、recording破棄、mapped CopyFrom metadataを照合した。
setup／shader compile／oracleをtimed operationへ混ぜない。新規resource作成やcold型初期化の測定ではない。

## 全process観測値

各cellはmedian／P95、単位µs／benchmark呼び出し。P95は五つのiteration平均値の分布であり、個々のframe latencyではない。

| Case | Count | A1 | B1 | B2 | A2 |
| --- | ---: | ---: | ---: | ---: | ---: |
| CopyFromCpu | 64 | 8.580559 / 8.662982 | 6.513872 / 6.545820 | 6.367261 / 6.404364 | 8.727857 / 8.775514 |
| SetRecordCpu | 1 | 51.986880 / 52.429773 | 50.003040 / 50.164433 | 50.134805 / 50.658905 | 49.993028 / 50.341474 |
| FullCompletion | 1 | 101.295410 / 103.360301 | 100.545408 / 101.882985 | 102.988913 / 104.156263 | 100.556985 / 101.566097 |
| SetRecordCpu | 64 | 894.995805 / 903.315573 | 863.611719 / 903.690047 | 885.629117 / 901.986436 | 885.243086 / 900.760475 |
| FullCompletion | 64 | 2140.381672 / 2160.631597 | 2130.820646 / 2133.304929 | 2126.688500 / 2138.667329 | 2129.930375 / 2139.950121 |

## Exact managed allocation

Full JSONのBytesAllocatedPerOperation。各rowはA1=A2、B1=B2で一致した。native／driver allocationではない。

| Case | Count | A bytes | B bytes | B−A bytes | Reduction |
| --- | ---: | ---: | ---: | ---: | ---: |
| CopyFromCpu | 64 | 24640 | 11264 | -13376 | 54.286% |
| SetRecordCpu | 1 | 1072 | 800 | -272 | 25.373% |
| FullCompletion | 1 | 3808 | 3536 | -272 | 7.143% |
| SetRecordCpu | 64 | 44464 | 27056 | -17408 | 39.151% |
| FullCompletion | 64 | 148040 | 130632 | -17408 | 11.759% |

CopyFromの観測差は13,376 Bであり、単純な参照record／byte[16]見積もり10,240 Bへ読み替えない。
object別allocation traceは取っていない。Bにも依存List／backing、metadata object／array／read-only wrapper、
root／native allocation、factory delegate、実行ごとのbinding collectionなどが残る。zero-allocationではない。

## Paired差と逆転

ABはB1/A1、BAはB2/A2。負が低下。絶対差はµs。

| Case | Count | AB median Δµs / Δ% | BA median Δµs / Δ% | AB P95 Δ% | BA P95 Δ% |
| --- | ---: | ---: | ---: | ---: | ---: |
| CopyFromCpu | 64 | -2.066687 / -24.086% | -2.360596 / -27.047% | -24.439% | -27.020% |
| SetRecordCpu | 1 | -1.983840 / -3.816% | 0.141776 / 0.284% | -4.321% | 0.631% |
| FullCompletion | 1 | -0.750002 / -0.740% | 2.431927 / 2.418% | -1.429% | 2.550% |
| SetRecordCpu | 64 | -31.384086 / -3.507% | 0.386031 / 0.044% | 0.041% | 0.136% |
| FullCompletion | 64 | -9.561026 / -0.447% | -3.241875 / -0.152% | -1.265% | -0.060% |

SetRecordCpu Count64のP95は両pairで僅かに悪化した。FullCompletion Count1もBAで悪化した。
有利なpairだけを選ばず、allocation低下をend-to-end高速化とは呼ばない。

## 実行条件・qualification

- BenchmarkDotNet 0.15.8、.NET SDK10.0.403／.NET10、Release、RyuJIT x64。
- AMD Ryzen AI MAX+395、32 logical CPUs、Radeon8060S、RADV STRIX_HALO、Mesa26.0.8-1ubuntu0.3。
- Linux7.0.0-42-generic、明示Radeon ICD、DOTNET_GCServer=0、TieredCompilation／TieredPGO／ReadyToRun=0。
- 1 launch／process、warmup2、measured5、100ms target、MemoryDiagnoser、OutlierMode=DontRemove。
- outlierは全て保持。A1 Set64、B1 Set1／Set64／Full64、B2 Full64、A2 Copy／Full64に検出された。
- Minimum-iteration警告はA1 Copy、B1 Set1／Full1、B2 Copy／Set1／Full1／Full64、A2 Copy／Set1にあった。
  約96.878–99.812msのiterationを含む。正式な長時間noise qualificationではなくbounded診断である。
- binary manifestはlauncher出力DLLの範囲であり、BDN生成child executableの別manifestは保存していない。
  全20 benchmark childのexit0と最終manifestは残る。外側runnerのexit0はtool結果で確認したが、
  空のaggregate log自体にはexit code行を保存していない。

## Provenance・再現

raw BDN full JSON／CSV／logs、環境、vulkaninfo、各processのsource／binary hashはlocalの
artifacts/backend-native-writers-finalに保持する。raw binary／gzipをrepositoryへ追加しない。
小さい全rowのtext転記は[summary.csv](data/backend-native-writers/summary.csv)。

- baseline SHA: f27e4b29d02a7e5d78c37b91dc94d33881264e2b。
- candidate checkpoint SHA: 3582af8ada8c00ff38553eebc216bc64527171c1。
- candidate tracked-diff SHA256（計測時）: 8cd4600c9bcf23877e91ca8de57cb417c0cf4adce2d5dd27f240f04ac45e1e14。
- 完全source manifest SHA256: A=2ec5a3e973cc941f5cc7c798b91a3fd396b7b79dfd6a844a48697ccbacb5e57a、
  B=0fbc35a4a5933264abfe5026679e4763e07e0cf3495c4e63b776fbf91f6c3bf2。
  manifestは未追跡の新規backend writer sourceも含む。manifest内の絶対pathもhashに含まれる。
- frozen harness manifest: Fixture.cs 8eb9ffec…、Inputs.cs d0495052…、Program.cs b95291ea…、
  csproj de81c6a8…、runner cb39cd6f…。完全hashはlocal harness.sha256に保持。

実行はtools/setup環境を設定し、BACKEND_WRITERS_FINAL_FROZEN=yesでrunnerへbaseline checkout、
上記checkpointのcandidate checkout（最新HEADではない）、local artifacts出力先を渡す。runnerはbuild／oracle後にABBAを直列実行し、途中変更を拒否する。
過去の凍結harness／旧測定結果は一切変更していない。機能検証と未実行platformは[検証記録](../shader-transfer-validation.md)を参照。
