# 技術スタック

## アーキテクチャ

WPF による単一プロセスのデスクトップアプリ。**MVVM を部分適用**した構成です。

- `MainViewModel` / `SessionViewModel` がセッション（タブ）の状態とライフサイクルを保持
- ただし画面制御の相当部分は `MainWindow.xaml.cs` のコードビハインドが担う（後述の「既知の負債」参照）
- スキャン・整形・永続化・多言語化は `Services/` の各クラスに分離済み

## コア技術

- **言語**: C# 12（`LangVersion 12.0`）
- **フレームワーク**: WPF（`UseWPF`。Windows Forms は使わない）
- **ターゲット**: .NET 10（`net10.0-windows`、LTS、サポート終了 2028-11-14）— SDK 形式の csproj。`RuntimeIdentifier` は `win-x64`
- **Nullable**: `enable`（`ImplicitUsings` は `disable`。using は各ファイルで明示する）
- **SDK**: `global.json` で固定（「SDK の版」の節）

## 主要ライブラリ

開発パターンに影響するものだけを挙げます。版は `LargeFolderFinder.csproj` を正とします。

| ライブラリ | 用途 | 影響するパターン |
|---|---|---|
| **MessagePack**（3.1.9） | 設定・セッションの永続化 | `[MessagePackObject]` + `[Key(n)]` の**連番手動採番**。LZ4BlockArray 圧縮を既定で適用。版を変えたら、以前の版が書いた設定・セッションを読めることを確かめる |
| **YamlDotNet**（18.1.0） | 言語リソース、`Config.txt` の読み取り | 人が編集できるテキスト設定という前提。版を変えたら `LocalizationCheck` の `selfcheck` と `Config.txt` の読み込みを確かめる |
| **CommunityToolkit.Mvvm**（未導入） | MVVM の部品（`architecture-refactoring` で導入予定） | 導入するときは **8.4.2 以上**にする。8.4.0 は .NET 10 でビルドできない（`MVVMTK0041` / `CS9248`） |
| **xunit.v3**（テスト専用） | テストの基盤（Microsoft Testing Platform v2 で走る） | 配布物に含めない。アプリのプロジェクトからは参照しない |

フォルダ選択は実行基盤が備える `Microsoft.Win32.OpenFolderDialog` を使います（外部のダイアログ部品には依存しない）。

### Win32 API 直接呼び出し

`Helpers/Win32.cs` に `kernel32.dll` の P/Invoke を集約しています。用途は限定的で、**列挙処理のすべてが Win32 経由なわけではありません**。

| 用途 | API | 使用箇所 |
|---|---|---|
| 進捗率の分母となるフォルダー数の事前カウント | `FindFirstFileEx` / `FindNextFile` / `FindClose` | `Scanner.CountFoldersRecursive` |
| クラスタサイズ取得（ディスク上のサイズ計算用） | `GetDiskFreeSpace` | `Scanner.GetClusterSize` |
| ワーキングセットの切り詰め | `SetProcessWorkingSetSize` | `MainWindow.OptimizeMemory` |

本スキャン（`Scanner.ScanRecursiveInternal`）は `DirectoryInfo.EnumerateFiles` / `EnumerateDirectories` を使っています。.NET 10 のこの経路は 260 文字を超えるパスも列挙できますが、**Win32 を直接呼ぶ事前カウントは移行後も MAX_PATH の制約を受けます**（「既知の制約」参照）。

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
`Tests/LargeFolderFinder.Tests`（xunit.v3）を `dotnet test` の1コマンドで実行します。テストは判定を自前で組み立てず、**2つの検証ツールの実行ファイルを子プロセスで呼び、終了コードで成否を決めます**（走査結果の期待値データとの比較と自己検証、翻訳の網羅の検証と自己検証）。

