---
name: ccx-security-check
description: Add a three-stage security gate (plan/test/review) to the cc-sdd workflow, detecting API-key leaks, unnecessary external communication, dependency vulnerabilities, and PII leaks, and generating a structured report. トリガー例:「セキュリティチェックして」「APIキー漏洩を確認」「配布前のセキュリティレビュー」「脆弱性を検査して」「セキュリティゲート」
allowed-tools: Read, Grep, Glob, Write, WebSearch, WebFetch, Bash
argument-hint: <feature-name> <gate>
---

# ccx-security-check

## Overview

ccx-security-check adds a three-stage security gate to the cc-sdd workflow. For tool
development intended for internal or external distribution, it runs AI security validation at
each checkpoint: at design time, after implementation, and before distribution.

**Gates:**
- `plan`  — Gate 1: design-time security analysis (targets design.md)
- `test`  — Gate 2: post-implementation code scan (secrets / communication / dependency vulnerabilities)
- `review` — Gate 3: pre-distribution final code review (data collection / PII / licenses)

## Usage

```
/ccx-security-check {feature-name} {gate}

例:
  /ccx-security-check my-tool plan
  /ccx-security-check my-tool test
  /ccx-security-check my-tool review
```

**Arguments:**
- `feature-name` — the kebab-case name matching `.kiro/specs/{feature}/`
- `gate` — one of `plan` | `test` | `review`

## Execution flow

1. Parse and validate arguments
2. Confirm `.kiro/specs/{feature}/spec.json` exists
3. Check gate-specific prerequisite files
4. Read `rules/gate-{N}-{gate}.md`
5. Reference `rules/report-format.md` to confirm the report structure
6. Run the gate-specific analysis/scan
7. Save the result to `.kiro/specs/{feature}/security-report-{gate}.md`
8. Display the verdict (PASS/FAIL or GO/NO-GO) and next step

## Argument error handling

- `feature-name` omitted: display usage and stop
- `gate` omitted or invalid: display the valid gate list (plan/test/review) and stop
- `spec.json` absent: prompt `/kiro-spec-init` and stop with an error
- `design.md` absent (Gate 1/2): prompt `/kiro-spec-design {feature}` and stop with an error

## Gate prerequisites

| Gate   | Required files | Recommended phase |
|--------|-------------|-------------|
| plan   | `.kiro/specs/{feature}/design.md` | after kiro-spec-design |
| test   | `.kiro/specs/{feature}/design.md` + source code | after kiro-impl |
| review | source code (Gate 2 recommended) | before distribution packaging |

## Rule files

- `rules/gate-1-plan.md`  — Gate 1: design-time security analysis checklist
- `rules/gate-2-test.md`  — Gate 2: code-scan rules (secrets / communication / dependencies)
- `rules/gate-3-review.md` — Gate 3: pre-distribution final review rules
- `rules/report-format.md` — report format definition shared across all gates

## Output

- **Console**: the verdict (PASS/FAIL or GO/NO-GO) and next-step guidance
- **Files**: `.kiro/specs/{feature}/security-report-{gate}.md`

---

## Step 1: Argument Parsing and Prerequisite Checks

### 1.1 Parse Arguments

Extract two positional arguments from the skill invocation:
- **1st argument** → `feature-name` (kebab-case, e.g. `my-tool`)
- **2nd argument** → `gate` (one of: `plan`, `test`, `review`)

### 1.2 Validate feature-name

If `feature-name` is missing (no first argument provided), display the following usage message and **stop immediately**:

```
使用方法: /ccx-security-check <feature-name> <gate>

  feature-name  — .kiro/specs/{feature}/ に対応するkebab-caseの名前
  gate          — plan | test | review

例:
  /ccx-security-check my-tool plan
  /ccx-security-check my-tool test
  /ccx-security-check my-tool review
```

Do not proceed further.

### 1.3 Validate gate

If `gate` is missing or is not one of `plan`, `test`, or `review`, display the following usage message and **stop immediately**:

```
使用方法: /ccx-security-check <feature-name> <gate>

  gate には以下のいずれかを指定してください:

  plan    — Gate 1: 設計時セキュリティ分析（design.md を対象）
              必要ファイル: design.md
              推奨フェーズ: /kiro-spec-design 実行後

  test    — Gate 2: 実装後コードスキャン（シークレット/通信/依存脆弱性）
              必要ファイル: design.md + ソースコード
              推奨フェーズ: /kiro-impl 完了後

  review  — Gate 3: 配布前最終コードレビュー（データ収集/PII/ライセンス）
              必要ファイル: ソースコード（design.md 推奨）
              推奨フェーズ: 配布パッケージ作成前

例:
  /ccx-security-check my-tool plan
  /ccx-security-check my-tool test
  /ccx-security-check my-tool review
```

