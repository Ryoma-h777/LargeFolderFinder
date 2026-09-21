# Requirements Document

## Project Description (Input)

### 誰の問題か

Large Folder Finder の利用者（NAS やローカルドライブの容量の圧迫原因を調べる人）と、開発者（個人開発、単独）。利用者は走査の途中経過と結果を信頼して容量整理の判断をする。開発者はこのあと走査の処理を速度のために作り替える（`scan-performance`）ため、その前に走査を「正しく、失敗が見える」状態にしておく必要がある。

### 現状（2026-09-22、.NET 10 移行の完了後）

discovery の時点（brief.md）から、移行によって状況が変わっている。**brief の問題1のうち「本スキャンで260文字を超えるパスが集計から漏れる」は、.NET 10 移行で解消済み**である（dotnet10-migration の 2.6 で、移行前に欠けていた4項目が走査結果に現れることを確かめ、期待値データ `baselines/fixture-v1.golden.txt` を更新した。既知の欠落は0件）。残っている問題は次のとおり。

1. **フォルダ数の事前カウントが長いパスを数え損ねる。** 事前カウント（`Scanner.CountFoldersRecursive`）は手書きの P/Invoke（`FindFirstFileEx` / `FindNextFile` / `FindClose`）を使っており、実行ファイルが long path aware でないため、260文字を超える配下を数えられない。結果として**進捗率の分母がずれる**（本スキャンの集計値は正しい）
2. **P/Invoke が他にも残っている。** `GetDiskFreeSpace`（物理サイズ換算のクラスタサイズ）、`SetProcessWorkingSetSize`（メモリの切り詰め）、`ShowWindow`（user32）。`GetCompressedFileSize` は宣言だけで使われていない。app.manifest は無く、`longPathAware` の宣言もない
3. **走査中の暫定ツリーを画面がロックなしで読んでいる。** `Scanner.cs:308` が走査中のツリー（`counter.RootNode`）をそのまま進捗の報告に載せ、`SessionViewModel.cs:184` がそれを結果として描画する。走査側は `lock (Children)` で書き込むが、読み側は保護されていない
4. **デバッグ用のダイアログが製品のコードに残っている**（`SessionViewModel.cs:194` の `MessageBox.Show(..., "Debug")`）。進捗の更新で例外が起きたことがある形跡
5. **例外の握りつぶしが常態化している。** 本体の `catch` は67箇所あり、その多くが空かコメントのみ。例として、`Config.Load` は `Config.txt` の解析に失敗しても黙って既定値を返し、ログにも残らない（dotnet10-migration の 5.4 で確認）
6. **結果の描画の取り消しがタブごとに効いていない。** タブごとの取り消しの欄はあるが、描画の処理が使っていない（`.kiro/steering/decisions.md` の「描画の取り消しを、タブ間で干渉させない」に食い違いとして記録済み。本スペックの担当）

### 何が変わるべきか

- 事前カウントも含めて、260文字を超えるパス（日本語を含む場合も）を正しく数え、**進捗率の分母が正しくなる**
- 走査の経路から手書きの P/Invoke が無くなり、長いパスの扱いが実行環境の設定（レジストリ・マニフェスト）に依存しない
- 走査中に途中経過を表示しても、読み書きが競合しない
- 失敗が黙って消えない。意図して無視する失敗（アクセス拒否でのスキップなど）は理由が分かり、隠してはいけない失敗は記録され、必要なら利用者に伝わる
- デバッグ用の残骸が製品のコードに無い
- 結果の描画の取り消しがタブごとに効き、タブ間で干渉しない

### 前提と留意点

