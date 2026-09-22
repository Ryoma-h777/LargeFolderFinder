# プロジェクト構造

## 構成方針

**レイヤー別（役割別）のフラット構成**です。機能単位ではなくレイヤー単位でディレクトリを切り、各レイヤーは 1 階層に収めます。規模的にサブプロジェクト分割は行っていません。

## ディレクトリのパターン

### Models — データ構造と定数
**場所**: `Models/`
**入るもの**: 永続化・受け渡しの対象になるデータ型、列挙型、アプリ全体の定数
**例**: `FolderInfo`（走査結果ツリーのノード）、`SessionData`（タブ 1 つ分）、`AppConstants`

`AppConstants` は定数の集約先です。マジックナンバー・パス名・書式・ログ文言はここに置きます。

### Services — 処理ロジック
**場所**: `Services/`
**入るもの**: UI に依存しない処理。1 クラス 1 責務
**例**: `Scanner`（走査）、`FolderCounter`（事前カウント）、`ScanSkipRecorder`（走査のスキップの記録）、`ResultFormatter`（テキスト整形）、`TreeFilter`（絞り込み）、`SessionFileManager`（永続化）、`LocalizationManager`、`Logger`

**判断基準**: WPF の型（`Window`、`Control` 等）を参照せずに書けるものは Services に置きます。

### ViewModels — 画面状態
**場所**: `ViewModels/`
**入るもの**: `INotifyPropertyChanged` を実装し、タブ・セッションの状態を保持するクラス

### Views — 画面
**場所**: `Views/`、レイアウト実装は `Views/Layouts/`
**入るもの**: XAML とそのコードビハインド

### Helpers — 横断的な小道具
**場所**: `Helpers/`
**入るもの**: どのレイヤーからも呼ばれる補助。P/Invoke（`Win32`）、`LatestOnlyCancellation`（最新の要求だけを有効にする取り消し）、`RelayCommand`、`TextMeasurer`、`AppSettings`、`AppInfo`

### Resources — 配布物に含めるファイル
**場所**: `Resources/`
**構成**: `Languages/{lang}.yaml`、`Readme/Readme_{lang}.txt`、`License/`

**追加時の注意**: csproj の `<Content Include>` でビルド出力にコピーされます。新しい言語やファイル種別を足す場合、既存のワイルドカード（`Languages/**/*.yaml` 等）に合致するか確認してください。

### Tools — 開発用の検証ツール
**場所**: `Tools/{ツール名}/`
**入るもの**: 配布物に含めない、開発者が実行するコンソールアプリ。アプリ本体を読み取り専用で参照し、サブコマンドと終了コードで結果を返す。それぞれ外部依存の無い `selfcheck`（自己検証）を持つ
**例**: `GoldenBaseline`（走査結果の検証。検証用のフォルダ構造を走査し、期待値（リポジトリ直下の `baselines/*.golden.txt`）と突き合わせる）、`LocalizationCheck`（言語ファイルの網羅の検証。実行方法は tech.md の「コマンド」）

**追加時の注意**: 本体の csproj は `DefaultItemExcludes` で `Tools\**` を除外しています。ツール側の `.cs` が本体のビルドに取り込まれないのはこのためです。ツールは `LargeFolderFinder.sln` に登録します。名前空間は本体のフラットな規約に従わず、`LargeFolderFinder.{ツール名}` を起点にフォルダごとに切ります。ツールは本体と同じ `net10.0-windows` を対象にし、RID を付けないので出力は `Tools/{ツール名}/bin/{構成}/net10.0-windows/` です（本体は RID 付きの `bin/{構成}/net10.0-windows/win-x64/`）。

### Tests — 自動テスト
**場所**: `Tests/LargeFolderFinder.Tests/`
**入るもの**: xunit.v3 のテスト。検証ツールの実行ファイルを子プロセスで呼び、終了コードで判定する（判定の仕組みはツール側に置き、テストで作り直さない）
**追加時の注意**: 検証ツールは `ProjectReference`（`ReferenceOutputAssembly=false`）で参照し、ビルドの順序と出力の存在だけを保証する。本体からテストを参照しない（配布物に入れない）。ソリューションの構成は既存の `Debug|Any CPU`・`Release|Any CPU` だけにそろえる

### build — 発行・梱包・起動確認のスクリプト
**場所**: `build/`（`Publish.ps1`・`Package.ps1`・`Test-Launch.ps1`）
**入るもの**: 発行・梱包・起動確認の PowerShell スクリプト。リリースはこれらを手元で順に実行して行う（手順は tech.md の「発行と配布」）
**追加時の注意**: スクリプトは Windows PowerShell 5.1 でも動く書き方にし、UTF-8（BOM 付き）・CRLF で保存する。失敗は 0 以外の終了コードで返す。出力の既定はバージョン管理外の `artifacts/`

