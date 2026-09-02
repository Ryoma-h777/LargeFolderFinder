# Brief: scan-performance

## Problem

「速度は機能である」がこのプロダクトの第一優先事項だが、現在の実装には並列化の効果を自ら打ち消している箇所がある。

最大の疑いは `FolderInfo.AddSize` である。

```csharp
public void AddSize(long bytes)
{
    System.Threading.Interlocked.Add(ref _size, bytes);
    Parent?.AddSize(bytes);   // 親を再帰的に遡る
}
```

ファイル1件ごとに**ルートまでの全祖先に対して `Interlocked.Add` を実行**する。117万ファイル・深さ10なら約1,200万回のアトミック演算になり、しかも**全スレッドがルートノードの同じキャッシュラインへ書き込む**。並列度を上げるほど競合が悪化する構造になっている。

加えて、再帰の各階層で `Parallel.ForEach` が入れ子になり並列度が制御されていないこと、本スキャンが `DirectoryInfo.EnumerateFiles` + `FileInfo.Length` でファイルごとにサイズを取りに行っていることも改善余地である。

## Current State

- `Models/FolderInfo.cs` の `AddSize` が祖先を再帰的に遡って `Interlocked.Add`
- `Services/Scanner.cs` の `ScanRecursiveInternal` が再帰の各階層で `Parallel.ForEach`（`MaxDegreeOfParallelism` 未指定）
- 子ノードの追加は `lock (currentNode.Children)`
- `FolderInfo` は class で `Parent` 参照を持つ。大量ノードで GC 圧が高い
- `GetFullPath()` は `Parent` を辿って `Path.Combine` を繰り返す（呼び出しのたびに O(深さ) の文字列確保）
- `RestoreParentReferences` / `CountFolderRecursive` も再帰
- README 公開の実測値: PC ローカル 約400GB / 117万ファイルで 5〜13秒、NAS 約20TB / 140万ファイルで 18〜30分

## Desired Outcome

- 走査時間が現状より明確に短縮されている（特に大規模・多ファイル環境）
- 並列度が意図して制御されており、ローカル SSD と NAS の双方で妥当に振る舞う
- **走査結果の数値は一切変わっていない**
- 改善が実測で裏付けられ、README の公開値が更新されている

## Approach

計測なしに変更しないことを原則とする。ボトルネックの仮説は立っているが、**着手前に実測して裏を取る**。

改善候補は以下。優先度は実測結果で決める。

1. **`AddSize` の再設計** — 各スレッドがローカルに集計し、階層ごとに1回だけ合算する。祖先への逐次 `Interlocked` を廃止する。最も効果が見込まれる
2. **`FileSystemEnumerator<T>` による1パス列挙** — 列挙と同時にサイズ・更新日時・属性を取得し、`FileInfo.Length` の個別取得をなくす。`scan-correctness` で P/Invoke を全廃する際に導入済みの基盤を、ここで性能面から作り込む
3. **並列度の制御** — 再帰的な `Parallel.ForEach` の入れ子をやめ、並列の粒度を明示的に管理する。ローカルと NAS で最適値が異なるため、`Config.txt` での調整余地を残すことも検討する
4. **ノード構造の見直し** — 保存データの互換性を捨てる判断がされているため、`FolderInfo` の構造は自由に変更できる。GC 圧の軽減やメモリ局所性の改善を検討する
5. **再帰の見直し** — 深い構造でのスタック消費を抑える

## Scope

- **In**:
  - 着手前および各変更後の実測（Release ビルド、ローカルとネットワークの2系統）
  - `AddSize` の集計方式の再設計
  - `FileSystemEnumerator<T>` による列挙の性能面での作り込み
  - 並列度の制御方式の決定と実装
  - `FolderInfo` のノード構造・保存形式の見直し
  - 再帰処理の見直し
  - README の実測値の更新（計測環境の併記を含む）
  - performance.md の更新（変更した方針の反映）

- **Out**:
  - 正しさの修正（`scan-correctness` で完了済みであることが前提）
  - UI 側の描画性能（仮想化・デバウンスは維持するが、表示の作り替えは `ui-redesign`）
  - キャッシュの保存・復元の速度（必要なら別途切り出す）

## Boundary Candidates

- 集計方式の再設計（`FolderInfo`）
- 列挙の作り込み（`Scanner`）
- 並列度の制御方式
- ノード構造・保存形式の見直し

## Out of Boundary

- 表示条件変更時の再描画性能（`ui-redesign` の範囲）
- 走査アルゴリズムの根本的な変更（例: インデックスの事前構築や常駐監視）は本プロジェクトの範囲外

## Upstream / Downstream

- **Upstream**: `scan-correctness`（正しく動くことが前提。壊れたまま速くしない）、`scan-golden-baseline`（数値が変わっていないことの確認に必須）
- **Downstream**: `architecture-refactoring` が `FolderInfo` に触れるため、本スペックの構造変更が先に確定している必要がある

## Constraints

- **走査結果の数値が変わってはならない。** これが本スペックで最も重要な制約であり、`scan-golden-baseline` の期待値との突き合わせを各変更ごとに行うこと
- **計測なしに最適化しない。** performance.md の計測手順に従う。Release ビルド、ローカルとネットワークの2系統、OS ファイルキャッシュの影響を分けて記録、`Config.txt` の設定を記録に残す
- **並列度を触る変更は必ず実測とセットで行う。** 低速なネットワーク越しでは並列が効くが、ローカル SSD では過剰なスレッドが逆効果になり得る
- 逐次実行の経路（`UseParallelScan = false`）は、並列起因の不具合を切り分けるための退避手段として維持すること
- 進捗通知のスロットリング（報告5秒・ログ20秒、EMA による平滑化）は維持すること。最適化のために高頻度通知を導入しないこと
- 保存データの互換性は捨てる方針のため、`FolderInfo` の構造とシリアライズ形式は自由に変更してよい。ただし v2.0.0 として扱い、既存利用者の履歴がリセットされる旨の告知が必要
- `MessagePack` の `[Key(n)]` 採番を変更する場合、既存の保存ファイルは読めなくなる（互換を捨てる方針なので許容されるが、意図した変更であることを明示すること）
