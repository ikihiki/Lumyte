# Coreカテゴリの独立評価

Coreは毎フレームの実行だけでなく、構築・破棄・ファイル操作・コード生成を含む。
このスイートでは、実行可能なケースのカタログと計測結果を区別し、未測定を合格と扱わない。
既存のBenchmarkDotNet 0.15.8、対象別project、Falloutを使用する。

## 対象

| 製品 | 独立して評価する主な仕事 |
| --- | --- |
| Lumyte.Core | 単調時計、手動時計、Duration／TimePoint演算 |
| Lumyte.Mathematics | 2D／3D形状・境界・交差判定、距離batch、補間、色・pixel変換 |
| Lumyte.Composition | slotのcapture／apply、adapter、生成factory、recipe再構築 |
| Lumyte.Composition.Generators | 初回生成、変更／無変更／無関係編集の増分生成 |
| Lumyte.Execution | command、batch、graph、range、workflow、sender、gate、lifecycle |
| Lumyte.Execution.Generators | 初回生成と増分生成 |
| Lumyte.Memory | scope、予約・commit、charge、chunk取得／再利用、回収、競合 |
| Lumyte.Resources | 取得・pin・snapshot、依存graph、reload、世代寿命、独立stream |
| Lumyte.Settings | 公開sourceのJSON読込み、破損／既定値経路、storeのファイルread／write |
| Lumyte.Signals | 読出し・変更、effect配送、batch、computed／共有DAG、scheduler、非同期状態、寿命 |
| Lumyte.StateMachines | 定義構築、instance、遷移、guard候補選択、失敗・診断経路 |
| Lumyte.TextEditing | 文書編集、selection／composition、grapheme、変更drain、IME候補 |
| Lumyte.Worlds | 所有構築、境界commit、input／fixed／update／drawのCPU配送、破棄 |

「主な仕事」は機能ファミリーの範囲であり、全公開overload・型・例外分岐を総当たりするという意味ではない。
具体的な入力と拡張後ケース数は、コンパイルしたカタログのType／Method／Parametersを正本とする。
DTOやinterfaceだけを空のbenchmarkにせず、その契約を実際に使う操作で観測する。

## 除外境界

独立評価は`Standalone`カテゴリを持つケースに限定する。
以下の連携projectは独立した評価対象に含めない。

- Lumyte.Memory.DependencyInjection
- Lumyte.Resources.DependencyInjection
- Lumyte.Resources.Embedded
- Lumyte.Resources.Execution

同じbenchmark projectに残っているDI登録／provider構築、ResourcesとExecutionの統合stream、
他の製品を主対象とするComposition consumerなども`Standalone`に含めない。
既存ケースは削除せず、`Integration`／`DependencyInjection`として通常のカタログから参照できる。
Settingsのtyped snapshot／編集／保存は内部実装がDI登録を前提にするため、既存のDI付きケースでのみ参照でき、独立評価へ含めない。
この境界を隠すためのreflectionや製品API公開変更は行わない。
製品自体の依存先をビルドすることと、連携機能を測定対象に含めることは別である。
Resources本体の独立評価では、in-memory source／inline runtime等で外部実行を固定する。

## 測定境界

- `SteadyState`: 初期化済みの通常操作。ただしメソッド内にあるbuffer生成・pin・結果回収・破棄は時間／割当へ含む。
- `Construction`: 新しい定義やownerの構築。繰返しのwarm測定であり、process cold startを意味しない。
- `Lifetime`: 所有と破棄を含む操作。初回構築と継続利用を区別する。
- `CompileTime`: ソース生成。120Hzのフレーム予算へ加算しない。
- `FileIO`: 設定ファイル等のI/O。in-memory処理や実機storage保証と混同しない。
- `Contention`: 有限の競合wave。thread起床／待機を含み、通常の単一thread throughputとは別。

カテゴリは重複できる。タグから「この操作が必ず毎フレーム必要」とは推論しない。
batch／waveの1opはメソッド全体。Countがコマンド数、要素数、定義の状態数のどれかを各fixtureで確認する。
GlobalSetup等の準備は計測外で、事前構築した入力・所有状態を明記する。
IterationSetupを使う破壊的・有限waveは1 invocation型で、150ms targetの通常throughputと同じ形ではない。

## 検証

正当性は既存の通常テストで検証する。時間やmanaged割当量の閾値を単体テストの合否に使わない。

~~~sh
dotnet test tests/Core/Lumyte.Core.Benchmark.Tests -c Release
dotnet test tests/Core/Lumyte.Signals.Benchmarks.Tests -c Release
dotnet test tests/Tools/Lumyte.Build.Tests -c Release
~~~