- 検証ツールが終了コード 2 を返し、出力が環境の制約（`未生成の項目:`、`文字に固定できません`）を示すときは**スキップ**にする。スキップがあっても `dotnet test` は終了コード 0 になるので、**集計の「スキップ」が 0 件であることを確かめる**
- 期待値データ（`baselines/fixture-v1.golden.txt`）は .NET 10 の走査結果で更新済みで、既知の欠落は 0 件。更新は `GoldenBaseline` の `update` で行い、手で編集しない
- 画面の操作は自動テストの対象外。速度に影響する変更は、従来どおり実測値の再計測が必要（performance.md）

## 設定・データの保存先

ユーザーデータは `%LocalAppData%\Cat & Chocolate Laboratory\LargeFolderFinder\` 配下に置きます（`AppConstants.AppDataDirectory`。末尾は `AssemblyTitle` 由来なので変えない）。

| 対象 | 形式 | 備考 |
|---|---|---|
| アプリ設定 | `Settings.msgpack` | 言語・レイアウト・ウィンドウの位置・サイズ・状態・タブ構成。ただし言語とウィンドウの位置・サイズ・状態は保存されるだけで、現状は起動時に読み戻していない（`architecture-refactoring` で復元する） |
| スキャン結果 | `Sessions/Scan{日時}.msgpack` | タブ 1 つ = 1 ファイル |
| ログ | `Logs/` | 世代数上限 `LogFilesMax` |
| 動作設定 | `Config.txt`（exe と同階層） | **ユーザーが手で編集する**前提。並列処理の有無、事前カウントのスキップ等。拡張子が `.txt` なのは、アプリが関連付けられていなくてもダブルクリックでメモ帳などですぐ開けるようにするため。中身を YAML にしたのは、コメントを書けるので外部の文書を見なくても設定の意味が分かるようにするためだった（現行の `Config.txt` は各項目の説明を同梱の Readme に委ねており、アプリが生成する既定のファイルにもコメントはない） |

**アプリの起動だけでなく、検証ツールとテストの実行もこのフォルダのログを書き、古いログを消します**（走査の部品を同じプロセスで使うため）。手元でアプリを起動する確認（`build/Test-Launch.ps1` など）は、このフォルダを退避してから行い、終わったら戻します。

## 開発環境

### 必要なもの
- Windows 10 / 11（x64）
- .NET SDK 10.0.4xx（10.0.401 以上）
- Visual Studio（任意。.NET 10 SDK を使える版。`LargeFolderFinder.sln` を同梱）

### SDK の版
`global.json` で **SDK 10.0.401、`rollForward: latestPatch`**（4xx 系のパッチだけを追い、3xx・5xx には移らない）に固定し、テストの実行方式（`Microsoft.Testing.Platform`）も指定しています。

- **理由**: 自己完結版は SDK に付いているランタイムを同梱して配るため、2026-09-08 のセキュリティ修正（ランタイム 10.0.12）を含む SDK を選んだ（該当は 10.0.401 と 10.0.112）。4xx 系は実測で発行・起動とも問題がなかった
- 1xx 系は `--self-contained false` が効かず自己完結になる不具合（dotnet/sdk#51888）がある。2xx 系は WPF と単一ファイル発行の組み合わせで起動時の例外・無言終了の報告（dotnet/wpf#11678）がある
- **版を変えるとき**: `global.json` を変え、ビルド・テスト・2形態の発行・起動確認・梱包を通す。同梱するランタイムの版が変わるので `Resources/License/ThirdPartyNotices.txt` の .NET ランタイムの節を見直す。選んだ版と理由をこの節に記録する

### コマンド
リポジトリ直下で実行します。**ビルドは順番に行う**（検証ツールとテストは同じアプリのプロジェクトを参照するため、同時にビルドすると出力の取り合いになる）。

```bash
# ビルド（アプリ・検証ツール2つ・テスト。警告を失敗として扱う）
dotnet build LargeFolderFinder.sln -c Release -warnaserror

# テスト（直下に csproj と sln が並ぶのでソリューションを明示する。構成はビルドとそろえる）
dotnet test --solution LargeFolderFinder.sln -c Release --no-build

