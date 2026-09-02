---
name: ccx-tech-selection
description: Generate a technology selection comparison report for a feature spec, researching candidates per category and producing tech-selection.md with comparison tables, adoption decisions, and a design-integration summary.
allowed-tools: Read, Write, Edit, Glob, WebSearch, WebFetch, Agent
argument-hint: <feature-name>
---

# ccx-tech-selection

## Overview

When a developer runs `/ccx-tech-selection {feature}`, the skill:

1. Validates arguments and prerequisite files
2. Loads the feature context and rule files
3. Identifies technology categories and candidates from `requirements.md`
4. Researches each candidate (version, maintenance, license, known issues)
5. Generates a structured comparison report with adoption decisions
6. Writes `.kiro/specs/{feature}/tech-selection.md` as a standalone reference document

**Target user**: A developer who has completed the requirements phase and wants to
document technology choices before generating `design.md`.

**Output**: `.kiro/specs/{feature}/tech-selection.md` — contains comparison tables,
adoption decisions, rejection reasons, and a Design Integration Summary formatted to be
easy to carry over into a design document's Technology Stack section. `ccx-tech-selection`
does not modify any other skill's behavior — this file is produced purely for the developer
(or whichever design-generation step they use next) to read and apply manually.

## Usage

```
/ccx-tech-selection {feature-name}

Example:
  /ccx-tech-selection my-feature
```

**Arguments:**
- `feature-name` — kebab-case name matching `.kiro/specs/{feature}/` directory

---

## Step 1: Argument Parsing and Prerequisite Checks

### 1.1 Parse the Feature Argument

Extract the single positional argument from the skill invocation:
- **1st argument** → `feature` (kebab-case, e.g. `my-feature`)

### 1.2 Validate Feature Argument Presence

If `feature` is missing (no argument provided), display the following usage message
and **stop immediately**:

```
Usage: /ccx-tech-selection <feature-name>

  feature-name  — The kebab-case name of the feature spec directory
                  (e.g. my-feature → .kiro/specs/my-feature/)

Example:
  /ccx-tech-selection my-feature
```

Do not proceed further. (`ArgumentParseResult.status = "stop"`, `stopReason = "missing-feature-argument"`)

### 1.3 Check spec.json Existence

Use the Glob tool to check whether `.kiro/specs/<feature>/spec.json` exists.

If the file does **not** exist, display the following error and **stop immediately**:

```
Error: .kiro/specs/<feature>/spec.json not found.
Run `/kiro-spec-init <feature>` first to initialize the spec.
```

Replace `<feature>` with the actual parsed feature name.

Do not proceed further. (`ArgumentParseResult.status = "stop"`, `stopReason = "spec-json-not-found"`)

### 1.4 Check requirements.md Existence

Use the Glob tool to check whether `.kiro/specs/<feature>/requirements.md` exists.

If the file does **not** exist, display the following error and **stop immediately**:

```
Error: .kiro/specs/<feature>/requirements.md not found.
Run `/kiro-spec-requirements <feature>` first to generate requirements.
```

Replace `<feature>` with the actual parsed feature name.

Do not proceed further. (`ArgumentParseResult.status = "stop"`, `stopReason = "requirements-not-found"`)

### 1.5 Check Requirements Approval Status

Read `.kiro/specs/<feature>/spec.json` and inspect the `approvals.requirements.approved` field.

If the field is `false` or absent (undefined), display the following warning and **continue**
(do not stop):

```
Warning: Requirements for '<feature>' have not been approved yet
         (approvals.requirements.approved is false or undefined).
         Proceeding anyway — tech-selection.md may need to be regenerated
         after requirements are approved.
```

(`ArgumentParseResult.status = "proceed"`, `warning = "requirements-unapproved"`)

### 1.6 Check for Existing tech-selection.md

Use the Glob tool to check whether `.kiro/specs/<feature>/tech-selection.md` already exists.

If the file **exists**, display the following notice and **continue** (do not stop):

```
Notice: .kiro/specs/<feature>/tech-selection.md already exists and will be overwritten.
```

