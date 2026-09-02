---
name: ccx-thirdlicense-gen
description: Detect the licenses of dependency packages, tools, and libraries, and record the attribution-required portions into a THIRD_PARTY_LICENSES file without modification. Targets the whole project (workspace); no feature name is required. Enumerates packages with potential license-conflict concerns and reports them to the developer (it does not propose workarounds or fixes). Proposes an output format (txt/html/md) per distribution shape. トリガー例:「サードパーティライセンスをまとめて」「第三者ライセンス表記を生成」「OSSライセンスを一覧化」「ライセンス表記ファイルを作る」
allowed-tools: Read, Write, Glob, Grep, WebSearch, WebFetch
---

# ccx-thirdlicense-gen

## Overview

ccx-thirdlicense-gen detects dependency-package licenses and records the attribution-required
portions into a `THIRD_PARTY_LICENSES` file without modification. It targets the
**whole project (workspace)** and does not require a specific feature. It can run standalone,
independently of `ccx-build-package`.

**Execution flow:**
1. Detect dependency-package licenses and judge conflict concerns (scan the whole project)
2. Propose the output format (txt/html/md) and confirm with the developer
3. Generate THIRD_PARTY_LICENSES (project root), self-check, final report

## Usage

```
/ccx-thirdlicense-gen

例:
  /ccx-thirdlicense-gen
```

**Arguments:**
- None (this skill targets the whole project)

## Rule files

- `rules/license-detection.md` — dependency scan, license determination, attribution-obligation judgment, conflict-concern detection
- `rules/format-selection.md` — distribution-shape inference, output-format proposal and developer confirmation
- `rules/notice-generation.md` — THIRD_PARTY_LICENSES generation and self-check

## Output

- **Console**: number of detected packages, conflict-concern list, finalized output format, generation result
- **Files**: project-root `THIRD_PARTY_LICENSES.{ext}` (copied into the Release directory when `ccx-build-package` runs)

---

## Step 1: License Detection

This skill takes no feature argument. The dependency scan targets the whole project (the root dependency manifests).

1. Read `.claude/skills/ccx-thirdlicense-gen/rules/license-detection.md`.
2. Apply its rules to discover dependency files, extract packages, determine licenses, judge attribution obligation, and detect license-conflict concerns.
3. Retain the resulting `PackageLicenseInfo[]` (including `concern` entries) for the remaining steps.
4. If the package list is empty, note "サードパーティ依存パッケージなし" and continue — this is not an error condition.

Display a brief interim summary before proceeding:
```
依存パッケージ検出: {N}件
ライセンス種別未確認: {M}件
懸念事項: {K}件
```

---

## Step 2: Format Selection

1. Read `.claude/skills/ccx-thirdlicense-gen/rules/format-selection.md`.
2. Apply the rules to infer the distribution shape (reading a `design.md` from an auto-resolved spec if one is available) and propose an extension.
3. Present the proposal and reasoning to the developer and wait for explicit confirmation (or an alternative extension) before proceeding. **Never finalize the format without this confirmation**, even for unambiguous cases.

---

## Step 3: Notice Generation and Self-Check

1. Read `.claude/skills/ccx-thirdlicense-gen/rules/notice-generation.md`.
2. Apply its rules to render `THIRD_PARTY_LICENSES.{ext}` using the confirmed extension and the `PackageLicenseInfo[]` from Step 1.
3. Run the self-check defined in the rules file. If it finds a violation, regenerate the affected entry before proceeding.
4. Write the result to the project-root `THIRD_PARTY_LICENSES.{ext}`.

---

## Step 4: Final Report

Display:

```
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
 ccx-thirdlicense-gen
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

生成ファイル: THIRD_PARTY_LICENSES.<ext>（プロジェクトルート）

## 検出結果
| パッケージ | バージョン | ライセンス | 表記義務 | 懸念 |
|-----------|----------|-----------|---------|------|
| ...       | ...      | ...       | ...     | ...  |

## ライセンス違反懸念（要開発者判断・回避策の提案はありません）
- {concern-1}
- {concern-2}

（懸念なしの場合: 「懸念事項は検出されませんでした」）

次のアクション: 配布パッケージ作成時（/ccx-build-package）に本ファイルが Release ディレクトリへ組み込まれます。
```

---

## Output Description

Provide output in Japanese:

1. **検出サマリー**: パッケージ数、未確認数、懸念事項数
2. **出力形式の提案と確認結果**
3. **生成結果**: ファイルパス、自己点検の結果
4. **最終レポート**: 検出結果テーブルと懸念事項一覧

---

## Safety & Fallback

### Error Scenarios

- **No dependency files found at all**: continue as "サードパーティ依存パッケージなし" (not an error)
- **License type indeterminable**: record it as "ライセンス種別未確認" and continue processing the other packages
- **Distribution shape indeterminable**: ask the developer directly and wait for the answer before continuing
- **Self-check finds a missing notice or modification**: regenerate the affected entry before emitting (do not hide the defect)

### Boundary Notes

- This skill targets the whole project and takes no feature name. The dependency scan targets the project-root dependency manifests.
- It does not propose workarounds or fixes for license-conflict concerns (report only).
- It does not validate security vulnerabilities (that is `ccx-security-check`'s responsibility).
- It does not generate the README (that is `ccx-readme-gen`'s responsibility).
- It does not orchestrate the whole distribution package, manage versions, or zip (that is `ccx-build-package`'s responsibility).
- Source code and dependency files are read-only. Writes are limited to the project-root `THIRD_PARTY_LICENSES.{ext}`.
