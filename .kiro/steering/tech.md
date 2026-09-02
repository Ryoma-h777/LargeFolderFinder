# 技術スタック

## アーキテクチャ

WPF による単一プロセスのデスクトップアプリ。**MVVM を部分適用**した構成です。

- `MainViewModel` / `SessionViewModel` がセッション（タブ）の状態とライフサイクルを保持
- ただし画面制御の相当部分は `MainWindow.xaml.cs` のコードビハインドが担う（後述の「既知の負債」参照）
- スキャン・整形・永続化・多言語化は `Services/` の各クラスに分離済み

## コア技術

- **言語**: C# 12（`LangVersion 12.0`）
- **フレームワーク**: WPF（`UseWPF` + `UseWindowsForms` 併用）
- **ターゲット**: .NET Framework 4.8（`net48`）— SDK 形式の csproj
- **Nullable**: `enable`（`ImplicitUsings` は `disable`。using は各ファイルで明示する）

## 主要ライブラリ

開発パターンに影響するものだけを挙げます。

| ライブラリ | 用途 | 影響するパターン |
|---|---|---|
| **MessagePack** | 設定・セッションの永続化 | `[MessagePackObject]` + `[Key(n)]` の**連番手動採番**。LZ4BlockArray 圧縮を既定で適用 |
| **YamlDotNet** | 言語リソース、`Config.txt` の読み取り | 人が編集できるテキスト設定という前提 |
| **Ookii.Dialogs.Wpf** | フォルダー選択ダイアログ | — |
| **Fody + Costura.Fody** | 依存 DLL を exe に埋め込み | **単一 exe 配布**が前提。DLL を別ファイルで配る選択肢は取らない |

### Win32 API 直接呼び出し

`Helpers/Win32.cs` に `kernel32.dll` の P/Invoke を集約しています。`.NET` の `DirectoryInfo` 系ではなく `FindFirstFileEx` / `FindNextFile` を使うのは速度のためです。

- `FindExInfoBasic`（代替名を取得しない）
- `FIND_FIRST_EX_LARGE_FETCH`（バッファを大きく取る）
- ハンドルは必ず `try` / `finally` で `FindClose` する
- リパースポイント（`FILE_ATTRIBUTE_REPARSE_POINT`）はスキップし、循環を避ける

**新しい列挙処理を書く場合も、この経路を使ってください。** 標準 API への置き換えは速度要件（product.md の優先順位 1）に反します。

## 開発標準

### 非同期・並列
- 長時間処理は `async` / `await` + `Task.Run`
- **`CancellationToken` を必ず引き回す**（走査は中断できることが要件）
- 集計は `System.Collections.Concurrent` 系で受ける
- UI 更新は `Dispatcher` 経由。進捗通知は間引く（`ProgressCounter` が最終報告時刻を保持して抑制する）

### コメント・命名
- **XML ドキュメントコメント（`///`）は日本語で記述**。コードベース全体で徹底されています（248 箇所）
- ただし **`Logger` に出すログメッセージは英語**。定数として `AppConstants` に集約する（`LogScanStart` など）

### エラーハンドリング
- 永続化・IO 系は例外を握って `Logger.Log(メッセージ, ex)` に流し、既定値を返す（アプリを落とさない）
- ユーザー向け表示は必ずローカライズ経由

### テスト
**自動テストは存在しません。** 検証は実機での手動確認と、README に載せる実測タイムで行っています。速度に影響する変更を入れた場合は、実測値の再計測が必要です。

## 設定・データの保存先

ユーザーデータは `%LocalAppData%\Cat & Chocolate Laboratory\Large Folder Finder\` 配下に置きます（`AppConstants.AppDataDirectory`）。

| 対象 | 形式 | 備考 |
|---|---|---|
| アプリ設定 | `Settings.msgpack` | 言語・レイアウト・ウィンドウ位置・タブ構成 |
| スキャン結果 | `Sessions/Scan{日時}.msgpack` | タブ 1 つ = 1 ファイル |
| ログ | `Logs/` | 世代数上限 `LogFilesMax` |
| 動作設定 | `Config.txt`（exe と同階層） | **ユーザーが手で編集する**前提。並列処理の有無、事前カウントのスキップ等 |

## 開発環境

### 必要なもの
- Windows 10 / 11
- .NET SDK（`net48` をターゲットにビルドできるもの）
- Visual Studio 2022（`LargeFolderFinder.sln` を同梱）

### コマンド
```bash
# ビルド
dotnet build LargeFolderFinder.csproj

# リリースビルド
dotnet build LargeFolderFinder.csproj -c Release
```

## 主要な技術判断とその理由

- **.NET Framework 4.8 を維持** — Windows に標準搭載されており、利用者にランタイム導入を要求しないため。.NET 8+ への移行は「導入の手軽さ」を損なうため、安易に行わない
- **MessagePack + LZ4** — キャッシュを 50〜70% 削減。起動時のセッション復元を速くするため
- **Costura による単一 exe 化** — zip を解凍して exe をダブルクリックするだけ、という利用体験を守るため
- **ローカライズは `enum LanguageKey` と YAML の順序一致で対応** — キー追加時は **enum と全 13 言語の YAML の両方を、同じ位置に**追加する必要がある（片方だけの変更はズレを生む）

---
_標準とパターンを記述する。依存の全列挙はしない_
