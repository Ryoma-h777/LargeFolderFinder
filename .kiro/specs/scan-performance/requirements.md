# Requirements Document

## Project Description (Input)

### 誰の問題か

Large Folder Finder の利用者（NAS やローカルドライブの容量の圧迫原因を調べる人）。「速度は機能である」がこのプロダクトの第一の優先事項で、利用者の判断（2026-09-22）により、**このアプリの存在意義のため WizTree と同等以上の走査速度**を目標とした。WizTree もローカルの NTFS を管理者として走査するとき以外（NAS、管理者でない、NTFS 以外）は通常の列挙に戻るため、この場面が本スペックの主戦場である。

### 現状（2026-09-22、scan-correctness の完了後）

- 走査の結果は正しく、失敗も記録される（scan-correctness）。集計値は期待値データ（`baselines/fixture-v1.golden.txt` と物理サイズ換算ありの控え）で守られている
- 速度の面は手つかずで、並列化の効果を自ら打ち消している疑いがある
  - `FolderInfo.AddSize` がファイルのフォルダごとに**ルートまでの全祖先へ `Interlocked.Add`** を行い、全スレッドがルートのノードの同じ場所へ書き込む
  - 再帰の各階層で `Parallel.ForEach` が入れ子になり、並列度が制御されていない
  - 本スキャンは `DirectoryInfo.EnumerateFiles` + `FileInfo.Length` で列挙している（1パスで属性を読む `FileSystemEnumerator<T>` は使っていない）
  - `FolderInfo` は class で `Parent` 参照を持ち、大量のノードで GC 圧が高い。`GetFullPath` は呼ぶたびに祖先を辿って文字列を作る
- README の公開値: PC ローカル約400GB・117万ファイルで 5〜13秒、NAS 約20TB・140万ファイルで 18〜30分。WizTree との比較の実測は無い

### 何が変わるべきか

- 走査時間が現状より明確に短くなる（特に多ファイル・NAS）
- **通常の列挙の場面（NAS、管理者でない・NTFS 以外のローカル）で WizTree と同等以上**の速さになり、実測で裏付けられる
- 並列度が意図して制御され、ローカルの SSD と NAS の両方で妥当に振る舞う
- **走査結果の数値は一切変わらない**

### 前提と留意点

- 計測なしに最適化しない（performance.md の計測の手順）。着手前に現状を実測し、ボトルネックの仮説の裏を取る
- 走査の事前カウントと本スキャンの数える集合を揃え続ける（scan-correctness の要件1.4、自己検証あり）
- 進捗の通知の間引き（報告5秒・ログ20秒）、逐次の経路（`UseParallelScan = false`）、アクセス拒否でスキップして続ける動き、スキップの記録（`ScanSkipRecorder`）、最後の進捗の報告は維持する
- MFT を直接読む方式は `ntfs-mft-scan` の担当で、本スペックの範囲外
- 詳細な背景・候補・境界は [brief.md](brief.md) を参照。ただし brief の「`scan-correctness` で P/Invoke を全廃する際に導入済みの基盤」は事実と異なる（事前カウントは `DirectoryInfo.EnumerateDirectories` に置き換えたのみで、`FileSystemEnumerator<T>` は未導入）。本節を正とする

## Introduction

本フィーチャーは、Large Folder Finder の走査を速くする。集計値（どのフォルダが何バイトか）は一切変えずに、ファイルごとの祖先への加算や制御されていない入れ子の並列化など、並列化の効果を打ち消している箇所を実測にもとづいて取り除く。

目標は、通常の列挙で走査する場面（NAS、管理者でない・NTFS 以外のローカル）で **WizTree と同等以上** の速さを出し、それを実測で示すことである（2026-09-22 利用者の決定）。

## Boundary Context

- **In scope**: 本スキャンの所要時間の短縮、並列度の制御、走査中のメモリ使用量、走査の結果の保存と復元が引き続き動くこと、変更の前後と WizTree との比較の計測とその記録、README の公開値の更新
- **Out of scope**: MFT（NTFS の全ファイルの目録）を直接読む走査方式（`ntfs-mft-scan`）、結果の表示・再描画の速さ（`ui-redesign`）、保存・復元そのものの速さ、常駐監視やインデックスの事前構築、事前カウントの速さの作り込み（集合の一致は守る）
- **Adjacent expectations**: 集計値が変わらないことは `scan-golden-baseline` の期待値データ（物理サイズ換算なしの `baselines/fixture-v1.golden.txt` と、換算ありの控え）で確かめる。`scan-correctness` が決めた振る舞い（事前カウントと本スキャンの数の一致、スキップの記録、最後の進捗の報告、取り消しの扱い、進捗の通知の間隔）は維持する。保存データの互換性は捨てる方針で決定済み（roadmap.md）

## Requirements

### Requirement 1: 集計値と走査の振る舞いの保全

**Objective:** As a 利用者, I want 速くなっても結果が変わらないこと, so that 走査の結果をこれまでどおり信頼して容量整理を判断できる

#### Acceptance Criteria