# 検証ツールを直接実行する（出力は Tools/<名前>/bin/<構成>/net10.0-windows/）
Tools/GoldenBaseline/bin/Release/net10.0-windows/GoldenBaseline.exe compare --golden baselines/fixture-v1.golden.txt
Tools/GoldenBaseline/bin/Release/net10.0-windows/GoldenBaseline.exe selfcheck
Tools/LocalizationCheck/bin/Release/net10.0-windows/LocalizationCheck.exe check       # --dir <path> で別の言語フォルダも検証できる
Tools/LocalizationCheck/bin/Release/net10.0-windows/LocalizationCheck.exe selfcheck

# 発行（-Form SelfContained | FrameworkDependent。-OutputDir 省略時は artifacts/publish/<形態>）
powershell -NoProfile -ExecutionPolicy Bypass -File build/Publish.ps1 -Form SelfContained
powershell -NoProfile -ExecutionPolicy Bypass -File build/Publish.ps1 -Form FrameworkDependent

# 起動確認（アプリデータを書くので、手元では退避してから）と梱包（出力 artifacts/package）
powershell -NoProfile -ExecutionPolicy Bypass -File build/Test-Launch.ps1 -ExePath artifacts/publish/SelfContained/LargeFolderFinder.exe
powershell -NoProfile -ExecutionPolicy Bypass -File build/Package.ps1
```

- アプリの出力は `bin/<構成>/net10.0-windows/win-x64/`（RID を指定しているため）。検証ツールの出力には RID が付かない
- 検証ツールの終了コードは 0 = 一致・問題なし / 1 = 差異・問題あり / 2 = 検証不能（引数の誤り、言語フォルダが無い、環境の制約など）
- `LocalizationCheck` は期待するキーの一覧を**アプリのビルド出力から得る**。`LanguageKey` を変えたら、ソリューションをビルドし直してから `check` を実行する。出力が古いかどうかは、報告の要約のキー数（`問題はありません（言語 13、キー 81）` など）で確かめられる

## 発行と配布

- **2形態の単一 exe**（2026-09-17 利用者の決定。経緯は decisions.md）: 既定の配布物は**自己完結版**（`LargeFolderFinder.zip`。ランタイムの導入不要、exe 約140MB・zip 約59MB）。あわせて**フレームワーク依存版**（`LargeFolderFinder-FrameworkDependent.zip`。.NET 10 Desktop Runtime (x64) が必要、exe 約1MB・zip 約0.5MB）を配る
- 発行は `build/Publish.ps1` が行う。自己完結は `--self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true`、フレームワーク依存は `--no-self-contained -p:PublishSingleFile=true`（`--self-contained false` は使わない）。**単一ファイルの圧縮は使わない**（zip が縮まず、起動が遅くなるだけのため）
- `Config.txt` と `Resources/`（言語・Readme・ライセンス）は csproj の `Content` の設定で exe の隣に出る。zip は `build/Package.ps1` が exe・`Config.txt`・言語・Readme・ライセンスだけで作り（pdb などは入れない）、2つの zip の構成が exe の大きさを除いて一致することを検査する
- 版の文字列は `IncludeSourceRevisionInInformationalVersion=false` でコミットハッシュを付けない `X.Y.Z` の形にする
- **リリースは手作業で行う**（2026-09-21 利用者の決定。自動ビルドは使わない。経緯は decisions.md）。csproj の `Version` を上げ、次を順に実行して2つの zip を作り、GitHub の画面でリリースを作って添付する
  1. `dotnet build LargeFolderFinder.sln -c Release -warnaserror`
  2. `dotnet test --solution LargeFolderFinder.sln -c Release --no-build`
  3. `build/Publish.ps1 -Form SelfContained` と `build/Publish.ps1 -Form FrameworkDependent`
  4. `build/Test-Launch.ps1 -ExePath <各 exe>`
  5. `build/Package.ps1`

## 既知の制約

- **自己完結版は約140MB**。WPF がトリミングに非対応（dotnet/wpf#3811）のため削れない。軽さが要る利用者にはフレームワーク依存版を案内する
- **フォルダ数の事前カウントに長さの制限が残る**。本スキャンは長いパスを列挙できるが、事前カウントの P/Invoke は MAX_PATH の制約を受け、長いパスの配下を数え損ねる（進捗率の分母がずれる）。解消は `scan-correctness` の範囲
- **ビルドはコードを変えていなくても失敗しうる**。警告を失敗として扱うため、使っているパッケージ（.NET 10 では推移的な依存も監査の対象）に脆弱性が新たに公表されただけで、NuGet の監査の警告（`NU1901`〜`NU1904`）が出てビルドが失敗する。失敗の原因がこの警告かどうかを警告の番号で見分け、コードの変更による失敗と取り違えない
- **梱包は構成外のファイルを黙って除く**。発行の設定を変えるときは、発行フォルダに exe 以外の dll が出ていないかを確かめる（出ても zip から黙って抜ける。現状は `IncludeNativeLibrariesForSelfExtract=true` でネイティブ DLL も exe に入るので出ない）
- **手元の起動確認で、閉じたアプリのプロセスが消えずに残ったことが1回ある**（終了済みのままスレッドが残り、発行先の exe がロックされる。OS 側の I/O の完了待ちとみられ、PC の再起動で解消する）。起動確認の出力は管で受けず、ファイルに取る

## 主要な技術判断とその理由

- **.NET 10 へ移行し、2形態で配る** — 移行前の実行基盤（.NET Framework）では 260 文字を超えるパスが走査から無言で漏れる欠陥を直せないため。後続のスペックが前提とする土台（長いパスを扱える基盤、テストの基盤、自動ビルド）もこの移行で整えた（roadmap.md）。移行で失われる「ランタイム導入不要」は自己完結版で守り、失われる「実行ファイルの軽さ」はフレームワーク依存版で補う（2026-09-17 利用者の決定）
- **MessagePack + LZ4** — MessagePack は JSON や YAML より読み書きが速くコンパクトなため採用した。起動時に不要な巨大データを読まないよう、アプリ設定（`Settings.msgpack`）とタブごとの結果（`Sessions/`）を別ファイルに分けている。LZ4 圧縮によるデータ量の削減は、コード中のコメントでは 50〜70% とされるが、計測の記録はない
- **単一 exe の配布** — zip を解凍して exe をダブルクリックするだけ、という利用体験を守るため。依存 DLL は実行基盤の単一ファイルの発行で exe に入れる（埋め込みの外部の仕組みは使わない）
- **発行・梱包・起動確認はスクリプトを単一の入口にする** — 手順を `build/*.ps1` に閉じ込め、いつでも同じ入口から呼べるようにする
- **ローカライズは `enum LanguageKey` の名前を文字列キーとして YAML を辞書引き** — `GetText` は `key.ToString()` で解決し、見つからなければ `en.yaml` にフォールバックする。**YAML 側の並び順は問わない**（`LanguageKey` の宣言コメントもこの実態に合わせてある）
  - キー追加時は enum と**全 13 言語の YAML** に追加する。欠落しても例外にはならず英語表示に落ちるため、**翻訳漏れが発覚しにくい**。欠落・差し込み位置（`{0}` など）のずれ・重複は `Tools/LocalizationCheck` の `check` で確かめる（テストでも走る）
  - ただし **`Key:` と書いて訳文の値を省くと、英語に落ちずに `GetText` の中で例外になる**（値が null のまま `Replace` を呼ぶため）。検証ツールはこれを欠落として報告する
  - 単位名（KB・GB など）は訳さないと決めた例外がある（decisions.md の「単位の名前を翻訳すること」）

---
_標準とパターンを記述する。依存の全列挙はしない_
