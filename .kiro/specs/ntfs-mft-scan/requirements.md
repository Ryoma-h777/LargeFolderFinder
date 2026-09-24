# Requirements Document

## Project Description (Input)

### 誰の問題か

Large Folder Finder の利用者（ローカルドライブや NAS の容量の圧迫原因を調べる人）。利用者の判断（2026-09-22）により、**このアプリの存在意義のため WizTree と同等以上の走査速度**を目標とした。WizTree が非常に速いのは、ローカルの NTFS ドライブを管理者として走査するときに MFT（ドライブ上の全ファイルの目録）を直接読むためで、この場面は通常の列挙では追いつけない。

### 現状（2026-09-24、scan-performance の完了後）

- 走査は通常の列挙（1フォルダずつ読む方式）のみ。`scan-performance` で、決まった数のワーカーが共有の作業の列からフォルダを取り出し、1フォルダを1回の列挙で処理する方式に作り替え、**この開発機で 1.6〜1.7 倍速くなった**（システムドライブ全体 約178万ファイルが 11.6秒 → 7.1秒）
- 並列度は `Config.txt` の `ScanThreads`（0 は自動。ローカルは論理プロセッサ数を 4〜8 に丸めた値、ネットワークは仮の 16）
- 集計値は期待値データ（`baselines/fixture-v1.golden.txt` と物理サイズ換算ありの控え）と、計測の道具の要約値で守られている
- NAS と WizTree との比較は、利用者の計測を待っている段階（`scan-performance` のタスク6）
- 本体の OS の直接呼び出しは2つだけ（クラスタサイズの取得、メモリの切り詰め）。列挙は BCL のみ
- 「管理者として開き直す」メニューはあるが、管理者で動いているときに項目を無効にする処理は失われている（`decisions.md` の食い違い。担当は `architecture-refactoring`）

### 何が変わるべきか

- ローカルの NTFS ドライブを**管理者として**走査するとき、MFT を直接読む方式が使われ、**WizTree の最速モードと同等以上**の所要時間になる
- 使えない条件（NAS、NTFS 以外、管理者でない、読み取りに失敗した）では、いまの通常の走査に自動で戻る。利用者はどちらの方式で走査したかが分かる
- 走査の対象がドライブ全体でなくフォルダでも、そのフォルダの配下だけが結果になる

### 利用者の決定（2026-09-24）

1. **アクセス権が無いフォルダの中身も数える**（WizTree と同じ）。ドライブの容量の全体を正しく把握できることを優先する。通常の走査の結果とは合計が変わる
2. **ハードリンクは名前ごとに数える**（いまの走査と同じ）。ただし「実体は1つでも複数の名前があるため、合計がディスクの実使用量より大きく出ることがある」ことを、**利用者が気づける場所に目立たない形で記載する**
3. **管理者に昇格するかを走査のたびに尋ねる**。断られたら通常の走査で続ける
4. **管理者になれないアカウント（管理者の権限を持たない利用者）では、昇格を尋ねない**。速い方式は使えないので、黙って通常の走査で走査する（2026-09-24 の判断）
5. あわせて、**設定で「起動時に管理者で開く」を選べるようにする**（既定は無効）。有効にした人は起動のときだけ権限の確認を通り、走査のたびに尋ねられない。マニフェストで管理者を要求する形は採らない（管理者権限を持たないアカウントでアプリが起動できなくなり、将来のドラッグ＆ドロップも使えなくなるため。2026-09-24 の判断）

### 前提と留意点

- 集計値の考え方が変わる場面（権限の無い場所を数える）があるため、**期待値データとの比較は「通常の走査の結果」を守るものとして維持し、MFT の走査は別の期待値で確かめる**
- MFT の読み取りにはボリュームのハンドルを開く OS の直接呼び出しが要る。プロジェクトは長いパスの制約を避けるために直接呼び出しを減らしてきたが、ボリュームのハンドルはパスを渡さないため長いパスの問題は生じない（`ntfs-mft-scan/brief.md`）
- 走査の結果の木の規約（ルートの名前は完全パス、子に親を設定してから `lock` の下で一括追加、リパースポイントのフォルダは潜らない）は `scan-performance` が決めたものを守る
- 詳しい背景・方式の候補・境界は [brief.md](brief.md) を参照

