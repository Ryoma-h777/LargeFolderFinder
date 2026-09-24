# Research & Design Decisions

## Summary
- **Feature**: `ntfs-mft-scan`
- **Discovery Scope**: Complex Integration（OS の直接呼び出しを使う新しい走査の経路を、既存の走査と並べて入れる）
- **Key Findings**:
  - **MFT を生で解析する必要はない**。`FSCTL_QUERY_FILE_LAYOUT`（Windows 8.1 以降、NTFS 専用の文書化された呼び出し）が、ボリューム全体のファイルについて **名前・親のファイル参照番号・属性・ストレームごとの論理サイズと確保サイズ** を 1 回の連続した列挙で返す。ハードリンクの別名は名前ごとに、8.3 の短縮名は印つきで返るため、利用者の決定（名前ごとに数える・短縮名で二重に数えない）をそのまま満たせる
  - 生の MFT 解析（`FSCTL_GET_NTFS_VOLUME_DATA` → $MFT のデータラン → ファイルレコードの解析）は最速だが、update sequence の補正、$MFT 自身の断片化、拡張レコード、8.3 の別名など落とし穴が多い。**二次の手段**として残す
  - `FSCTL_ENUM_USN_DATA`（Everything の方式）は**サイズを返さない**。1件ずつ補うと 134 万ファイルで約120秒という報告があり、単独では採らない
  - どの方式もボリュームのハンドルを開くため**管理者が必須**（ボリュームのデバイスの既定の権限が管理者に限られる）。ボリュームの生の読み取りは NTFS のファイル単位の権限の確認を通らないので、**アクセス権の無い場所も数えられる**（利用者の決定どおり）
  - WizTree の速さは「ファイル1件ずつ OS に尋ねる往復」を「目録の1回の連続した読み取り」に置き換えたことによる。100万ファイルなら目録は約1GB で、NVMe なら読み取り自体は1秒未満

## Research Log

### 方式の候補
- **Sources**: learn.microsoft.com（`FSCTL_QUERY_FILE_LAYOUT`、`QUERY_FILE_LAYOUT_INPUT`、`FILE_LAYOUT_ENTRY`、`FILE_LAYOUT_NAME_ENTRY`、`STREAM_LAYOUT_ENTRY`、`FSCTL_GET_NTFS_VOLUME_DATA`、`FSCTL_ENUM_USN_DATA`、`USN_RECORD_V2`、`GetVolumeInformation`、ACLs and the device stack）、flatcap.github.io の NTFS の資料、diskanalyzer.com（WizTree の about / faq）、voidtools.com（Everything の faq）、dev.to の MFT 検索の実装記事、OSR のスレッド
- **Findings**:

| 方式 | 得られるもの | 権限 | 主な難しさ |
|---|---|---|---|
| A. 生の MFT の解析 | すべて（名前・親・サイズ・属性） | 管理者 | update sequence の補正、データランの展開、$MFT の断片化、拡張レコード、予約レコードの除外、8.3 の別名 |
| B. `FSCTL_ENUM_USN_DATA` | 名前・親・属性（**サイズなし**） | 管理者 | サイズを1件ずつ補うと非常に遅い（134万件で約120秒の報告） |
| **C. `FSCTL_QUERY_FILE_LAYOUT`（採用）** | 名前（複数＝ハードリンク、短縮名の印つき）・親のファイル参照番号・属性・ストリームごとの論理サイズと確保サイズ・作成/更新日時 | 管理者（正確な必要条件は要実機確認） | 出力の入れ子の読み解き、Windows 8.1 以降・NTFS 専用、速さは未公表で実測が必要 |

- **C で必要な指定**: `QUERY_FILE_LAYOUT_INCLUDE_NAMES | INCLUDE_STREAMS | INCLUDE_STREAMS_WITH_NO_CLUSTERS_ALLOCATED | INCLUDE_EXTRA_INFO`。**小さなファイル（目録の中に実体が入るもの）を取りこぼさないために「クラスタを持たないストリームも含める」が必須**。範囲（extent）の情報は不要（出力が膨らむだけ）
- **名前の印**: `FILE_LAYOUT_NAME_ENTRY_PRIMARY = 1`、`FILE_LAYOUT_NAME_ENTRY_DOS = 2`。**短縮名（DOS）は除く**
- **サイズの意味**: ストリームの `EndOfFile` が論理サイズ、`AllocationSize` が確保量。`FileInfo.Length`（いまの走査が使う値）は無名のストリームの `EndOfFile` に対応する。ADS（名前付きのストリーム）は `FileInfo.Length` に含まれないので、**いまの走査と合わせるには無名のストリームだけを数える**
- **ルートと親**: ファイル参照番号は下位48ビットが目録の索引、上位16ビットが世代の番号。ルートは索引 5。親の世代の番号が食い違うノードは親が作り替えられた「はぐれ」で、到達できないものは除いて件数を記録する
- **NTFS の判定**: `GetVolumeInformation` のファイルシステム名が `NTFS` であることを一次の条件にし、最初の呼び出しが失敗したら通常の走査へ切り替える（C は ReFS 非対応）
- **BitLocker（解除済み）**: 復号は NTFS より下の層で行われるため、ボリュームの生の読み取りでも平文が読める
- **一貫性**: ロックやスナップショットは不要。読んでいる間に目録が変わるため、新しく作られた/消えた項目の取りこぼしは起こりうる（いまの走査も同じ性質）

