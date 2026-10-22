# ADR-RESOURCES-0007: CPUで集約したシーンからGPU表現を構築する

- 状態: 提案
- 日付: 2026-10-20

## 背景

複合documentから階層・メッシュ・マテリアル・外部画像を取得し、
GPUなしでも内容を確認できる中間表現が必要である。多段Pipelineを実描画で検証したい。

## 決定

documentからCPUシーンを集約し、その後GPUシーンへ変換する。
CPUシーンはノードの親子関係、local/world行列、共有CPUメッシュ・マテリアル・デコード済み画像を保持する。
CPUノード・マテリアルは具体型の子への参照を保持し、デバイス初期化を必要としない。
ノードにメッシュのキー、マテリアルに画像のキーを重複保持せず、参照先の型が自身の識別情報を持つ。
メッシュ→マテリアル→画像をResident依存として保持し、CPUシーン利用中のtrimで参照を失わない。

GPUシーンStageはCPU階層とアドレスを使い、GPUメッシュのPipelineを要求する。
GPUメッシュは同じ構築viewのCPUメッシュを入力にする。画像とshader artifactも別のPipelineで解決する。
GPUシーンにとってCPUシーンはBuild入力とし、GPU子へのResident依存は別に保持する。
共有ノードはGPUメッシュを共有し、各ノードのworld行列で描画する。

これはsample内部の設計であり、汎用モデル形式ライブラリの公開APIを追加しない。
CPUシーンを別managerへ移してGPU化するAPIは定めない。
画像更新ではCPUマテリアル・メッシュ・シーンとGPU表現も再構築する。
ジオメトリとマテリアル更新の細分化は将来の評価対象とする。

## APIと契約

比較元はこのPRの依存ブランチ。以下は判断対象の追加・変更APIの抜粋。

```diff
+// Sample内部の境界。新たな公開APIは追加しない。
+internal sealed record CpuScene(IReadOnlyList<CpuSceneNode> Roots);
+internal sealed record CpuSceneNode(int Index, Matrix4x4 Local, Matrix4x4 World,
+    CpuMesh? Mesh, IReadOnlyList<CpuSceneNode> Children);
+internal sealed class SceneStage : IResourceTransform<Document, CpuScene> { }
+internal sealed class SceneUploadStage : IResourceTransform<CpuScene, GpuScene> { }
```

## 検討した代替案

documentから直接GPUシーンを構築すると、CPUだけでシーン全体を取得できない。
CPU側にGPUキーを持たせると中間表現がデバイス依存の解決を要求する。

## 結果と影響

CPU段階で依存と階層を検証できる。現在のGPU変換はmanagerのキー解決へ依存し、
CPU値を渡すだけの独立コンバーターではない。画像だけの更新でもメッシュ再構築と頂点転送が発生する。

## 検証方針

GPU未初期化でのCPUシーン取得、階層と行列合成、共有、trim後の寿命、
循環や画像欠落時のrollback、実GPU描画、画像だけの自動更新、旧leaseの画像再現とcharge返却を検証する。

## 関連ADR

- [ADR-RESOURCES-0004](RESOURCES-0004-source-access.md)
- [ADR-RESOURCES-0005](RESOURCES-0005-automatic-monitoring.md)
- [ADR-RESOURCES-0006](RESOURCES-0006-graphics-generations.md)
