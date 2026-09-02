# Security Check Report Format

This file defines the standard report structure used by all three security gates
(gate-1-plan / gate-2-test / gate-3-review). When generating a report, follow
every section, template, and rule described here exactly.

---

## Report Structure

Generate the report as a Markdown document with the following sections **in order**.

---

### 1. Header (Req 5.1)

```markdown
# セキュリティチェックレポート — {gate} ゲート

**フィーチャー**: {feature-name}
**実行日時**: {ISO 8601 timestamp, e.g. 2026-05-29T14:30:00+09:00}
**最終判定**: {verdict}
**ネットワーク通信**: {network-status}
```

Placeholder rules:
- `{gate}` — one of `plan`, `test`, `review` (the gate that was executed)
- `{feature-name}` — the kebab-case feature name passed as the skill argument
- `{ISO 8601 timestamp}` — current date/time in ISO 8601 format including timezone offset
- `{verdict}` — exactly one of the four verdict tokens below:
  - Gate plan / test → `✅ PASS` or `❌ FAIL`
  - Gate review     → `✅ GO`   or `❌ NO-GO`
- `{network-status}` — exactly one of:
  - `通信あり` — if any network communication was declared or detected
  - `通信なし` — if no network communication was declared or detected

---

### 2. Check Summary Table (Req 5.4)

Immediately after the header, render a summary table that lists every check item
executed during this gate run and its individual result.

```markdown
## チェックサマリー

| チェック項目 | 結果 |
|------------|------|
| {check-name} | {result-icon} |
```

Result icon rules:
- `✅` — check passed with no issues
- `❌` — check failed (one or more CRITICAL or HIGH findings)
- `⚠️` — check passed with warnings (MEDIUM or LOW findings only)

Include one row per check item. Use the gate-specific check names defined in the
corresponding gate rule file (gate-1-plan.md, gate-2-test.md, gate-3-review.md).

---

### 3. Findings Section (Req 5.2)

```markdown
## 検出結果
```

#### 3a. When findings exist

For each finding, render a subsection using this template:

```markdown
### {finding-title}

- **重大度**: {severity}
- **検出箇所**: {location}
- **説明**: {description}
- **修正ガイダンス**: {guidance}
```

Placeholder rules:
- `{finding-title}` — a short, descriptive title for the issue (in Japanese)
- `{severity}` — exactly one of the five severity levels (see Severity Classification below)
- `{location}` — file path (e.g., `src/api/client.py:42`) or design section name
  (e.g., `design.md § 外部通信設計`) where the issue was detected
- `{description}` — a clear explanation of what the problem is and why it is a risk
- `{guidance}` — concrete, actionable steps the developer can take to fix the issue

Order findings from highest to lowest severity: CRITICAL → HIGH → MEDIUM → LOW → INFO.

#### 3b. When zero findings (Req 5.3)

If no issues were detected across all checks, render the following message instead
of individual finding subsections:

```markdown
問題は検出されませんでした ✅
```

This message is **mandatory** when the finding count is zero. Do not omit it or
replace it with a blank section.

---

### 4. Severity Classification

Use the following five-level severity scale for every finding:

| Severity | Label    | Meaning |
|----------|----------|---------|
| 1 (highest) | `CRITICAL` | Immediate exploitability or confirmed secret/credential exposure. Causes automatic FAIL/NO-GO. |
| 2 | `HIGH` | Significant risk that is likely to be exploited; requires urgent remediation. Causes FAIL/NO-GO when combined with CRITICAL. |
| 3 | `MEDIUM` | Moderate risk; should be fixed but does not block the gate on its own. |
| 4 | `LOW` | Minor risk or deviation from best practice; low exploitation likelihood. |
| 5 (lowest) | `INFO` | Informational observation. No action required; no impact on verdict. |

Gate-level verdict rules (from gate rule files):
- **plan gate**: CRITICAL or HIGH ≥ 1 → FAIL; otherwise PASS
- **test gate**: CRITICAL ≥ 1 → FAIL; HIGH or below → PASS with warnings
- **review gate**: CRITICAL or HIGH ≥ 1 → NO-GO; otherwise GO

---

### 5. Next Action Section (Req 5.5, 5.6, 6.2, 6.3)

```markdown
## 次のアクション
```

#### 5a. When verdict is PASS (gate: plan or test)

```markdown
✅ ゲートを通過しました。次フェーズへ進行できます。

推奨コマンド: `{next-recommended-command}`
```

