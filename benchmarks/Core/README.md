# Coreカテゴリの評価

Coreの主な機能を対象別の13 benchmark projectで測定する。
DI登録や製品間アダプターの連携操作は測定ケースに含めず、既存の正当性確認用helperとして保持する。
測定対象だけがBenchmarkDotNetのカタログへ現れるため、評価範囲を表す追加カテゴリは設けない。

## カテゴリは測定目的だけを表す

Coreの`BenchmarkCategory`は次の3種類に限定する。

- `Screening`: 短時間で退行候補を見つけるためのケース。性能差の確定や保証ではない。
- `Comparison`: 条件を揃えて変更前後・実装方式を比較するためのケース。
- `Diagnostic`: トレースや詳細調査によって時間・割り当ての原因を調べるためのケース。

同じケースが複数の目的に参加できる。目的を宣言しないケースをmodeへ自動参加させない。
診断専用のケースには`Diagnostic`だけを宣言し、通常比較へ混ぜない。
機能名、所有project、初期化・構築・寿命・競合などを追加のカテゴリとして重ねない。

## 機能は名前空間・クラス・メソッドで表す

例として、Mathematicsは次の型階層から選択できる。

~~~text
Lumyte.Mathematics.Benchmark
  GeometryQueries
    GeometryBenchmarks
    SpatialBenchmarks
    ShapeBenchmarks
    BatchBenchmark
  ColorOperations
    ColorBenchmarks
    ColorLookupBenchmarks
    ColorModelBenchmarks
  InterpolationFunctions
    MathBenchmarks
    TimingBenchmarks
~~~

機能の選択は、この名前空間・クラス・メソッドに対するBDNのglobで行う。
対象projectは`--benchmark-project`、型・操作は`--benchmark-filter`で指定する。
カタログのType／Method／Parametersと公式BDN result identityを正本とし、管理用の別名を増やさない。

## 実行

固定SDKをPATHへ設定し、リポジトリルートで実行する。通常の`BenchmarkCore`は単独method調査にも使える。

~~~sh
# 全13projectのscreen対象を列挙する。時間測定はしない
dotnet run --project build -c Release -- --target BenchmarkCoreEvaluate --benchmark-mode screen --benchmark-list --output artifacts/core-plan

# 全projectのscreen対象を直列測定・集計する
dotnet run --project build -c Release -- --target BenchmarkCoreEvaluate --benchmark-mode screen --output artifacts/core-evaluation

# 名前空間の階層で幾何の機能群を選ぶ
dotnet run --project build -c Release -- --target BenchmarkCoreEvaluate --benchmark-mode screen --benchmark-project Lumyte.Mathematics --benchmark-filter "*GeometryQueries.*" --output artifacts/core-geometry

# 色名lookupのクラスだけを選ぶ
dotnet run --project build -c Release -- --target BenchmarkCoreEvaluate --benchmark-mode screen --benchmark-project Lumyte.Mathematics --benchmark-filter "*ColorLookupBenchmarks.*" --output artifacts/core-color-lookup

# 通常の単独method調査
dotnet run --project build -c Release -- --target BenchmarkCore --benchmark-mode screen --benchmark-project Lumyte.Signals --benchmark-filter "*FanoutBenchmarks.Update" --output artifacts/core-fanout
~~~

`screen`は1 launch・warmup3・測定8 iterations・150ms target。
`compare`は3 launches、`diagnose`は短時間測定条件と追加EventPipe計測を使用する。
`compare`の起動だけで異なる製品revisionの比較が自動成立するわけではない。
`--benchmark-list`はmodeなしでも全purposeのカタログを列挙できる。

全選択projectのbuildとカタログ取得を済ませてから、Type.Methodごとの有限partitionを直列測定する。
method globの判定は、カタログ列挙時にもBDN自身のGlobFilterを使用する。
各build・catalog command・測定partitionに`--benchmark-timeout-seconds`（既定・上限900秒）を個別に適用する。
全カテゴリが900秒で終わるという意味ではない。子process時間の最大合計をplanへ記録する。
自動retry／resumeは行わず、出力は新しい空directoryを要求する。

`summary.json`／`summary.md`に予定・観測・欠落・想定外のケース、filter／modeによる除外理由、
中央値・iteration p95・SD／IQR・managed B/opを保存する。
元のfull JSON・log・カタログ・source／harness／binaryの情報を保持する。
空選択、重複、欠落、sample不足、不正結果、異なるjob／hostは成功として扱わない。
列挙だけは`planned-untimed`、部分選択は`completed-subset`、途中失敗は`incomplete`として残す。
mode対象の完了と、診断専用ケースまで含む全カテゴリの完了は別々に記録する。

## 測定する境界と解釈

各メソッドの説明とSetup／Cleanupから、含まれる仕事を確認する。
構築・ファイルI/O・所有と破棄・コード生成などの違いは、型／メソッドと説明で示す。
GlobalSetup等の準備は計測外だが、測定メソッド内のbuffer生成・結果回収・参照破棄は時間と割当へ含む。
batch／waveの1opはメソッド全体であり、Countの意味もfixtureごとに確認する。
IterationSetup付きの有限waveは通常の反復throughputとは測定形態が異なる。

120Hzの全体予算は8.333msだが、カテゴリやcaseの存在から毎フレーム必要な仕事とは判断しない。
実際に使う操作数とその境界を揃えて検討し、同じ仕事を含む複数caseを二重加算しない。
BDNのiteration p95は実ゲームの個別フレームp95ではない。managed B/opは保持heapやGC停止時間でもない。
微小時間の測定限界付近の数値から処理能力の保証を作らない。

Settingsのtyped snapshot／編集／保存は内部実装がDI構成を必要とするため測定対象外とする。
公開source／storeは測定できる。内部APIを公開したりreflectionで境界を迂回したりしない。
主な機能ファミリーを代表入力で評価するスイートであり、全overload・数値型・例外分岐の総当たりではない。
ゲームロジック・物理・入力・Animation・UI・描画・GPU・Present／VSyncは別途統合評価が必要である。

## 正当性確認

~~~sh
dotnet test tests/Core/Lumyte.Core.Benchmark.Tests -c Release
dotnet test tests/Core/Lumyte.Signals.Benchmarks.Tests -c Release
dotnet test tests/Tools/Lumyte.Build.Tests -c Release
~~~

機能結果、状態の復元、所有と破棄、全13projectの実ケース、目的カテゴリだけのmetadataを検証する。
時間や割当量の閾値を通常の単体テストの合否には使用しない。
