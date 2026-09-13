# テストの実行と並列化

repository root から実行する。Browser 適合試験には WebGPU の直接入力に対応する Chromium が必要で、この PC では Chrome for Testing 155.0.8048.0 を使用する。詳細は [Browser 適合試験](../src/graphics/Lumyte.Graphics.WebGPU.Browser.Tests/Integration/README.md)を参照する。

```powershell
$env:LUMYTE_WEBGPU_BROWSER = Join-Path $PWD 'artifacts/tools/chrome-for-testing/155.0.8048.0/chrome-win64/chrome.exe'
dotnet test Lumyte.slnx --logger 'trx;LogFilePrefix=solution-parallel' --blame-hang-timeout 2m --blame-hang-dump-type none --blame-crash --blame-crash-dump-type mini
```

通常は `-m:1` を指定せず、project の build と test を並列に進める。xUnit は collection 間を並行し、同一 collection のテストは順番に実行する。[xUnit の並列実行](https://xunit.net/docs/running-tests-in-parallel)

## 並列にする範囲

| 範囲 | 実行方法 |
| --- | --- |
| GPU を使わない単体テスト | project 間・collection 間の並列実行を有効にする。fake と可変状態は各テストが所有する。 |
| DirectX 12 の実機テスト | 一つの GPU collection と DirectX12 専用 named mutex で直列化する。 |
| Vulkan の実機テスト | 一つの GPU collection と Vulkan 専用 named mutex で直列化する。探索時の capability probe も同じ mutex を取得する。 |
| Dawn の実機テスト | 一つの GPU collection と Dawn 専用 named mutex で直列化する。assembly 全体の並列禁止は設けない。 |
| Browser の実機テスト | 一つの Browser collection と Browser 専用 named mutex で直列化する。browser、HTTP server、profile は fixture が所有する。 |
| 別 backend 同士 | 互いの mutex を取得せず、別 test host の device と resource を使って並行する。 |

GPU collection に `DisableParallelization = true` を付けると、その collection と CPU 単体テストの並行も禁止するため使用しない。GPU テスト同士の順序は同一 collection に入れることで維持する。Dawn の過去の assembly 全体の禁止も同じ理由で除去した。

全 backend 共通 mutex を使っていた構成では、別 backend の長い試験を待つ時間が VSTest の無進行時間へ加算され、Browser／Vulkan の開始前に2分の監視が発火した。backend 単位に分けることで、この待機依存を除く。監視時間の延長、試験の skip、期待値の変更は行わない。

同一 backend を含むコマンドを複数起動した場合、backend 専用 mutex は process をまたいで排他する。その待ち時間は引き続き VSTest の監視対象なので、一つの全体実行へまとめる。`-m:1` は障害の切り分け用に指定できる。

## 新しいテストと backend

GPU を使うテストは担当 backend の既存 collection に置く。CPU の計算・fake・状態遷移の試験には GPU collection を付けない。共有状態が必要なら、その状態を共有するテストだけを collection にまとめる。

新しい backend は引数なしの専用 fixture から `GpuBackendTestGate` に一意の完全な mutex 名を渡す。backend の探索時に実 device を作る場合は `CreateProbe(probe, mutexName)` に同じ名前を渡し、不変の結果だけを保持する。別名へ分けて同一 backend の排他を回避しない。gate の所有 thread と、async fixture の破棄 thread が異なる場合も対応する。

実行結果は各 project の `TestResults/` に保存する。TRX の成功件数だけでなく、全 project の outcome が Completed であり、コマンドの終了コードが0であることを確認する。中断した実行を、完了済みの単体テストだけで成功とは扱わない。

## Shader compiler の適合試験

新しい `tools/Lumyte.Graphics.Native.Shaders.Offline.Tests` の `SlangConformance` と
`tools/Lumyte.Graphics.Portable.Shaders.Offline.Tests` の `ShaderToolchainConformance` は外部 compiler を起動する。
前者は Slang 2026.17 と DXC、後者は公式 Dawn v20260911.162847 の tint_info／tint を使う。
`LUMYTE_SLANGC`、`LUMYTE_DXC_DIRECTORY`、`LUMYTE_TINT_INFO` で配置を指定できる。
この PC の既存調査ディレクトリも探索するが、ネットワークからの自動取得は行わない。
Tint 未配置時には該当試験の skip 理由を報告し、全 compiler 適合試験に成功したとは扱わない。
各試験は固有の作業ディレクトリを使い、GPU collection には入れない。

外部 compiler を使わない実行は `--filter 'Category!=SlangConformance&Category!=ShaderToolchainConformance'`。
必須の最終確認では filter を付けず全体を実行する。
生成 C# と Resources 入力は consumer としてコンパイル・実行し、shader の生成物全文は比較しない。
[MSBuild consumer](../tools/experiments/shader-build-inputs/README.md) は targets の import 順を変えた接続例であり、
各 project を build／run して XML の手入力なしで動くことを確認できる。
