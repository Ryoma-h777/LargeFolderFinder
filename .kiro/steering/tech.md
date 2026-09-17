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

`Helpers/Win32.cs` に `kernel32.dll` の P/Invoke を集約しています。用途は限定的で、**列挙処理のすべてが Win32 経由なわけではありません**。

| 用途 | API | 使用箇所 |
|---|---|---|
| 進捗率の分母となるフォルダー数の事前カウント | `FindFirstFileEx` / `FindNextFile` / `FindClose` | `Scanner.CountFoldersRecursive` |
| クラスタサイズ取得（ディスク上のサイズ計算用） | `GetDiskFreeSpace` | `Scanner.GetClusterSize` |
| ワーキングセットの切り詰め | `SetProcessWorkingSetSize` | `MainWindow.OptimizeMemory` |

本スキャン（`Scanner.ScanRecursiveInternal`）は `DirectoryInfo.EnumerateFiles` / `EnumerateDirectories` を使っています。

Win32 側では `FindExInfoBasic`（代替名を取得しない）と `FIND_FIRST_EX_LARGE_FETCH`（バッファ拡大）を指定し、ハンドルは必ず `try` / `finally` で `FindClose` します。リパースポイントは両経路とも除外します。

走査性能に関わる変更を行う場合は、**performance.md を必ず参照してください。**

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
| アプリ設定 | `Settings.msgpack` | 言語・レイアウト・ウィンドウの位置・サイズ・状態・タブ構成。ただし言語とウィンドウの位置・サイズ・状態は保存されるだけで、現状は起動時に読み戻していない（`architecture-refactoring` で復元する） |
| スキャン結果 | `Sessions/Scan{日時}.msgpack` | タブ 1 つ = 1 ファイル |
| ログ | `Logs/` | 世代数上限 `LogFilesMax` |
| 動作設定 | `Config.txt`（exe と同階層） | **ユーザーが手で編集する**前提。並列処理の有無、事前カウントのスキップ等。拡張子が `.txt` なのは、アプリが関連付けられていなくてもダブルクリックでメモ帳などですぐ開けるようにするため。中身を YAML にしたのは、コメントを書けるので外部の文書を見なくても設定の意味が分かるようにするためだった（現行の `Config.txt` は各項目の説明を同梱の Readme に委ねており、アプリが生成する既定のファイルにもコメントはない） |

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
- **MessagePack + LZ4** — MessagePack は JSON や YAML より読み書きが速くコンパクトなため採用した。起動時に不要な巨大データを読まないよう、アプリ設定（`Settings.msgpack`）とタブごとの結果（`Sessions/`）を別ファイルに分けている。LZ4 圧縮によるデータ量の削減は、コード中のコメントでは 50〜70% とされるが、計測の記録はない
- **Costura による単一 exe 化** — zip を解凍して exe をダブルクリックするだけ、という利用体験を守るため
- **ローカライズは `enum LanguageKey` の名前を文字列キーとして YAML を辞書引き** — `GetText` は `key.ToString()` で解決し、見つからなければ `en.yaml` にフォールバックする。**順序は実際には無関係**（`LanguageKey` の宣言コメントは「YAML と順序を一致させること」と書いているが、実装は順序に依存しない）
  - キー追加時は enum と**全 13 言語の YAML** に追加する。欠落しても例外にはならず英語表示に落ちるため、**翻訳漏れが発覚しにくい**
  - 現に `HeaderOwner` / `ContextShowOwner` が en・ja 以外の 11 言語で欠落している
  - 単位名（KB・GB など）は訳さないと決めた例外がある（decisions.md の「単位の名前を翻訳すること」）

---
_標準とパターンを記述する。依存の全列挙はしない_