(`ArgumentParseResult.status = "proceed"`, `notice = "overwrite-existing"`)

### 1.7 Proceed to Step 2

Once all prerequisite checks pass (or after displaying any warnings/notices),
set `ArgumentParseResult.status = "proceed"` and continue to **Step 2: Load Context and Rules**.

---

## Step 2: Load Context and Rules

### 2.1 Load requirements.md

Read the primary input file:

```
Read: .kiro/specs/<feature>/requirements.md
```

This file is the main source for identifying technology categories and understanding
the feature's technical requirements.

### 2.2 Load brief.md (if exists)

Use the Glob tool to check whether `.kiro/specs/<feature>/brief.md` exists.

If it exists, read it:

```
Read: .kiro/specs/<feature>/brief.md
```

`brief.md` is optional supplementary context. If it does not exist, skip this step
and continue without it. Do not error or warn on absence.

### 2.3 Load Steering Tech Context

Read the project-wide technology steering file:

```
Read: .kiro/steering/tech.md
```

This file contains technology constraints and pre-confirmed choices. Any technology
explicitly mandated here must be treated as `confirmed` (no comparison needed).

### 2.4 Load Rules Files

Read both rule files that govern skill behavior:

```
Read: .claude/skills/ccx-tech-selection/rules/comparison-format.md
Read: .claude/skills/ccx-tech-selection/rules/research-process.md
```

- `comparison-format.md` — defines the exact output format for `tech-selection.md`
  (section structure, table columns, special-case formats, Design Integration Summary)
- `research-process.md` — defines the research methodology for Step 4
  (query templates, source priority, 4-item collection rule, parallel dispatch policy,
  failure handling)

Apply these rules in all subsequent steps. Do **not** hardcode format or research
logic directly in this SKILL.md beyond what is described here.

### 2.5 Proceed to Step 3

With all context loaded, continue to **Step 3: Identify Technology Categories**.

---

## Step 3: Identify Technology Categories

### 3.1 Define the Three Target Categories

Analyze the loaded context (requirements.md, optional brief.md, and tech.md) against
the following three fixed categories:

| Category Name | What to Look For |
|--------------|-----------------|
| `言語` (Language) | Implementation language — runtime, scripting, or compile target (e.g. Python, TypeScript, Go) |
| `フレームワーク` (Framework) | Application or service framework (e.g. FastAPI, Next.js, Spring Boot) |
| `ライブラリ` (Library) | Key packages/libraries central to the feature's functionality (e.g. Pydantic, Prisma, Axios) |

Produce a `CategoryIdentificationResult` object (defined at the end of this step)
by evaluating each category in sequence.

### 3.2 Check tech.md for Pre-Confirmed Choices

For each of the three categories, inspect the content loaded from `.kiro/steering/tech.md`:

- If the technology for that category is **explicitly stated** in tech.md
  (e.g., "Use Python 3.12", "Framework: FastAPI", "Required library: SQLAlchemy"),
  mark the category as **`confirmed`**:
  - `status: "confirmed"`
  - `confirmedChoice: <technology name and version as stated in tech.md>`
  - `candidates: null`
  - `skipReason: null`
  - Display: `[Tech Selection] <CategoryName>: confirmed by tech.md → <confirmedChoice>. Skipping comparison.`

Do **not** research or compare confirmed categories. Proceed directly to the next category.

### 3.3 Identify Candidates from requirements.md

For each category **not** already `confirmed`, read the requirements.md content
(and brief.md if loaded) to enumerate candidate technologies.

**Identification rules:**
- Candidates may be explicitly named (e.g., "use React or Vue") or inferable from
  described constraints (e.g., "must run on Node.js" implies a JavaScript/TypeScript language candidate).
- Include only technologies that are realistically usable for this category and project context.
- Collect **at most 4 candidates** per category. If more than 4 are mentioned, select the
  4 most relevant based on prominence in requirements.md and alignment with the project context.

**Status assignment based on candidate count:**