Do not proceed further.

### 1.4 Show Startup Header

After argument validation passes, display the startup header before running any checks:

```
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
 ccx-security-check — <feature-name> / <gate> ゲート
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

ゲート前提条件:
| ゲート  | 必要なファイル                              | 推奨フェーズ                     |
|--------|----------------------------------------|-------------------------------|
| plan   | design.md                              | /kiro-spec-design 実行後        |
| test   | design.md + ソースコード                  | /kiro-impl 完了後               |
| review | ソースコード（design.md 推奨）              | 配布パッケージ作成前             |

実行ゲート: <gate>
対象フィーチャー: <feature-name>
```

Replace `<feature-name>` and `<gate>` with the actual parsed values.

### 1.5 Check spec.json Existence

Use the Read tool (or Glob) to check whether `.kiro/specs/<feature-name>/spec.json` exists.

If the file does **not** exist, display the following error and **stop immediately**:

```
エラー: .kiro/specs/<feature-name>/spec.json が見つかりません。
先に `/kiro-spec-init <feature-name>` を実行してスペックを初期化してください。
```

Do not proceed further.

### 1.6 Gate-Specific Prerequisite Checks

#### For gate = `plan`

Check whether `.kiro/specs/<feature-name>/design.md` exists.

If it does **not** exist, display the following error and **stop immediately**:

```
エラー: .kiro/specs/<feature-name>/design.md が見つかりません。
plan ゲートには設計書が必要です。先に `/kiro-spec-design <feature-name>` を実行してください。
```

Do not proceed further.

#### For gate = `test`

Check whether `.kiro/specs/<feature-name>/design.md` exists.

If it does **not** exist, display the following error and **stop immediately**:

```
エラー: .kiro/specs/<feature-name>/design.md が見つかりません。
test ゲートには設計書が必要です。先に `/kiro-spec-design <feature-name>` を実行してください。
```

Do not proceed further.

#### For gate = `review`

Check whether `.kiro/specs/<feature-name>/security-report-test.md` exists.

If it does **not** exist, display the following warning and **continue** (do not stop):

```
⚠️  警告: Gate 2（test）がまだ実行されていません（security-report-test.md が未存在）。
    結果が不完全になる可能性があります。続行しますが、先に `/ccx-security-check <feature-name> test` の実行を推奨します。
```

Then proceed with the review gate analysis.

### 1.7 Proceed to Step 2

Once all prerequisite checks pass (or in the case of `review`, after displaying any warning), proceed to **Step 2: Gate Routing and Execution** (task 4.2).

---

## Step 2: Gate Routing and Execution

### 2.1 Route to Gate Rules

Based on the validated `gate` argument, load the corresponding gate rules file using the Read tool:

- `plan`   → Read `.claude/skills/ccx-security-check/rules/gate-1-plan.md`
- `test`   → Read `.claude/skills/ccx-security-check/rules/gate-2-test.md`
- `review` → Read `.claude/skills/ccx-security-check/rules/gate-3-review.md`

Apply the rules loaded from the file. Do **not** hardcode gate analysis logic directly in SKILL.md.

### 2.2 Load Input Files

Load the input files required for each gate:

**For `plan`:**
- Read `.kiro/specs/<feature-name>/design.md`

**For `test`:**
- Read `.kiro/specs/<feature-name>/design.md` (for network declaration comparison)
- Search `design.md` for a `## File Structure Plan` heading (see **2.2a** below for the fallback if it is missing), then Read each identified source file

**For `review`:**
- Apply the same File Structure Plan detection as `test` (see **2.2a**) to identify source files
- Read `.kiro/specs/<feature-name>/design.md`
- Optionally read `.kiro/specs/<feature-name>/security-report-test.md` if it exists (for Gate 2 findings reference)

If `design.md` is longer than 500 lines, Read it in sections to avoid truncation.

### 2.2a Handle Missing File Structure Plan