### リポジトリ直下のその他
- `global.json`: SDK の版とテストの実行方式の固定（理由は tech.md の「SDK の版」）
- `baselines/`: 走査結果の期待値データ。`GoldenBaseline` の `update` で更新し、手で編集しない

## レイアウト切り替えのパターン

縦型 / 横型のレイアウトを差し替えられる構造になっています。**画面要素を増やす際は必ずこの経路に乗せてください。**

```
IMainLayoutView       … 画面が公開すべきコントロールと操作を定義するインターフェース
      ↑
LayoutViewBase        … UserControl 基底。FindName で XAML 要素を解決し、共通イベントを結線
      ↑
VerticalLayoutView / HorizontalLayoutView   … XAML + 薄いコードビハインド
```

`LayoutViewBase` は、2 つのビューで重複していた処理をなくし、保守性を上げるために設けた基底クラスです。

- 新しいコントロールを足す場合: `IMainLayoutView` にプロパティを追加 → `LayoutViewBase` で `FindName("要素名")` を実装 → **両方の XAML に同名の要素を配置**
- 片方のレイアウトにしか無い要素は、インターフェース側を **null 許容**（`TextBox?` など）で宣言する
- ローカライズ対象のラベル・カラムもインターフェース経由で公開し、`ApplyLocalization` でまとめて適用する

## 名前空間の規約

**原則としてディレクトリ構造に関わらず `LargeFolderFinder` のフラットな名前空間**を使います。

```csharp
// Models/FolderInfo.cs, Services/Scanner.cs, Helpers/Win32.cs …すべて同一
namespace LargeFolderFinder
```

例外は `ViewModels/` のみで、`LargeFolderFinder.ViewModels` を使います（利用側は `using LargeFolderFinder.ViewModels;`）。

新規ファイルは、この既存の慣習に合わせてください。

## 命名規則

- **ファイル名**: 型名と一致させる（`Scanner.cs` → `class Scanner`）
- **型・メソッド・プロパティ**: PascalCase
- **private フィールド**: `_camelCase`（`_mainWindow`、`_viewModel`）
- **XAML の `x:Name`**: camelCase（`minSizeTextBox`、`outputListBox`）
  - `LayoutViewBase` の `FindName` はこの名前を文字列で参照するため、**XAML 側の名前変更は静的検査に掛からない**。改名時は両方を必ず追う
- **ローカライズキー**: `LanguageKey` 列挙の PascalCase。UI の区画ごとにコメントで区切って並べる

## using の書き方

`ImplicitUsings` は無効です。各ファイルの先頭で必要な名前空間を明示します。

```csharp
using System;                       // BCL を先に
using System.Collections.Generic;
using System.Windows;               // WPF
using YamlDotNet.Serialization;     // サードパーティ
using LargeFolderFinder.ViewModels; // プロジェクト内
```

## 依存の方向

```
Views  →  ViewModels  →  Services  →  Models
  └──────────────────────────────────→ Helpers（どこからでも可）
```

- `Services` から WPF の型を参照しない
- `Models` は他レイヤーに依存しない（永続化属性と YAML 属性の付与は許容）

## リポジトリ運用

- `docs/` は **`.gitignore` 済み**（`/docs/`）。AI 生成の設計メモ置き場であり、リポジトリには含めない
- `build*.txt` / `msbuild.log` / `Cache.bin` も同様に除外対象
- `artifacts/`（発行物・zip・移行前の版の控え）も除外対象。本体の csproj は `DefaultItemExcludes` で `Tests\**`・`build\**`・`artifacts\**` もビルドから外している
- `.kiro/` と `.claude/` は仕様・スキルの共有対象として追跡してよい

## 既知の負債（変更時に留意）

- **`Views/MainWindow.xaml.cs` が約 1,260 行**と突出して大きく、MVVM の責務分離が徹底されていません。ここに機能を足し続けると保守が難しくなります。**新規ロジックは可能な限り `Services/` か `ViewModels/` に置いてください。**
- 行データを表す `FolderRowItem` が `MainWindow.xaml.cs` 内に同居しています。本来は `Models/` 相当です。
- `AppConstants.CacheFileName`（`Cache.txt`）は旧形式の互換目的で残存しています。新規の永続化には使いません。

---
_ファイルツリーではなくパターンを記述する。パターンに沿った新規ファイルの追加でこの文書を更新する必要はない_