| Candidate Count | Assigned Status | Notes |
|----------------|----------------|-------|
| 2 – 4 | `needs-comparison` | Full comparison in Step 4 |
| 1 | `confirmed` | Single option; set `confirmedChoice` to that option, `candidates: null` |
| 0 | `skipped-no-candidates` | No candidates identifiable from requirements.md |

For `needs-comparison` categories:
- `status: "needs-comparison"`
- `candidates: [<up to 4 candidate names>]`
- `confirmedChoice: null`
- `skipReason: null`

For `skipped-no-candidates` categories:
- `status: "skipped-no-candidates"`
- `candidates: null`
- `confirmedChoice: null`
- `skipReason: "<CategoryName>: 比較対象候補が特定できませんでした（requirements.md に技術要件の記載なし）"`
- Display: `[Tech Selection] <CategoryName>: no candidates identified. Skipping.`

### 3.4 Determine the Overall Mode

After evaluating all three categories, determine the overall `mode` of the
`CategoryIdentificationResult`:

- **`confirmation-only`**: Every category has `status` of either `confirmed` or
  `skipped-no-candidates`. There are **zero** `needs-comparison` categories.
  - Display:
    ```
    技術スタックは steering により確定済みです。確認レポートを生成します。
    (All technology categories are already confirmed. Generating confirmation report.)
    ```
  - In this mode, **skip Step 4 (Research Engine)** entirely and proceed directly to
    Step 5 (Report Generator) in confirmation-only mode.

- **`comparison`**: At least one category has `status: "needs-comparison"`.
  - Proceed normally to Step 4 to research those candidates.
  - `confirmed` and `skipped-no-candidates` categories are excluded from Step 4 research.

### 3.5 CategoryIdentificationResult Contract

The output of this step is a `CategoryIdentificationResult` object used by Step 4
(Research Engine) and Step 5 (Report Generator):

```
CategoryIdentificationResult:
  mode: "comparison" | "confirmation-only"
  categories: Array of:
    name: "言語" | "フレームワーク" | "ライブラリ"
    status: "needs-comparison" | "confirmed" | "skipped-no-candidates"
    confirmedChoice: string | null    // non-null only when status = "confirmed"
    candidates: Array<string> | null  // non-null only when status = "needs-comparison" (max 4 items)
    skipReason: string | null         // non-null only when status = "skipped-no-candidates"
```

**Status decision matrix:**

| Condition | status | confirmedChoice | candidates | skipReason |
|-----------|--------|----------------|------------|------------|
| Explicitly mandated in tech.md | `confirmed` | value from tech.md | `null` | `null` |
| Single candidate from requirements.md | `confirmed` | that candidate name | `null` | `null` |
| 2–4 candidates from requirements.md, not in tech.md | `needs-comparison` | `null` | array of names | `null` |
| No candidates identifiable | `skipped-no-candidates` | `null` | `null` | skip message string |

**Mode decision rule:**

| Category statuses | mode |
|-------------------|------|
| All `confirmed` or `skipped-no-candidates` | `confirmation-only` |
| At least one `needs-comparison` | `comparison` |

### 3.6 Proceed to Step 4 (or Step 5 if confirmation-only)

- If `mode = "comparison"`: pass the `CategoryIdentificationResult` to
  **Step 4: Research Candidates** for all `needs-comparison` categories.
- If `mode = "confirmation-only"`: skip Step 4 entirely and pass the
  `CategoryIdentificationResult` directly to **Step 5: Generate Comparison Report**.

---

## Step 4: Research Candidates

> **Scope**: This step is executed only when `mode = "comparison"` (i.e., at least one
> category has `status: "needs-comparison"`). If `mode = "confirmation-only"`, skip
> this step entirely and proceed to Step 5.

### 4.1 Determine Dispatch Strategy

Inspect the `CategoryIdentificationResult` from Step 3 and count only the categories
whose `status` is `"needs-comparison"`. **Do not count `confirmed` or
`skipped-no-candidates` categories.**

Select the dispatch strategy from the table below:

| Condition | Dispatch Strategy |
|-----------|------------------|
| `needs-comparison` count ≥ 2 | One Agent subagent **per category** in parallel |
| `needs-comparison` count = 1 AND candidate count ≥ 2 | One Agent subagent **per candidate** in parallel |
| `needs-comparison` count = 1 AND candidate count = 1 | No subagent — research inline |