Recommended command per gate:
- After `plan` PASS → `/kiro-impl {feature-name}`
- After `test` PASS → `/ccx-security-check {feature-name} review`

#### 5b. When verdict is GO (gate: review)

`ccx-build-package` is a separate, optional skill that may not be installed in every environment. Check whether `.claude/skills/ccx-build-package/SKILL.md` exists (Glob) before choosing the wording below.

If it exists:
```markdown
✅ ゲートを通過しました。配布パッケージの作成に進行できます。

推奨コマンド: `/ccx-build-package`
```

If it does not exist:
```markdown
✅ ゲートを通過しました。配布物の作成・パッケージングに進行できます。
```

#### 5c. When verdict is FAIL (gate: plan or test)

```markdown
❌ ゲートが失敗しました。以下の問題を修正してから再実行してください。

- CRITICAL: {critical-count} 件
- HIGH: {high-count} 件

修正後の再実行コマンド: `/ccx-security-check {feature-name} {gate}`
```

Placeholder rules:
- `{critical-count}` — number of CRITICAL findings in this run (integer ≥ 0)
- `{high-count}` — number of HIGH findings in this run (integer ≥ 0)
- Highlight these counts so the developer immediately sees the scope of required fixes.

#### 5d. When verdict is NO-GO (gate: review)

```markdown
❌ 配布前チェックが失敗しました。配布パッケージの作成を進めないでください。
以下の問題を修正してから再実行してください。

- CRITICAL: {critical-count} 件
- HIGH: {high-count} 件

修正後の再実行コマンド: `/ccx-security-check {feature-name} review`
```

---

### 6. Report Save Path (Req 5.6)

After generating the report content, save it to:

```
.kiro/specs/{feature-name}/security-report-{gate}.md
```

Use the Write tool. Overwrite if the file already exists (re-run scenario).
Confirm the save path in a short message after writing:

```
レポートを保存しました: .kiro/specs/{feature-name}/security-report-{gate}.md
```

---

## Complete Example (FAIL case)

The following shows what a fully-rendered report looks like for a failed plan gate:

```markdown
# セキュリティチェックレポート — plan ゲート

**フィーチャー**: my-tool
**実行日時**: 2026-05-29T14:30:00+09:00
**最終判定**: ❌ FAIL
**ネットワーク通信**: 通信あり

## チェックサマリー

| チェック項目 | 結果 |
|------------|------|
| 外部通信の宣言 | ✅ |
| 認証情報のリスク | ❌ |
| 入力検証設計 | ⚠️ |
| 攻撃ベクター考慮 | ✅ |

## 検出結果

### APIキーのハードコードを示す設計記述

- **重大度**: CRITICAL
- **検出箇所**: design.md § 外部API連携
- **説明**: design.md の「外部API連携」セクションに `api_key = "sk-..."` 形式でAPIキーをコードに直接記述する設計が含まれています。これは認証情報の漏洩リスクを生じさせます。
- **修正ガイダンス**: 環境変数（例: `os.getenv("API_KEY")`）または秘密管理サービスを使用するよう設計を修正してください。ハードコードされた認証情報をコードに含めないでください。

### 入力エスケープ設計の不足

- **重大度**: MEDIUM
- **検出箇所**: design.md § ユーザー入力処理
- **説明**: ユーザー入力の受け取り処理が設計に記載されていますが、出力エスケープの方針が明示されていません。
- **修正ガイダンス**: 出力先（HTML, SQL, コマンドライン等）に応じたエスケープ処理の方針を design.md に追記してください。

## 次のアクション

❌ ゲートが失敗しました。以下の問題を修正してから再実行してください。

- CRITICAL: 1 件
- HIGH: 0 件

修正後の再実行コマンド: `/ccx-security-check my-tool plan`
```

---

## Complete Example (PASS / zero findings case)

```markdown
# セキュリティチェックレポート — plan ゲート

**フィーチャー**: my-tool
**実行日時**: 2026-05-29T15:00:00+09:00
**最終判定**: ✅ PASS
**ネットワーク通信**: 通信なし

## チェックサマリー

| チェック項目 | 結果 |
|------------|------|
| 外部通信の宣言 | ✅ |
| 認証情報のリスク | ✅ |
| 入力検証設計 | ✅ |
| 攻撃ベクター考慮 | ✅ |

## 検出結果

問題は検出されませんでした ✅

## 次のアクション

✅ ゲートを通過しました。次フェーズへ進行できます。

推奨コマンド: `/kiro-impl my-tool`
```