### 速さの見込み
- WizTree は「1TB の SSD を5秒未満」「速さは目録を読む入出力だけで決まり、ファイル数にほぼ依らない」と説明している（管理者のときだけ）
- 100万ファイルの目録は約1GB。NVMe の連続読み取り（2〜3GB/s）なら読み取りは1秒未満で、解析を含めて数秒の見込み
- C の速さは未公表。**実測して A に切り替えるかを決める**

### 実機で確かめないと決められないこと（設計の最初のタスクで確かめる）
1. C が管理者で動くか。非管理者では期待どおり失敗するか
2. C の速さ（100万ファイル規模）。目標（WizTree 以下）に届くか
3. ストリームの印（圧縮・スパースなど）の値と、無名のストリームの見分け方
4. 無名のストリームの `EndOfFile` が `FileInfo.Length` と一致するか（リパースポイント、クラウドの置き換えファイル、目録の中に実体があるファイルで確かめる）
5. 対象がフォルダのときの絞り込みで、ボリューム全体を列挙してから絞るのが十分速いか

## Architecture Pattern Evaluation

| Option | Description | Strengths | Risks / Limitations | Notes |
|---|---|---|---|---|
| **1. 走査の方式を選ぶ層を足し、MFT の経路を並べる（採用）** | 走査の入口で方式を決め、MFT の経路と通常の経路のどちらかで同じ形の木を作る | 通常の走査に一切触らない。失敗したら切り替えられる | 方式ごとに木を作る処理が2つになる | `scan-performance` が決めた木の規約を両方が守る |
| 2. 通常の走査の中に MFT の経路を差し込む | `DirectoryWalker` の中で分岐 | 部品が増えない | 通常の走査（速度の作り込み済み）に手を入れることになり、回帰の危険が大きい | 採らない |
| 3. 先に目録を読んでから通常の走査で補う | 併用 | — | 二重の作業で遅く、意味も分かりにくい | 採らない |

## Design Decisions

### Decision: 一次の方式は `FSCTL_QUERY_FILE_LAYOUT`（生の MFT の解析は二次）
- **Context**: 要件1（速さと全件）、要件4（集計値の意味）。生の解析は落とし穴が多い
- **Selected**: 文書化された `FSCTL_QUERY_FILE_LAYOUT` を使う。実現性の確認（最初のタスク）で速さが目標に届かない、または必要な情報が取れないと分かった場合に限り、生の解析を検討する（その判断は記録に残す）
- **Rationale**: 名前・親・サイズ・ハードリンクの別名・短縮名の印が1回の列挙で取れる。補正や断片化の扱いが不要で、壊れにくい
- **Trade-offs**: Windows 8.1 以降・NTFS 専用（アプリの対象は Windows 10 以降なので問題にならない）。速さは未公表

### Decision: 集計値は「いまの走査と同じ意味」に合わせる
- **Selected**: 無名のストリームの論理サイズ（`EndOfFile`）を数える。ADS（名前付きのストリーム）は数えない。物理サイズ換算はいまと同じ式（クラスタの大きさへの切り上げ）で行う
- **Rationale**: 要件4.4・4.5（同じファイルに同じ大きさ、換算も同じ）。`FileInfo.Length` は無名のストリームの論理サイズに対応する
- **Trade-offs**: ADS を持つファイルは、ディスクの実使用量より小さく出る（いまの走査と同じ性質）

### Decision: 数える対象の違いは「アクセス権の無い場所」と「はぐれ」だけにする
- **Selected**: 予約レコード（目録そのものなど）と短縮名は除く。ハードリンクは名前ごとに数える。到達できない「はぐれ」は結果に含めず件数を記録する
- **Rationale**: 要件4.1・4.2・4.6。通常の走査との差を説明できる形に限定する

