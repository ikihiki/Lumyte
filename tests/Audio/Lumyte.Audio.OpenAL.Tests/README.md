# OpenAL 回帰テスト

OpenAL Soft をインストールし、明示的に有効化して実行します。通常のsolutionテストではスキップし、共通CIのLinux x64で有効化して実行します。ヘッドレス環境では null driver を指定します。

```sh
LUMYTE_OPENAL_TESTS=1 ALSOFT_DRIVERS=null dotnet test tests/Audio/Lumyte.Audio.OpenAL.Tests -c Release
```

- Native の source 上限まで voice を生成し、割り当て失敗直後の既存 voice の解放、再割り当て、再生、clip／device の終了を検証します。OpenAL Soft の既定設定を使用してください。source 上限を 4096 以上に増やした設定は、この枯渇テストの対象外です。
- loss状態を注入し、旧voiceの制御を拒否しながら旧context上で子を解放できること、新deviceの生成物が影響を受けないことを検証します。このテストは切断検出後の契約を確認するもので、物理デバイスの抜き差しは再現していません。

テストは実際の Native ライブラリを使いますが、null driver での成功はスピーカーでの可聴品質を保証しません。
