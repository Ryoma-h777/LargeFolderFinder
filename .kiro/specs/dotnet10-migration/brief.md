# Brief: dotnet10-migration

## Problem

本アプリは .NET Framework 4.8 を対象としている。4.8 は保守モードにあり新機能は追加されない。さらに `LangVersion 12.0` を指定していながら C# 12 の言語機能をほとんど使っておらず、技術的な追従が止まっている。

より実務的な問題として、**.NET Framework 上では解決できない欠陥がある**。260文字を超えるパスが走査から無言で脱落する問題は、.NET Framework の `MAX_PATH` チェックに由来する。NAS の深い階層を探索することがこのアプリの中核用途である以上、これは看過できない。

また、この移行を後回しにすると、テストコードの整備・長いパス対応・言語機能の適用がすべて二重作業になる。

## Current State

- ターゲット: `net48`、SDK 形式の csproj、`UseWPF` + `UseWindowsForms`
- 依存: MessagePack 3.1.7、YamlDotNet 16.3.0、Ookii.Dialogs.Wpf 5.0.1、Fody 6.8.2 + Costura.Fody 6.0.0
- 配布: Costura により依存 DLL を exe へ埋め込み、GitHub Releases で zip 配布
- CI なし。ビルドとリリースは手作業
- `ConfigurationManager` / `Properties.Settings` / `app.config` / `Assembly.Location` は**いずれも未使用**（調査済み。単一 exe 化の既知の障害を回避できている）
- `AppDomain.CurrentDomain.BaseDirectory` を8箇所で使用し、`Resources/` 配下のリソースと `Config.txt` を参照している

## Desired Outcome

- .NET 10（LTS、サポート終了 2028年11月14日）でビルド・動作する
- 依存が整理され、すべて最新かつ安全なバージョンになっている
- **自己完結・単一exe 版（既定）とフレームワーク依存版（軽量）の2形態**が GitHub Actions で自動生成される
- リリース作業の手間が現状から増えていない

## Approach

移行と同時に依存の整理を行う。実現性検証で確定した事項に従う。

1. **ターゲットを `net10.0-windows` へ変更**
2. **Costura.Fody + Fody を除去し、`PublishSingleFile` へ置き換える**。`RuntimeIdentifier` の指定と `IncludeNativeLibrariesForSelfExtract=true` が必須
3. **Ookii.Dialogs.Wpf を削除し、標準の `Microsoft.Win32.OpenFolderDialog` へ置き換える**（最終リリースが2021年12月で実質メンテナンス停止。標準 API で同等機能が得られ、依存を1つ減らせる）
4. **依存を更新**: MessagePack 3.1.8 以上、YamlDotNet 最新、CommunityToolkit.Mvvm 8.4.2（導入は `architecture-refactoring` だが、バージョン制約はここで確定させる）
5. **`global.json` で SDK を固定**する（WPF + 単一exe のリグレッション回避）
6. **GitHub Actions で2形態を自動ビルド**し、生成した exe が実際に起動することを確認する工程を含める
7. **xUnit v3 のテストプロジェクトを新規作成**し、`scan-golden-baseline` の期待値データと突き合わせる

## Scope

- **In**:
  - ターゲットフレームワークの変更と、それに伴うコード修正
  - Costura.Fody / Fody の除去と PublishSingleFile 化
  - Ookii.Dialogs.Wpf の削除と標準ダイアログへの置き換え
  - 依存バージョンの更新とバージョン下限の確定
  - `global.json` による SDK 固定
  - GitHub Actions による2形態の自動ビルドと起動確認
  - xUnit v3 テストプロジェクトの新設と、ゴールデンデータとの突き合わせ
  - `ThirdPartyNotices.txt` の更新（削除・追加した依存の反映）
  - 移行前後で走査結果の数値が変わっていないことの確認

