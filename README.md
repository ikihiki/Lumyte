# Lumyte

C# を中心に、DirectX／Vulkan の Native バックエンドに C++ を使用するゲームエンジン。

- [開発環境のセットアップ](docs/development-environment.md)
- [ADR の書き方と運用](docs/adr/0001-adr-writing-policy.md)
- [リポジトリのフォルダ構成](docs/adr/0002-repository-layout.md)
- [mise による共通開発環境の設計](docs/adr/0003-development-environment.md)
- [コードスタイルと lint](docs/development-environment.md#コードスタイル)

- [Composition の設計](docs/adr/composition/COMPOSITION-0001-declarative-composition.md)
- [Composition の利用例](samples/Lumyte.Composition.Sample/README.md)

ソリューションは `Lumyte.slnx` を使用します。`mise exec -- dotnet test Lumyte.slnx -c Release`
で `tests/` 内のソリューションに登録されたテストを実行できます。
CI でも main への push とすべての PR でテストを実行し、TRX 形式の結果を保存します。