## Introduction

本フィーチャーは、ローカルの NTFS ドライブを管理者として走査するときに、MFT を直接読む走査方式を加える。使えない条件では通常の走査に戻す。走査の方式が変わっても、利用者にはどちらで走査したかが分かり、結果の意味の違い（アクセス権の無い場所を数えること、ハードリンクの数え方）が分かるようにする。

## Boundary Context

- **In scope**: MFT を直接読む走査方式、使える条件の判定と通常の走査への切り替え、管理者への昇格の確認、対象がフォルダのときの配下の絞り込み、走査の方式と結果の意味の利用者への提示（最小限）、MFT の走査の集計値を確かめる仕組み、WizTree の最速モードとの比較の計測と記録
- **Out of scope**: 通常の列挙の経路の速さ（`scan-performance` が担当）、常駐監視やインデックスの事前構築、NTFS 以外のファイルシステムの目録の直接読み取り、結果の表示の作り替え（`ui-redesign`）、「管理者として開き直す」メニューの状態の整理（`architecture-refactoring`）
- **Adjacent expectations**: 通常の走査の集計値は `scan-golden-baseline` の期待値データで守られ続ける。`scan-performance` が決めた結果の木の規約、進捗の通知の間隔（報告5秒・ログ20秒）、スキップの記録、取り消しの扱い、`Config.txt` の並列度の設定は維持する

## Requirements

### Requirement 1: MFT を直接読む走査

**Objective:** As a 利用者, I want ローカルのドライブの走査が WizTree と同じくらい速く終わること, so that 容量の原因を調べる作業を待たずに進められる

#### Acceptance Criteria

1. Where 対象がローカルの NTFS のドライブであり、かつアプリが管理者として動いているとき, the Large Folder Finder shall MFT を直接読む方式で走査する
2. When 同じローカルの NTFS のドライブを管理者として WizTree と同じ条件で走査したとき, the Large Folder Finder shall WizTree 以下の所要時間で走査を終える
3. The Large Folder Finder shall MFT を直接読む走査で、ドライブ上のすべてのファイルとフォルダ（アクセス権が無い場所にあるものを含む）を集計の対象にする
4. The Large Folder Finder shall MFT を直接読む走査で、ドライブを読み取るだけで書き込まない
5. The Large Folder Finder shall MFT を直接読む走査で得たファイルの名前や構成を、走査の結果とログ以外に残さない

### Requirement 2: 使える条件の判定と通常の走査への切り替え

**Objective:** As a 利用者, I want どの環境でも走査が失敗しないこと, so that NAS でも USB メモリでも同じ操作で使える

#### Acceptance Criteria

1. If 対象がネットワーク上の場所、NTFS 以外のファイルシステム、またはアプリが管理者として動いていないとき, then the Large Folder Finder shall 通常の走査で走査する
2. If MFT の読み取りを始められない、または途中で失敗したとき, then the Large Folder Finder shall 通常の走査に切り替えて走査を完了させ、切り替えた理由をログに記録する
3. When 走査が完了したとき, the Large Folder Finder shall どちらの方式で走査したかを利用者が確かめられるようにする
4. Where MFT を直接読む方式を使わない設定が有効であるとき, the Large Folder Finder shall 常に通常の走査で走査する

### Requirement 3: 管理者への昇格の確認

**Objective:** As a 利用者, I want 速い方式が使える場面でそれを教えてもらえること, so that 必要なときだけ管理者で開き直せる

#### Acceptance Criteria

1. When 対象がローカルの NTFS のドライブで、アプリが管理者として動いていない状態で走査を始め、かついまの利用者が管理者として開き直せるとき, the Large Folder Finder shall 管理者として開き直すかどうかを利用者に尋ねる
2. If いまの利用者が管理者として開き直せないとき, then the Large Folder Finder shall 昇格を尋ねずに通常の走査で走査する
3. If 利用者が管理者として開き直すことを断ったとき, then the Large Folder Finder shall そのまま通常の走査で走査を続ける
4. If 利用者が管理者として開き直すことを選び、権限の確認が拒否されたとき, then the Large Folder Finder shall いまのアプリのまま通常の走査で走査を続ける
5. Where 昇格を尋ねない設定が有効であるとき, the Large Folder Finder shall 尋ねずに通常の走査で走査する
6. Where 起動時に管理者で開く設定が有効であり、アプリが管理者として動いておらず、いまの利用者が管理者として開き直せるとき, the Large Folder Finder shall 起動の時点で管理者として開き直すことを試みる
7. If 起動の時点で管理者として開き直すことが拒否されたとき, then the Large Folder Finder shall いまのアプリのまま起動を続け、走査のたびに昇格を尋ねない
8. The Large Folder Finder shall 管理者権限を持たないアカウントでも起動できる

