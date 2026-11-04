# UI retained performance regression harness

PR #71のレビューに対応した、変更前後で同じ処理を実行するCPU測定です。
実Parleyと既存TextInput test fixture、実Fallback/Hosted manager、標準hostのEditor検証、Signalによる1,000要素の再構築を使用します。
fixtureの構築とreflectionによるdelegate取得は計測外です。test projectへの参照はこの非配布ハーネスに限定します。

```sh
DOTNET_TieredCompilation=0 DOTNET_gcServer=0 dotnet run \
  --project tools/benchmarks/ui-retained -c Release --no-build
```

先にRelease buildとParley nativeの準備を行います。SDKはリポジトリの指定に合わせます。
計測は他のbuildや計測と並行実行しません。
各caseで5回warmup後、5 batchを順次測定し、batch平均の中央値と各batchの生データをJSON Linesへ出力します。
割当はGC.GetAllocatedBytesForCurrentThreadで、常駐メモリ量とは異なります。GCはbatch間だけ実行します。

- IME idle: 文書・layout・clip等が同じPublishGeometry。各batch 5回。
- Range generation: 全書記素のGetRangeBounds。各batch 5回。shapingは計測外。
- Window idle: 0/1,000/10,000件の操作が終端になった後のAdvance。各batch 100回。setup中のsupersedeも終端結果として検証します。
- Editor validation: TextBoxなし10,000要素のValidateEditors。各batch 100回。host全turnの測定とは区別します。
- Signal rebuild: 浅いPanelの1,000要素の幅を変更し、再構築とlayoutまで実行。各batch 10回。

geometryの存在と文書revision、全要求の完了をassertします。
GPU・実OS IME・OS loop・p99遅延は測定しません。過去レビューの数値とは測定環境・ハーネスが異なるため直接比較しません。

[測定結果と環境・全batch](../../../docs/performance/ui-retained-review.md)を保存しています。

`--signal-only`はIME・Windowの測定を省き、Editor検証と100回/batchのSignal再構築を実行します。短い計測の時間ばらつきを確認するために使用しました。