独立13projectの実行可能ケース、DI／連携除外、生成器のCompileTime分類をcompiled metadataで検査する。
空選択、mode不一致、結果欠落や不正値は成功へ変換しない。

## 120Hzでの解釈

120Hzの全体予算は8.333ms。部品の1op時間から、実際に必要な操作数を掛けた算術予算を検討する。
複数の操作に同じ仕事が含まれる場合は二重加算しない。個々のケースを足した値は統合フレーム測定ではない。
BDNの中央値・p95はiteration推定値であり、個々のゲームフレームのp95／p99ではない。
managed B/opは保持heap／native memoryではなく、GC停止時間の保証でもない。

CPU-onlyのカウンターscene、整数resource、単純handlerは基盤の費用を見るfixtureである。
実ゲームのAI・物理・入力・Animation・UI・描画・GPU・Present／VSyncは各所有カテゴリと統合実行で別途評価する。
機種やruntimeが違う結果を直接比較せず、SDK・CPU・OS・source／harness／binaryを固定する。

## カタログ・評価の実行

固定SDKをPATHへ設定し、リポジトリルートで実行する。既存の`BenchmarkCore`は通常のmethod glob測定として残す。
DI・連携を除くカテゴリ評価には`BenchmarkCoreEvaluate`を使用する。

~~~sh
# 全13projectをビルドし、screenで実行する独立ケースを列挙する（計測しない）
dotnet run --project build -c Release -- --target BenchmarkCoreEvaluate --benchmark-mode screen --benchmark-list --output artifacts/core-plan

# 同じ独立範囲を全件計測する（ケース数に応じて長時間かかる）
dotnet run --project build -c Release -- --target BenchmarkCoreEvaluate --benchmark-mode screen --output artifacts/core-evaluation

# 通常操作だけを抽出し、8.333msに対する1opの比率も表示する
dotnet run --project build -c Release -- --target BenchmarkCoreEvaluate --benchmark-mode screen --benchmark-category SteadyState --benchmark-budget-share --output artifacts/core-steady

# 対象projectを絞る。結果にはカテゴリ全体ではなくsubsetとして記録する
dotnet run --project build -c Release -- --target BenchmarkCoreEvaluate --benchmark-mode screen --benchmark-project Lumyte.Core --output artifacts/core-time

# 既存の単独method選択。独立カテゴリを明示する
dotnet run --project build -c Release -- --target BenchmarkCore --benchmark-mode screen --benchmark-project Lumyte.Signals --benchmark-category Standalone --benchmark-filter "*FanoutBenchmarks.Update" --output artifacts/core-fanout
~~~

評価ターゲットは`Standalone`を必須にし、追加カテゴリがある場合はAND条件で選ぶ。
用途は`screen`（1 launch、warmup3、測定8、150ms target）、`compare`（3 launches）、
`diagnose`（screen条件＋追加EventPipe計測）から明示する。
`--benchmark-list`だけはmodeなしで全modeのカタログを作成できる。

全projectのビルドとカタログ収集が完了してから、Type＋Methodごとの有限partitionを直列に計測する。
各partitionは全パラメーターを含み、`--benchmark-timeout-seconds`（既定900、上限900秒）を個別に適用する。
準備の各build／catalog commandにも個別期限を適用する。全カテゴリ全体が900秒以内に終わるという意味ではない。
planにpartition数と子process時間の最大合計を残す。自動retry／resumeは行わない。
出力は新しい空directoryを指定する。過去結果の混入や重複実行を避けるため、既存の非空出力先は拒否する。

`summary.json`／`summary.md`に、独立範囲・mode・追加filterの除外理由、予定／観測ケース、
欠落・想定外・重複、中央値・iteration p95・ばらつき・managed B/opを保存する。
元のBDN full JSONとlog、コンパイルしたカタログ、入力／実行出力ハッシュも保持する。
カタログ列挙は`planned-untimed`であり、計測完了や性能合格ではない。
project／追加カテゴリを絞った実行は`completed-subset`で、全カテゴリ完了を意味しない。
全projectを指定してもmodeで除外された診断専用ケースがあれば、mode対象の完了と全独立ケースの完了を区別する。
`full_mode_eligible_scope_complete`と`full_category_complete`は別々に記録する。
途中の失敗・期限超過は`incomplete`として残し、非zeroで終了する。

`BenchmarkCoreEvaluate`はmethod globを`*`に固定する。個別method調査は既存の`BenchmarkCore`を使う。
生成器・構築・I/OをSteadyStateのフレーム費用へ混ぜないため、カテゴリ別に結果を読む。
全件実行は他のbuild／test／計測と並行しない。同一sourceでも繰返しlaunchの変動は別途確認する。