### Requirement 4: 集計値の意味と、その伝え方

**Objective:** As a 利用者, I want 表示された合計の意味が分かること, so that ディスクの空き容量と見比べて判断を誤らない

#### Acceptance Criteria

1. The Large Folder Finder shall MFT を直接読む走査で、アクセス権が無いフォルダの中のファイルも合計に含める
2. The Large Folder Finder shall 1つのファイルに複数の名前がある場合（ハードリンク）、名前ごとに合計へ含める
3. The Large Folder Finder shall 複数の名前があるファイルにより合計がディスクの実使用量より大きく出ることがある旨を、利用者が確かめられる場所に記載する
4. The Large Folder Finder shall MFT を直接読む走査と通常の走査で、同じ場所の同じファイルに対して同じ大きさを示す
5. Where 物理サイズ換算が有効であるとき, the Large Folder Finder shall MFT を直接読む走査でも通常の走査と同じ換算の結果を示す
6. The Large Folder Finder shall MFT を直接読む走査で、ファイルシステム自身の管理用の領域（目録そのものなど）を通常のファイルとして合計に含めない

### Requirement 5: 対象がフォルダのときの絞り込み

**Objective:** As a 利用者, I want ドライブ全体でなく特定のフォルダを指定しても正しい結果が出ること, so that 調べたい場所だけを見られる

#### Acceptance Criteria

1. When 対象がドライブの中のフォルダであるとき, the Large Folder Finder shall そのフォルダとその配下だけを結果に含める
2. When 対象がドライブの中のフォルダであるとき, the Large Folder Finder shall そのフォルダの合計を、通常の走査で同じフォルダを走査した場合の合計と一致させる（アクセス権が無い場所を含むことによる差を除く）
3. If 対象のフォルダの配下が小さく、MFT を読むほうが遅くなる条件のとき, then the Large Folder Finder shall 通常の走査で走査する

### Requirement 6: 走査の振る舞いの保全

**Objective:** As a 利用者, I want 速い方式でも今までどおりの使い勝手であること, so that 途中で止めたり進み具合を見たりできる

#### Acceptance Criteria

1. While MFT を直接読む走査が進行している間, the Large Folder Finder shall 進み具合を表示する
2. When 利用者が MFT を直接読む走査を取り消したとき, the Large Folder Finder shall 走査を止めて取り消したことを状態表示に示す
3. The Large Folder Finder shall MFT を直接読む走査でも、途中経過の通知の間隔（報告は5秒・ログは20秒）を短くしない
4. When MFT を直接読む走査を終えたとき, the Large Folder Finder shall 結果を通常の走査と同じ形で表示・保存・復元できるようにする
5. The Large Folder Finder shall MFT を直接読む走査の完了時のメモリ使用量を、同じ対象を通常の走査で走査したときより大きくしない

### Requirement 7: 速さと正しさの裏付け

**Objective:** As a 利用者と開発者, I want 速さと集計値が実測で裏付けられていること, so that 速くなったことと結果が正しいことを信頼できる

#### Acceptance Criteria

1. The Large Folder Finder shall MFT を直接読む走査の集計値を、同じ対象を通常の走査で走査した結果と突き合わせて確かめられる仕組みを持つ
2. When 同じローカルの NTFS のドライブを管理者として WizTree と比べたとき, the Large Folder Finder shall 両方の所要時間と条件（対象・容量・ファイル数・初回か2回目以降か・WizTree の版）を記録として残す
3. If WizTree より遅いとき, then the Large Folder Finder shall その差と、測った範囲で分かった原因を記録に残す
4. The Large Folder Finder shall 計測の手順を、第三者が同じ手順で再び測れる形で残す