- **Out**:
  - 長いパス対応の実装（`scan-correctness` の範囲。移行はその前提を整えるだけ）
  - 速度の最適化（`scan-performance` の範囲）
  - CommunityToolkit.Mvvm を使った実際の書き換え（`architecture-refactoring` の範囲）
  - C# の新しい言語機能の全面適用（`architecture-refactoring` の範囲。ここでは移行に必要な最小限にとどめる）
  - UI の変更

## Boundary Candidates

- ターゲット変更と依存整理（プロジェクト設定）
- 配布方式の変更（Costura → PublishSingleFile）
- CI 構築（GitHub Actions）
- テスト基盤の新設（xUnit v3）

## Out of Boundary

- 機能の追加・変更
- 保存データ形式の変更（互換性は捨てる方針だが、実際の形式変更は `scan-performance` 以降で構造を変える際に行う）

## Upstream / Downstream

- **Upstream**: `scan-golden-baseline`（移行によって集計値が変わっていないことを確認するために必要）
- **Downstream**: `scan-correctness`、`scan-performance`、`architecture-refactoring`、`ui-redesign` のすべてが本スペックの完了を前提とする

## Constraints

実現性検証（2026年9月時点）で確定した制約。いずれも回避策込みで確認済み。

- **`--self-contained false` は .NET 10 SDK で機能しない。** self-contained フラグが MSBuild に転送されず、常に自己完結ビルドになる（[dotnet/sdk#51888](https://github.com/dotnet/sdk/issues/51888)、SDK 10.0.201 でも再現、オープン中）。**必ず `--no-self-contained` または csproj の `<SelfContained>false</SelfContained>` を使うこと**
- **WPF + PublishSingleFile に SDK 10.0.200 / 10.0.202 のリグレッションあり。** 起動時 `XamlParseException`、配置フォルダー名による無言終了、カスタム `AssemblyName` でのビルドエラーが報告されている（[dotnet/wpf#11678](https://github.com/dotnet/wpf/issues/11678)、未トリアージ）。**`global.json` で SDK を固定し、CI に exe の起動確認を必ず組み込むこと。** 動作実績のある 10.0.103 / 10.0.106 系も候補
- **WPF はトリミング非対応**（[dotnet/wpf#3811](https://github.com/dotnet/wpf/issues/3811) オープン中）。自己完結版のサイズは 80〜200MB 程度を見込む。削減は `EnableCompressionInSingleFile` による起動コストとのトレードオフのみ
- **`PublishSingleFile` では `Assembly.Location` が空文字を返し、`Assembly.CodeBase` / `GetFile` は例外を投げる。** 本プロジェクトはこれらを使用していないが、`AppDomain.CurrentDomain.BaseDirectory` を8箇所で使用しているため、単一exe 環境で `Resources/` と `Config.txt` が正しく解決されることを実機で確認すること
- **MessagePack は 3.1.8 以上**とする。`>= 3.0, < 3.1.7` を対象とする脆弱性が10件（うち High 1件）登録されており、現行の 3.1.7 は修正版ちょうどで余裕がない。LZ4 展開の無制限確保（`GHSA-v72x-2h86-7f8m`）と `MessagePackReader.Skip` の深度制限欠如（`GHSA-vh6j-jc39-fggf`）は本アプリの使い方に直接関係する
- **CommunityToolkit.Mvvm は 8.4.2 を指定。** 8.4.0 は .NET 10 でビルド不能（`MVVMTK0041` / `CS9248`）
- **ライセンス**: 追加・削除した依存を `Resources/License/ThirdPartyNotices.txt` に反映すること。xUnit v3 は Apache-2.0 で著作権表示の同梱義務があるが、テスト専用で配布物に含まれないなら記載不要（配布物に含まれるかで判断する）
- GitHub Actions の `windows-2025` ランナーには .NET 10 SDK と `Microsoft.WindowsDesktop.App` がプリインストール済み。ただし上記のリグレッション対策として `actions/setup-dotnet` と `global.json` で明示的に固定すること