### Decision: 管理者の扱い（2026-09-24 の利用者の判断）
- **Selected**: マニフェストで管理者を要求しない。走査のたびに「管理者で開き直すか」を尋ねる。**管理者になれない利用者には尋ねない**。設定で「起動時に管理者で開く」を選べる（既定は無効）。尋ねるのをやめる設定も持つ
- **Rationale**: 管理者権限を持たないアカウントでも起動できるようにする（要件3.8）。将来のドラッグ＆ドロップを妨げない
- **Follow-up**: 昇格して開き直すときに走査の対象を引数で渡し、開き直した直後にその走査を始める（いまの「管理者として開き直す」メニューは対象を引き継がない）

### Decision: MFT の走査の検証は、昇格していない環境では飛ばす
- **Context**: 自動の試験（`dotnet test`）と検証ツールは管理者ではない状態で走る。MFT の経路は管理者でしか動かない
- **Selected**: 自己検証の項目は「管理者でなければ、その理由を残して飛ばす（成功にはしない）」形にする。管理者での確認は、利用者が管理者のコマンドプロンプトから検証ツールと計測の道具を走らせる手順を用意する
- **Rationale**: 開発の自動の検証を壊さず、確かめたことの記録を残せる

## Risks & Mitigations
- **`FSCTL_QUERY_FILE_LAYOUT` が期待どおり動かない/遅い** → 最初のタスクを実現性の確認にし、届かない場合は生の解析の検討（またはこのフィーチャーの見直し）を記録して判断する
- **集計値がいまの走査とずれる** → 同じ対象を両方の方式で走査して突き合わせる検証を用意する。差が出るのは「アクセス権の無い場所」だけであることを確かめる
- **管理者で動くコードの危険** → ボリュームは読み取りだけで開き、書き込みの呼び出しを使わない。得た名前や構成は結果とログ以外に残さない
- **昇格のダイアログが煩わしい** → 尋ねない設定と、起動時に昇格する設定を用意する。管理者になれない利用者には尋ねない
- **走査の方式が増えることによる混乱** → 完了時にどちらの方式で走査したかを示す。ハードリンクにより合計がディスクの実使用量より大きく出ることを記載する

## References
- [FSCTL_QUERY_FILE_LAYOUT](https://learn.microsoft.com/en-us/windows-hardware/drivers/ifs/fsctl-query-file-layout)
- [QUERY_FILE_LAYOUT_INPUT](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntifs/ns-ntifs-_query_file_layout_input)
- [FILE_LAYOUT_ENTRY](https://microsoft.github.io/windows-docs-rs/doc/windows/Win32/System/Ioctl/struct.FILE_LAYOUT_ENTRY.html) / [FILE_LAYOUT_NAME_ENTRY](https://microsoft.github.io/windows-docs-rs/doc/windows/Win32/System/Ioctl/struct.FILE_LAYOUT_NAME_ENTRY.html) / [STREAM_LAYOUT_ENTRY](https://microsoft.github.io/windows-docs-rs/doc/windows/Win32/System/Ioctl/struct.STREAM_LAYOUT_ENTRY.html)
- [FSCTL_GET_NTFS_VOLUME_DATA](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ni-winioctl-fsctl_get_ntfs_volume_data) / [NTFS_VOLUME_DATA_BUFFER](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ns-winioctl-ntfs_volume_data_buffer)
- [FSCTL_ENUM_USN_DATA](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ni-winioctl-fsctl_enum_usn_data) / [USN_RECORD_V2](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ns-winioctl-usn_record_v2)
- [ACLs and the device stack](https://learn.microsoft.com/en-us/previous-versions/windows/drivers/storage/acls-and-the-device-stack)
- [GetVolumeInformation](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getvolumeinformationw)
- [NTFS の資料（ファイルレコード・属性・名前空間・補正）](https://flatcap.github.io/linux-ntfs/ntfs/concepts/file_record.html)
- [WizTree について](https://diskanalyzer.com/about) / [WizTree の FAQ](https://diskanalyzer.com/faq)
- [Everything の FAQ](https://www.voidtools.com/faq/)
- [MFT を直接読む実装の記事（USN 方式との比較の実測）](https://dev.to/jearry/why-i-replaced-everything-with-a-homegrown-mft-search-engine-memory-hundreds-of-mb-a-few-mb-p87)
- 参考にできる実装（取り込まず方式の確認のみ）: [NtfsLib（MIT）](https://github.com/LordMike/NtfsLib)、[DiscUtils（MIT）](https://github.com/DiscUtils/DiscUtils)