This table is the authoritative policy. Refer to `research-process.md §Parallel Dispatch Policy`
for the full normative definition and edge-case notes (already loaded in Step 2).

### 4.2 Dispatch Research Subagents (or Inline Research)

#### Case A — Category-level parallel (needs-comparison count ≥ 2)

Use the `Agent` tool to dispatch one subagent for each `needs-comparison` category
**simultaneously** (all Agent calls in a single batch):

```
For each needs-comparison category C:
  Agent task:
    "Research all candidates for the {C.name} category.
     Candidates: {C.candidates}
     Follow the query templates, source priority order, and 4-item collection rule
     defined in the research-process.md that was loaded in Step 2.
     Return one CandidateResearchResult per candidate."
```

Each subagent is responsible for researching every candidate in its assigned category.
Collect all results before proceeding to step 4.3.

#### Case B — Candidate-level parallel (needs-comparison count = 1 AND candidate count ≥ 2)

Use the `Agent` tool to dispatch one subagent for each candidate in the single
`needs-comparison` category **simultaneously**:

```
For each candidate X in the single needs-comparison category:
  Agent task:
    "Research the candidate '{X}' in the '{categoryName}' category.
     Follow the query templates, source priority order, and 4-item collection rule
     defined in the research-process.md that was loaded in Step 2.
     Return one CandidateResearchResult."
```

Collect all results before proceeding to step 4.3.

#### Case C — Inline research (needs-comparison count = 1 AND candidate count = 1)

No Agent subagent is needed. Research the single candidate directly using
`WebSearch` and `WebFetch`:

1. Apply the four query templates from `research-process.md §Query Templates`,
   replacing `{name}` with the candidate name.
2. Follow the source priority order from `research-process.md §Source Priority`.
3. Produce one `CandidateResearchResult` directly.

### 4.3 Collect the Four Data Items per Candidate

Whether the research is done in a subagent or inline, each candidate MUST yield
exactly the following four items as defined in `research-process.md §4-Item Collection Rule`:

| # | Field in CandidateResearchResult | Description |
|---|----------------------------------|-------------|
| 1 | `latestVersion` | Latest stable version string (non-pre-release; note pre-release if only option) |
| 2 | `maintenanceStatus` | `"アクティブ"` if any release in the last 6 months; otherwise `"非アクティブ"` |
| 3 | `licenseType` | SPDX identifier (e.g., `MIT`, `Apache-2.0`) or common name for non-standard licenses |
| 4 | `knownIssues` | Plain-text description of adoption-blocking issues, or `"なし"` if none found |

All four items MUST appear in the result. If an item cannot be retrieved, record
`"情報取得不可"` in that field — never leave the field empty or omit it.

### 4.4 Record the Source Reference

For each candidate, record the `sourceReference` field in `CandidateResearchResult`:

- **Preferred**: the full URL of the page used to retrieve the information
  (e.g., `https://pypi.org/project/fastapi/`, `https://react.dev/versions`).
- **Fallback**: the canonical source name when a URL cannot be obtained
  (e.g., `"PyPI"`, `"npmjs.com"`, `"crates.io"`, `"GitHub"`).
- **Last resort**: `"情報取得不可"` only when no source could be reached at all.

Refer to `research-process.md §Source Priority` for the ranked list of preferred
sources and the recording rule.

### 4.5 Apply Failure Handling

If any individual data item cannot be retrieved for a candidate, apply the rules
defined in `research-process.md §Failure Handling`:

1. Record `"情報取得不可"` in the affected field of `CandidateResearchResult`.
2. Continue researching all remaining items for the same candidate (do not abort).
3. Continue researching all other candidates (do not abort the category or skill).
4. Retry once with an alternate query or source before recording unavailable.
5. Never invent or estimate a value — if data is unavailable, use `"情報取得不可"`.

A candidate with one or more `"情報取得不可"` fields is still a valid
`CandidateResearchResult` and MUST be included in the output passed to Step 5.

