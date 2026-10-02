# VeriRandom への貢献

VeriRandom への問題報告、改善提案、ドキュメント改善、コード提供に感謝します。

> [!IMPORTANT]
> 本製品は SecRandom のオープンソースフォークであり、SecRandom 本体とは見なされません。本ガイドに記載するリポジトリ、Issue、リリースフローはすべて本フォーク（VeriRandom）を指します。上流プロジェクトへ問題を報告する場合は [SECTL/SecRandom](https://github.com/SECTL/SecRandom) を使用してください。

**言語** [ [简体中文](../CONTRIBUTING.md) | [English](CONTRIBUTING_EN.md) | **日本語** ]

## Issue の作成

- 本フォークの不具合報告や機能要望には [GitHub Issues](https://github.com/WinFunan/VeriRandom/issues) を使用してください。
- 不具合には、再現手順、期待する動作、実際の動作、VeriRandom のバージョン、システム環境、必要なログまたはスクリーンショットを含めてください。
- 機能要望には、利用場面、期待する動作、既存機能では満たせない理由を記載してください。
- パスワード、TOTP シークレット、USB バインディングトークン、その他の機密情報を公開 Issue、ログ、スクリーンショットに含めないでください。

## 開発環境

VeriRandom v3 は .NET デスクトップアプリケーションです。

| 分類 | 技術 | 用途 |
| --- | --- | --- |
| 言語とランタイム | C# / .NET 10 | アプリケーション、コアサービス、テスト |
| デスクトップ UI | Avalonia + FluentAvalonia | クロスプラットフォームのデスクトップ UI |
| 依存性注入 | Microsoft.Extensions.Hosting | アプリケーションサービスと ViewModel の構成 |
| テスト | xUnit v3 | 単体テスト |
| ビルドとリリース | GitHub Actions | マルチプラットフォームのビルド、パッケージ、リリース |

### 前提条件

- [.NET SDK 10.0.x](https://dotnet.microsoft.com/download/dotnet/10.0)
- Git
- 対象プラットフォームに対応するデスクトップ実行環境。パッケージとインストールの詳細は各リリースノートを参照してください。

### プロジェクトの取得と実行

```bash
git clone https://github.com/WinFunan/VeriRandom.git
cd VeriRandom
git remote add upstream https://github.com/SECTL/SecRandom.git

dotnet restore SecRandom.sln
dotnet build SecRandom.sln -c Release --no-restore
dotnet test SecRandom.sln -c Release --no-build
dotnet run --project SecRandom.Desktop/SecRandom.Desktop.csproj
```

コアのテストプロジェクトだけを検証する場合:

```bash
dotnet test SecRandom.Core.Tests/SecRandom.Core.Tests.csproj -c Release --no-restore
```

## コード規約

変更前にルートの [AGENTS.md](../AGENTS.md) と [プロジェクトルール](../docs/project_rules.md) を読んでください。そこにある要件は一般的なコーディング習慣より優先されます。特に次の規則が重要です。

- `SecRandom.Desktop` は起動シェルのみです。アプリケーションの業務ロジックを置かないでください。
- 再利用可能なサービスと ViewModel は `SecRandom/App.axaml.cs` の `BuildHost()` に登録します。ページから再利用可能なサービスを直接 `new` しないでください。
- ナビゲーションページには `[PageInfo(...)]` と `AddMainPage<T>()` または `AddSettingsPage<T>()` の登録が必要です。サイドバーメニューをハードコードしないでください。
- UI テキストは `Langs` 配下のページごとのリソースディレクトリに置きます。基本、英語、日本語のリソースは同じキー集合を維持し、すべてのページテキストを共有リソースへ移動しないでください。
- 生徒、賞品、履歴の内部 ID には `RecordId` を使用します。表示 ID や名前は必須の一意識別子ではありません。
- 辞書とコレクションの直接変更では設定が自動保存されません。所有するコードが適切なライフサイクル境界で保存してください。
- すべてのセキュリティ認可は `ISecurityService` を経由します。資格情報を通常設定、ログ、エクスポート、診断情報へ含めてはいけません。
- 新しいアプリケーションアイコンには、raw Unicode Fluent 字形ではなく、プロジェクトの Fluent Filled アイコンシステムを使用してください。

## 変更範囲とテスト

- 変更は解決する問題に必要な範囲へ絞り、無関係なリファクタリングを避けてください。
- 新規または修正したコア動作には、特に抽選、設定、インポート/エクスポート、セキュリティ、証明、共有契約について焦点を絞ったテストを追加してください。
- UI、システム連携、権限が必要な動作、更新、クロスプラットフォーム動作には実行時の検証が必要です。コンパイル成功だけでは十分ではありません。
- 生成物の `bin/`、`obj/`、`artifacts/`、`publish/`、パッケージ出力をコミットしないでください。
- ドキュメントを変更する場合、簡体中国語、英語、日本語の README と貢献ガイドで事実、リンク、構造を一致させてください。

## 貢献者ライセンス契約（CLA）

最初の Pull Request の前に [CLA](CLA_JA.md)（簡体字中国語の [CLA.md](../CLA.md) が正式版）を読み、署名してください。PR で次を返信すると署名とみなされます。

> I have read the CLA Document and I hereby sign the CLA
>
> 我已阅读 CLA 文件并在此签署本协议

CLA は、あなたの貢献をライセンスおよび再ライセンスする権利をメンテナーに付与し、出站ライセンスを変更する前に 30 日以上の公示を義務付けます。本リポジトリは SecRandom のオープンソースフォークであり、CLA は上流の GPLv3 コードのライセンスを**変更できません**。

## コミットと Pull Request

- 対象となる現在のブランチからトピックブランチを作成し、作業開始前に upstream と同期してください。
- 例として `fix: correct proof retention cleanup` のように、[Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/) を推奨します。
- Pull Request では、問題、対応方針、検証内容を説明してください。UI 変更にはスクリーンショットまたは録画を添付し、動作変更には互換性や移行への影響を記載してください。
- PR を開く前に、変更範囲に適した restore、build、test を実行し、実行していない確認項目と理由を記載してください。
- 人間によるレビューなしに生成 AI の出力を提出しないでください。貢献者は正確性とライセンス遵守に責任を負います。

## CI とリリース

リポジトリは GitHub Actions を使用しています。

- `.github/workflows/build_publish.yml` はマルチプラットフォームビルド、パッケージ、署名付きマニフェスト生成、手動リリースフローを担当します。
- `.github/workflows/codeQL.yml` は CodeQL セキュリティ分析を実行します。

通常の貢献では、コミットメッセージのキーワードによってリリースを起動しません。メンテナーが明示的なリリースタグを指定して、手動起動の GitHub Actions ワークフローから公開します。

貢献ありがとうございます。
