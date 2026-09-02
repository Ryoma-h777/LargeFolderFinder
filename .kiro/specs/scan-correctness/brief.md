# Brief: scan-correctness

## Problem

走査処理に、利用者に実害のある欠陥が3つある。

1. **260文字を超えるパスが無言で集計から漏れる。** `WIN32_FIND_DATA.cFileName` は `SizeConst = 260` で宣言され、長いパスへの対応は何も行われていない。NAS の深い階層を探索するというこのアプリの中核用途に直撃する
2. **走査中の暫定ツリーを UI がロックなしで読んでいる。** `Scanner` は `lock (currentNode.Children)` で書き込む一方、`SessionViewModel` は同じツリーを受け取って描画する。読み側にロックがない。`MessageBox.Show($"Failed to update progress: ...", "Debug")` というデバッグ用ダイアログが製品コードに残っており、発生していた形跡がある
3. **例外の握りつぶしが常態化している。** `catch` 65箇所のうち64箇所が空またはコメントのみ。上記1の欠落もこれによって隠蔽されている

これらは「速くする」より前に片付けるべきである。壊れたまま速くしても意味がなく、`scan-golden-baseline` の期待値が不完全なまま固定されてしまう。

## Current State

- 事前カウント（`Scanner.CountFoldersRecursive`）は手書き P/Invoke の `FindFirstFileEx` / `FindNextFile` を使用
- 本スキャン（`Scanner.ScanRecursiveInternal`）は `DirectoryInfo.EnumerateFiles` / `EnumerateDirectories` を使用
- `Helpers/Win32.cs` に `FindFirstFileEx` / `FindNextFile` / `FindClose` / `GetDiskFreeSpace` / `SetProcessWorkingSetSize` の P/Invoke を集約
- app.manifest は存在せず、`longPathAware` の宣言もない
- `Services/Scanner.cs:308` が暫定ツリー（`counter.RootNode`）を `IProgress` で報告し、`ViewModels/SessionViewModel.cs:184` がそれを受けて描画する
- `ViewModels/SessionViewModel.cs:194` にデバッグ用 `MessageBox`

## Desired Outcome

- 260文字を超えるパスも、**日本語を含む場合も**正しく走査・集計される
- 走査中に暫定結果を表示しても競合が起きない
- 失敗が無言で消えず、記録され、利用者にも必要に応じて伝わる
- デバッグ用の残骸が製品コードにない

## Approach

**P/Invoke の全廃が長いパス対応の必須条件**である。これは実現性検証で確定した重要な制約であり、本スペックの設計を規定する。

.NET 10 の BCL は内部で `PathInternal.EnsureExtendedPrefixIfNeeded()` によりパス長260以上で自動的に `\\?\` を付与するため、`System.IO` 経由なら manifest もレジストリ設定も不要である。一方 **apphost（生成される exe）自体は long path aware ではない**ため、手書き P/Invoke が1つでも残るとその呼び出しは Windows 側の制限を受ける。

したがって、
1. 事前カウントの `FindFirstFileEx` を `System.IO.Enumeration.FileSystemEnumerator<T>` に置き換える
2. `GetDiskFreeSpace`（クラスタサイズ取得）と `SetProcessWorkingSetSize`（ワーキングセット切り詰め）の扱いを決める。これらはパス列挙とは無関係だが、残す場合は long path とは独立に安全性を確認する
3. 暫定ツリーの共有をやめ、UI へ渡す時点でスナップショットを作るか、読み書き双方を保護する方式に改める
4. 例外方針を定め、握りつぶしを是正する。走査継続のための意図的な無視（アクセス拒否など）と、隠してはいけない失敗とを区別する

## Scope

- **In**:
  - 手書き P/Invoke の全廃（少なくともパス列挙経路から完全に除去）
  - 260文字超パスへの対応と、**日本語を含む長いパスでの実機検証**
  - 走査中の暫定ツリー共有による競合の解消
  - デバッグ用 `MessageBox` の除去
  - 例外ハンドリング方針の策定と適用（意図的な無視は理由を明記、それ以外は記録）
  - 上記の修正を反映した `scan-golden-baseline` 期待値データの更新

- **Out**:
  - 速度の最適化（`scan-performance` の範囲。ただし `FileSystemEnumerator<T>` への置き換えは両スペックにまたがるため、本スペックでは「正しく動くこと」を担保し、性能の作り込みは次スペックで行う）
  - `FolderInfo` のノード構造の変更（`scan-performance` の範囲）
  - UI 側のエラー表示デザイン（`ui-redesign` の範囲）

## Boundary Candidates

- P/Invoke の除去と BCL への置き換え
- 長いパスの対応と検証
- 並行アクセスの是正
- 例外方針の策定と適用

## Out of Boundary

- 並列度の調整やアルゴリズムの改善（性能に関する判断はすべて `scan-performance`）
- ログ出力機構そのものの作り替え（`architecture-refactoring`）

## Upstream / Downstream

- **Upstream**: `dotnet10-migration`（.NET 10 の BCL が長いパス対応の前提）、`scan-golden-baseline`（修正前後の比較に必要）
- **Downstream**: `scan-performance` が本スペックの結果の上で最適化を行う

## Constraints

- **P/Invoke を全廃できない場合、`app.manifest` の `longPathAware` に加えて Windows のレジストリ `HKLM\SYSTEM\CurrentControlSet\Control\FileSystem\LongPathsEnabled=1` の両方が必要になる**（[dotnet/runtime#43555](https://github.com/dotnet/runtime/issues/43555)、[Maximum Path Length Limitation](https://learn.microsoft.com/en-us/windows/win32/fileio/maximum-file-path-limitation)）。レジストリは利用者環境に依存するため**そこに依存する設計にしないこと**。BCL 経由への統一が唯一の確実な解
- **日本語を含む260文字超のパスで `PathTooLongException` / `DirectoryNotFoundException` が発生する未解決の報告がある**（[dotnet/runtime#126535](https://github.com/dotnet/runtime/issues/126535)、.NET 6/7/8 対象、.NET 10 での再現有無は未確認）。**日本語パスでの実機検証を必須タスクとすること**
- アクセス拒否時に走査を継続する挙動は**仕様であり維持する**（1フォルダの権限不足で全体を止めない）。ただし「何件スキップしたか」を利用者が知れることが望ましい
- 進捗通知のスロットリング（報告5秒・ログ20秒）は performance.md の方針であり維持すること。競合の解消策がこの間隔を短くする方向に働かないこと
- リパースポイントの除外は維持すること（循環回避）
- 修正により集計値が変わる（長いパスが新たに含まれる）。**ゴールデンデータの更新が必要であり、変わったこと自体が正しい**。どのエントリが新規に含まれるようになったかを説明できる状態にすること