### 4.6 Handle confirmation-only Mode: Version Supplement for Confirmed Categories

When `mode = "confirmation-only"` (all categories are `confirmed` or
`skipped-no-candidates`), Step 4 is normally skipped entirely. However, apply
the following special procedure **before skipping**:

For each `confirmed` category whose `confirmedChoice` does **not** include a version
number (e.g., tech.md states `"FastAPI"` without specifying `"FastAPI 0.115.x"`):

1. Use `WebSearch` with the query `{confirmedChoice} latest stable version`
   to retrieve the current stable version.
2. If a version is found, store it as `latestVersion` for use in Step 5's
   confirmation report and Design Integration Summary.
3. If the version cannot be retrieved, record `"情報取得不可"` for that field.

**Purpose**: This ensures Requirement 3.1 (collect latest stable version for
confirmed categories) is satisfied even in confirmation-only mode, so the Design
Integration Summary contains actionable version data when carried over into a
design document.

### 4.7 CandidateResearchResult Contract

The output of this step is a list of `CandidateResearchResult` objects, one per
researched candidate. Pass this list to **Step 5: Generate Comparison Report**.

```
CandidateResearchResult:
  categoryName: string                           // e.g., "フレームワーク"
  candidateName: string                          // e.g., "FastAPI"
  latestVersion: string | "情報取得不可"          // e.g., "0.115.5"
  maintenanceStatus: "アクティブ" | "非アクティブ" | "情報取得不可"
  licenseType: string | "情報取得不可"            // e.g., "MIT", "Apache-2.0"
  knownIssues: string | "なし" | "情報取得不可"   // plain-text or sentinel value
  sourceReference: string | "情報取得不可"        // URL or source name (e.g., "PyPI")
```

**Completion criterion**: All `needs-comparison` candidates have a
`CandidateResearchResult` with every field populated (sentinel values are
acceptable). No candidate is silently omitted.

### 4.8 Proceed to Step 5

Pass the full list of `CandidateResearchResult` objects (and the
`CategoryIdentificationResult` from Step 3) to
**Step 5: Generate Comparison Report**.

---

## Step 5: Generate Comparison Report

> **Input**: `CategoryIdentificationResult` from Step 3 and the list of
> `CandidateResearchResult` objects from Step 4 (empty list when
> `mode = "confirmation-only"`).
>
> **Output**: `ReportGeneratorOutput` — the full Markdown string for
> `tech-selection.md` and a boolean flag indicating GPL/LGPL risk.

### 5.1 Build the Report According to comparison-format.md

Construct the complete Markdown document by following every rule defined in
`comparison-format.md` (already loaded in Step 2). The document MUST contain
the sections described below, assembled in the order listed.

#### 5.1.1 Header

Begin the document with the mandatory header as defined in
`comparison-format.md §Header`:

```markdown
# Technology Selection Report: {feature}

- **Feature**: {feature}
- **Generated**: {ISO 8601 datetime, e.g. 2026-05-29T14:30:00+09:00}
```

Replace `{feature}` with the actual feature name parsed in Step 1.
Replace the datetime with the current UTC+local timestamp in ISO 8601 format.

#### 5.1.2 Mode Routing

After the header, route the remaining sections based on `mode`:

- **`comparison` mode** → Render sections 5.1.3 through 5.1.6 in sequence.
- **`confirmation-only` mode** → Render section 5.1.3-alt (Confirmed Technology
  Stack), then skip to section 5.1.6 (Design Integration Summary).

#### 5.1.3 Comparison Table (comparison mode only)

For **each** category whose `status` is `"needs-comparison"`, render a Markdown
comparison table as defined in `comparison-format.md §Comparison Table`:

```markdown
## {Category Name} Comparison

| Candidate | Version | License | Maintenance | Key Advantages | Key Concerns |
|-----------|---------|---------|-------------|---------------|--------------|
| ...       | ...     | ...     | ...         | ...           | ...          |

> Sources: {comma-separated list of URLs or source names, one per candidate}
```

- Populate each row from the `CandidateResearchResult` objects whose
  `categoryName` matches this category.