1. The Large Folder Finder shall 本スキャンの集計値を本フィーチャーの変更の前後で変えない
2. Where 物理サイズ換算が有効であるとき, the Large Folder Finder shall 集計値を本フィーチャーの変更の前後で変えない
3. The Large Folder Finder shall 並列の走査と逐次の走査で同じ集計値を示す
4. When 事前カウントを行った走査が取り消されずに完了したとき, the Large Folder Finder shall 事前カウントの数と、走査で数えた事前カウントの範囲のフォルダの数を一致させる
5. If 走査中にアクセスできないフォルダまたはファイルがあったとき, then the Large Folder Finder shall 走査を止めずに続け、スキップした対象をログに記録する
6. When 利用者が走査を取り消したとき, the Large Folder Finder shall 走査を止めて取り消したことを状態表示に示す
7. The Large Folder Finder shall 途中経過の通知の間隔を、既存の間隔（報告は5秒・ログは20秒）より短くしない

### Requirement 2: 走査時間の短縮

**Objective:** As a 利用者, I want 走査が今より短い時間で終わること, so that 大きなドライブや NAS でも待たずに容量の原因を探せる

#### Acceptance Criteria

1. When 同じローカルのドライブを同じ条件で走査したとき, the Large Folder Finder shall 本フィーチャーの変更の前より短い時間で走査を終える
2. When 同じ NAS の共有を同じ条件で走査したとき, the Large Folder Finder shall 本フィーチャーの変更の前より短い時間で走査を終える
3. The Large Folder Finder shall 計測したいずれの場面（ローカルと NAS、並列と逐次、初回と2回目以降）でも、本フィーチャーの変更の前より、同じ条件で繰り返し測ったときのばらつきの範囲を超えて遅くならない
4. While 走査が進行している間, the Large Folder Finder shall 画面の操作（タブの切り替え、取り消し）に応答する

### Requirement 3: 並列度の制御

**Objective:** As a 利用者, I want 走査の並列の度合いが対象に応じて妥当であること, so that 深い・広いフォルダ構造でも PC が過負荷にならず、NAS でも速く走査できる

#### Acceptance Criteria

1. The Large Folder Finder shall 同時に行う列挙の数を、フォルダの深さや広さに関わらず定めた上限以内に保つ
2. Where 逐次の走査の設定が有効であるとき, the Large Folder Finder shall 列挙を1つずつ順に行う
3. The Large Folder Finder shall 並列度の上限とその決め方を、利用者が `Config.txt` の説明または README で確かめられるようにする

### Requirement 4: WizTree との比較

**Objective:** As a 利用者, I want このアプリが WizTree と同等以上に速いことが実測で分かること, so that このアプリを使う理由がはっきりする

#### Acceptance Criteria

1. When 同じ NAS の共有を WizTree と同じ条件で走査したとき, the Large Folder Finder shall WizTree 以下の所要時間で走査を終える
2. When 同じローカルのドライブを管理者でない状態で WizTree と同じ条件で走査したとき, the Large Folder Finder shall WizTree 以下の所要時間で走査を終える
3. The Large Folder Finder shall WizTree との比較の結果を、場面・対象（種類・容量・ファイル数・接続方式）・条件（管理者か否か、初回か2回目以降か）・両方の所要時間・このアプリの版とともに記録として残す
4. If いずれかの場面で WizTree より遅いとき, then the Large Folder Finder shall その差と、測った範囲で分かった原因を記録に残す

### Requirement 5: 計測と公開値

**Objective:** As a 開発者と利用者, I want 速さの主張が再現できる計測で裏付けられていること, so that 速くなったのか、どれだけ速いのかを信頼できる

#### Acceptance Criteria

1. The Large Folder Finder shall 本フィーチャーの変更の前と後の所要時間を、同じ対象・同じ条件（配布と同じ構成、初回と2回目以降の区別、`Config.txt` の設定）で計測した記録を残す
2. The Large Folder Finder shall 計測の手順を、第三者が同じ手順で再び測れる形で残す
3. When 計測の結果を README の公開値に反映するとき, the Large Folder Finder shall 計測した環境（CPU・ストレージ・接続方式）を併記する

### Requirement 6: メモリの使用量

**Objective:** As a 利用者, I want 大きなフォルダを走査してもメモリの使用量が増えないこと, so that 数百万ファイルの走査でも PC が重くならない

#### Acceptance Criteria

1. When 同じ対象を同じ条件で走査したとき, the Large Folder Finder shall 走査の完了時のメモリ使用量を、本フィーチャーの変更の前より、同じ条件で繰り返し測ったときのばらつきの範囲を超えて大きくしない

### Requirement 7: 走査の結果の保存と復元

**Objective:** As a 利用者, I want 走査の結果をこれまでどおり保存して次回に開けること, so that 再び走査しなくても前回の結果を確かめられる

#### Acceptance Criteria

1. When 走査を終えたタブを保存して次回の起動で開いたとき, the Large Folder Finder shall 保存したときと同じ集計値と構造の結果を表示する
2. If 保存されている結果が以前の形式で読めないとき, then the Large Folder Finder shall 異常終了せずに起動し、読めなかったことをログに記録する
3. Where 保存の形式を以前の版から変えたとき, the Large Folder Finder shall 新しい版の番号を、以前の版で保存した結果が引き継がれない変更を示す番号にする