- **本スキャンの集計値は既に正しい**。本スペックの変更で `baselines/fixture-v1.golden.txt` との比較に差が出てはならない（出た場合は不具合として扱う）。事前カウントの修正は進捗率の分母にだけ効き、集計値は変えない
- 速度の最適化は `scan-performance` の範囲。本スペックは「正しく動き、失敗が見える」ことに徹する。ただし `FileSystemEnumerator<T>` への置き換えは両スペックにまたがるため、本スペックでは事前カウントの正しさを担保し、性能の作り込みは次に回す
- アクセス拒否でスキップしても走査全体を止めない挙動は仕様であり維持する（decisions.md）
- 進捗の通知の間引き（報告5秒・ログ20秒）は維持する（performance.md）。競合の解消策がこの間隔を短くしないこと
- リパースポイントの除外（循環の回避）は維持する
- 進捗率は事前カウントした範囲で数える、事前カウントを省いたときは残り時間を出さない、という既存の決定（decisions.md）は維持する
- テストや起動確認は利用者のアプリデータにログを書く。手元で確かめる前にアプリデータを退避する（dotnet10-migration の Implementation Notes）
- 詳細な背景と境界の候補は [brief.md](brief.md) を参照。ただし brief の「現状」「問題1」は上記のとおり移行前の記述であり、本節を正とする

## Introduction

本フィーチャーは、Large Folder Finder の走査を「正しく数え、失敗が見える」状態にする。.NET 10 移行で本スキャンの集計値は正しくなったが、進捗率の分母となる事前カウントは長いパスを数え損ね、走査中の途中経過の表示は読み書きが競合しうる状態のままで、失敗の多くは黙って捨てられている。

このあと速度のために走査を作り替える（`scan-performance`）前提として、集計値を一切変えずに、数え方・途中経過・失敗の扱いを整える。

## Boundary Context

- **In scope**: 事前カウントの長いパス対応と進捗率の整合、長いパスの扱いの実行環境への非依存、使われていない OS 呼び出しの宣言の除去、走査中の途中経過の表示の競合の解消、開発用のダイアログの除去、失敗の扱いの方針とその適用（`Config.txt` の解析の失敗を含む）、結果の描画の取り消しのタブごとの分離
- **Out of scope**: 走査の速度の最適化と並列度の調整（`scan-performance`）、結果のツリーの構造の変更（`scan-performance`）、スキップした件数や失敗の新しい画面表示の設計（`ui-redesign`）、ログ出力の仕組みそのものの作り替えと死んだコードの一般的な除去（`architecture-refactoring`）、移行で失われた要望の復元（`architecture-refactoring`）
- **Adjacent expectations**: 集計値が変わらないことは `scan-golden-baseline` の期待値データ（`baselines/fixture-v1.golden.txt`）との比較で確かめる。検証ツールの `ScanRunner` は本体のクラスタサイズの計算と同じ式を独自に持っているため、本スペックで本体の計算方法を変える場合は検証ツール側も同じ結果になることを確かめる。既存の決定（進捗率は事前カウントした範囲で数える、事前カウントを省いたときは残り時間を出さない、アクセス拒否では走査を止めない、進捗の通知は5秒・ログは20秒の間隔）は維持する

## Requirements

### Requirement 1: 事前カウントの長いパス対応と進捗率の整合

**Objective:** As a 利用者, I want 走査の進捗率が長いパスを含むフォルダでも正しく表示されること, so that 走査があとどれくらいで終わるかを正しく判断できる

#### Acceptance Criteria

1. When 事前カウントの範囲に 260 文字を超えるパスのフォルダがあるとき, the Large Folder Finder shall そのフォルダを事前カウントの数に含める
2. When 事前カウントの範囲に日本語を含む 260 文字を超えるパスのフォルダがあるとき, the Large Folder Finder shall そのフォルダを事前カウントの数に含める
3. While 事前カウントを行った走査が進行している間, the Large Folder Finder shall 進捗率を 100% を超えない値で表示する
4. When 事前カウントを行った走査が取り消されずに完了したとき, the Large Folder Finder shall 事前カウントの数と、走査で数えた事前カウントの範囲のフォルダの数を一致させる
5. The Large Folder Finder shall 事前カウントにおいて、アクセスできないフォルダを数えられない場合も事前カウントを止めずに続け、リパースポイントを数に含めない
6. Where 事前カウントを省く設定が有効であるとき, the Large Folder Finder shall 残り時間を表示せず経過時間を表示する