- `Maintenance` column: display `Active` when `maintenanceStatus = "アクティブ"`,
  `Inactive` when `maintenanceStatus = "非アクティブ"`, and `"情報取得不可"` as-is.
- `Key Advantages` and `Key Concerns`: derive up to three concise bullet points
  from `knownIssues` and the research data. If `knownIssues = "なし"`, leave
  Key Concerns as `—`.
- Sources row: collect the `sourceReference` values for every candidate in this
  category, comma-separated.

**Special Case — Single Candidate**: When a category has exactly one candidate
(`confirmedChoice` is set, `candidates` is null, and the reason is a tech.md
constraint or business mandate), **skip the comparison table** and output:

```markdown
**Pre-selected**: {CandidateName} (Reason: {constraint description})
```

#### 5.1.3-alt Confirmed Technology Stack (confirmation-only mode only)

When `mode = "confirmation-only"`, output this section **instead of** the
Comparison Table, Adoption Decision, and Rejection Reasons sections:

```markdown
## Confirmed Technology Stack

| Technology | Version | Confirmed By |
|------------|---------|-------------|
| {name} | {version} | {tech.md reference} |
```

- List every category whose `status` is `"confirmed"`.
- `Version`: use the version string from `confirmedChoice` if it includes a
  version; otherwise use the `latestVersion` obtained by Step 4 §4.6 (version
  supplement for confirmed categories). If neither is available, write
  `"情報取得不可"`.
- `Confirmed By`: the relevant passage or section from `tech.md` that mandates
  this technology.
- Categories with `status: "skipped-no-candidates"` are omitted from this table.

#### 5.1.4 Adoption Decision (comparison mode only)

For **each** category whose `status` is `"needs-comparison"`, render an
Adoption Decision subsection as defined in
`comparison-format.md §Adoption Decision`:

```markdown
### {Category Name}

**Adopted**: {CandidateName}

{Reason paragraph — 2 to 3 sentences explaining why this candidate was selected.}
```

**Evaluation Priority Order** — Apply the following priority from highest to
lowest when selecting the adopted candidate (as defined in
`comparison-format.md §Evaluation Priority Order`):

1. **tech.md explicit constraint** (highest priority): If `tech.md` mandates a
   specific technology, select it regardless of other factors.
2. **License type**: Prefer permissive licenses (MIT, Apache-2.0, BSD). Accept
   GPL/LGPL only with an explicit risk warning (see below). Reject candidates
   with proprietary or unclear licenses when a permissive alternative exists.
3. **Maintenance status**: Prefer `Active` (`"アクティブ"`) candidates over
   `Inactive` ones.
4. **Community adoption rate** (lowest priority): Prefer the candidate with the
   larger user base and ecosystem when all higher-priority criteria are equal.

**GPL / LGPL License Warning**: When the adopted candidate carries a GPL or LGPL
license, append the following warning block **immediately after** the reason
paragraph (as defined in `comparison-format.md §GPL / LGPL License Warning`):

```markdown
> **License Risk Notice**: This package is licensed under {GPL/LGPL variant}.
> Depending on how it is linked or distributed, your project may be subject to
> source-disclosure obligations and combination restrictions.
> Verify compatibility with your project's distribution model before proceeding.
```

Set `hasLicenseRiskWarning = true` in `ReportGeneratorOutput` whenever this
warning is emitted for any category. The default value is `false`.

#### 5.1.5 Rejection Reasons (comparison mode only)

For **each** category whose `status` is `"needs-comparison"`, list every
non-adopted candidate with at least one sentence as defined in
`comparison-format.md §Rejection Reasons`:

```markdown
### {Category Name} — Rejected Candidates

- **{CandidateName}**: {One or more sentences stating the primary rejection reason.}
```

If a category had only one candidate (pre-selected format was used in 5.1.3),
**omit this section for that category**.

#### 5.1.6 Design Integration Summary (always present)

Append the Design Integration Summary at the end of the document, **regardless
of mode** (comparison, confirmation-only, or mixed). This section MUST always
be present as defined in `comparison-format.md §Design Integration Summary`:

