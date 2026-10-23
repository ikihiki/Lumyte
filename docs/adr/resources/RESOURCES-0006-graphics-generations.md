# ADR-RESOURCES-0006: Runtime基盤とGraphicsデバイス世代の接続

- 状態: 提案
- 日付: 2026-10-23

## 背景

GPU出力は物理デバイスに属する。ロスト後の値を利用させず、旧デバイスの解放前に子と利用を終了する。

## 決定

共通のRuntime依存契約・version・失効watermark・再構築・DI登録は前段のRuntime PRで導入する。
本PRはその基盤とGraphics.Hostingの名前付きIGraphicsDeviceServiceを接続するアダプターに限定する。
UseGraphicsAsyncは正確なデバイスleaseを取得し、identity・versionとBuild/Resident寿命を記録する。
Stageが借用leaseを独自にDisposeせず、GPU child解放後のcleanupで返す。
Resident依存のGPU値はロストで失効して親へ伝播し、Runtimeに依存しないCPU値は維持する。
初回ロードとHotReloadの両方で同じStageを使い、公開前にデバイス世代を再検証する。

## APIと契約

GraphicsResourceDependencyがserviceのepochをIResourceRuntimeDependencyへ対応させる。
ResourceGraphicsBuildExtensions.UseGraphicsAsyncは取得したepochを構築へ記録する。
DIのUseGraphicsServiceは同じ名前付きservice instanceへ接続する。
デバイス本体のロスト通知・回復実装は既存Graphics.Hostingを使用する。
アプリはretirement確認・旧GPU値の解放・旧owner退役・復旧を進め、RebuildRuntimeAsyncで既存handleを再接続する。
submissionとGPU完了待ちはStageと利用側の責任とする。

## 検討した代替案

Resourcesからデバイスを再作成する方式は、既存のGraphics.Hostingと寿命管理が重複する。
生のdeviceだけを保持する方式は、世代と失効を検証できない。

## 結果と影響

Graphics固有の借用と解放順を分離してレビューできる。
共通RuntimeとAudioはこのPRに依存せず、ハンドル・世代管理の直後から利用できる。
解放失敗時はchildとepoch leaseの所有を保持して再試行する。

## 検証方針

名前付きserviceの一致、旧世代失効、CPU値維持、復旧後の同一handle再接続、child → epochの解放順を検証する。

## 関連ADR

- [Runtimeと実行先](RESOURCES-0009-runtime-execution.md)
- [HotReload](RESOURCES-0008-hot-reload.md)