The source-file discovery in `test` and `review` depends on `design.md` containing a `## File Structure Plan` heading (this is the cc-sdd design.md template's standard section). This is an external format contract, not something this skill controls — the heading name or structure may change if the design.md template changes.

- If the `## File Structure Plan` heading **is found**: identify source files from its contents as normal.
- If the heading **is not found**: do not stop the gate. Instead:
  1. Record a finding with severity `LOW`, location `design.md`, description `design.md に「File Structure Plan」セクションが見つかりませんでした。ソースファイルを自動特定できません（design.md のフォーマットが想定と異なる可能性があります）`, and guidance `対象ソースファイルを手動で確認するか、design.md に File Structure Plan セクションを追加してください`.
  2. Continue the gate using whatever files can reasonably be inferred (e.g. via Glob on common source directories), and proceed with the network/secret/dependency checks that do not require the file list.

### 2.3 Execute Gate Analysis

Apply the rules loaded in Step 2.1 to the input files loaded in Step 2.2.

Generate a `findings` list. Each finding must include:
- **重大度 (severity)**: `CRITICAL` / `HIGH` / `MEDIUM` / `LOW` / `INFO`
- **検出箇所 (location)**: file path or design section name
- **説明 (description)**: description of the problem
- **修正ガイダンス (guidance)**: concrete remediation steps

If no issues are found, record: 「問題は検出されませんでした」

### 2.4 Build and Save Report (Req 5.6)

1. Read `.claude/skills/ccx-security-check/rules/report-format.md` to load the report template structure.
2. Construct the full Markdown report using the template, filling in:
   - Gate name, feature name, execution datetime (ISO 8601)
   - Final verdict (PASS/FAIL or GO/NO-GO)
   - Network communication declaration (通信あり / 通信なし)
   - Check summary table
   - All findings
   - Next action guidance (see Step 2.5)
3. Save the completed report using the **Write tool** to the exact path:
   ```
   .kiro/specs/<feature-name>/security-report-<gate>.md
   ```
   If the file already exists, **overwrite** it (re-run scenario).
4. After saving, display the confirmation message:
   ```
   レポートを保存しました: .kiro/specs/<feature-name>/security-report-<gate>.md
   ```

### 2.5 Display Verdict and Next-Step Guidance

#### Determine Verdict

| Gate   | PASS / GO condition              | FAIL / NO-GO condition                 |
|--------|----------------------------------|----------------------------------------|
| plan   | No CRITICAL or HIGH findings     | At least 1 CRITICAL or HIGH finding    |
| test   | No CRITICAL findings             | At least 1 CRITICAL finding            |
| review | No CRITICAL or HIGH findings     | At least 1 CRITICAL or HIGH finding    |

#### PASS / GO Guidance (Req 6.2)

Display the verdict and the recommended next command:

- **After `plan` PASS:**
  ```
  ✅ PASS — セキュリティゲート（plan）を通過しました。
  実装を続けてください: `/kiro-impl <feature-name>`
  ```

- **After `test` PASS:**
  ```
  ✅ PASS — セキュリティゲート（test）を通過しました。
  最終レビューに進んでください: `/ccx-security-check <feature-name> review`
  ```

- **After `review` GO:**
  Check whether `.claude/skills/ccx-build-package/SKILL.md` exists (use Glob). `ccx-build-package` is a separate, optional skill — it may not be present in every environment this skill is installed in.
  - If it exists:
    ```
    ✅ GO — セキュリティゲート（review）を通過しました。
    配布パッケージの作成を進めてください: `/ccx-build-package`
    ```
  - If it does not exist:
    ```
    ✅ GO — セキュリティゲート（review）を通過しました。
    配布物の作成・パッケージングに進んでください。
    ```

#### FAIL / NO-GO Guidance (Req 6.3)

Display the verdict with CRITICAL and HIGH counts prominently, then show the rerun command:

- **After `plan` or `test` FAIL:**
  ```
  ❌ FAIL — セキュリティゲート（<gate>）に失敗しました。
  CRITICAL: <N>件 / HIGH: <N>件
  修正後に再実行してください: `/ccx-security-check <feature-name> <gate>`
  ```

- **After `review` NO-GO:**
  ```
  ❌ NO-GO — セキュリティゲート（review）に失敗しました。配布パッケージの作成を停止してください。
  CRITICAL: <N>件 / HIGH: <N>件
  修正後に再実行してください: `/ccx-security-check <feature-name> review`
  ```

Replace `<N>` with the actual counts from the findings list. Replace `<feature-name>` and `<gate>` with the actual parsed values.