```markdown
## Design Integration Summary

| Layer | Technology / Version | Role in Feature | Notes |
|-------|---------------------|-----------------|-------|
| {layer} | {name} {version} | {what it does in this feature} | {optional caveats or warnings} |
```

Rules for this table:
- **Layer**: Short descriptive label (e.g., `Language`, `Framework`,
  `HTTP Client`, `Database ORM`, `Testing`).
- **Technology / Version**: Adopted technology name + version in one cell
  (e.g., `Python 3.12`, `FastAPI 0.111`).
- **Role in Feature**: One concise phrase describing how this technology is
  used within the feature being specified.
- **Notes**: License risk warning, deprecation notice, or version-pinning
  requirement. Write `—` if none apply.
- Include one row per adopted technology across **all** categories (confirmed,
  single-candidate, and comparison-selected).
- For `skipped-no-candidates` categories, omit the row.

### 5.2 Produce ReportGeneratorOutput

After assembling the full Markdown string, return:

```
ReportGeneratorOutput:
  reportMarkdown: string        // complete content of tech-selection.md
  hasLicenseRiskWarning: bool   // true if any GPL/LGPL warning was emitted
```

Pass `ReportGeneratorOutput` to **Step 6: Write tech-selection.md**.

---

## Step 6: Write tech-selection.md

> **Input**: `ReportGeneratorOutput` from Step 5 and the feature name from
> Step 1.

### 6.1 Write tech-selection.md with the Write Tool

Use the **Write** tool to save the report to disk:

```
Write: .kiro/specs/{feature}/tech-selection.md
Content: ReportGeneratorOutput.reportMarkdown
```

- Replace `{feature}` with the actual feature name.
- The Write tool overwrites any existing file automatically; no special
  handling is required for pre-existing files (Requirement 5.3).
- The file content already contains the feature name and generated datetime
  in the header (produced in Step 5 §5.1.1), satisfying Requirement 5.2.

### 6.2 Update spec.json with the Edit Tool

After the Write tool completes successfully, use the **Edit** tool to append
the `tech_selection` field to the `approvals` object in
`.kiro/specs/{feature}/spec.json`.

Locate the `"approvals"` object in `spec.json` and add (or replace) the
`tech_selection` key:

```json
"tech_selection": {
  "generated": true,
  "generated_at": "<ISO 8601 datetime matching the header>"
}
```

Use the same ISO 8601 datetime value that was written into the
`tech-selection.md` header in Step 5 §5.1.1.

If the `approvals` object does not yet exist in `spec.json`, create it with
`tech_selection` as its first key.

This update satisfies DD-1: `kiro-spec-status` scans the `approvals` object
to display workflow progress, so the `tech_selection` field must be present
and `"generated": true` for the status command to reflect completion.

### 6.3 Display the Completion Message

After both the Write and Edit operations complete, display the following
completion message to the user:

```
✓ tech-selection.md saved to: .kiro/specs/{feature}/tech-selection.md

Next step:
  /kiro-spec-design {feature}
```

Replace `{feature}` with the actual feature name.

**When `ReportGeneratorOutput.hasLicenseRiskWarning` is `true`**, append the
following notice to the completion message:

```
⚠ License Risk Notice: One or more adopted technologies carry a GPL or LGPL
  license. Review the License Risk Notice sections in tech-selection.md and
  verify compatibility with your project's distribution model before running
  /kiro-spec-design.
```

This satisfies Requirements 5.4 (completion message with recommended next
step) and 4.5 (GPL/LGPL risk surfaced to the user at completion).

### 6.4 Completion Criterion

The skill run is considered complete when:
1. `.kiro/specs/{feature}/tech-selection.md` exists and contains all required
   sections (Header, and either Confirmed Technology Stack or the full
   Comparison Table + Adoption Decision + Rejection Reasons, plus Design
   Integration Summary).
2. `spec.json` has `approvals.tech_selection.generated` set to `true`.

If either condition is not met due to a Write or Edit tool error, surface the
error message to the user and stop without displaying the completion message.