### Requirement 2: 長いパスの扱いの実行環境への非依存と集計値の保全

**Objective:** As a 利用者, I want 長いパスの扱いが PC の設定に左右されないこと, so that どの PC で走査しても同じ結果が得られる

#### Acceptance Criteria

1. The Large Folder Finder shall 260 文字を超えるパスの列挙と計数を、OS の長いパスの設定と実行ファイルの長いパスの宣言のいずれにも依存せずに行う
2. The Large Folder Finder shall 使われていない OS 呼び出しの宣言を含まない
3. The Large Folder Finder shall 本スキャンの集計値を本フィーチャーの変更の前後で変えない
4. Where 物理サイズ換算が有効であるとき, the Large Folder Finder shall 集計値を本フィーチャーの変更の前後で変えない
5. When 走査の検証ツールで期待値データと比較したとき, the Large Folder Finder shall 差分のない結果を示す

### Requirement 3: 走査中の途中経過の表示

**Objective:** As a 利用者, I want 走査中に途中経過を見ても表示が乱れたり止まったりしないこと, so that 大きな走査の途中でも安心して結果を確認できる

#### Acceptance Criteria

1. While 走査が進行している間, the Large Folder Finder shall 途中経過の表示のための結果の読み取りと走査による結果の書き込みが同時に起きても、例外を発生させない
2. While 走査が進行している間, the Large Folder Finder shall 途中経過として、表示の時点で読み取りを終えた一貫した結果を表示する
3. When 走査が完了したとき, the Large Folder Finder shall 表示する結果を走査の最終結果と一致させる
4. The Large Folder Finder shall 途中経過の通知の間隔を、既存の間隔（報告は5秒・ログは20秒）より短くしない

### Requirement 4: 開発用の残骸の除去

**Objective:** As a 利用者, I want 開発用のダイアログが出ないこと, so that 意味の分からない英語のダイアログで作業を止められない

#### Acceptance Criteria

1. The Large Folder Finder shall 開発者向けの確認用のダイアログを利用者に表示しない
2. If 途中経過の表示の更新で失敗が起きたとき, then the Large Folder Finder shall 失敗の内容をログに記録し、走査を続ける

### Requirement 5: 失敗の扱い

**Objective:** As a 利用者と開発者, I want 失敗が黙って消えないこと, so that 設定の誤りや想定外の不具合に気づいて対処できる

#### Acceptance Criteria

1. The Large Folder Finder shall 意図して無視する失敗の種類を定め、それ以外の失敗を黙って捨てない
2. If 意図して無視する種類以外の失敗を捕捉したとき, then the Large Folder Finder shall 失敗の内容と発生した処理をログに記録する
3. If 走査中にアクセスできないフォルダまたはファイルがあったとき, then the Large Folder Finder shall 走査を止めずに続け、スキップした対象をログに記録する
4. If `Config.txt` を解析できないとき, then the Large Folder Finder shall 既定の設定で起動し、解析できなかったことと理由をログに記録する
5. If `Config.txt` を解析できないとき, then the Large Folder Finder shall 既定の設定で動いていることを利用者に知らせる
6. If 走査が想定外の失敗で中断したとき, then the Large Folder Finder shall 状態表示に失敗の内容を示し、ログに記録する

### Requirement 6: 結果の描画の取り消しのタブごとの分離

**Objective:** As a 利用者, I want あるタブの表示の更新が別のタブの表示を妨げないこと, so that 複数のタブを行き来しても各タブの結果が正しく表示される

#### Acceptance Criteria

1. When あるタブで結果の描画を始めたとき, the Large Folder Finder shall 同じタブで進行中の以前の描画を取り消す
2. While あるタブで描画が進行している間, when 別のタブで描画を始めたとき, the Large Folder Finder shall 最初のタブの描画を取り消さない
3. When 描画が取り消されたとき, the Large Folder Finder shall 取り消した描画の結果を表示に反映しない

